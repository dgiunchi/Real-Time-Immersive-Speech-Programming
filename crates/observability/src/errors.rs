use thiserror::Error;

/// Errors writing JSONL timing output.
#[derive(Debug, Error)]
pub enum ObservabilityError {
    #[error("failed to serialize timing event: {0}")]
    Serialize(String),
    #[error("io error writing jsonl: {0}")]
    Io(#[from] std::io::Error),
}
