use std::env;
use std::net::SocketAddr;

use secrecy::SecretString;

use crate::errors::ConfigError;

/// Run mode. Phase 1/2 use the action-plan fast path; the enum extends later.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RunMode {
    ActionPlanFast,
}

impl RunMode {
    pub fn as_str(&self) -> &'static str {
        match self {
            RunMode::ActionPlanFast => "action_plan_fast",
        }
    }

    /// Parse a mode token (public so it is unit-testable without env mutation).
    pub fn from_token(s: &str) -> Result<Self, ConfigError> {
        match s {
            "action_plan_fast" => Ok(RunMode::ActionPlanFast),
            other => Err(ConfigError::InvalidMode(other.to_string())),
        }
    }
}

/// Explicit deployment security posture — the primary safety switch (spec §6),
/// replacing ad-hoc booleans. `Hardened` fails closed: if a required control is
/// missing it refuses to start rather than silently downgrading, and it never
/// falls back to `Legacy` behaviour.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum SecurityProfile {
    /// Original-compatible research path. New controls may be off; insecure by
    /// design for local/single-user use and logged as such. DEFAULT, so existing
    /// deployments and the baseline test-suite behave exactly as before.
    #[default]
    Legacy,
    /// Multi-user deployment posture: authentication + replay protection + real
    /// semantic analysis + admin auth + a secure message profile are mandatory;
    /// the backend fails closed when any required control is unavailable.
    Hardened,
    /// Deterministic CI/integration posture: loopback-only, mock STT/LLM allowed,
    /// deterministic local keys, no external providers.
    Test,
}

impl SecurityProfile {
    pub fn as_str(&self) -> &'static str {
        match self {
            SecurityProfile::Legacy => "legacy",
            SecurityProfile::Hardened => "hardened",
            SecurityProfile::Test => "test",
        }
    }

    /// Parse a profile token (public so it is unit-testable without env mutation).
    pub fn from_token(s: &str) -> Result<Self, ConfigError> {
        match s.trim().to_lowercase().as_str() {
            "legacy" => Ok(SecurityProfile::Legacy),
            "hardened" => Ok(SecurityProfile::Hardened),
            "test" => Ok(SecurityProfile::Test),
            other => Err(ConfigError::InvalidSecurityProfile(other.to_string())),
        }
    }

    pub fn is_hardened(&self) -> bool {
        matches!(self, SecurityProfile::Hardened)
    }
}

