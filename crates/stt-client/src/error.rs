use thiserror::Error;

#[derive(Debug, Error)]
pub enum SttError {
    #[error("empty audio utterance")]
    EmptyAudio,
    #[error("stt request failed: {0}")]
    Request(String),
    #[error("stt returned status {status}: {body}")]
    Status { status: u16, body: String },
    /// The audio failed the hardened-profile input bounds (size / format / duration).
    /// A distinct, observable variant so an anti-injection reject is not confused with
    /// a backend outage. The router still maps any `Err` to a fail-closed reject.
    #[error("audio out of bounds: {0}")]
    AudioOutOfBounds(String),
}
