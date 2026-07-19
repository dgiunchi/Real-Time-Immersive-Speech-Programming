//! Cryptographic authentication for the [`AuthEnvelope`] (Phase 1, spec §10).
//!
//! Two trust directions, deliberately different primitives so a compromise on one
//! leg cannot forge the other:
//!
//! * **Client -> backend:** a per-session **HMAC-SHA256** MAC ([`EnvelopeMac`]).
//!   Symmetric, cheap, and already the primitive behind the admission tokens in
//!   [`crate::auth`]. Any secret-holder can MAC as anyone who shares that secret,
//!   which is exactly the room-admission trust model.
//! * **Backend -> Unity:** an asymmetric **Ed25519** signature
//!   ([`BackendSigner`] / [`BackendVerifier`]). The private signing key lives only
//!   in the backend; Unity holds the public key. So even a leaked client MAC
//!   secret cannot forge a backend-approved NID-94 code decision.
//!
//! All verification is constant-time (via `ring`), checks the envelope version and
//! expiry, and works over [`AuthEnvelope::signing_input`], so every authenticated
//! field — including the `network_id_b` domain and the `payload_hash` code hash —
//! is covered. Replay/reordering is a separate, stateful concern handled by
//! [`SessionSequence`].

use ring::{digest, hmac, signature};

use dcvr_protocol::{AuthEnvelope, ENVELOPE_HASH_LEN, ENVELOPE_VERSION};

/// Why envelope authentication failed. Fail-closed: any error means the message
/// is rejected, never processed as if authenticated.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum EnvelopeAuthError {
    #[error("unsupported envelope version {0}")]
    UnsupportedVersion(u16),
    #[error("envelope authentication tag/signature is invalid")]
    BadTag,
    #[error("envelope has expired")]
    Expired,
    #[error("envelope replays or reorders sequence {0} (last accepted {1})")]
    Replay(u64, u64),
    #[error("envelope serialisation failed")]
    Serialisation,
    #[error("backend signing key material is invalid")]
    BadKey,
}

/// SHA-256 of a message body. For a NID-94 code message this is the exact code
/// hash bound into the envelope's `payload_hash`.
pub fn payload_hash(body: &[u8]) -> [u8; ENVELOPE_HASH_LEN] {
    let d = digest::digest(&digest::SHA256, body);
    let mut out = [0u8; ENVELOPE_HASH_LEN];
    // SHA-256 output is exactly 32 bytes == ENVELOPE_HASH_LEN.
    out.copy_from_slice(d.as_ref());
    out
}

/// Generate 32 cryptographically-random bytes — for admission secrets, backend
/// signing seeds, and profile-encryption keys (see the `keygen` utility). `None`
/// only if the OS RNG fails.
pub fn random_32() -> Option<[u8; 32]> {
    use ring::rand::{SecureRandom, SystemRandom};
    let mut b = [0u8; 32];
    SystemRandom::new().fill(&mut b).ok()?;
    Some(b)
}

fn check_version(env: &AuthEnvelope) -> Result<(), EnvelopeAuthError> {
    if env.version != ENVELOPE_VERSION {
        return Err(EnvelopeAuthError::UnsupportedVersion(env.version));
    }
    Ok(())
}

/// Client -> backend MAC over the authenticated envelope region (HMAC-SHA256).
pub struct EnvelopeMac {
    key: hmac::Key,
}

impl EnvelopeMac {
    /// Construct from a shared per-session (or per-room) secret.
    pub fn new(secret: &[u8]) -> Self {
        Self {
            key: hmac::Key::new(hmac::HMAC_SHA256, secret),
        }
    }

    /// Compute and attach the MAC over `env.signing_input()`.
    pub fn sign(&self, env: &mut AuthEnvelope) -> Result<(), EnvelopeAuthError> {
        let input = env
            .signing_input()
            .map_err(|_| EnvelopeAuthError::Serialisation)?;
        env.auth_tag = hmac::sign(&self.key, &input).as_ref().to_vec();
        Ok(())
    }

    /// Verify version, authenticity (constant-time), and freshness. Authenticity is
    /// checked before expiry so a forged tag is `BadTag` regardless of its expiry.
    pub fn verify(&self, env: &AuthEnvelope, now_unix: u64) -> Result<(), EnvelopeAuthError> {
        if env.version != ENVELOPE_VERSION {
            return Err(EnvelopeAuthError::UnsupportedVersion(env.version));
        }
        let input = env
            .signing_input()
            .map_err(|_| EnvelopeAuthError::Serialisation)?;
        hmac::verify(&self.key, &input, &env.auth_tag).map_err(|_| EnvelopeAuthError::BadTag)?;
        if now_unix > env.expiry_unix {
            return Err(EnvelopeAuthError::Expired);
        }
        Ok(())
    }
}

