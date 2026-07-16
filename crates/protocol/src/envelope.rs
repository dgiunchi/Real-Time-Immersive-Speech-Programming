//! Authenticated message envelope (Phase 1, spec §10).
//!
//! A versioned, canonically-serialised header that sits *inside* the frame
//! payload (the wire frame in [`crate::frame`] is unchanged). It carries the
//! fields an authenticator needs to prove a message is genuine, fresh, and bound
//! to one request — WITHOUT performing any cryptography itself. This crate stays
//! a pure, dependency-light codec: the SHA-256 body hash, the client→backend
//! HMAC tag, and the backend→Unity Ed25519 signature are computed by the
//! transport/auth layer (which already depends on `ring`) and passed in as
//! bytes.
//!
//! Trust model:
//! * [`AuthEnvelope::signing_input`] returns the canonical bytes of every
//!   authenticated field EXCEPT `auth_tag`. That byte string is what the auth
//!   layer MACs/signs and later verifies, so tampering with any field (including
//!   the `network_id_b` domain or the `payload_hash`) invalidates the tag.
//! * [`AuthEnvelope::to_bytes`] appends the tag; [`AuthEnvelope::from_bytes`]
//!   parses fail-closed (bounded fields, no trailing bytes, never panics).
//!
//! `profile`: `0 = legacy`, `1 = hardened`, `2 = test` — bound into the signed
//! region so a hardened session cannot be silently downgraded on the wire.

use crate::error::ProtocolError;

/// Current envelope wire version. Bumped on any layout change; a verifier binds
/// this into the signed region so an old version cannot be replayed as a new one.
pub const ENVELOPE_VERSION: u16 = 1;

/// Length of the SHA-256 body/code hash carried in the envelope.
pub const ENVELOPE_HASH_LEN: usize = 32;

/// Per-field byte bound for the string fields (fail-closed anti-DoS/anti-abuse).
pub const MAX_ENVELOPE_FIELD_LEN: usize = 256;

/// Upper bound on the auth tag (HMAC-SHA256 = 32, Ed25519 signature = 64; 128
/// leaves head-room for a future longer scheme without unbounded allocation).
pub const MAX_TAG_LEN: usize = 128;

/// Authenticated header for one message. All multi-byte integers are little-endian.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AuthEnvelope {
    /// Wire version ([`ENVELOPE_VERSION`]).
    pub version: u16,
    /// Security profile of the sender: 0 legacy, 1 hardened, 2 test.
    pub profile: u8,
    /// The logical channel / message domain (Ubiq `NetworkId.b`), bound in for
    /// domain separation so a tag for one NID cannot be replayed on another.
    pub network_id_b: u32,
    /// Per-session monotonically-increasing counter (anti-replay / ordering).
    pub sequence_number: u64,
    /// Absolute expiry (unix seconds); a verifier rejects an expired envelope.
    pub expiry_unix: u64,
    /// Session identifier the tag is bound to.
    pub session_id: String,
    /// The proven (not merely asserted) peer identity.
    pub authenticated_peer_id: String,
    /// Correlates the frames of one utterance (selection ↔ audio ↔ code ↔ result).
    pub request_id: String,
    /// Intended recipient; empty string = broadcast / not targeted.
    pub target_peer_id: String,
    /// SHA-256 of the message body (for NID-94 this is the exact code hash).
    pub payload_hash: [u8; ENVELOPE_HASH_LEN],
    /// HMAC tag (client→backend) or Ed25519 signature (backend→Unity) over
    /// [`AuthEnvelope::signing_input`]. Excluded from the signed region itself.
    pub auth_tag: Vec<u8>,
}

fn write_string(out: &mut Vec<u8>, s: &str, field: &'static str) -> Result<(), ProtocolError> {
    if s.len() > MAX_ENVELOPE_FIELD_LEN {
        return Err(ProtocolError::MalformedEnvelope(field));
    }
    // Length fits in u16 because it is <= MAX_ENVELOPE_FIELD_LEN (256).
    out.extend_from_slice(&(s.len() as u16).to_le_bytes());
    out.extend_from_slice(s.as_bytes());
    Ok(())
}

/// Minimal fail-closed cursor over a byte buffer. Never panics; every read is
/// bounds-checked and returns `Incomplete`/`MalformedEnvelope` on shortfall.
struct Cursor<'a> {
    buf: &'a [u8],
    pos: usize,
}

impl<'a> Cursor<'a> {
    fn new(buf: &'a [u8]) -> Self {
        Self { buf, pos: 0 }
    }

