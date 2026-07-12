use thiserror::Error;

/// Outcome of static C# validation. `ApproveForResearch` means the candidate
/// passed the lexical/structural pre-filter AND the consistency check — it may
/// run only in dev/research mode (Editor/Mono), and only after the deeper Roslyn
/// semantic analyzer (and, for risky cases, the sandbox) also approve it.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CsharpDecision {
    ApproveForResearch,
    Reject,
}

/// A single static-validation finding. Carries the offending token so refusals
/// are specific.
#[derive(Debug, Clone, PartialEq, Eq, Error)]
pub enum CsharpViolation {
    #[error("c# failed to parse (syntax errors)")]
    ParseError,
    #[error("c# too large: {len} bytes (max {max})")]
    TooLarge { len: usize, max: usize },
    #[error("c# too long: {lines} lines (max {max})")]
    TooManyLines { lines: usize, max: usize },
    #[error("expected exactly one class, found {0}")]
    ClassCount(usize),
    #[error("generated class must inherit MonoBehaviour")]
    NotMonoBehaviour,
    #[error("banned API/token referenced: {0}")]
    BannedToken(String),
    #[error("c# uses capability '{0}' that the action plan does not authorize")]
    InconsistentCapability(String),
}

/// The full verdict.
#[derive(Debug, Clone, PartialEq)]
pub struct CsharpVerdict {
    pub decision: CsharpDecision,
    pub violations: Vec<CsharpViolation>,
}
