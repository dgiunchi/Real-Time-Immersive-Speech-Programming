use thiserror::Error;

#[derive(Debug, Error)]
pub enum RoslynError {
    #[error("roslyn analyzer request failed: {0}")]
    Request(String),
    #[error("roslyn analyzer returned status {status}")]
    Status { status: u16 },
    #[error("could not parse roslyn analyzer response: {0}")]
    Parse(String),
    #[error("roslyn analyzer capability unavailable: {0}")]
    Unavailable(String),
}
