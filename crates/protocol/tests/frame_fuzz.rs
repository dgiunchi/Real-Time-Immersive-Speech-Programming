//! Deterministic fuzz corpus for the OUTERMOST wire parser, `decode_frame`.
//!
//! Before any auth check runs, every byte a peer sends passes through
//! `decode_frame` — the `[u32 LE body_len][u32 LE a][u32 LE b][payload]` Ubiq
//! reader. A length-prefixed reader is the classic spot for an integer-overflow
//! or over-read; this corpus proves ours is memory-safe and canonical over tens
//! of thousands of malformed inputs, with invariants tuned to a *streaming*
//! decoder (it decodes one frame from the front and reports `consumed`, so
//! trailing bytes are the next frame, not an error):
//!
//! 1. **Never panics / over-allocates** on arbitrary bytes (bounded by
//!    `MAX_PAYLOAD_LEN`, all reads `checked_add` + `get`).
//! 2. **`consumed` is sane** — on success `HEADER_LEN <= consumed <= buf.len()`.
//! 3. **Canonical prefix** — the decoded frame re-encodes to exactly the
//!    `consumed` bytes it came from; no second wire encoding maps to one frame.
//! 4. **Streaming-stable** — decoding the `consumed`-byte prefix alone yields the
//!    identical frame, so trailing (next-frame) bytes never change this parse.
//!
//! Seeded splitmix64 (no `rand`/`proptest` dependency) → reproducible in CI.
#![allow(clippy::expect_used, clippy::unwrap_used, clippy::panic)]

use dcvr_protocol::{
    decode_frame, encode_frame, NetworkId, HEADER_LEN, LENGTH_PREFIX_LEN, MAX_PAYLOAD_LEN,
    NETWORK_ID_LEN,
};

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
    fn len(&mut self, max: usize) -> usize {
        (self.next() as usize) % (max + 1)
    }
}

/// Invariants 1-4 for one input buffer.
fn assert_frame_decode_is_safe(buf: &[u8]) {
    if let Ok(decoded) = decode_frame(buf) {
        // (2) consumed is within bounds.
        assert!(
            decoded.consumed >= HEADER_LEN && decoded.consumed <= buf.len(),
            "consumed {} out of range for buf len {}",
            decoded.consumed,
            buf.len()
        );
        // (3) canonical: the frame re-encodes to exactly the consumed prefix.
        let re = encode_frame(decoded.frame.network_id, &decoded.frame.payload)
            .expect("a decoded frame must re-encode");
        assert_eq!(
            re,
            &buf[..decoded.consumed],
            "non-canonical frame: re-encode differs from the consumed prefix"
        );
        // (4) streaming-stable: the prefix alone decodes to the identical frame.
        let prefix = decode_frame(&buf[..decoded.consumed]).expect("prefix must re-decode");
        assert_eq!(
            prefix, decoded,
            "trailing bytes changed the parse of the leading frame"
        );
    }
}

#[test]
fn arbitrary_bytes_never_panic_and_decodes_are_canonical() {
    let mut rng = SplitMix64(0x0FBA_0001);
    for _ in 0..40_000 {
        let n = rng.len(600);
        let mut buf = Vec::with_capacity(n);
        for _ in 0..n {
            buf.push(rng.byte());
        }
        assert_frame_decode_is_safe(&buf);
    }
}

#[test]
fn hostile_length_prefix_sweep_stays_bounded() {
    // A valid small frame, then overwrite the 4-byte body_len prefix with hostile
    // values: below the NetworkId floor, above MAX_PAYLOAD, and u32::MAX (overflow
    // bait). Every one must fail closed without allocating gigabytes or panicking.
    let base = encode_frame(NetworkId::new(1, 94), &[0xAA; 16]).expect("frame encodes");
    let hostile: Vec<u32> = vec![
        0,
        1,
        (NETWORK_ID_LEN as u32) - 1,
        NETWORK_ID_LEN as u32, // zero-payload: valid, must decode canonically (empty payload)
        (NETWORK_ID_LEN + MAX_PAYLOAD_LEN) as u32,
        (NETWORK_ID_LEN + MAX_PAYLOAD_LEN) as u32 + 1,
        0x7FFF_FFFF,
        0xFFFF_FFFF,
    ];
    for &bad in &hostile {
        let mut buf = base.clone();
        buf[0..LENGTH_PREFIX_LEN].copy_from_slice(&bad.to_le_bytes());
        // Must not panic; must not claim more bytes than present.
        assert_frame_decode_is_safe(&buf);
        if let Ok(d) = decode_frame(&buf) {
            assert!(d.consumed <= buf.len());
        }
    }
}

#[test]
fn every_single_bit_flip_is_safe() {
    let valid = encode_frame(NetworkId::new(7, 98), &[0x11, 0x22, 0x33, 0x44]).expect("encodes");
    for byte_idx in 0..valid.len() {
        for bit in 0..8u32 {
            let mut buf = valid.clone();
            buf[byte_idx] ^= 1 << bit;
            assert_frame_decode_is_safe(&buf); // never panics; canonical if it parses
        }
    }
}

#[test]
fn truncation_at_every_length_fails_closed() {
    let valid = encode_frame(NetworkId::new(3, 93), &[0xEE; 32]).expect("encodes");
    for cut in 0..valid.len() {
        assert!(
            decode_frame(&valid[..cut]).is_err(),
            "a truncated frame (cut {cut}) must be Incomplete/Malformed, never Ok"
        );
    }
}

#[test]
fn concatenated_frames_decode_one_at_a_time() {
    // Positive control + stream reassembly: encode N random valid frames back to
    // back, then peel them off using `consumed`. Proves the fuzz above rejects
    // malformed input rather than everything, and that the streaming contract holds.
    let mut rng = SplitMix64(0x0FBA_0002);
    for _ in 0..2_000 {
        let count = 1 + rng.len(4);
        let mut frames = Vec::with_capacity(count);
        let mut stream = Vec::new();
        for _ in 0..count {
            let plen = rng.len(64);
            let payload: Vec<u8> = (0..plen).map(|_| rng.byte()).collect();
            let id = NetworkId::new(rng.next() as u32, rng.next() as u32);
            stream.extend_from_slice(&encode_frame(id, &payload).expect("encodes"));
            frames.push(NetworkId::new(id.a, id.b));
            frames.push(NetworkId::new(payload.len() as u32, 0)); // stash len for check
        }
        // Peel frames off the front using `consumed`.
        let mut rest = stream.as_slice();
        let mut i = 0;
        while !rest.is_empty() {
            let d = decode_frame(rest).expect("each concatenated frame decodes");
            let expected_id = frames[i];
            let expected_len = frames[i + 1].a as usize;
            assert_eq!(d.frame.network_id, expected_id, "frame id mismatch");
            assert_eq!(
                d.frame.payload.len(),
                expected_len,
                "frame payload len mismatch"
            );
            rest = &rest[d.consumed..];
            i += 2;
        }
        assert_eq!(
            i,
            frames.len(),
            "did not consume exactly the frames encoded"
        );
    }
}
