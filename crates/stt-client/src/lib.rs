//! Speech-to-text client abstraction for DreamCodeVR+ (Phase 2).
//!
//! [`SttClient`] is the seam: [`MockSttClient`] (offline, deterministic) is the
//! default for tests and keyless local demos; [`HttpSttClient`] posts a WAV to a
//! faster-whisper HTTP endpoint over rustls TLS. No endpoint is hardcoded.

mod error;
mod http;
mod mock;
mod openai;
mod smart;
mod types;

pub use error::SttError;
pub use http::HttpSttClient;
pub use mock::MockSttClient;
pub use openai::OpenAiSttClient;
pub use smart::SmartSttClient;
pub use types::{AudioUtterance, Transcript};

use async_trait::async_trait;

#[async_trait]
pub trait SttClient: Send + Sync {
    /// Transcribe one push-to-talk utterance.
    async fn transcribe(&self, audio: &AudioUtterance) -> Result<Transcript, SttError>;
}
