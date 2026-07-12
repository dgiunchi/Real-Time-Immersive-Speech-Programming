use std::sync::Arc;

use async_trait::async_trait;

use crate::error::SttError;
use crate::types::{AudioUtterance, Transcript};
use crate::SttClient;

/// Wraps a real STT client but short-circuits when the NID-98 "audio" is actually a
/// **typed text command** (the desktop/Unity demo sends typed commands as the audio
/// payload). Real 16 kHz PCM is not short, valid, printable UTF-8, so it falls
/// through to the wrapped client (e.g. Whisper). This lets the same demo TYPE or
/// SPEAK with `DCVR_STT_OPENAI=true` — typed text never hits Whisper.
pub struct SmartSttClient {
    inner: Arc<dyn SttClient>,
}

impl SmartSttClient {
    pub fn new(inner: Arc<dyn SttClient>) -> Self {
        Self { inner }
    }

    /// True if `bytes` look like a short typed command rather than PCM audio.
    fn looks_like_text(bytes: &[u8]) -> Option<String> {
        if bytes.is_empty() || bytes.len() >= 512 {
            return None; // real push-to-talk PCM is far larger than any typed command
        }
        let text = std::str::from_utf8(bytes).ok()?;
        let t = text.trim();
        if t.is_empty() {
            return None;
        }
        let printable = t.chars().all(|c| c == ' ' || !c.is_control());
        let has_alpha = t.chars().any(|c| c.is_alphabetic());
        if printable && has_alpha {
            Some(t.to_string())
        } else {
            None
        }
    }
}

#[async_trait]
impl SttClient for SmartSttClient {
    async fn transcribe(&self, audio: &AudioUtterance) -> Result<Transcript, SttError> {
        if let Some(text) = Self::looks_like_text(&audio.pcm_s16le) {
            return Ok(Transcript { text });
        }
        self.inner.transcribe(audio).await
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    #[test]
    fn detects_typed_text() {
        assert_eq!(
            SmartSttClient::looks_like_text(b"make a small house"),
            Some("make a small house".to_string())
        );
        // empty / control-only / non-utf8 / huge -> not text (goes to real STT)
        assert_eq!(SmartSttClient::looks_like_text(b""), None);
        assert_eq!(
            SmartSttClient::looks_like_text(&[0x00, 0x01, 0x02, 0x80, 0x81]),
            None
        );
        assert_eq!(SmartSttClient::looks_like_text(&vec![b'a'; 600]), None);
    }
}
