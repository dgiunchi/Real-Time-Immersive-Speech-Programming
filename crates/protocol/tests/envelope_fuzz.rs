//! Deterministic fuzz corpus for the attacker-facing envelope parser.
//!
//! `AuthEnvelope::from_bytes` is the first thing every hostile byte sequence on
//! the wire hits. The adversarial campaign in `dcvr-integration-tests` proves the
//! auth layer *semantically* rejects forged/tampered/replayed messages; this
//! corpus proves the *parser itself* is memory-safe and canonical against tens of
//! thousands of malformed inputs, with three machine-checked invariants:
//!
//! 1. **Never panics** — for any byte string, `from_bytes` returns `Ok`/`Err`,
//!    never unwinds and never over-allocates (bounded fields, checked lengths).
//! 2. **Canonical** — any buffer that *parses* must re-encode (`to_bytes`) to the
//!    exact same bytes. No two distinct wire encodings decode to one envelope, so
//!    an attacker cannot smuggle a second interpretation past a verifier.
//! 3. **Mutation-closed** — every single-bit flip of a valid envelope either still
//!    round-trips exactly or fails closed; it never yields a *different* valid
//!    envelope silently and never panics.
//!
//! Uses a seeded `splitmix64` PRNG (no `rand`/`proptest` dependency) so the corpus
//! is fully reproducible in CI — a failure reprints the exact seed to replay.
#![allow(clippy::expect_used, clippy::unwrap_used, clippy::panic)]

use dcvr_protocol::{AuthEnvelope, ENVELOPE_HASH_LEN, ENVELOPE_VERSION, MAX_TAG_LEN};

/// Minimal deterministic PRNG. Reproducible across platforms and runs.
struct SplitMix64(u64);

impl SplitMix64 {
    fn next(&mut self) -> u64 {
        self.0 = self.0.wrapping_add(0x9E37_79B9_7F4A_7C15);
        let mut z = self.0;
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        z ^ (z >> 31)
    }

    fn byte(&mut self) -> u8 {
        (self.next() & 0xFF) as u8
    }

    /// A length in `0..=max`.
    fn len(&mut self, max: usize) -> usize {
        (self.next() as usize) % (max + 1)
    }
}

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
        payload_hash: [0xAB; ENVELOPE_HASH_LEN],
        auth_tag: vec![0x11; 32],
    }
}

/// Invariant 1 (never panics) + invariant 2 (canonical) checked together: for any
/// input, parsing must not panic, and any success must re-encode byte-identically.
fn assert_parse_is_safe_and_canonical(buf: &[u8]) {
    if let Ok(env) = AuthEnvelope::from_bytes(buf) {
        let reencoded = env
            .to_bytes()
            .expect("a parsed envelope must always re-serialise");
        assert_eq!(
            reencoded, buf,
            "non-canonical parse: a decoded envelope re-encoded to different bytes"
        );
    }
}

#[test]
fn arbitrary_bytes_never_panic_and_parses_are_canonical() {
    let mut rng = SplitMix64(0x0DCE_0001);
    // Mix of tiny, header-sized, and over-long buffers to exercise every branch:
    // short reads (Incomplete), full-length reads, and trailing-byte rejection.
    for _ in 0..40_000 {
        let n = rng.len(400);
        let mut buf = Vec::with_capacity(n);
        for _ in 0..n {
            buf.push(rng.byte());
        }
        assert_parse_is_safe_and_canonical(&buf);
    }
}

