//! Server-side authentication seam (Phase 1, P1.4).
//!
//! Turns the crypto primitives in `dcvr-unity-transport` into the two operations
//! the live server needs, gated by the [`SecurityProfile`]:
//!
//! * [`ServerAuth::sign_nid94`] — wrap an outgoing NID-94 code decision in a
//!   backend **Ed25519-signed** envelope (backend -> Unity), so the headset
//!   compiles only backend-approved code.
//! * [`ServerAuth::verify_incoming`] — in an active profile, require + verify the
//!   client **HMAC** envelope (admission + freshness + replay + domain) before
//!   processing a peer's request.
//!
//! In the **legacy** profile (the default, and what runs on the Quest today) the
//! seam is inert: `sign_nid94` returns the plain bytes UNCHANGED (byte-identical
//! to today) and `verify_incoming` trusts the self-asserted uuid exactly as
//! before. Only `hardened`/`test` activate the cryptography. Fail-closed: in an
//! active profile a missing/invalid key or a bad envelope means the message is
//! rejected, never processed or sent unsigned.
//!
//! Wire form of a signed/verified message: `[u32 LE envelope_len][envelope][body]`.

use std::sync::atomic::{AtomicU64, Ordering};
use std::time::{SystemTime, UNIX_EPOCH};

use dcvr_config::{SecurityProfile, Settings};
use dcvr_protocol::{split_peer_payload, AuthEnvelope, ENVELOPE_VERSION};
use dcvr_unity_transport::{
    payload_hash, BackendSigner, EnvelopeAuthError, EnvelopeMac, SessionSequence,
};

/// Freshness window applied to a backend-signed NID-94 decision.
const DEFAULT_SIGN_TTL_SECS: u64 = 30;

/// Why a server-side authentication operation failed (fail-closed).
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum ServerAuthError {
    #[error("message framing/envelope is malformed")]
    Malformed,
    #[error("message body does not match the signed payload hash")]
    HashMismatch,
    #[error("envelope domain (NID {got}) does not match the channel (NID {want})")]
    DomainMismatch { want: u32, got: u32 },
    #[error("envelope authentication failed: {0}")]
    Auth(#[from] EnvelopeAuthError),
    #[error("backend signing key is unavailable or invalid (fail-closed)")]
    BadSigningKey,
    #[error("client authentication is required but no admission key is configured")]
    NoClientKey,
}

/// A message that passed the incoming gate: the trusted peer id and the body.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct VerifiedIncoming {
    /// Authenticated peer id (hardened) or self-asserted uuid (legacy).
    pub peer_id: String,
    /// The message body (e.g. selection text or audio bytes).
    pub body: Vec<u8>,
}

/// Current wall-clock unix seconds (live path). Tests pass an explicit time.
pub fn now_unix() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

/// The server's authentication context, built once from [`Settings`] and shared.
pub struct ServerAuth {
    profile: SecurityProfile,
    /// True for hardened/test; false for legacy (the inert passthrough).
    active: bool,
    /// Client -> backend admission MAC (present iff active + configured).
    mac: Option<EnvelopeMac>,
    /// Backend -> Unity signer (present iff active + a valid seed configured).
    signer: Option<BackendSigner>,
    /// Monotonic outgoing sequence for backend-signed NID-94 (anti-replay).
    out_seq: AtomicU64,
    sign_ttl_secs: u64,
}

impl ServerAuth {
    /// Build from settings. Infallible: in an active profile a missing/invalid key
    /// leaves the corresponding capability `None`, so the operation later fails
    /// closed (rejects / refuses to send) rather than downgrading silently.
    pub fn from_settings(s: &Settings) -> Self {
        let active = !matches!(s.security_profile, SecurityProfile::Legacy);
        let (mac, signer) = if active {
            (
                s.peer_auth_secret
                    .as_ref()
                    .map(|sec| EnvelopeMac::new(sec.as_bytes())),
                s.backend_signing_seed_hex
                    .as_ref()
                    .and_then(|h| decode_hex32(h))
                    .and_then(|seed| BackendSigner::from_seed(&seed).ok()),
            )
        } else {
            // Legacy is always inert, regardless of any keys that happen to be set.
            (None, None)
        };
        Self {
            profile: s.security_profile,
            active,
            mac,
            signer,
            out_seq: AtomicU64::new(0),
            sign_ttl_secs: DEFAULT_SIGN_TTL_SECS,
        }
    }

