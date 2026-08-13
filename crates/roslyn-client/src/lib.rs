//! Client for the external .NET Roslyn semantic analyzer (Phase 3 deep layer).
//!
//! This is the SECOND C# validation layer. The first (`dcvr-csharp-policy`) is a
//! lexical/structural pre-filter that always runs in-process. This layer adds a
//! true semantic, symbol-resolving, allow-list check — implemented in .NET
//! because Roslyn is the only authoritative C# analyzer (see
//! `services/roslyn-analyzer/`). [`MockRoslynAnalyzer`] is the offline default;
//! [`HttpRoslynAnalyzer`] calls the real service. [`HttpRoslynAnalyzer`] is
//! **fail-closed** (unreachable / unparseable / non-approving analyzer ⇒ not
//! approved). [`MockRoslynAnalyzer`] is **approve-all (fail-OPEN)** for offline
//! dev — which is why `hardened` Mode A refuses to start without a real analyzer,
//! and why the in-process `dcvr-csharp-policy` lexical layer always runs first as
//! the effective gate.

mod error;
mod http;
mod mock;

pub use error::RoslynError;
pub use http::HttpRoslynAnalyzer;
pub use mock::MockRoslynAnalyzer;

use async_trait::async_trait;

/// A compiled assembly plus whatever the compiler had to say about it.
#[derive(Debug, Clone, serde::Deserialize, serde::Serialize)]
pub struct CompiledAssembly {
    pub approved: bool,
    /// Base64 IL. `None` whenever `approved` is false.
    pub assembly: Option<String>,
    #[serde(default)]
    pub diagnostics: Vec<String>,
}

/// A semantic-analysis verdict for a C# candidate.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RoslynVerdict {
    pub approved: bool,
    pub diagnostics: Vec<String>,
}

#[async_trait]
pub trait RoslynAnalyzer: Send + Sync {
    async fn analyze(&self, csharp: &str) -> Result<RoslynVerdict, RoslynError>;

    /// Compile validated C# to a .NET assembly, returned base64.
    ///
    /// Separate from `analyze` on purpose: compiling is a distinct capability from
    /// approving, and the caller must have already validated the source. The default
    /// implementation refuses, so an analyzer that cannot compile fails closed rather
    /// than silently returning nothing.
    async fn compile(&self, _csharp: &str) -> Result<CompiledAssembly, RoslynError> {
        Err(RoslynError::Unavailable(
            "this analyzer does not provide compilation".to_string(),
        ))
    }
}
