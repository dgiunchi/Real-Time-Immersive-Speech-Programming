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
    /// Require a valid per-peer admission token before processing a peer's requests
    /// (defence vs Casey 2021 Man-in-the-Room). false (DEFAULT) = open (local /
    /// single-user). Set via `DCVR_REQUIRE_PEER_AUTH=true`.
    pub require_peer_auth: bool,
    /// Shared HMAC secret for per-peer admission tokens. Env-only (never sent to the
    /// admin panel). `DCVR_PEER_AUTH_SECRET`. Required when `require_peer_auth`.
    pub peer_auth_secret: Option<String>,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            listen_addr: SocketAddr::from(([127, 0, 0, 1], 9098)),
            mode: RunMode::ActionPlanFast,
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
            csharp_research_dev: false,
            mode_a: false,
            ubiq_addr: None,
            room_guid: "6765c52b-3ad6-4fb0-9030-2c9a05dc4731".to_string(),
            roslyn_url: None,
            // Mirrors `dcvr_control::RuntimeConfig` defaults (shared contract).
            max_generations_per_min: 30,
            personal_space_radius_m: 0.5,
            min_plan_interval_ms: 334,
            comfort_rotate_max_deg_s: 120.0,
            perceptual_hardening: false,
            require_peer_auth: false,
            peer_auth_secret: None,
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
        if let Ok(v) = env::var("DCVR_REQUIRE_PEER_AUTH") {
            s.require_peer_auth = parse_bool_flag(&v);
        }
        if let Ok(v) = env::var("DCVR_PEER_AUTH_SECRET") {
            let v = v.trim();
            if !v.is_empty() {
                s.peer_auth_secret = Some(v.to_string());
            }
        }
        Ok(s)
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

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    // The two security flags (DCVR_PERCEPTUAL_HARDENING / DCVR_REQUIRE_PEER_AUTH) and
    // the four feature flags all route through parse_bool_flag, so testing the
    // canonical parser proves consistent behaviour for every DCVR_* boolean.

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
}
