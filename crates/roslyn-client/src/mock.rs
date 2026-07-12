use async_trait::async_trait;

use crate::error::RoslynError;
use crate::{RoslynAnalyzer, RoslynVerdict};

/// Offline stand-in. APPROVES, recording that semantic analysis was skipped.
///
/// This is safe because the ENFORCED lexical + consistency checks
/// (`dcvr-csharp-policy`) always run BEFORE this layer; the mock only stands in
/// for the deeper *semantic* allow-list, which the real `.NET` service
/// ([`crate::HttpRoslynAnalyzer`]) provides. In production the mock is not used.
#[derive(Debug, Default, Clone)]
pub struct MockRoslynAnalyzer;

#[async_trait]
impl RoslynAnalyzer for MockRoslynAnalyzer {
    async fn analyze(&self, _csharp: &str) -> Result<RoslynVerdict, RoslynError> {
        Ok(RoslynVerdict {
            approved: true,
            diagnostics: vec!["mock: semantic analysis skipped (offline)".to_string()],
        })
    }
}