/// Resolved backend settings.
///
/// Secrets are wrapped in [`SecretString`] (redacted in `Debug`, zeroized on
/// drop). This crate never writes a secret to disk. When `stt_http_url` /
/// `openai_api_key` are `None`, the backend selects the offline mock clients,
/// so the whole pipeline runs locally with no keys.
#[derive(Debug)]
pub struct Settings {
    pub listen_addr: SocketAddr,
    pub mode: RunMode,
    /// Deployment security posture (spec §6). Default `Legacy` (back-compat).
    pub security_profile: SecurityProfile,
    pub stt_http_url: Option<String>,
    pub openai_api_key: Option<SecretString>,
    pub openai_model: String,
    pub openai_base_url: Option<String>,
    /// Use OpenAI Whisper for STT (reuses `OPENAI_API_KEY`). Default false.
    pub stt_openai: bool,
    /// OpenAI STT model (e.g. `whisper-1`, `gpt-4o-mini-transcribe`). Default `whisper-1`.
    pub openai_stt_model: String,
    /// If set, start the web admin/debug panel on this localhost port.
    pub admin_port: Option<u16>,
    /// Optional token the admin panel requires for mutating routes (`X-Admin-Token`).
    pub admin_token: Option<String>,
    /// Directory for per-user personalization/RAG JSON files.
    pub personalization_dir: String,
    /// Use OpenAI embeddings for RAG (reuses `OPENAI_API_KEY`); else a local mock.
    pub embed_openai: bool,
    pub stt_timeout_ms: u64,
    pub llm_timeout_ms: u64,
    /// Overall per-utterance wall-clock deadline (ms). `0` = disabled (default,
    /// byte-identical legacy behaviour). When set, the whole STT→LLM→validate block
    /// is bounded; on elapse the utterance fails closed (nothing is sent). This is a
    /// belt-and-suspenders umbrella over the per-step timeouts.
    pub utterance_timeout_ms: u64,
    /// Max concurrent in-flight utterances PER PEER (backpressure vs task-flood DoS,
    /// A017/A023). A single push-to-talk speaker has ~1 in flight, so the generous
    /// default never trips real use; only a flood is bounded (excess dropped
    /// fail-closed). Env `DCVR_MAX_INFLIGHT_PER_PEER`.
    pub max_inflight_per_peer: usize,
    /// Opt-in: give each peer its OWN router (its own `Mutex<Router>`), so a slow
    /// STT/LLM call for one peer no longer blocks another peer's pipeline. Default
    /// false = one shared router for all peers (byte-identical to the original
    /// serialised design; a peer only ever touches its own session either way).
    /// Env `DCVR_PER_PEER_ROUTING`.
    pub per_peer_routing: bool,
    /// Dev/research only: allow the validated-C# (Mode B) path. Default false.
    pub csharp_research_dev: bool,
    /// Mode A (original DreamCodeVR): instead of the action-plan reply, send the
    /// VALIDATED generated C# to the original Unity `CodeGenerationManager`
    /// (NID 94 `{type,peer,data}`) for runtime RoslynCSharp compilation. Default false.
    pub mode_a: bool,
    /// If set, run as a Ubiq SERVICE PEER: connect to this RoomServer addr and
    /// JOIN `room_guid`, instead of running the standalone TCP listener.
    pub ubiq_addr: Option<String>,
    pub room_guid: String,
    /// If true, run an EMBEDDED Rust RoomServer (no external Node.js) inside this
    /// process and connect the pipeline to it over loopback, so one binary is the
    /// whole system. Env `DCVR_EMBED_ROOMSERVER`. Default false (legacy behaviour).
    pub embed_roomserver: bool,
    /// Bind address for the embedded RoomServer. Env `DCVR_ROOMSERVER_BIND`.
    /// Default `0.0.0.0:8009` — headsets always dial :8009 (the Ubiq RoomServer port).
    pub roomserver_bind: String,
    /// Retention limit for stored personalization profiles, in seconds (GDPR Art.
    /// 5(1)(e)). A background sweep deletes profiles untouched for longer than this.
    /// `0` = disabled (default), keeping the legacy keep-forever behaviour.
    /// Env `DCVR_PROFILE_TTL_SECS`.
    pub profile_ttl_secs: u64,
    /// Optional .NET Roslyn semantic analyzer URL (Mode B deep check). None = mock.
    pub roslyn_url: Option<String>,
    /// Anti-flood: max generated plans per peer per minute (SOC-03/PE-02).
    pub max_generations_per_min: u32,
    /// Per-recipient comfort: personal-space radius in metres.
    pub personal_space_radius_m: f64,
    /// Anti-strobe: minimum gap (ms) between accepted plans from one peer (WCAG 2.3.1).
    pub min_plan_interval_ms: u64,
    /// Anti-vection: hard clamp on rotation magnitude (deg/sec).
    pub comfort_rotate_max_deg_s: f64,
    /// Deploy-time perceptual hardening (Mode B). false (DEFAULT) = full creative
    /// freedom (system-access bans only). true = ALSO ban the perceptual/embodied-
    /// attack C# API surface. Set via `DCVR_PERCEPTUAL_HARDENING=true`. Opt-in.
    pub perceptual_hardening: bool,
    /// Age-adaptive safety (opt-in). `DCVR_AGE_GATING=true` makes a detected MINOR
    /// automatically tighten the code-safety plane. Default false => byte-identical.
    pub age_gating: bool,
    /// The user's age band (from the on-device ML gate / platform flag). `DCVR_AGE_BAND`
    /// = child|teen|adult; anything else = Unknown (fail-safe = minor). Only consulted
    /// when `age_gating` is on.
    pub age_band: crate::age::AgeBand,
    /// Require a valid per-peer admission token before processing a peer's requests
    /// (defence vs Casey 2021 Man-in-the-Room). false (DEFAULT) = open (local /
    /// single-user). Set via `DCVR_REQUIRE_PEER_AUTH=true`.
    pub require_peer_auth: bool,
    /// Shared HMAC secret for per-peer admission tokens. Env-only (never sent to the
    /// admin panel). `DCVR_PEER_AUTH_SECRET`. Required when `require_peer_auth`.
    pub peer_auth_secret: Option<String>,
    /// Backend Ed25519 signing seed as 64-char hex (32 bytes), for signing NID-94
    /// code decisions (backend -> Unity). Env-only: `DCVR_BACKEND_SIGNING_SEED`.
    /// Required in the `hardened` profile (fail-closed). The matching public key is
    /// provisioned to the Unity `BackendVerifier`.
    pub backend_signing_seed_hex: Option<String>,
    /// Optional 64-char hex (32-byte) key to encrypt personalization profiles at
    /// rest with ChaCha20-Poly1305. Env-only: `DCVR_PROFILE_ENC_KEY`. Unset =
    /// plaintext files (back-compat); set = AEAD-encrypted at rest.
    pub profile_enc_key_hex: Option<String>,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            listen_addr: SocketAddr::from(([127, 0, 0, 1], 9098)),
            mode: RunMode::ActionPlanFast,
            security_profile: SecurityProfile::Legacy,
            stt_http_url: None,
            openai_api_key: None,
            openai_model: "gpt-4o-mini".to_string(),
            openai_base_url: None,
            stt_openai: false,
            openai_stt_model: "whisper-1".to_string(),
            admin_port: None,
            admin_token: None,
            personalization_dir: ".dcvr-data/personalization".to_string(),
            embed_openai: false,
            stt_timeout_ms: 10_000,
            llm_timeout_ms: 60_000,
            utterance_timeout_ms: 0, // disabled by default (legacy byte-identical)
            max_inflight_per_peer: 8, // generous: never trips a single human speaker
            per_peer_routing: false, // one shared router (legacy byte-identical)
            csharp_research_dev: false,
            mode_a: false,
            ubiq_addr: None,
            room_guid: "6765c52b-3ad6-4fb0-9030-2c9a05dc4731".to_string(),
            embed_roomserver: false,
            roomserver_bind: "0.0.0.0:8009".to_string(),
            profile_ttl_secs: 0,
            roslyn_url: None,
            // Mirrors `dcvr_control::RuntimeConfig` defaults (shared contract).
            max_generations_per_min: 30,
            personal_space_radius_m: 0.5,
            min_plan_interval_ms: 334,
            comfort_rotate_max_deg_s: 120.0,
            perceptual_hardening: false,
            age_gating: false,
            age_band: crate::age::AgeBand::Unknown,
            require_peer_auth: false,
            peer_auth_secret: None,
            backend_signing_seed_hex: None,
            profile_enc_key_hex: None,
        }
    }
}

