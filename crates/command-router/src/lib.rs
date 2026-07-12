//! Per-peer sessions and orchestration for DreamCodeVR+.
//!
//! Phase 1: `Router::process_transcript` (sync, deterministic mock). Phase 2:
//! `Router::process_audio` (async STT -> LLM -> validate, each bounded by a
//! timeout, fail-closed). Each peer has independent state in a map owned by
//! [`Router`] — there is no global lock, so one peer can never block another.

mod errors;
pub mod mock;
mod request;
pub mod reset;
mod router;
mod session;

pub use errors::RouterError;
pub use request::{Mode, RequestId};
pub use router::{AudioOutcome, CsharpResult, Router, RouterOutcome};
pub use session::{AdmitDecision, PeerSession, SessionState};