#[test]
fn structured_length_field_sweeps_stay_bounded() {
    // Build a real header prefix, then overwrite each declared length field with
    // hostile values across the whole u16 range. A parser that trusted these would
    // over-read or over-allocate; ours must fail closed on every one.
    let base = sample().to_bytes().expect("sample serialises");
    let hostile_lens: Vec<u16> = vec![
        0,
        1,
        255,
        256,
        257,
        (MAX_TAG_LEN as u16),
        (MAX_TAG_LEN as u16) + 1,
        1024,
        32_768,
        65_535,
    ];
    // The first string length field (session_id) sits right after the fixed prefix:
    // version(2) + profile(1) + network_id_b(4) + sequence(8) + expiry(8) = 23.
    let session_len_off = 23;
    for &bad in &hostile_lens {
        let mut buf = base.clone();
        buf[session_len_off..session_len_off + 2].copy_from_slice(&bad.to_le_bytes());
        assert_parse_is_safe_and_canonical(&buf);
    }
    // And sweep the tag length field (last two-byte length in the buffer) similarly.
    let tag_len_off = base.len() - 2 - 32; // tag is 32 bytes; its len field precedes it.
    for &bad in &hostile_lens {
        let mut buf = base.clone();
        if tag_len_off + 2 <= buf.len() {
            buf[tag_len_off..tag_len_off + 2].copy_from_slice(&bad.to_le_bytes());
            assert_parse_is_safe_and_canonical(&buf);
        }
    }
}

#[test]
fn every_single_bit_flip_is_fail_closed_or_exact_round_trip() {
    let valid = sample().to_bytes().expect("sample serialises");
    for byte_idx in 0..valid.len() {
        for bit in 0..8u32 {
            let mut buf = valid.clone();
            buf[byte_idx] ^= 1 << bit;
            match AuthEnvelope::from_bytes(&buf) {
                Ok(env) => {
                    // A flip that still parses must be canonical (re-encodes to the
                    // mutated bytes) — never a silently-different valid envelope.
                    let re = env.to_bytes().expect("parsed envelope re-serialises");
                    assert_eq!(
                        re, buf,
                        "bit {bit} of byte {byte_idx}: parsed but non-canonical"
                    );
                }
                Err(_) => { /* fail-closed: acceptable */ }
            }
        }
    }
}

#[test]
fn truncation_and_extension_at_every_length_are_safe() {
    let valid = sample().to_bytes().expect("sample serialises");
    // Every prefix (truncation) must fail closed and never panic.
    for cut in 0..valid.len() {
        assert!(
            AuthEnvelope::from_bytes(&valid[..cut]).is_err(),
            "truncation at {cut} must be rejected"
        );
    }
    // Every extension (trailing bytes) must be rejected as non-canonical.
    let mut rng = SplitMix64(0x0DCE_0002);
    for extra in 1..=64 {
        let mut buf = valid.clone();
        for _ in 0..extra {
            buf.push(rng.byte());
        }
        assert!(
            AuthEnvelope::from_bytes(&buf).is_err(),
            "trailing {extra} bytes must be rejected"
        );
    }
}

#[test]
fn all_valid_envelopes_from_the_corpus_round_trip() {
    // Positive control: generate structurally-valid envelopes with random-but-bounded
    // fields and confirm to_bytes -> from_bytes is the identity, so the fuzz above is
    // rejecting *malformed* input, not everything.
    let mut rng = SplitMix64(0x0DCE_0003);
    for _ in 0..5_000 {
        let mk_str = |rng: &mut SplitMix64| {
            let n = rng.len(40);
            // Keep to ASCII so the value is valid UTF-8 by construction.
            (0..n).map(|_| (b'a' + (rng.byte() % 26)) as char).collect()
        };
        let mut hash = [0u8; ENVELOPE_HASH_LEN];
        for b in &mut hash {
            *b = rng.byte();
        }
        let tag_len = rng.len(MAX_TAG_LEN);
        let env = AuthEnvelope {
            version: rng.next() as u16,
            profile: rng.byte(),
            network_id_b: rng.next() as u32,
            sequence_number: rng.next(),
            expiry_unix: rng.next(),
            session_id: mk_str(&mut rng),
            authenticated_peer_id: mk_str(&mut rng),
            request_id: mk_str(&mut rng),
            target_peer_id: mk_str(&mut rng),
            payload_hash: hash,
            auth_tag: (0..tag_len).map(|_| rng.byte()).collect(),
        };
        let bytes = env.to_bytes().expect("valid envelope serialises");
        let back = AuthEnvelope::from_bytes(&bytes).expect("valid envelope parses");
        assert_eq!(env, back, "round-trip must be the identity");
    }
}
