use async_trait::async_trait;

use crate::error::SttError;
use crate::types::{AudioUtterance, Transcript};
use crate::SttClient;

/// Deterministic mock: treats the audio bytes as UTF-8 text, so the Phase-1
/// fake-Quest client (which sends a transcript on NID 98) keeps working with no
/// real STT. Non-UTF-8 bytes are lossily decoded. Offline + reproducible.
#[derive(Debug, Default, Clone)]
pub struct MockSttClient;

#[async_trait]
impl SttClient for MockSttClient {
    async fn transcribe(&self, audio: &AudioUtterance) -> Result<Transcript, SttError> {
        if audio.pcm_s16le.is_empty() {
            return Err(SttError::EmptyAudio);
        }
        Ok(Transcript {
            text: String::from_utf8_lossy(&audio.pcm_s16le).trim().to_string(),
        })
    }
}