/// Backend-only Ed25519 signer for NID-94 decisions (backend -> Unity).
pub struct BackendSigner {
    keypair: signature::Ed25519KeyPair,
}

impl BackendSigner {
    /// Load from a PKCS#8 v2 document (the production key provisioning path).
    pub fn from_pkcs8(pkcs8: &[u8]) -> Result<Self, EnvelopeAuthError> {
        signature::Ed25519KeyPair::from_pkcs8(pkcs8)
            .map(|keypair| Self { keypair })
            .map_err(|_| EnvelopeAuthError::BadKey)
    }

    /// Deterministic key from a 32-byte seed — used by the `test` profile so CI has
    /// stable local keys. NOT for production (a seed is as sensitive as the key).
    pub fn from_seed(seed: &[u8]) -> Result<Self, EnvelopeAuthError> {
        signature::Ed25519KeyPair::from_seed_unchecked(seed)
            .map(|keypair| Self { keypair })
            .map_err(|_| EnvelopeAuthError::BadKey)
    }

    /// The public verification key bytes to hand to the Unity `BackendVerifier`.
    pub fn public_key_bytes(&self) -> Vec<u8> {
        use ring::signature::KeyPair;
        self.keypair.public_key().as_ref().to_vec()
    }

    /// Sign `env.signing_input()` and attach the signature as the auth tag.
    pub fn sign(&self, env: &mut AuthEnvelope) -> Result<(), EnvelopeAuthError> {
        let input = env
            .signing_input()
            .map_err(|_| EnvelopeAuthError::Serialisation)?;
        env.auth_tag = self.keypair.sign(&input).as_ref().to_vec();
        Ok(())
    }
}

/// Public-key verifier for backend-signed envelopes (this is the Rust mirror of
/// the Unity `BackendVerifier` — same check, so both ends agree byte-for-byte).
pub struct BackendVerifier {
    public_key: Vec<u8>,
}

impl BackendVerifier {
    pub fn new(public_key: Vec<u8>) -> Self {
        Self { public_key }
    }

    /// Verify version, Ed25519 signature (constant-time), and freshness.
    pub fn verify(&self, env: &AuthEnvelope, now_unix: u64) -> Result<(), EnvelopeAuthError> {
        check_version(env)?;
        let input = env
            .signing_input()
            .map_err(|_| EnvelopeAuthError::Serialisation)?;
        signature::UnparsedPublicKey::new(&signature::ED25519, &self.public_key)
            .verify(&input, &env.auth_tag)
            .map_err(|_| EnvelopeAuthError::BadTag)?;
        if now_unix > env.expiry_unix {
            return Err(EnvelopeAuthError::Expired);
        }
        Ok(())
    }
}

/// Per-session anti-replay / ordering guard. Ubiq runs over ordered TCP, so a
/// strictly-increasing sequence is both correct and the simplest safe policy:
/// any repeated or out-of-order sequence is rejected as a replay.
#[derive(Debug, Default, Clone)]
pub struct SessionSequence {
    last: Option<u64>,
}

