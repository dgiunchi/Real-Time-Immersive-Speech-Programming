use thiserror::Error;

#[derive(Debug, Clone, PartialEq, Eq, Error)]
pub enum ConfigError {
    #[error("invalid DCVR_LISTEN_ADDR: '{0}' (expected host:port)")]
    InvalidListenAddr(String),
    #[error("invalid DCVR_MODE: '{0}' (Phase 1 supports only 'action_plan_fast')")]
    InvalidMode(String),
    #[error("invalid DCVR_SECURITY_PROFILE: '{0}' (expected 'legacy', 'hardened', or 'test')")]
    InvalidSecurityProfile(String),
    #[error(
        "hardened security profile requires control '{0}' but it is not configured \
         (fail-closed: refusing to run insecurely)"
    )]
    HardenedMissingControl(&'static str),
}