    fn take(&mut self, n: usize) -> Result<&'a [u8], ProtocolError> {
        let end = self
            .pos
            .checked_add(n)
            .ok_or(ProtocolError::MalformedEnvelope("length overflow"))?;
        let slice = self
            .buf
            .get(self.pos..end)
            .ok_or(ProtocolError::Incomplete {
                needed: end,
                have: self.buf.len(),
            })?;
        self.pos = end;
        Ok(slice)
    }

    fn u8(&mut self) -> Result<u8, ProtocolError> {
        Ok(self.take(1)?[0])
    }

    fn u16(&mut self) -> Result<u16, ProtocolError> {
        let b: [u8; 2] = self
            .take(2)?
            .try_into()
            .map_err(|_| ProtocolError::MalformedEnvelope("u16"))?;
        Ok(u16::from_le_bytes(b))
    }

    fn u32(&mut self) -> Result<u32, ProtocolError> {
        let b: [u8; 4] = self
            .take(4)?
            .try_into()
            .map_err(|_| ProtocolError::MalformedEnvelope("u32"))?;
        Ok(u32::from_le_bytes(b))
    }

    fn u64(&mut self) -> Result<u64, ProtocolError> {
        let b: [u8; 8] = self
            .take(8)?
            .try_into()
            .map_err(|_| ProtocolError::MalformedEnvelope("u64"))?;
        Ok(u64::from_le_bytes(b))
    }

    fn string(&mut self, field: &'static str) -> Result<String, ProtocolError> {
        let len = self.u16()? as usize;
        if len > MAX_ENVELOPE_FIELD_LEN {
            return Err(ProtocolError::MalformedEnvelope(field));
        }
        let bytes = self.take(len)?;
        String::from_utf8(bytes.to_vec()).map_err(|_| ProtocolError::MalformedEnvelope(field))
    }
}

impl AuthEnvelope {
    /// Canonical bytes of every authenticated field EXCEPT `auth_tag`. This is the
    /// exact input the auth layer MACs/signs and later verifies; deterministic so
    /// the same envelope always produces the same bytes.
    pub fn signing_input(&self) -> Result<Vec<u8>, ProtocolError> {
        let mut out = Vec::with_capacity(64);
        out.extend_from_slice(&self.version.to_le_bytes());
        out.push(self.profile);
        out.extend_from_slice(&self.network_id_b.to_le_bytes());
        out.extend_from_slice(&self.sequence_number.to_le_bytes());
        out.extend_from_slice(&self.expiry_unix.to_le_bytes());
        write_string(&mut out, &self.session_id, "session_id")?;
        write_string(
            &mut out,
            &self.authenticated_peer_id,
            "authenticated_peer_id",
        )?;
        write_string(&mut out, &self.request_id, "request_id")?;
        write_string(&mut out, &self.target_peer_id, "target_peer_id")?;
        out.extend_from_slice(&self.payload_hash);
        Ok(out)
    }

    /// Serialise the full envelope: `signing_input || u16 tag_len || tag`.
    pub fn to_bytes(&self) -> Result<Vec<u8>, ProtocolError> {
        if self.auth_tag.len() > MAX_TAG_LEN {
            return Err(ProtocolError::MalformedEnvelope("auth_tag too long"));
        }
        let mut out = self.signing_input()?;
        out.extend_from_slice(&(self.auth_tag.len() as u16).to_le_bytes());
        out.extend_from_slice(&self.auth_tag);
        Ok(out)
    }

