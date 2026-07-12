use thiserror::Error;

#[derive(Debug, Error)]
pub enum LlmError {
    #[error("llm request failed: {0}")]
    Request(String),
    #[error("llm returned status {status}: {body}")]
    Status { status: u16, body: String },
    #[error("could not parse llm output into an action plan: {0}")]
    Parse(String),
    #[error("llm returned an empty response")]
    EmptyResponse,
}
