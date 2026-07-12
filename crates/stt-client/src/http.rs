use async_trait::async_trait;

use crate::error::SttError;
use crate::types::{AudioUtterance, Transcript};
use crate::SttClient;

/// faster-whisper HTTP client: multipart WAV upload over rustls TLS. The default
/// endpoint is injected from config (no hardcoded IP here).
#[derive(Debug, Clone)]
pub struct HttpSttClient {
    url: String,
    client: reqwest::Client,
}

impl HttpSttClient {
    pub fn new(url: impl Into<String>) -> Self {
        Self {
            url: url.into(),
            client: reqwest::Client::new(),
        }
    }

    pub fn with_client(url: impl Into<String>, client: reqwest::Client) -> Self {
        Self {
            url: url.into(),
            client,
        }
    }
}

#[async_trait]
impl SttClient for HttpSttClient {
    async fn transcribe(&self, audio: &AudioUtterance) -> Result<Transcript, SttError> {
        if audio.pcm_s16le.is_empty() {
            return Err(SttError::EmptyAudio);
        }
        let part = reqwest::multipart::Part::bytes(audio.to_wav())
            .file_name("utterance.wav")
            .mime_str("audio/wav")
            .map_err(|e| SttError::Request(e.to_string()))?;
        let form = reqwest::multipart::Form::new()
            .part("file", part)
            .text("language", "en")
            .text("beam_size", "1");

        let resp = self
            .client
            .post(&self.url)
            .multipart(form)
            .send()
            .await
            .map_err(|e| SttError::Request(e.to_string()))?;

        let status = resp.status();
        let body = resp
            .text()
            .await
            .map_err(|e| SttError::Request(e.to_string()))?;
        if !status.is_success() {
            return Err(SttError::Status {
                status: status.as_u16(),
                body,
            });
        }
        Ok(Transcript {
            text: body.trim().to_string(),
        })
    }
}
