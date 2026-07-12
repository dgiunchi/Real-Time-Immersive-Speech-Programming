use std::time::{Instant, SystemTime, UNIX_EPOCH};

use serde::Serialize;

/// Milliseconds since the Unix epoch for `t`. Returns 0 if the clock is before
/// the epoch (never panics).
pub fn epoch_millis(t: SystemTime) -> u64 {
    t.duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

/// A simple monotonic stopwatch for measuring a single stage's duration.
#[derive(Debug)]
pub struct StageTiming {
    start: Instant,
}

impl StageTiming {
    pub fn start() -> Self {
        Self {
            start: Instant::now(),
        }
    }
    pub fn elapsed_ms(&self) -> u64 {
        self.start.elapsed().as_millis() as u64
    }
}

/// A single privacy-safe timing record (one JSONL line per request).
///
/// PRIVACY INVARIANT: this struct intentionally contains NO raw audio, NO
/// transcript text, NO biometric data, and NO secrets — only ids, timestamps,
/// the decision, structured error strings, and counts. A test
/// (`tests/timing_tests.rs`) asserts the serialized key set to prevent
/// regressions that would leak sensitive fields.
#[derive(Debug, Clone, PartialEq, Serialize)]
pub struct TimingEvent {
    pub request_id: String,
    pub peer_id: String,
    pub mode: String,
    pub decision: String,
    pub t_received: u64,
    pub t_validated: u64,
    pub t_sent: u64,
    pub validation_ms: u64,
    pub errors: Vec<String>,
    pub action_count: usize,
    pub spawned_count: u32,
}