/// Canonical parser for boolean env flags. Trims surrounding whitespace and
/// lowercases, then treats `1` / `true` / `yes` as true; every other value
/// (including empty or unrecognised) is false. Used by ALL `DCVR_*` boolean flags
/// so that `true`, `TRUE`, ` True `, and `1` behave identically.
fn parse_bool_flag(raw: &str) -> bool {
    let v = raw.trim().to_lowercase();
    v == "1" || v == "true" || v == "yes"
}

impl Settings {
    /// Build from environment variables, falling back to defaults. Fail-closed:
    /// an unparseable address or unknown mode is an error, not a silent default.
    pub fn from_env() -> Result<Self, ConfigError> {
        let mut s = Settings::default();
        if let Ok(addr) = env::var("DCVR_LISTEN_ADDR") {
            s.listen_addr = addr
                .parse()
                .map_err(|_| ConfigError::InvalidListenAddr(addr))?;
        }
        if let Ok(mode) = env::var("DCVR_MODE") {
            s.mode = RunMode::from_token(&mode)?;
        }
        if let Ok(p) = env::var("DCVR_SECURITY_PROFILE") {
            if !p.trim().is_empty() {
                s.security_profile = SecurityProfile::from_token(&p)?;
            }
        }
        if let Ok(url) = env::var("DCVR_STT_HTTP_URL") {
            if !url.trim().is_empty() {
                s.stt_http_url = Some(url);
            }
        }
        if let Ok(key) = env::var("OPENAI_API_KEY") {
            if !key.trim().is_empty() {
                s.openai_api_key = Some(SecretString::from(key));
            }
        }
        if let Ok(model) = env::var("OPENAI_MODEL") {
            if !model.trim().is_empty() {
                s.openai_model = model;
            }
        }
        if let Ok(v) = env::var("DCVR_STT_OPENAI") {
            s.stt_openai = parse_bool_flag(&v);
        }
        if let Ok(m) = env::var("OPENAI_STT_MODEL") {
            if !m.trim().is_empty() {
                s.openai_stt_model = m;
            }
        }
        if let Ok(p) = env::var("DCVR_ADMIN_PORT") {
            if let Ok(port) = p.trim().parse::<u16>() {
                s.admin_port = Some(port);
            }
        }
        if let Ok(t) = env::var("DCVR_ADMIN_TOKEN") {
            if !t.trim().is_empty() {
                s.admin_token = Some(t);
            }
        }
        if let Ok(d) = env::var("DCVR_PERSONALIZATION_DIR") {
            if !d.trim().is_empty() {
                s.personalization_dir = d;
            }
        }
        if let Ok(v) = env::var("DCVR_EMBED_OPENAI") {
            s.embed_openai = parse_bool_flag(&v);
        }
        if let Ok(base) = env::var("OPENAI_BASE_URL") {
            if !base.trim().is_empty() {
                s.openai_base_url = Some(base);
            }
        }
        if let Ok(v) = env::var("DCVR_STT_TIMEOUT_MS") {
            if let Ok(n) = v.parse() {
                s.stt_timeout_ms = n;
            }
        }
        if let Ok(v) = env::var("DCVR_LLM_TIMEOUT_MS") {
            if let Ok(n) = v.parse() {
                s.llm_timeout_ms = n;
            }
        }
        if let Ok(v) = env::var("DCVR_UTTERANCE_TIMEOUT_MS") {
            if let Ok(n) = v.parse() {
                s.utterance_timeout_ms = n;
            }
        }
        if let Ok(v) = env::var("DCVR_MAX_INFLIGHT_PER_PEER") {
            if let Ok(n) = v.parse::<usize>() {
                if n >= 1 {
                    s.max_inflight_per_peer = n;
                }
            }
        }
        if let Ok(v) = env::var("DCVR_PER_PEER_ROUTING") {
            s.per_peer_routing = parse_bool_flag(&v);
        }
        if let Ok(v) = env::var("DCVR_CSHARP_RESEARCH") {
            s.csharp_research_dev = parse_bool_flag(&v);
        }
        if let Ok(v) = env::var("DCVR_MODE_A") {
            s.mode_a = parse_bool_flag(&v);
        }
        if let Ok(a) = env::var("DCVR_UBIQ_ADDR") {
            if !a.trim().is_empty() {
                s.ubiq_addr = Some(a);
            }
        }
        if let Ok(g) = env::var("DCVR_ROOM_GUID") {
            if !g.trim().is_empty() {
                s.room_guid = g;
            }
        }
        if let Ok(v) = env::var("DCVR_EMBED_ROOMSERVER") {
            s.embed_roomserver = parse_bool_flag(&v);
        }
        if let Ok(b) = env::var("DCVR_ROOMSERVER_BIND") {
            if !b.trim().is_empty() {
                s.roomserver_bind = b;
            }
        }
        if let Ok(v) = env::var("DCVR_PROFILE_TTL_SECS") {
            if let Ok(n) = v.trim().parse() {
                s.profile_ttl_secs = n;
            }
        }
        if let Ok(u) = env::var("DCVR_ROSLYN_URL") {
            if !u.trim().is_empty() {
                s.roslyn_url = Some(u);
            }
        }
        if let Ok(v) = env::var("DCVR_MAX_GENERATIONS_PER_MIN") {
            if let Ok(n) = v.trim().parse() {
                s.max_generations_per_min = n;
            }
        }
        if let Ok(v) = env::var("DCVR_PERSONAL_SPACE_RADIUS_M") {
            if let Ok(n) = v.trim().parse() {
                s.personal_space_radius_m = n;
            }
        }
        if let Ok(v) = env::var("DCVR_MIN_PLAN_INTERVAL_MS") {
            if let Ok(n) = v.trim().parse() {
                s.min_plan_interval_ms = n;
            }
        }
        if let Ok(v) = env::var("DCVR_COMFORT_ROTATE_MAX_DEG_S") {
            if let Ok(n) = v.trim().parse() {
                s.comfort_rotate_max_deg_s = n;
            }
        }
        if let Ok(v) = env::var("DCVR_PERCEPTUAL_HARDENING") {
            s.perceptual_hardening = parse_bool_flag(&v);
        }
        if let Ok(v) = env::var("DCVR_AGE_GATING") {
            s.age_gating = parse_bool_flag(&v);
        }
        if let Ok(v) = env::var("DCVR_AGE_BAND") {
            s.age_band = crate::age::AgeBand::from_token(&v);
        }
        if let Ok(v) = env::var("DCVR_REQUIRE_PEER_AUTH") {
            s.require_peer_auth = parse_bool_flag(&v);
        }
        if let Ok(v) = env::var("DCVR_PEER_AUTH_SECRET") {
            let v = v.trim();
            if !v.is_empty() {
                s.peer_auth_secret = Some(v.to_string());
            }
        }
        if let Ok(v) = env::var("DCVR_BACKEND_SIGNING_SEED") {
            let v = v.trim();
            if !v.is_empty() {
                s.backend_signing_seed_hex = Some(v.to_string());
            }
        }
        if let Ok(v) = env::var("DCVR_PROFILE_ENC_KEY") {
            let v = v.trim();
            if !v.is_empty() {
                s.profile_enc_key_hex = Some(v.to_string());
            }
        }
        s.enforce_profile_invariants()
    }

