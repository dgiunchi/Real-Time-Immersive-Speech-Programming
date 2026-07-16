//! Optional AEAD encryption of personalization profiles at rest (Phase 2b).
//!
//! ChaCha20-Poly1305 via the audited `ring`, keyed by an operator-provided 32-byte
//! key (env `DCVR_PROFILE_ENC_KEY`). On-disk encrypted format:
//! `MAGIC || 12-byte nonce || ciphertext+tag`. Files without the magic prefix are
//! plaintext JSON (back-compat), so enabling the key never orphans profiles written
//! before encryption — they are re-encrypted on the next save. Every decrypt error
//! (wrong key, tampered, truncated) fails closed to `None`.

use ring::aead::{Aad, LessSafeKey, Nonce, UnboundKey, CHACHA20_POLY1305, NONCE_LEN};
use ring::rand::{SecureRandom, SystemRandom};

/// Marks an encrypted profile file. Versioned so the scheme can evolve.
pub(crate) const MAGIC: &[u8] = b"DCVRENC1\n";

pub(crate) struct ProfileCipher {
    key: LessSafeKey,
    rng: SystemRandom,
}

impl ProfileCipher {
    /// Build from a raw 32-byte key. `None` if the key is the wrong length.
    pub(crate) fn new(key: &[u8]) -> Option<Self> {
        let unbound = UnboundKey::new(&CHACHA20_POLY1305, key).ok()?;
        Some(Self {
            key: LessSafeKey::new(unbound),
            rng: SystemRandom::new(),
        })
    }

    /// Build from a 64-hex-char key string.
    pub(crate) fn from_hex(hex: &str) -> Option<Self> {
        Self::new(&decode_hex32(hex)?)
    }

    /// Encrypt `plaintext` -> `MAGIC || nonce || ciphertext+tag`.
    pub(crate) fn encrypt(&self, plaintext: &[u8]) -> Option<Vec<u8>> {
        let mut nonce_bytes = [0u8; NONCE_LEN];
        self.rng.fill(&mut nonce_bytes).ok()?;
        let nonce = Nonce::assume_unique_for_key(nonce_bytes);
        let mut in_out = plaintext.to_vec();
        self.key
            .seal_in_place_append_tag(nonce, Aad::empty(), &mut in_out)
            .ok()?;
        let mut out = Vec::with_capacity(MAGIC.len() + NONCE_LEN + in_out.len());
        out.extend_from_slice(MAGIC);
        out.extend_from_slice(&nonce_bytes);
        out.extend_from_slice(&in_out);
        Some(out)
    }

    /// Decrypt `MAGIC || nonce || ciphertext+tag`. `None` on any error (fail-closed).
    pub(crate) fn decrypt(&self, data: &[u8]) -> Option<Vec<u8>> {
        let rest = data.strip_prefix(MAGIC)?;
        if rest.len() < NONCE_LEN {
            return None;
        }
        let (nonce_bytes, ct) = rest.split_at(NONCE_LEN);
        let nonce = Nonce::try_assume_unique_for_key(nonce_bytes).ok()?;
        let mut in_out = ct.to_vec();
        let plain = self
            .key
            .open_in_place(nonce, Aad::empty(), &mut in_out)
            .ok()?;
        Some(plain.to_vec())
    }
}

/// True if a stored blob is in the encrypted format.
pub(crate) fn is_encrypted(data: &[u8]) -> bool {
    data.starts_with(MAGIC)
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
    fn key() -> String {
        "c".repeat(64)
    }

    #[test]
    fn encrypt_decrypt_round_trip_and_hides_plaintext() {
        let c = ProfileCipher::from_hex(&key()).unwrap();
        let pt = br#"{"profile":"secret taste"}"#;
        let ct = c.encrypt(pt).unwrap();
        assert!(is_encrypted(&ct));
        assert!(
            !ct.windows(6).any(|w| w == b"secret"),
            "plaintext must not survive on disk"
        );
        assert_eq!(c.decrypt(&ct).unwrap(), pt);
    }

    #[test]
    fn wrong_key_fails_closed() {
        let a = ProfileCipher::from_hex(&key()).unwrap();
        let b = ProfileCipher::from_hex(&"d".repeat(64)).unwrap();
        let ct = a.encrypt(b"hello").unwrap();
        assert!(b.decrypt(&ct).is_none(), "wrong key must not decrypt");
    }

    #[test]
    fn tampered_ciphertext_fails_closed() {
        let c = ProfileCipher::from_hex(&key()).unwrap();
        let mut ct = c.encrypt(b"hello world").unwrap();
        let n = ct.len();
        ct[n - 1] ^= 0xFF;
        assert!(c.decrypt(&ct).is_none());
    }

    #[test]
    fn bad_key_hex_rejected() {
        assert!(ProfileCipher::from_hex("zz").is_none());
        assert!(ProfileCipher::from_hex(&"a".repeat(63)).is_none());
    }
}
