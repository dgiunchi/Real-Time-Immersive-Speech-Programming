//! Ubiq-compatible wire protocol for DreamCodeVR+ (Phase 1).
//!
//! Accepts: little-endian length-prefixed frames carrying a [`NetworkId`] and a
//! bounded payload. Rejects (fail-closed): truncated, oversized, or malformed
//! frames. Contains no async, no I/O, and no `unsafe`; it is a pure codec so the
//! one byte-level Ubiq assumption (see `PROJECT.md`) stays isolated.
//!
//! Invariants:
//! * `decode_frame(encode_frame(id, p)) == (NetworkFrame { id, p }, len)` for any
//!   `p` with `p.len() <= MAX_PAYLOAD_LEN` (round-trip; property-tested).
//! * A payload larger than [`MAX_PAYLOAD_LEN`] is never encoded or decoded.

mod envelope;
mod error;
mod frame;
mod network_id;
mod payload;

pub use envelope::{
    AuthEnvelope, ENVELOPE_HASH_LEN, ENVELOPE_VERSION, MAX_ENVELOPE_FIELD_LEN, MAX_TAG_LEN,
};
pub use error::ProtocolError;
pub use frame::{
    decode_frame, encode_frame, DecodedFrame, NetworkFrame, HEADER_LEN, LENGTH_PREFIX_LEN,
    MAX_PAYLOAD_LEN, NETWORK_ID_LEN,
};
pub use network_id::{NetworkId, NID_AUDIO_INPUT, NID_BACKEND_OUTPUT, NID_SELECTED_OBJECT};
pub use payload::{join_peer_payload, split_peer_payload, PeerPayload, PEER_UUID_LEN};
