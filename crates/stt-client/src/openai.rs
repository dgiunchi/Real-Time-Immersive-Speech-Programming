use async_trait::async_trait;
use secrecy::{ExposeSecret, SecretString};

use crate::error::SttError;
use crate::types::{AudioUtterance, Transcript};
use crate::SttClient;

/// OpenAI speech-to-text (Whisper / `gpt-4o*-transcribe`): multipart WAV upload to
/// `{base_url}/audio/transcriptions` with bearer auth, over rustls TLS. The API key
/// is held in a `SecretString` and never logged.
#[derive(Debug, Clone)]
pub struct OpenAiSttClient {
    api_key: SecretString,
    model: String,
    base_url: String,
    client: reqwest::Client,
}

impl OpenAiSttClient {
    pub fn new(api_key: SecretString, model: impl Into<String>) -> Self {
        Self {
            api_key,
            model: model.into(),
            base_url: "https://api.openai.com/v1".to_string(),
            client: reqwest::Client::new(),
        }
    }

    pub fn with_base_url(mut self, base_url: impl Into<String>) -> Self {
        self.base_url = base_url.into();
        self
    }
}

#[async_trait]
impl SttClient for OpenAiSttClient {
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
            .text("model", self.model.clone())
            .text("response_format", "json")
            .text("language", "en");

        let resp = self
            .client
            .post(format!("{}/audio/transcriptions", self.base_url))
            .bearer_auth(self.api_key.expose_secret())
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
        // Success body is JSON: {"text": "..."}
        let v: serde_json::Value =
            serde_json::from_str(&body).map_err(|e| SttError::Request(format!("bad json: {e}")))?;
        let text = v
            .get("text")
            .and_then(|t| t.as_str())
            .unwrap_or("")
            .trim()
            .to_string();
        Ok(Transcript { text })
    }
}
