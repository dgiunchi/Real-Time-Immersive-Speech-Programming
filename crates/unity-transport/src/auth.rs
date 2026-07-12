//! Per-peer admission tokens — the defence against Casey et al.'s (2021)
//! "Man-in-the-Room" attack, where an unauthenticated peer joins the shared room
//! and injects/poisons traffic (e.g. NID 95 feedback, spoofed NID 94 output).
//!
//! A trusted issuer (the room owner / backend) hands each legitimate peer a token
//! `"{expiry_unix}.{hex(HMAC_SHA256(secret, "peer_id|expiry"))}"`. The backend
//! verifies the token (constant-time, via `ring`) before processing that peer's
//! requests, so a rogue peer that merely joined the RoomServer cannot drive the
//! pipeline. This is admission control at the application layer; transport
//! encryption (TLS) is the complementary confidentiality layer (deployment step).
//!
//! Config-gated and OFF by default (`require_peer_auth = false`) for back-compat
//! and local/offline use, so it never gets in the way of single-user creation.

use ring::hmac;

/// Why a token was rejected.
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
pub enum AuthError {
    #[error("token is malformed")]
    Malformed,
    #[error("token has expired")]
    Expired,
    #[error("token signature is invalid (unauthenticated peer)")]
    BadSignature,
}

/// Issues and verifies per-peer admission tokens with a shared HMAC-SHA256 secret.
pub struct PeerAuthenticator {
    key: hmac::Key,
}

impl PeerAuthenticator {
    /// Construct from a shared secret (e.g. `DCVR_PEER_AUTH_SECRET`). The secret
    /// must be distributed only to legitimate peers.
    pub fn new(secret: &[u8]) -> Self {
        Self {
            key: hmac::Key::new(hmac::HMAC_SHA256, secret),
        }
    }

    fn message(peer_id: &str, expiry_unix: u64) -> String {
        format!("{peer_id}|{expiry_unix}")
    }

    /// Issue a token for `peer_id`, valid until `expiry_unix` (seconds since epoch).
    pub fn issue(&self, peer_id: &str, expiry_unix: u64) -> String {
        let tag = hmac::sign(&self.key, Self::message(peer_id, expiry_unix).as_bytes());
        format!("{expiry_unix}.{}", to_hex(tag.as_ref()))
    }

    /// Verify `token` for `peer_id` at `now_unix`. Constant-time authenticity check.
    /// Returns `Ok(())` only if the token is well-formed, authentic, and unexpired.
    pub fn verify(&self, peer_id: &str, token: &str, now_unix: u64) -> Result<(), AuthError> {
        let (exp_str, hex_tag) = token.split_once('.').ok_or(AuthError::Malformed)?;
        let expiry_unix: u64 = exp_str.parse().map_err(|_| AuthError::Malformed)?;
        let tag_bytes = from_hex(hex_tag).ok_or(AuthError::Malformed)?;
        // Authenticity (constant-time) before expiry, so a forged token is rejected
        // as BadSignature regardless of its claimed expiry.
        hmac::verify(
            &self.key,
            Self::message(peer_id, expiry_unix).as_bytes(),
            &tag_bytes,
        )
        .map_err(|_| AuthError::BadSignature)?;
        if now_unix > expiry_unix {
            return Err(AuthError::Expired);
        }
        Ok(())
    }
}

const HEX: &[u8; 16] = b"0123456789abcdef";

fn to_hex(bytes: &[u8]) -> String {
    let mut s = String::with_capacity(bytes.len() * 2);
    for &b in bytes {
        s.push(char::from(HEX[(b >> 4) as usize]));
        s.push(char::from(HEX[(b & 0x0f) as usize]));
    }
    s
}

fn from_hex(s: &str) -> Option<Vec<u8>> {
    let bytes = s.as_bytes();
    if bytes.is_empty() || !bytes.len().is_multiple_of(2) {
        return None;
    }
    let mut out = Vec::with_capacity(bytes.len() / 2);
    let mut i = 0;
    while i + 1 < bytes.len() {
        let hi = hex_val(bytes[i])?;
        let lo = hex_val(bytes[i + 1])?;
        out.push((hi << 4) | lo);
        i += 2;
    }
    Some(out)
}

fn hex_val(c: u8) -> Option<u8> {
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

    const SECRET: &[u8] = b"shared-room-secret-32-bytes-long!";

    #[test]
    fn valid_token_round_trips() {
        let a = PeerAuthenticator::new(SECRET);
        let t = a.issue("peer-1", 1000);
        assert_eq!(a.verify("peer-1", &t, 999), Ok(()));
        assert_eq!(a.verify("peer-1", &t, 1000), Ok(()));
    }

    #[test]
    fn expired_token_rejected() {
        let a = PeerAuthenticator::new(SECRET);
        let t = a.issue("peer-1", 1000);
        assert_eq!(a.verify("peer-1", &t, 1001), Err(AuthError::Expired));
    }

    #[test]
    fn token_for_another_peer_rejected() {
        // The Man-in-the-Room peer cannot reuse peer-1's token as peer-2.
        let a = PeerAuthenticator::new(SECRET);
        let t = a.issue("peer-1", 1000);
        assert_eq!(a.verify("peer-2", &t, 999), Err(AuthError::BadSignature));
    }

    #[test]
    fn tampered_token_rejected() {
        let a = PeerAuthenticator::new(SECRET);
        let mut t = a.issue("peer-1", 1000);
        // Flip the last hex nibble.
        let last = t.pop().unwrap();
        t.push(if last == 'a' { 'b' } else { 'a' });
        assert_eq!(a.verify("peer-1", &t, 999), Err(AuthError::BadSignature));
    }

    #[test]
    fn wrong_secret_rejected() {
        let issuer = PeerAuthenticator::new(SECRET);
        let attacker = PeerAuthenticator::new(b"a-different-secret-the-rogue-has");
        let t = issuer.issue("peer-1", 1000);
        assert_eq!(
            attacker.verify("peer-1", &t, 999),
            Err(AuthError::BadSignature)
        );
    }

    #[test]
    fn malformed_token_rejected() {
        let a = PeerAuthenticator::new(SECRET);
        assert_eq!(
            a.verify("peer-1", "not-a-token", 1),
            Err(AuthError::Malformed)
        );
        assert_eq!(a.verify("peer-1", "1000.zz", 1), Err(AuthError::Malformed));
        assert_eq!(
            a.verify("peer-1", "abc.deadbeef", 1),
            Err(AuthError::Malformed)
        );
    }
}