    /// The backend Ed25519 public key to provision to the Unity `BackendVerifier`
    /// (`None` in legacy or if no signing key is configured).
    pub fn backend_public_key(&self) -> Option<Vec<u8>> {
        self.signer.as_ref().map(|s| s.public_key_bytes())
    }

    pub fn is_active(&self) -> bool {
        self.active
    }

    /// Wrap an outgoing NID-94 code decision. Legacy: returns `plain` unchanged.
    /// Active: returns `[u32 env_len][signed envelope][plain]`, binding the exact
    /// code hash. Fail-closed: an active profile with no signer returns `Err` so
    /// the caller does NOT send unsigned code.
    pub fn sign_nid94(
        &self,
        plain: &[u8],
        target_peer: &str,
        request_id: &str,
        now_unix: u64,
    ) -> Result<Vec<u8>, ServerAuthError> {
        if !self.active {
            return Ok(plain.to_vec());
        }
        let signer = self.signer.as_ref().ok_or(ServerAuthError::BadSigningKey)?;
        let seq = self.out_seq.fetch_add(1, Ordering::Relaxed).wrapping_add(1);
        let mut env = AuthEnvelope {
            version: ENVELOPE_VERSION,
            profile: profile_tag(self.profile),
            network_id_b: 94,
            sequence_number: seq,
            expiry_unix: now_unix.saturating_add(self.sign_ttl_secs),
            session_id: target_peer.to_string(),
            authenticated_peer_id: "backend".to_string(),
            request_id: request_id.to_string(),
            target_peer_id: target_peer.to_string(),
            payload_hash: payload_hash(plain),
            auth_tag: Vec::new(),
        };
        signer
            .sign(&mut env)
            .map_err(|_| ServerAuthError::BadSigningKey)?;
        let env_bytes = env.to_bytes().map_err(|_| ServerAuthError::Malformed)?;
        Ok(frame(&env_bytes, plain))
    }

    /// Verify + unwrap an incoming message on channel `network_id_b`. Legacy:
    /// splits the self-asserted `[uuid][body]` exactly as today. Active: verifies
    /// the client MAC, freshness, code-hash binding, channel domain, and replay.
    pub fn verify_incoming(
        &self,
        network_id_b: u32,
        payload: &[u8],
        seq_state: &mut SessionSequence,
        now_unix: u64,
    ) -> Result<VerifiedIncoming, ServerAuthError> {
        if !self.active {
            let pp = split_peer_payload(payload).map_err(|_| ServerAuthError::Malformed)?;
            return Ok(VerifiedIncoming {
                peer_id: pp.peer_uuid,
                body: pp.body,
            });
        }
        let mac = self.mac.as_ref().ok_or(ServerAuthError::NoClientKey)?;
        let (env_bytes, body) = unframe(payload).ok_or(ServerAuthError::Malformed)?;
        let env = AuthEnvelope::from_bytes(env_bytes).map_err(|_| ServerAuthError::Malformed)?;
        mac.verify(&env, now_unix)?;
        if env.network_id_b != network_id_b {
            return Err(ServerAuthError::DomainMismatch {
                want: network_id_b,
                got: env.network_id_b,
            });
        }
        if payload_hash(body) != env.payload_hash {
            return Err(ServerAuthError::HashMismatch);
        }
        seq_state.accept(env.sequence_number)?;
        Ok(VerifiedIncoming {
            peer_id: env.authenticated_peer_id,
            body: body.to_vec(),
        })
    }

