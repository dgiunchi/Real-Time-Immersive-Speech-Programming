//! Protocol codec tests (required 1-5) + a GOLDEN real-Ubiq wire-byte test and a
//! round-trip property test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_protocol::{
    decode_frame, encode_frame, NetworkId, ProtocolError, HEADER_LEN, LENGTH_PREFIX_LEN,
    MAX_PAYLOAD_LEN, NID_AUDIO_INPUT, NID_BACKEND_OUTPUT, NID_SELECTED_OBJECT,
};
use proptest::prelude::*;

// Test 1: a valid frame parses.
#[test]
fn valid_frame_parses() {
    let payload = b"hello world";
    let bytes = encode_frame(NID_AUDIO_INPUT, payload).unwrap();
    let decoded = decode_frame(&bytes).unwrap();
    assert_eq!(decoded.frame.network_id, NID_AUDIO_INPUT);
    assert_eq!(decoded.frame.payload, payload);
    assert_eq!(decoded.consumed, bytes.len());
}

// Test 2: a short frame is rejected as Incomplete.
#[test]
fn short_frame_rejected() {
    let err = decode_frame(&[0u8; 3]).unwrap_err();
    assert!(matches!(err, ProtocolError::Incomplete { .. }));
}

// Test 3: length/payload mismatch + oversized + body-too-small are rejected.
#[test]
fn length_mismatch_rejected() {
    // body_len declares 100 but only NID(8)+2 bytes present -> Incomplete (need 4+100).
    let mut bytes = Vec::new();
    bytes.extend_from_slice(&100u32.to_le_bytes()); // body_len
    bytes.extend_from_slice(&0u32.to_le_bytes()); // a
    bytes.extend_from_slice(&98u32.to_le_bytes()); // b
    bytes.extend_from_slice(&[1, 2]);
    let err = decode_frame(&bytes).unwrap_err();
    assert!(
        matches!(err, ProtocolError::Incomplete { needed, .. } if needed == LENGTH_PREFIX_LEN + 100)
    );

    // body_len < 8 (cannot even contain the NetworkId) -> MalformedHeader.
    let mut tiny = Vec::new();
    tiny.extend_from_slice(&4u32.to_le_bytes());
    tiny.extend_from_slice(&[0, 0, 0, 0]);
    assert!(matches!(
        decode_frame(&tiny).unwrap_err(),
        ProtocolError::MalformedHeader
    ));

    // payload larger than the bound -> PayloadTooLarge.
    let mut huge = Vec::new();
    huge.extend_from_slice(&((MAX_PAYLOAD_LEN + 9) as u32).to_le_bytes()); // payload = MAX+1
    huge.extend_from_slice(&0u32.to_le_bytes());
    huge.extend_from_slice(&94u32.to_le_bytes());
    assert!(matches!(
        decode_frame(&huge).unwrap_err(),
        ProtocolError::PayloadTooLarge { .. }
    ));
}

// Test 4: NID constants are correct.
#[test]
fn nid_constants_are_correct() {
    assert_eq!(NID_AUDIO_INPUT, NetworkId::new(0, 98));
    assert_eq!(NID_SELECTED_OBJECT, NetworkId::new(0, 93));
    assert_eq!(NID_BACKEND_OUTPUT, NetworkId::new(0, 94));
}

// Test 5: encode/decode round-trip works.
#[test]
fn encode_decode_roundtrip() {
    for payload in [&b""[..], &b"x"[..], &b"make this cube red"[..]] {
        let bytes = encode_frame(NID_BACKEND_OUTPUT, payload).unwrap();
        let decoded = decode_frame(&bytes).unwrap();
        assert_eq!(decoded.frame.payload, payload);
        assert_eq!(decoded.frame.network_id, NID_BACKEND_OUTPUT);
    }
}

// GOLDEN: exact bytes match the real Ubiq wire format (length = 8 + payload).
// NID 98, payload "AB" -> [0A 00 00 00][00 00 00 00][62 00 00 00][41 42].
#[test]
fn golden_ubiq_wire_bytes() {
    let bytes = encode_frame(NID_AUDIO_INPUT, b"AB").unwrap();
    assert_eq!(
        bytes,
        vec![
            0x0A, 0x00, 0x00, 0x00, // body_len = 8 + 2 = 10
            0x00, 0x00, 0x00, 0x00, // a = 0
            0x62, 0x00, 0x00, 0x00, // b = 98
            0x41, 0x42, // "AB"
        ]
    );
    assert_eq!(HEADER_LEN, 12);
}

proptest! {
    #[test]
    fn prop_roundtrip(a in any::<u32>(), b in any::<u32>(), payload in proptest::collection::vec(any::<u8>(), 0..2048)) {
        let id = NetworkId::new(a, b);
        let bytes = encode_frame(id, &payload).unwrap();
        let decoded = decode_frame(&bytes).unwrap();
        prop_assert_eq!(decoded.frame.network_id, id);
        prop_assert_eq!(decoded.frame.payload, payload);
        prop_assert_eq!(decoded.consumed, bytes.len());
    }
}
