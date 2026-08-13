use async_trait::async_trait;

use crate::error::RoslynError;
use crate::CompiledAssembly;
use crate::{RoslynAnalyzer, RoslynVerdict};

/// Calls the external .NET Roslyn analyzer microservice (see
/// `services/roslyn-analyzer/`). POST `{ "csharp": "..." }` ->
/// `{ "approved": bool, "diagnostics": [..] }`.
#[derive(Debug, Clone)]
pub struct HttpRoslynAnalyzer {
    url: String,
    client: reqwest::Client,
}

/// Default analyzer request timeout. A hostile snippet that makes the analyzer hang
/// must not stall the whole pipeline (attack A056) — the request fails fast, and the
/// router treats an analyzer error as fail-closed (not approved).
const DEFAULT_ANALYZE_TIMEOUT: std::time::Duration = std::time::Duration::from_secs(10);

impl HttpRoslynAnalyzer {
    pub fn new(url: impl Into<String>) -> Self {
        Self::with_timeout(url, DEFAULT_ANALYZE_TIMEOUT)
    }

    /// Build with an explicit per-request timeout (fail-closed on a hung analyzer).
    pub fn with_timeout(url: impl Into<String>, timeout: std::time::Duration) -> Self {
        let client = reqwest::Client::builder()
            .timeout(timeout)
            .build()
            .unwrap_or_else(|_| reqwest::Client::new());
        Self {
            url: url.into(),
            client,
        }
    }
}

#[async_trait]
impl RoslynAnalyzer for HttpRoslynAnalyzer {
    async fn compile(&self, csharp: &str) -> Result<CompiledAssembly, RoslynError> {
        // /analyze and /compile sit on the same service; derive one URL from the other so
        // a deployment configures a single endpoint.
        let url = self.url.replace("/analyze", "/compile");
        let resp = self
            .client
            .post(&url)
            .json(&serde_json::json!({ "csharp": csharp }))
            .send()
            .await
            .map_err(|e| RoslynError::Request(e.to_string()))?;
        let status = resp.status();
        if !status.is_success() {
            return Err(RoslynError::Status {
                status: status.as_u16(),
            });
        }
        let v: CompiledAssembly = resp
            .json()
            .await
            .map_err(|e| RoslynError::Parse(e.to_string()))?;
        // Fail-closed: an "approved" assembly with no bytes is not an assembly.
        if v.approved && v.assembly.as_deref().unwrap_or("").is_empty() {
            return Err(RoslynError::Parse(
                "approved but empty assembly".to_string(),
            ));
        }
        Ok(v)
    }

    async fn analyze(&self, csharp: &str) -> Result<RoslynVerdict, RoslynError> {
        let body = serde_json::json!({ "csharp": csharp });
        let resp = self
            .client
            .post(&self.url)
            .json(&body)
            .send()
            .await
            .map_err(|e| RoslynError::Request(e.to_string()))?;
        let status = resp.status();
        if !status.is_success() {
            return Err(RoslynError::Status {
                status: status.as_u16(),
            });
        }
        let v: serde_json::Value = resp
            .json()
            .await
            .map_err(|e| RoslynError::Parse(e.to_string()))?;
        // Fail-closed: unparseable/absent "approved" => not approved.
        let approved = v.get("approved").and_then(|a| a.as_bool()).unwrap_or(false);
        let diagnostics = v
            .get("diagnostics")
            .and_then(|d| d.as_array())
            .map(|a| {
                a.iter()
                    .filter_map(|x| x.as_str().map(String::from))
                    .collect()
            })
            .unwrap_or_default();
        Ok(RoslynVerdict {
            approved,
            diagnostics,
        })
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn unreachable_analyzer_fails_closed_without_hanging() {
        // A closed port errors immediately; combined with the timeout this proves the
        // analyzer call cannot hang the pipeline, and an analyzer error is fail-closed.
        let a = HttpRoslynAnalyzer::with_timeout(
            "http://127.0.0.1:1/analyze",
            std::time::Duration::from_millis(300),
        );
        assert!(
            a.analyze("class X {}").await.is_err(),
            "unreachable analyzer must error (fail-closed), not hang"
        );
    }
}