    /// The per-peer bucket key for replay state, chosen BEFORE verification (because
    /// [`Self::verify_incoming`] needs the caller to supply that peer's
    /// [`SessionSequence`]). Legacy: the self-asserted uuid (used only as a map key;
    /// legacy enforces no replay). Hardened: the CLAIMED `authenticated_peer_id` from
    /// the envelope — untrusted, used ONLY to pick the bucket. A forged claim cannot
    /// advance that bucket, because `verify_incoming` rejects it (bad MAC / hash /
    /// domain) before `SessionSequence::accept` ever runs. Returns `None` when the
    /// frame is unparseable, so the caller drops it.
    pub fn incoming_peer_key(&self, payload: &[u8]) -> Option<String> {
        if !self.active {
            return split_peer_payload(payload).ok().map(|pp| pp.peer_uuid);
        }
        let (env_bytes, _body) = unframe(payload)?;
        AuthEnvelope::from_bytes(env_bytes)
            .ok()
            .map(|e| e.authenticated_peer_id)
    }
}

fn profile_tag(p: SecurityProfile) -> u8 {
    match p {
        SecurityProfile::Legacy => 0,
        SecurityProfile::Hardened => 1,
        SecurityProfile::Test => 2,
    }
}

fn frame(env_bytes: &[u8], body: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(4 + env_bytes.len() + body.len());
    out.extend_from_slice(&(env_bytes.len() as u32).to_le_bytes());
    out.extend_from_slice(env_bytes);
    out.extend_from_slice(body);
    out
}

fn unframe(payload: &[u8]) -> Option<(&[u8], &[u8])> {
    let len_bytes: [u8; 4] = payload.get(0..4)?.try_into().ok()?;
    let len = u32::from_le_bytes(len_bytes) as usize;
    let env_end = 4usize.checked_add(len)?;
    let env = payload.get(4..env_end)?;
    let body = payload.get(env_end..)?;
    Some((env, body))
}

fn decode_hex32(s: &str) -> Option<[u8; 32]> {
    if s.len() != 64 {
        return None;
    }
    let b = s.as_bytes();
    let mut out = [0u8; 32];
    for (i, slot) in out.iter_mut().enumerate() {
        *slot = (hexval(b[2 * i])? << 4) | hexval(b[2 * i + 1])?;
    }
    Some(out)
}