    /// Apply fail-closed invariants for the selected [`SecurityProfile`]. Public so
    /// the fail-closed behaviour is unit-testable without env mutation. In the
    /// `hardened` profile, peer authentication is mandatory and a missing required
    /// control is a startup error rather than a silent downgrade (spec §6).
    pub fn enforce_profile_invariants(mut self) -> Result<Self, ConfigError> {
        if self.security_profile.is_hardened() {
            // The C# perceptual/device denylist (DeployHardened) is a DELIBERATELY
            // INDEPENDENT deploy choice, not implied by the security profile — a
            // hardened deployment may still want full creative freedom. We do not
            // couple them (that would change hardened behaviour); instead warn so an
            // operator is not surprised the perceptual bans are still off.
            if !self.perceptual_hardening {
                eprintln!(
                    "[config] note: security_profile=hardened does NOT enable the C# \
                     perceptual/device denylist; set DCVR_PERCEPTUAL_HARDENING=true to also \
                     ban XR rig / haptics / biometric / passthrough APIs in generated C#"
                );
            }
            self.require_peer_auth = true;
            if self.peer_auth_secret.is_none() {
                return Err(ConfigError::HardenedMissingControl("peer_auth_secret"));
            }
            if self.backend_signing_seed_hex.is_none() {
                return Err(ConfigError::HardenedMissingControl("backend_signing_seed"));
            }
            // Fail-closed C# validation (Phase 3): Mode A in hardened mode must not
            // silently fall back to the approve-all mock analyzer — require a real
            // semantic analyzer endpoint.
            if self.mode_a && self.roslyn_url.is_none() {
                return Err(ConfigError::HardenedMissingControl(
                    "roslyn_url (hardened Mode A requires a real semantic analyzer)",
                ));
            }
            // Transport confidentiality for the provider hops: any CUSTOM external
            // endpoint (self-hosted STT, an OpenAI-compatible base URL, the Roslyn
            // analyzer) must be https:// (or loopback) so transcripts / prompts /
            // generated code never leave the host over plaintext http. The OpenAI
            // defaults are already https. Loopback http is allowed for local dev.
            for (field, url) in [
                ("stt_http_url", &self.stt_http_url),
                ("openai_base_url", &self.openai_base_url),
                ("roslyn_url", &self.roslyn_url),
            ] {
                if let Some(u) = url {
                    if !is_secure_endpoint(u) {
                        return Err(ConfigError::HardenedInsecureUrl {
                            field,
                            url: u.clone(),
                        });
                    }
                }
            }
        }
        Ok(self)
    }

