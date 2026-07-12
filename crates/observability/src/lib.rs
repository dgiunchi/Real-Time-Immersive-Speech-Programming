//! Privacy-safe structured observability for DreamCodeVR+ (Phase 1).
//!
//! Produces one [`TimingEvent`] per request as a JSONL line. The event schema is
//! deliberately minimal: ids, timestamps, decision, structured error strings,
//! and counts — never audio, transcripts, biometrics, or secrets. See
//! `docs/SECURITY_MODEL.md` (Privacy).

mod errors;
mod jsonl;
mod timing;

pub use errors::ObservabilityError;
pub use jsonl::JsonlWriter;
pub use timing::{epoch_millis, StageTiming, TimingEvent};
