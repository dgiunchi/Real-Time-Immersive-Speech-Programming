use thiserror::Error;

/// Errors from (de)serialising an [`crate::ActionPlan`]. Parsing is delegated to
/// `serde_json`; its messages are wrapped into a typed, fail-closed error so the
/// rest of the system never sees a raw panic or an `unwrap`.
#[derive(Debug, Clone, PartialEq, Eq, Error)]
pub enum DslError {
    #[error("malformed action plan json: {0}")]
    Malformed(String),
    #[error("failed to serialize action plan: {0}")]
    Serialize(String),
}