impl SessionSequence {
    /// Accept `seq` iff it is strictly greater than the last accepted sequence.
    pub fn accept(&mut self, seq: u64) -> Result<(), EnvelopeAuthError> {
        match self.last {
            Some(last) if seq <= last => Err(EnvelopeAuthError::Replay(seq, last)),
            _ => {
                self.last = Some(seq);
                Ok(())
            }
        }
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    const SEED: [u8; 32] = [7u8; 32];

    fn env(now_ok: bool) -> AuthEnvelope {
        AuthEnvelope {
            version: ENVELOPE_VERSION,
            profile: 1,
            network_id_b: 94,
            sequence_number: 1,
            expiry_unix: if now_ok { 2_000_000_000 } else { 100 },
            session_id: "sess-1".into(),
            authenticated_peer_id: "peer-1".into(),
            request_id: "req-1".into(),
            target_peer_id: "peer-1".into(),
            payload_hash: payload_hash(b"cube.material = red"),
            auth_tag: Vec::new(),
        }
    }

    // ---- HMAC (client -> backend) ----

    #[test]
    fn hmac_sign_verify_round_trip() {
        let mac = EnvelopeMac::new(b"shared-room-secret");
        let mut e = env(true);
        mac.sign(&mut e).unwrap();
        assert_eq!(mac.verify(&e, 1000), Ok(()));
    }

    #[test]
    fn hmac_wrong_secret_rejected() {
        let mut e = env(true);
        EnvelopeMac::new(b"issuer-secret").sign(&mut e).unwrap();
        assert_eq!(
            EnvelopeMac::new(b"attacker-secret").verify(&e, 1000),
            Err(EnvelopeAuthError::BadTag)
        );
    }

    #[test]
    fn hmac_tampered_field_rejected() {
        let mac = EnvelopeMac::new(b"s");
        let mut e = env(true);
        mac.sign(&mut e).unwrap();
        // Flip the domain (NID) — must break the tag (domain separation).
        e.network_id_b = 93;
        assert_eq!(mac.verify(&e, 1000), Err(EnvelopeAuthError::BadTag));
    }

    #[test]
    fn hmac_expired_rejected_after_auth() {
        let mac = EnvelopeMac::new(b"s");
        let mut e = env(false); // expiry_unix = 100
        mac.sign(&mut e).unwrap();
        assert_eq!(mac.verify(&e, 1000), Err(EnvelopeAuthError::Expired));
    }

    #[test]
    fn version_mismatch_rejected() {
        let mac = EnvelopeMac::new(b"s");
        let mut e = env(true);
        e.version = ENVELOPE_VERSION + 1;
        mac.sign(&mut e).unwrap();
        assert_eq!(
            mac.verify(&e, 1000),
            Err(EnvelopeAuthError::UnsupportedVersion(ENVELOPE_VERSION + 1))
        );
    }

    // ---- Ed25519 (backend -> Unity) ----

    #[test]
    fn ed25519_sign_verify_round_trip() {
        let signer = BackendSigner::from_seed(&SEED).unwrap();
        let verifier = BackendVerifier::new(signer.public_key_bytes());
        let mut e = env(true);
        signer.sign(&mut e).unwrap();
        assert_eq!(verifier.verify(&e, 1000), Ok(()));
        assert_eq!(e.auth_tag.len(), 64, "Ed25519 signature is 64 bytes");
    }

    #[test]
    fn ed25519_wrong_public_key_rejected() {
        let signer = BackendSigner::from_seed(&SEED).unwrap();
        let other = BackendSigner::from_seed(&[9u8; 32]).unwrap();
        let verifier = BackendVerifier::new(other.public_key_bytes());
        let mut e = env(true);
        signer.sign(&mut e).unwrap();
        assert_eq!(verifier.verify(&e, 1000), Err(EnvelopeAuthError::BadTag));
    }

    #[test]
    fn ed25519_tampered_code_hash_rejected() {
        // A rogue peer cannot swap the approved code for a different payload:
        // the code hash is signed, so any change breaks the signature.
        let signer = BackendSigner::from_seed(&SEED).unwrap();
        let verifier = BackendVerifier::new(signer.public_key_bytes());
        let mut e = env(true);
        signer.sign(&mut e).unwrap();
        e.payload_hash = payload_hash(b"System.IO.File.Delete(...)");
        assert_eq!(verifier.verify(&e, 1000), Err(EnvelopeAuthError::BadTag));
    }

    #[test]
    fn client_mac_does_not_verify_as_backend_signature() {
        // Cross-primitive forgery guard: an HMAC tag is not a valid Ed25519 sig.
        let signer = BackendSigner::from_seed(&SEED).unwrap();
        let verifier = BackendVerifier::new(signer.public_key_bytes());
        let mut e = env(true);
        EnvelopeMac::new(b"leaked-client-secret")
            .sign(&mut e)
            .unwrap();
        assert_eq!(verifier.verify(&e, 1000), Err(EnvelopeAuthError::BadTag));
    }

    // ---- replay / ordering ----

    #[test]
    fn sequence_accepts_increasing_rejects_replay_and_reorder() {
        let mut s = SessionSequence::default();
        assert_eq!(s.accept(1), Ok(()));
        assert_eq!(s.accept(2), Ok(()));
        assert_eq!(s.accept(5), Ok(())); // gaps are fine
        assert_eq!(s.accept(5), Err(EnvelopeAuthError::Replay(5, 5))); // exact replay
        assert_eq!(s.accept(3), Err(EnvelopeAuthError::Replay(3, 5))); // reorder/old
    }

    #[test]
    fn payload_hash_is_deterministic_and_sized() {
        assert_eq!(payload_hash(b"x"), payload_hash(b"x"));
        assert_ne!(payload_hash(b"x"), payload_hash(b"y"));
        assert_eq!(payload_hash(b"x").len(), ENVELOPE_HASH_LEN);
    }

    #[test]
    fn random_32_is_fresh_each_call() {
        let a = random_32().unwrap();
        let b = random_32().unwrap();
        assert_ne!(a, b, "two random keys must differ");
        assert_eq!(a.len(), 32);
    }
}
