//! Config tests. Avoids mutating process env (racy/unsafe); tests defaults and
//! the pure token parser.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_config::{RunMode, Settings};

#[test]
fn default_settings_are_localhost_action_plan() {
    let s = Settings::default();
    assert_eq!(s.listen_addr.port(), 9098);
    assert!(s.listen_addr.ip().is_loopback());
    assert_eq!(s.mode, RunMode::Secure);
}

#[test]
fn defaults_select_offline_mock_clients() {
    let s = Settings::default();
    assert!(s.stt_is_mock(), "no STT url -> mock");
    assert!(s.llm_is_mock(), "no API key -> mock");
    assert_eq!(s.stt_timeout_ms, 10_000);
    assert_eq!(s.llm_timeout_ms, 60_000);
}

#[test]
fn mode_parser_accepts_known_and_rejects_unknown() {
    assert_eq!(
        RunMode::from_token("baseline").unwrap(),
        RunMode::Baseline
    );
    assert_eq!(
        RunMode::from_token("secure").unwrap(),
        RunMode::Secure
    );
    assert!(RunMode::from_token("arbitrary_csharp").is_err());
}

#[test]
fn from_env_without_vars_is_default() {
    if std::env::var("DCVR_LISTEN_ADDR").is_err() && std::env::var("DCVR_MODE").is_err() {
        let s = Settings::from_env().unwrap();
        assert_eq!(s.listen_addr, Settings::default().listen_addr);
    }
}
