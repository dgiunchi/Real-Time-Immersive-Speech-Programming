//! Phase-1 mock generation now lives in `dcvr-llm-client`; re-exported here so
//! the original public path `dcvr_command_router::mock::mock_generate` stays
//! stable for existing callers and tests.
pub use dcvr_llm_client::mock_generate;
