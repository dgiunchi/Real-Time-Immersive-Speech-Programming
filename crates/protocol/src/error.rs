use thiserror::Error;

/// Errors produced by the frame codec.
///
/// Accepts: well-formed Ubiq-like frames within the size bound.
/// Rejects (fail-closed): truncated buffers, oversized declared lengths,
/// and malformed headers. Never panics.
#[derive(Debug, Clone, PartialEq, Eq, Error)]
pub enum ProtocolError {
    /// The buffer does not yet contain a full frame. `needed` is the total
    /// number of bytes required before a frame can be decoded; the caller
    /// should read more bytes and retry (streaming-friendly).
    #[error("incomplete frame: need {needed} bytes, have {have}")]
    Incomplete { needed: usize, have: usize },

    /// The declared payload length exceeds the hard maximum. Fail-closed
    /// bound that prevents a hostile peer from forcing huge allocations.
    #[error("declared payload length {declared} exceeds maximum {max}")]
    PayloadTooLarge { declared: usize, max: usize },

    /// The header bytes could not be interpreted (should be unreachable for
    /// buffers that passed the length checks, but handled rather than panicking).
    #[error("malformed frame header")]
    MalformedHeader,

    /// An authenticated envelope could not be parsed, exceeded a field bound, or
    /// carried trailing bytes. Fail-closed: a message whose envelope is malformed
    /// is rejected, never processed as if it were unauthenticated. The `&'static
    /// str` names the offending field for diagnostics (never attacker-controlled).
    #[error("malformed authenticated envelope: {0}")]
    MalformedEnvelope(&'static str),
}
