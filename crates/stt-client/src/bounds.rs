//! Hardened-profile audio-input bounds for the STT trust boundary.
//!
//! Every NID-98 push-to-talk buffer is attacker-controlled. In the default `legacy`
//! profile the only checks are "non-empty" + the 8 MiB accumulation cap. The
//! `hardened` profile additionally validates the audio against [`AudioBounds`] before
//! it ever reaches a (possibly paid, possibly slow) STT backend: reject sub-frame
//! noise probes, multi-minute buffers that inflate latency/cost, and payloads whose
//! declared format is not the expected 16 kHz / mono / 16-bit PCM.
//!
//! This module is purely additive: nothing on the legacy path constructs a
//! [`BoundedSttClient`], so legacy behaviour is byte-identical. The decorator is
//! installed only under the hardened profile, and it MUST wrap the real client while
//! being wrapped BY [`crate::SmartSttClient`] — i.e. `Smart(Bounded(real))` — so a
//! short typed demo command still short-circuits in `SmartSttClient` before any audio
//! bound is applied.

use std::sync::Arc;

use async_trait::async_trait;

use crate::error::SttError;
use crate::types::{AudioUtterance, Transcript};
use crate::SttClient;

/// Bounds applied to one push-to-talk utterance under the hardened profile.
#[derive(Debug, Clone)]
pub struct AudioBounds {
    /// Reject buffers smaller than this (sub-frame noise / probe packets). Well below
    /// any real spoken command (16 kHz mono 16-bit ≈ 32 000 bytes/s).
    pub min_bytes: usize,
    /// Reject buffers larger than this (matches the server's 8 MiB accumulation cap).
    pub max_bytes: usize,
    /// The sample rates we accept (the original Unity client sends 16 kHz).
    pub allowed_sample_rates: Vec<u32>,
    /// Reject audio whose derived duration exceeds this (anti-DoS / cost guard).
    pub max_duration_ms: u64,
}

impl Default for AudioBounds {
    fn default() -> Self {
        Self {
            min_bytes: 320,             // ~10 ms at 16 kHz mono 16-bit
            max_bytes: 8 * 1024 * 1024, // mirrors MAX_UTTERANCE_BYTES on the server
            allowed_sample_rates: vec![16_000],
            max_duration_ms: 30_000, // 30 s of speech is a generous single utterance
        }
    }
}

impl AudioBounds {
    /// Validate an utterance fail-closed. Pure and total (never panics); returns a
    /// distinct [`SttError::AudioOutOfBounds`] describing the first violation.
    pub fn validate(&self, a: &AudioUtterance) -> Result<(), SttError> {
        let n = a.pcm_s16le.len();
        if n == 0 {
            return Err(SttError::EmptyAudio);
        }
        if n < self.min_bytes {
            return Err(SttError::AudioOutOfBounds(format!(
                "{n} bytes < min {}",
                self.min_bytes
            )));
        }
        if n > self.max_bytes {
            return Err(SttError::AudioOutOfBounds(format!(
                "{n} bytes > max {}",
                self.max_bytes
            )));
        }
        if !self.allowed_sample_rates.contains(&a.sample_rate) {
            return Err(SttError::AudioOutOfBounds(format!(
                "sample_rate {} not allowed",
                a.sample_rate
            )));
        }
        if a.channels != 1 {
            return Err(SttError::AudioOutOfBounds(format!(
                "channels {} != 1 (mono expected)",
                a.channels
            )));
        }
        if a.bits_per_sample != 16 {
            return Err(SttError::AudioOutOfBounds(format!(
                "bits_per_sample {} != 16",
                a.bits_per_sample
            )));
        }
        // Derived duration (ms). bytes_per_sample >= 1 by the check above (bits==16);
        // checked_div guards the (unreachable) zero-denominator case fail-safe.
        let bytes_per_sample = u64::from(a.bits_per_sample) / 8;
        let denom = u64::from(a.sample_rate) * u64::from(a.channels) * bytes_per_sample.max(1);
        let duration_ms = (n as u64)
            .saturating_mul(1000)
            .checked_div(denom)
            .unwrap_or(0);
        if duration_ms > self.max_duration_ms {
            return Err(SttError::AudioOutOfBounds(format!(
                "duration {duration_ms} ms > max {} ms",
                self.max_duration_ms
            )));
        }
        Ok(())
    }
}

