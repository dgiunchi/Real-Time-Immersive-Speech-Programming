//! DreamCodeVR+ Phase-1 backend library.
//!
//! Receives Ubiq-like frames on NID 93 (selection) and NID 98 (mock transcript),
//! validates a mock action plan via `dcvr-code-policy`, and emits a NID 94
//! decision. Exposed as a library so the integration tests can drive a real
//! loopback server without spawning a subprocess.

pub mod app;
pub mod mock_generation;
pub mod server;
