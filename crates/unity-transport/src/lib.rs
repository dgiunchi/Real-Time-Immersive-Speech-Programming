//! Ubiq-compatible service peer for DreamCodeVR+ (the real-Ubiq transport).
//!
//! Lets the Rust backend participate in a Ubiq room exactly like the Node Genie
//! app: connect to the RoomServer, JOIN the room (NID 1 control), then receive
//! NID 98 audio + send NID 94 output. Wire format is `dcvr-protocol` (verified
//! byte-for-byte against `com.ucl.ubiq`). The Unity client and the RoomServer are
//! unchanged; this peer drops in beside them.

mod auth;
mod envelope_auth;
mod error;
mod ids;
mod peer;

pub use auth::{AuthError, PeerAuthenticator};
pub use envelope_auth::{
    payload_hash, BackendSigner, BackendVerifier, EnvelopeAuthError, EnvelopeMac, SessionSequence,
};
pub use error::TransportError;
pub use ids::{NetworkIdJson, PeerInfo};
pub use peer::{PeerSender, UbiqServicePeer};
