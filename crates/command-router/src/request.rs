use uuid::Uuid;

/// A per-request correlation id, propagated into timing logs and the NID 94
/// response so a transcript can be traced to its decision end-to-end.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RequestId(String);

impl RequestId {
    pub fn new() -> Self {
        Self(Uuid::new_v4().to_string())
    }
    pub fn from_string(s: String) -> Self {
        Self(s)
    }
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

impl Default for RequestId {
    fn default() -> Self {
        Self::new()
    }
}

/// Execution mode (which of the four DreamCodeVR+ paths a request takes).
/// Phase 1 only uses `ActionPlanFast`; the rest are wired in later phases.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Mode {
    /// C: action-plan -> validate -> Unity ActionPlanExecutor (Quest-safe default).
    ActionPlanFast,
    /// B: validated C# -> Roslyn (research/dev).
    ValidatedCSharpResearch,
    /// A: original DreamCodeVR Roslyn pipeline (baseline/control).
    OriginalRoslynBaseline,
    /// D: risky C# -> isolated sandbox worker.
    Sandbox,
}

impl Mode {
    pub fn as_str(&self) -> &'static str {
        match self {
            Mode::ActionPlanFast => "action_plan_fast",
            Mode::ValidatedCSharpResearch => "validated_csharp_research",
            Mode::OriginalRoslynBaseline => "original_roslyn_baseline",
            Mode::Sandbox => "sandbox",
        }
    }
}
