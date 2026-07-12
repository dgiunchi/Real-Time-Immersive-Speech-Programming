use thiserror::Error;

/// Errors from session/router bookkeeping. Transition errors are advisory in
/// Phase 1 (logged, not fatal) so a bookkeeping glitch never drops a request.
#[derive(Debug, Clone, PartialEq, Eq, Error)]
pub enum RouterError {
    #[error("invalid session transition from {from} to {to}")]
    InvalidTransition { from: String, to: String },
}