/// Hardened-profile decorator: validates [`AudioBounds`] before delegating to the
/// real STT client. Legacy never constructs this, so it is a pure opt-in gate.
pub struct BoundedSttClient {
    inner: Arc<dyn SttClient>,
    bounds: AudioBounds,
}

impl BoundedSttClient {
    pub fn new(inner: Arc<dyn SttClient>, bounds: AudioBounds) -> Self {
        Self { inner, bounds }
    }
}

#[async_trait]
impl SttClient for BoundedSttClient {
    async fn transcribe(&self, audio: &AudioUtterance) -> Result<Transcript, SttError> {
        // Pure, synchronous bounds check BEFORE the (awaited) network call — so a
        // rejected utterance never touches the backend, and no lock is held across it.
        self.bounds.validate(audio)?;
        self.inner.transcribe(audio).await
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    // A stub that records whether the inner client was reached, so we can prove a
    // rejected utterance never delegates.
    struct SpyStt {
        reached: std::sync::atomic::AtomicBool,
    }
    #[async_trait]
    impl SttClient for SpyStt {
        async fn transcribe(&self, _a: &AudioUtterance) -> Result<Transcript, SttError> {
            self.reached
                .store(true, std::sync::atomic::Ordering::SeqCst);
            Ok(Transcript {
                text: "ok".to_string(),
            })
        }
    }

    fn valid_1s() -> AudioUtterance {
        // 1 second of 16 kHz mono 16-bit = 32 000 bytes.
        AudioUtterance::new_16k_mono(vec![0u8; 32_000])
    }

    #[test]
    fn accepts_a_normal_utterance() {
        assert!(AudioBounds::default().validate(&valid_1s()).is_ok());
    }

    #[test]
    fn rejects_empty_and_sub_frame() {
        let b = AudioBounds::default();
        assert!(matches!(
            b.validate(&AudioUtterance::new_16k_mono(vec![])),
            Err(SttError::EmptyAudio)
        ));
        assert!(matches!(
            b.validate(&AudioUtterance::new_16k_mono(vec![0u8; 8])),
            Err(SttError::AudioOutOfBounds(_))
        ));
    }

    #[test]
    fn rejects_oversized_and_over_long() {
        let b = AudioBounds::default();
        // > 8 MiB
        assert!(matches!(
            b.validate(&AudioUtterance::new_16k_mono(vec![
                0u8;
                8 * 1024 * 1024 + 1
            ])),
            Err(SttError::AudioOutOfBounds(_))
        ));
        // 40 s of audio at 16 kHz mono 16-bit = 1 280 000 bytes < 8 MiB, but too long.
        assert!(matches!(
            b.validate(&AudioUtterance::new_16k_mono(vec![0u8; 40 * 32_000])),
            Err(SttError::AudioOutOfBounds(_))
        ));
    }

    #[test]
    fn rejects_wrong_format() {
        let b = AudioBounds::default();
        let mut a = valid_1s();
        a.sample_rate = 44_100;
        assert!(matches!(b.validate(&a), Err(SttError::AudioOutOfBounds(_))));
        let mut a = valid_1s();
        a.channels = 2;
        assert!(matches!(b.validate(&a), Err(SttError::AudioOutOfBounds(_))));
        let mut a = valid_1s();
        a.bits_per_sample = 8;
        assert!(matches!(b.validate(&a), Err(SttError::AudioOutOfBounds(_))));
    }

    #[tokio::test]
    async fn decorator_rejects_before_delegating() {
        let spy = Arc::new(SpyStt {
            reached: std::sync::atomic::AtomicBool::new(false),
        });
        let bounded = BoundedSttClient::new(spy.clone(), AudioBounds::default());
        // Over-long audio must be rejected WITHOUT reaching the inner client.
        let err = bounded
            .transcribe(&AudioUtterance::new_16k_mono(vec![0u8; 40 * 32_000]))
            .await;
        assert!(matches!(err, Err(SttError::AudioOutOfBounds(_))));
        assert!(
            !spy.reached.load(std::sync::atomic::Ordering::SeqCst),
            "a rejected utterance must never reach the backend"
        );
    }

    #[tokio::test]
    async fn decorator_delegates_a_valid_utterance() {
        let spy = Arc::new(SpyStt {
            reached: std::sync::atomic::AtomicBool::new(false),
        });
        let bounded = BoundedSttClient::new(spy.clone(), AudioBounds::default());
        let out = bounded.transcribe(&valid_1s()).await.unwrap();
        assert_eq!(out.text, "ok");
        assert!(spy.reached.load(std::sync::atomic::Ordering::SeqCst));
    }
}
