//! Phase-1 mock generation lives in `dcvr-command-router`; re-exported here so
//! the server keeps a stable seam to swap for real LLM generation in Phase 2.
pub use dcvr_command_router::mock::mock_generate;
