//! `dcvr-roomserver`: a minimal, Ubiq-compatible RoomServer implemented in Rust.
//!
//! Ubiq clients (and our own Rust service peer) rendezvous by connecting to a
//! RoomServer over TCP and exchanging `{type, args}` control messages on the
//! reserved channel `NetworkId {0,1}`; the server assigns them to a *room* and
//! then relays every other frame between the members of that room. Upstream
//! reference: UCL-VR Ubiq `Node/modules/roomserver.js` (Apache-2.0).
//!
//! This crate is the pure, I/O-free protocol + room state machine
//! ([`RoomServer`]): given a control frame or an application frame from a
//! connection, it mutates its room/peer bookkeeping and returns the frames to
//! send back out, each addressed to a connection id. The async TCP transport
//! that owns the sockets and drives this state machine lives in the server
//! binary (Phase 2), so this layer stays deterministic and unit-testable.

pub mod message;
mod state;

pub use crate::state::{ConnId, Outbound, RoomServer, ROOMSERVER_ID};

/// Errors from parsing a control message or encoding an outbound frame.
#[derive(Debug, thiserror::Error)]
pub enum RoomError {
    /// A control message (or its stringified `args`) was not valid JSON.
    #[error("control message JSON error: {0}")]
    Json(#[from] serde_json::Error),
    /// The frame codec rejected an outbound frame (e.g. payload too large).
    #[error("frame codec error: {0}")]
    Protocol(#[from] dcvr_protocol::ProtocolError),
    /// A reply was addressed to a connection the server does not know.
    #[error("unknown connection {0}")]
    UnknownConn(ConnId),
}
