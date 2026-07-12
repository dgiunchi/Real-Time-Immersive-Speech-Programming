use thiserror::Error;

#[derive(Debug, Error)]
pub enum SttError {
    #[error("empty audio utterance")]
    EmptyAudio,
    #[error("stt request failed: {0}")]
    Request(String),
    #[error("stt returned status {status}: {body}")]
    Status { status: u16, body: String },
}