fn hexval(c: u8) -> Option<u8> {
    match c {
        b'0'..=b'9' => Some(c - b'0'),
        b'a'..=b'f' => Some(c - b'a' + 10),
        b'A'..=b'F' => Some(c - b'A' + 10),
        _ => None,
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;
    use dcvr_protocol::join_peer_payload;
    use dcvr_unity_transport::BackendVerifier;

    const SEED_HEX: &str = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const SECRET: &str = "shared-room-secret";

    fn legacy() -> ServerAuth {
        ServerAuth::from_settings(&Settings::default())
    }

    fn active() -> ServerAuth {
        let s = Settings {
            security_profile: SecurityProfile::Test,
            peer_auth_secret: Some(SECRET.to_string()),
            backend_signing_seed_hex: Some(SEED_HEX.to_string()),
            ..Default::default()
        };
        ServerAuth::from_settings(&s)
    }

    // ---- legacy is inert / byte-identical ----

    #[test]
    fn legacy_sign_is_byte_identical_passthrough() {
        let a = legacy();
        assert!(!a.is_active());
        let plain = br#"{"type":"code","peer":"p","data":"x"}"#;
        assert_eq!(
            a.sign_nid94(plain, "p", "req", 1000).unwrap(),
            plain.to_vec()
        );
    }

    #[test]
    fn legacy_verify_incoming_splits_self_asserted_uuid() {
        let a = legacy();
        let payload = join_peer_payload(&"u".repeat(36), b"select:cube");
        let mut seq = SessionSequence::default();
        let v = a.verify_incoming(93, &payload, &mut seq, 1000).unwrap();
        assert_eq!(v.peer_id, "u".repeat(36));
        assert_eq!(v.body, b"select:cube");
    }

    // ---- active outgoing: Unity can verify what the backend signs ----

    #[test]
    fn active_sign_nid94_is_verifiable_by_backend_public_key() {
        let a = active();
        let plain = br#"{"type":"code","data":"cube.red()"}"#;
        let signed = a.sign_nid94(plain, "peer-1", "req-1", 1000).unwrap();
        let (env_bytes, body) = unframe(&signed).expect("framed");
        assert_eq!(body, plain, "body travels intact after the envelope");
        let env = AuthEnvelope::from_bytes(env_bytes).unwrap();
        let verifier = BackendVerifier::new(a.backend_public_key().unwrap());
        assert_eq!(verifier.verify(&env, 1000), Ok(()));
        assert_eq!(env.payload_hash, payload_hash(plain));
        assert_eq!(env.network_id_b, 94);
    }

    #[test]
    fn active_sign_sequence_is_monotonic() {
        let a = active();
        let s1 = a.sign_nid94(b"a", "p", "r", 1000).unwrap();
        let s2 = a.sign_nid94(b"a", "p", "r", 1000).unwrap();
        let e1 = AuthEnvelope::from_bytes(unframe(&s1).unwrap().0).unwrap();
        let e2 = AuthEnvelope::from_bytes(unframe(&s2).unwrap().0).unwrap();
        assert!(e2.sequence_number > e1.sequence_number);
    }

    // ---- active incoming: full client->backend round-trip + rejections ----

    fn make_client_message(nid: u32, seq: u64, body: &[u8], now_ok_expiry: u64) -> Vec<u8> {
        let mut env = AuthEnvelope {
            version: ENVELOPE_VERSION,
            profile: 2,
            network_id_b: nid,
            sequence_number: seq,
            expiry_unix: now_ok_expiry,
            session_id: "sess".into(),
            authenticated_peer_id: "peer-authenticated".into(),
            request_id: "req".into(),
            target_peer_id: String::new(),
            payload_hash: payload_hash(body),
            auth_tag: Vec::new(),
        };
        EnvelopeMac::new(SECRET.as_bytes()).sign(&mut env).unwrap();
        frame(&env.to_bytes().unwrap(), body)
    }

    #[test]
    fn active_verify_incoming_accepts_authenticated_message() {
        let a = active();
        let mut seq = SessionSequence::default();
        let msg = make_client_message(98, 1, b"hello-audio", 2_000_000_000);
        let v = a.verify_incoming(98, &msg, &mut seq, 1000).unwrap();
        assert_eq!(v.peer_id, "peer-authenticated"); // proven, not self-asserted
        assert_eq!(v.body, b"hello-audio");
    }

    #[test]
    fn active_verify_rejects_forged_mac() {
        let a = active();
        let mut seq = SessionSequence::default();
        // Sign with the WRONG secret.
        let mut env = AuthEnvelope {
            version: ENVELOPE_VERSION,
            profile: 2,
            network_id_b: 98,
            sequence_number: 1,
            expiry_unix: 2_000_000_000,
            session_id: "s".into(),
            authenticated_peer_id: "rogue".into(),
            request_id: "r".into(),
            target_peer_id: String::new(),
            payload_hash: payload_hash(b"x"),
            auth_tag: Vec::new(),
        };
        EnvelopeMac::new(b"attacker").sign(&mut env).unwrap();
        let msg = frame(&env.to_bytes().unwrap(), b"x");
        assert_eq!(
            a.verify_incoming(98, &msg, &mut seq, 1000),
            Err(ServerAuthError::Auth(EnvelopeAuthError::BadTag))
        );
    }

    #[test]
    fn active_verify_rejects_tampered_body() {
        let a = active();
        let mut seq = SessionSequence::default();
        let mut msg = make_client_message(98, 1, b"original", 2_000_000_000);
        // Flip a byte of the body (last byte).
        let n = msg.len();
        msg[n - 1] ^= 0xFF;
        assert_eq!(
            a.verify_incoming(98, &msg, &mut seq, 1000),
            Err(ServerAuthError::HashMismatch)
        );
    }

    #[test]
    fn active_verify_rejects_wrong_domain() {
        // A tag minted for NID 98 must not be accepted on NID 93.
        let a = active();
        let mut seq = SessionSequence::default();
        let msg = make_client_message(98, 1, b"b", 2_000_000_000);
        assert_eq!(
            a.verify_incoming(93, &msg, &mut seq, 1000),
            Err(ServerAuthError::DomainMismatch { want: 93, got: 98 })
        );
    }

    #[test]
    fn active_verify_rejects_replay_and_expired() {
        let a = active();
        let mut seq = SessionSequence::default();
        let m1 = make_client_message(98, 5, b"b", 2_000_000_000);
        assert!(a.verify_incoming(98, &m1, &mut seq, 1000).is_ok());
        // Same sequence again = replay.
        let m1b = make_client_message(98, 5, b"b", 2_000_000_000);
        assert_eq!(
            a.verify_incoming(98, &m1b, &mut seq, 1000),
            Err(ServerAuthError::Auth(EnvelopeAuthError::Replay(5, 5)))
        );
        // Expired.
        let mut seq2 = SessionSequence::default();
        let old = make_client_message(98, 1, b"b", 100);
        assert_eq!(
            a.verify_incoming(98, &old, &mut seq2, 1000),
            Err(ServerAuthError::Auth(EnvelopeAuthError::Expired))
        );
    }

    #[test]
    fn hex_decode_roundtrip_and_rejects_bad() {
        assert_eq!(decode_hex32(SEED_HEX), Some([0xaau8; 32]));
        assert_eq!(decode_hex32("zz"), None);
        assert_eq!(decode_hex32(&"a".repeat(63)), None); // wrong length
    }

    // ---- incoming_peer_key: the per-peer replay-bucket selector used by the recv loop ----

    #[test]
    fn incoming_peer_key_legacy_is_self_asserted_uuid() {
        let a = legacy();
        let payload = join_peer_payload(&"z".repeat(36), b"body");
        assert_eq!(a.incoming_peer_key(&payload), Some("z".repeat(36)));
    }

    #[test]
    fn incoming_peer_key_hardened_is_the_claimed_authenticated_id() {
        let a = active();
        let msg = make_client_message(98, 1, b"x", 2_000_000_000);
        // The envelope's authenticated_peer_id is used to pick the replay bucket.
        assert_eq!(
            a.incoming_peer_key(&msg),
            Some("peer-authenticated".to_string())
        );
    }

    #[test]
    fn incoming_peer_key_none_on_garbage() {
        // Unparseable frames key to None so the recv loop drops them.
        assert_eq!(legacy().incoming_peer_key(&[0x00, 0x01]), None);
        assert_eq!(active().incoming_peer_key(&[0xFF; 3]), None);
    }

    #[test]
    fn recv_gate_pair_accepts_then_rejects_replay_in_the_same_bucket() {
        // Mirrors the recv loop: pick the bucket with incoming_peer_key, then verify
        // with that peer's SessionSequence. An identical replayed frame in the SAME
        // bucket is rejected — proving per-peer replay state works end to end.
        let a = active();
        let mut buckets: std::collections::HashMap<String, SessionSequence> =
            std::collections::HashMap::new();
        let msg = make_client_message(98, 7, b"audio", 2_000_000_000);

        let key = a.incoming_peer_key(&msg).expect("keyed");
        let seq = buckets.entry(key.clone()).or_default();
        assert!(
            a.verify_incoming(98, &msg, seq, 1000).is_ok(),
            "first accepted"
        );

        let key2 = a.incoming_peer_key(&msg).expect("keyed");
        let seq2 = buckets.entry(key2).or_default(); // same bucket
        assert!(
            a.verify_incoming(98, &msg, seq2, 1000).is_err(),
            "replay in the same peer bucket must be rejected"
        );
    }
}
