use async_trait::async_trait;

use crate::error::RoslynError;
use crate::{RoslynAnalyzer, RoslynVerdict};

/// Calls the external .NET Roslyn analyzer microservice (see
/// `services/roslyn-analyzer/`). POST `{ "csharp": "..." }` ->
/// `{ "approved": bool, "diagnostics": [..] }`.
#[derive(Debug, Clone)]
pub struct HttpRoslynAnalyzer {
    url: String,
    client: reqwest::Client,
}

impl HttpRoslynAnalyzer {
    pub fn new(url: impl Into<String>) -> Self {
        Self {
            url: url.into(),
            client: reqwest::Client::new(),
        }
    }
}

#[async_trait]
impl RoslynAnalyzer for HttpRoslynAnalyzer {
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
