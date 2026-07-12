//! Environment/default configuration for DreamCodeVR+.
//!
//! Reads a listen address, run mode, optional STT/LLM endpoints/timeouts, the
//! optional Ubiq room/Roslyn settings, and the one secret (the LLM API key) from
//! the environment with safe defaults. The API key is held in a
//! `secrecy::SecretString` (redacted `Debug`, zeroized on drop) and is never
//! written to disk or logs.

mod errors;
mod settings;

pub use errors::ConfigError;
pub use settings::{RunMode, Settings};