    /// Parse an envelope fail-closed: bounded fields, exact length (no trailing
    /// bytes), never panics.
    pub fn from_bytes(buf: &[u8]) -> Result<Self, ProtocolError> {
        let mut c = Cursor::new(buf);
        let version = c.u16()?;
        let profile = c.u8()?;
        let network_id_b = c.u32()?;
        let sequence_number = c.u64()?;
        let expiry_unix = c.u64()?;
        let session_id = c.string("session_id")?;
        let authenticated_peer_id = c.string("authenticated_peer_id")?;
        let request_id = c.string("request_id")?;
        let target_peer_id = c.string("target_peer_id")?;
        let payload_hash: [u8; ENVELOPE_HASH_LEN] = c
            .take(ENVELOPE_HASH_LEN)?
            .try_into()
            .map_err(|_| ProtocolError::MalformedEnvelope("payload_hash"))?;
        let tag_len = c.u16()? as usize;
        if tag_len > MAX_TAG_LEN {
            return Err(ProtocolError::MalformedEnvelope("auth_tag too long"));
        }
        let auth_tag = c.take(tag_len)?.to_vec();
        // Canonical: exactly one envelope, no extra bytes on the end.
        if c.pos != buf.len() {
            return Err(ProtocolError::MalformedEnvelope("trailing bytes"));
        }
        Ok(Self {
            version,
            profile,
            network_id_b,
            sequence_number,
            expiry_unix,
            session_id,
            authenticated_peer_id,
            request_id,
            target_peer_id,
            payload_hash,
            auth_tag,
        })
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    fn sample() -> AuthEnvelope {
        AuthEnvelope {
            version: ENVELOPE_VERSION,
            profile: 1,
            network_id_b: 94,
            sequence_number: 7,
            expiry_unix: 1_900_000_000,
            session_id: "sess-abc".to_string(),
            authenticated_peer_id: "peer-1".to_string(),
            request_id: "req-42".to_string(),
            target_peer_id: "peer-1".to_string(),
            payload_hash: [0xABu8; ENVELOPE_HASH_LEN],
            auth_tag: vec![0x11; 32],
        }
    }

    #[test]
    fn round_trips() {
        let e = sample();
        let bytes = e.to_bytes().unwrap();
        let back = AuthEnvelope::from_bytes(&bytes).unwrap();
        assert_eq!(e, back);
    }

    #[test]
    fn signing_input_excludes_tag() {
        // Two envelopes identical except for the tag must sign the same bytes,
        // so a verifier's input does not depend on the (attacker-supplied) tag.
        let a = sample();
        let mut b = sample();
        b.auth_tag = vec![0x22; 64];
        assert_eq!(a.signing_input().unwrap(), b.signing_input().unwrap());
    }

    #[test]
    fn tampering_any_authenticated_field_changes_signing_input() {
        let base = sample().signing_input().unwrap();
        let mutate = |f: &dyn Fn(&mut AuthEnvelope)| {
            let mut e = sample();
            f(&mut e);
            e.signing_input().unwrap()
        };
        assert_ne!(base, mutate(&|e| e.version += 1));
        assert_ne!(base, mutate(&|e| e.profile = 0)); // downgrade attempt detected
        assert_ne!(base, mutate(&|e| e.network_id_b = 93)); // domain separation
        assert_ne!(base, mutate(&|e| e.sequence_number += 1)); // replay/reorder
        assert_ne!(base, mutate(&|e| e.expiry_unix += 1));
        assert_ne!(base, mutate(&|e| e.session_id.push('x')));
        assert_ne!(base, mutate(&|e| e.authenticated_peer_id = "peer-2".into()));
        assert_ne!(base, mutate(&|e| e.request_id = "req-99".into()));
        assert_ne!(base, mutate(&|e| e.target_peer_id = "peer-2".into()));
        assert_ne!(base, mutate(&|e| e.payload_hash[0] ^= 0xFF)); // body/code tamper
    }

    #[test]
    fn from_bytes_rejects_truncated() {
        let bytes = sample().to_bytes().unwrap();
        for cut in 0..bytes.len() {
            match AuthEnvelope::from_bytes(&bytes[..cut]) {
                Err(ProtocolError::Incomplete { .. })
                | Err(ProtocolError::MalformedEnvelope(_)) => {}
                other => panic!("truncation at {cut} should fail-closed, got {other:?}"),
            }
        }
    }

    #[test]
    fn from_bytes_rejects_trailing_bytes() {
        let mut bytes = sample().to_bytes().unwrap();
        bytes.push(0x00);
        assert_eq!(
            AuthEnvelope::from_bytes(&bytes),
            Err(ProtocolError::MalformedEnvelope("trailing bytes"))
        );
    }

    #[test]
    fn from_bytes_rejects_oversized_string_len() {
        // Craft a header whose first string (session_id) declares a huge length.
        let mut bytes = Vec::new();
        bytes.extend_from_slice(&ENVELOPE_VERSION.to_le_bytes());
        bytes.push(1);
        bytes.extend_from_slice(&94u32.to_le_bytes());
        bytes.extend_from_slice(&0u64.to_le_bytes());
        bytes.extend_from_slice(&0u64.to_le_bytes());
        bytes.extend_from_slice(&((MAX_ENVELOPE_FIELD_LEN as u16) + 1).to_le_bytes());
        assert_eq!(
            AuthEnvelope::from_bytes(&bytes),
            Err(ProtocolError::MalformedEnvelope("session_id"))
        );
    }

    #[test]
    fn to_bytes_rejects_oversized_field_and_tag() {
        let mut e = sample();
        e.session_id = "x".repeat(MAX_ENVELOPE_FIELD_LEN + 1);
        assert!(matches!(
            e.to_bytes(),
            Err(ProtocolError::MalformedEnvelope("session_id"))
        ));
        let mut e = sample();
        e.auth_tag = vec![0u8; MAX_TAG_LEN + 1];
        assert!(matches!(
            e.to_bytes(),
            Err(ProtocolError::MalformedEnvelope("auth_tag too long"))
        ));
    }
}
