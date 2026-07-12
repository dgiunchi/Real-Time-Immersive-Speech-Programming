/// The safety gate's decision for a request.
///
/// Phase 1 only ever produces `ApproveActionPlan` or `RejectUnsafe`. The other
/// variants are reserved for later phases (validated C#, sandbox, clarification)
/// and exist now so downstream match sites are written exhaustively from day one.
#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
pub enum Decision {
    /// The action plan is within bounds and safe to execute.
    ApproveActionPlan,
    /// The action plan violates a bound/invariant; do not execute.
    RejectUnsafe,
    /// Phase 2+: clamp/retry the request automatically.
    SimplifyAndRetry,
    /// Phase 2+: the intent is ambiguous; ask the user.
    AskUserClarification,
    /// Phase 3+: a validated C# candidate may run in research/dev mode.
    ApproveCSharpResearchMode,
    /// Phase 4+: route risky C# to the isolated sandbox worker.
    SendToSandbox,
}