    /// True when no real STT endpoint is configured (offline mock in use).
    pub fn stt_is_mock(&self) -> bool {
        self.stt_http_url.is_none()
    }

    /// True when no API key is configured (offline mock in use).
    pub fn llm_is_mock(&self) -> bool {
        self.openai_api_key.is_none()
    }
}

/// True if `url` is transport-secure for the hardened profile: `https://`, or a
/// loopback `http://` endpoint (local dev — traffic never leaves the host).
fn is_secure_endpoint(url: &str) -> bool {
    let u = url.trim();
    if u.starts_with("https://") {
        return true;
    }
    if let Some(rest) = u.strip_prefix("http://") {
        let rest = rest.trim();
        // authority = everything before the first '/'. Reject any userinfo
        // (`user:pass@host`): a loopback dev endpoint never has it, and
        // `http://localhost:x@evil.com` must NOT be treated as loopback.
        let authority = rest.split('/').next().unwrap_or("");
        if authority.contains('@') {
            return false;
        }
        if authority.starts_with("[::1]") {
            return true; // IPv6 loopback http://[::1]:port
        }
        let host = authority.split(':').next().unwrap_or("");
        return matches!(host, "127.0.0.1" | "localhost");
    }
    false
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    // The two security flags (DCVR_PERCEPTUAL_HARDENING / DCVR_REQUIRE_PEER_AUTH) and
    // the four feature flags all route through parse_bool_flag, so testing the
    // canonical parser proves consistent behaviour for every DCVR_* boolean.

    #[test]
    fn secure_endpoint_accepts_https_and_loopback_only() {
        assert!(is_secure_endpoint("https://api.openai.com/v1"));
        assert!(is_secure_endpoint("http://127.0.0.1:5005/analyze"));
        assert!(is_secure_endpoint("http://localhost:5005/analyze"));
        assert!(is_secure_endpoint("http://[::1]:5005/analyze"));
        // plain remote http is not secure
        assert!(!is_secure_endpoint("http://evil.com/analyze"));
        // userinfo trick must NOT be treated as loopback (audit finding):
        assert!(!is_secure_endpoint("http://localhost:x@evil.com/analyze"));
        assert!(!is_secure_endpoint("http://127.0.0.1@evil.com/analyze"));
    }

    #[test]
    fn bool_flag_true_forms() {
        for t in [
            "true", "TRUE", " True ", "1", " 1 ", "yes", "YES", "\tTrue\n",
        ] {
            assert!(parse_bool_flag(t), "{t:?} should parse as true");
        }
    }

    #[test]
    fn bool_flag_false_forms() {
        for f in [
            "false", "FALSE", " False ", "0", "no", "", "   ", "enabled", "2", "off",
        ] {
            assert!(!parse_bool_flag(f), "{f:?} should parse as false");
        }
    }

    #[test]
    fn security_flags_default_off_when_unset() {
        // Unset-variable default: both hardening and peer-auth are OFF by default.
        let s = Settings::default();
        assert!(!s.perceptual_hardening);
        assert!(!s.require_peer_auth);
    }

    #[test]
    fn uppercase_true_enables_security_flags_not_silently_disabled() {
        // Regression: `DCVR_*=TRUE` (or whitespace-padded) must NOT evaluate as
        // disabled. Parsing a security toggle is the same path as the field wiring.
        assert!(parse_bool_flag("TRUE"));
        assert!(parse_bool_flag(" true "));
        assert!(!parse_bool_flag("FALSE"));
    }

    #[test]
    fn security_profile_defaults_to_legacy() {
        // Back-compat: unset profile keeps today's behaviour.
        assert_eq!(
            Settings::default().security_profile,
            SecurityProfile::Legacy
        );
        assert_eq!(SecurityProfile::default(), SecurityProfile::Legacy);
    }

    #[test]
    fn utterance_timeout_defaults_to_disabled() {
        // 0 = disabled = legacy byte-identical (no overall utterance bound applied).
        assert_eq!(Settings::default().utterance_timeout_ms, 0);
    }

    #[test]
    fn max_inflight_per_peer_defaults_generous() {
        // Generous default so a single push-to-talk speaker never trips backpressure.
        assert_eq!(Settings::default().max_inflight_per_peer, 8);
    }

    #[test]
    fn per_peer_routing_defaults_off() {
        // Off = one shared router = legacy byte-identical serialised design.
        assert!(!Settings::default().per_peer_routing);
    }

    #[test]
    fn is_secure_endpoint_accepts_https_and_loopback_only() {
        assert!(is_secure_endpoint("https://api.openai.com/v1"));
        assert!(is_secure_endpoint("http://127.0.0.1:5099/analyze"));
        assert!(is_secure_endpoint("http://localhost:8080"));
        assert!(is_secure_endpoint("http://[::1]:9000/x"));
        // Plaintext to a non-loopback host leaks transcripts/prompts/code.
        assert!(!is_secure_endpoint("http://transcribe.example.com/stt"));
        assert!(!is_secure_endpoint("http://10.0.0.5:5099"));
        assert!(!is_secure_endpoint("ftp://x"));
    }

    #[test]
    fn hardened_rejects_plaintext_external_endpoint() {
        // A self-hosted STT over plaintext http would send transcripts in the clear.
        let s = Settings {
            security_profile: SecurityProfile::Hardened,
            peer_auth_secret: Some("s".to_string()),
            backend_signing_seed_hex: Some("aa".repeat(32)),
            stt_http_url: Some("http://transcribe.example.com/stt".to_string()),
            ..Default::default()
        };
        assert!(matches!(
            s.enforce_profile_invariants(),
            Err(ConfigError::HardenedInsecureUrl {
                field: "stt_http_url",
                ..
            })
        ));
    }

    #[test]
    fn hardened_accepts_https_and_loopback_endpoints() {
        let s = Settings {
            security_profile: SecurityProfile::Hardened,
            peer_auth_secret: Some("s".to_string()),
            backend_signing_seed_hex: Some("aa".repeat(32)),
            stt_http_url: Some("https://stt.internal/transcribe".to_string()),
            roslyn_url: Some("http://127.0.0.1:5099/analyze".to_string()),
            ..Default::default()
        };
        assert!(s.enforce_profile_invariants().is_ok());
    }

    #[test]
    fn security_profile_parses_all_tokens_case_and_space_insensitive() {
        assert_eq!(
            SecurityProfile::from_token("legacy"),
            Ok(SecurityProfile::Legacy)
        );
        assert_eq!(
            SecurityProfile::from_token(" Hardened "),
            Ok(SecurityProfile::Hardened)
        );
        assert_eq!(
            SecurityProfile::from_token("TEST"),
            Ok(SecurityProfile::Test)
        );
        assert!(matches!(
            SecurityProfile::from_token("open"),
            Err(ConfigError::InvalidSecurityProfile(_))
        ));
    }

    #[test]
    fn is_hardened_only_for_hardened() {
        assert!(SecurityProfile::Hardened.is_hardened());
        assert!(!SecurityProfile::Legacy.is_hardened());
        assert!(!SecurityProfile::Test.is_hardened());
    }

    #[test]
    fn hardened_without_secret_fails_closed() {
        // Fail-closed: hardened mode must refuse to start without its required
        // admission secret, never silently downgrade to insecure legacy behaviour.
        let s = Settings {
            security_profile: SecurityProfile::Hardened,
            ..Default::default()
        };
        assert_eq!(
            s.enforce_profile_invariants().err(),
            Some(ConfigError::HardenedMissingControl("peer_auth_secret"))
        );
    }

    #[test]
    fn hardened_without_signing_seed_fails_closed() {
        // Hardened also requires the backend Ed25519 signing seed (to sign NID-94
        // code decisions). Present the admission secret but omit the seed.
        let s = Settings {
            security_profile: SecurityProfile::Hardened,
            peer_auth_secret: Some("deterministic-test-secret".to_string()),
            ..Default::default()
        };
        assert_eq!(
            s.enforce_profile_invariants().err(),
            Some(ConfigError::HardenedMissingControl("backend_signing_seed"))
        );
    }

    #[test]
    fn hardened_with_all_controls_forces_peer_auth_on() {
        // Even if peer-auth was explicitly disabled, hardened re-enables it: there
        // is no "hardened but unauthenticated" configuration. Requires BOTH the
        // admission secret and the backend signing seed.
        let s = Settings {
            security_profile: SecurityProfile::Hardened,
            require_peer_auth: false,
            peer_auth_secret: Some("deterministic-test-secret".to_string()),
            backend_signing_seed_hex: Some("aa".repeat(32)),
            ..Default::default()
        };
        let s = s
            .enforce_profile_invariants()
            .expect("hardened + all controls should be valid");
        assert!(s.require_peer_auth, "hardened must force peer auth on");
    }

    #[test]
    fn legacy_profile_leaves_flags_untouched() {
        // Legacy stays permissive: no invariant flips its defaults.
        let s = Settings::default()
            .enforce_profile_invariants()
            .expect("legacy always valid");
        assert!(!s.require_peer_auth);
        assert_eq!(s.security_profile, SecurityProfile::Legacy);
    }

    #[test]
    fn hardened_does_not_imply_perceptual_hardening() {
        // Documented decoupling: hardening the deployment (auth/replay/analyzer) must
        // NOT silently enable the C# perceptual/device denylist — that is an
        // independent deploy choice (a hardened deployment may still want full creative
        // freedom). enforce_profile_invariants forces peer-auth but leaves
        // perceptual_hardening exactly as configured. (A startup note is logged.)
        let s = Settings {
            security_profile: SecurityProfile::Hardened,
            perceptual_hardening: false,
            peer_auth_secret: Some("deterministic-test-secret".to_string()),
            backend_signing_seed_hex: Some("aa".repeat(32)),
            ..Default::default()
        }
        .enforce_profile_invariants()
        .expect("hardened + all controls should be valid");
        assert!(
            !s.perceptual_hardening,
            "hardened must NOT auto-enable perceptual_hardening"
        );
        assert!(s.require_peer_auth, "but hardened DOES force peer auth on");
    }

    #[test]
    fn hardened_mode_a_without_real_analyzer_fails_closed() {
        // Phase 3: hardened Mode A must not run on the approve-all mock analyzer.
        let s = Settings {
            security_profile: SecurityProfile::Hardened,
            peer_auth_secret: Some("s".to_string()),
            backend_signing_seed_hex: Some("aa".repeat(32)),
            mode_a: true,
            roslyn_url: None,
            ..Default::default()
        };
        assert!(matches!(
            s.enforce_profile_invariants(),
            Err(ConfigError::HardenedMissingControl(m)) if m.contains("roslyn")
        ));
    }

    #[test]
    fn hardened_mode_a_with_real_analyzer_ok() {
        let s = Settings {
            security_profile: SecurityProfile::Hardened,
            peer_auth_secret: Some("s".to_string()),
            backend_signing_seed_hex: Some("aa".repeat(32)),
            mode_a: true,
            roslyn_url: Some("http://127.0.0.1:5099".to_string()),
            ..Default::default()
        };
        assert!(s.enforce_profile_invariants().is_ok());
    }
}
