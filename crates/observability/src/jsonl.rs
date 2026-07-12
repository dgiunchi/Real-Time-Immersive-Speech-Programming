use std::io::Write;

use crate::errors::ObservabilityError;
use crate::timing::TimingEvent;

/// Writes [`TimingEvent`]s as newline-delimited JSON to any `Write` sink.
///
/// Generic over the sink so the server can target stdout while tests target
/// `io::sink()`. (Phase 2 swaps the sink for an async, non-blocking channel so
/// logging never stalls the hot path.)
#[derive(Debug)]
pub struct JsonlWriter<W: Write> {
    inner: W,
}

impl<W: Write> JsonlWriter<W> {
    pub fn new(inner: W) -> Self {
        Self { inner }
    }

    /// Serialize `event` and write it followed by a newline.
    pub fn write_event(&mut self, event: &TimingEvent) -> Result<(), ObservabilityError> {
        let line = serde_json::to_string(event)
            .map_err(|e| ObservabilityError::Serialize(e.to_string()))?;
        self.inner.write_all(line.as_bytes())?;
        self.inner.write_all(b"\n")?;
        Ok(())
    }

    pub fn into_inner(self) -> W {
        self.inner
    }
}
