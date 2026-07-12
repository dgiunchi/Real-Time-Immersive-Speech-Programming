//! Text embeddings for retrieval. The seam is [`EmbeddingClient`]:
//! - [`MockEmbeddingClient`] is deterministic and offline (hashed bag-of-words),
//!   so personalization works with no key and tests are reproducible.
//! - [`OpenAiEmbeddingClient`] calls OpenAI `text-embedding-3-small` for real
//!   semantic similarity (the ChatGPT/Gemini-style RAG path).

use async_trait::async_trait;
use secrecy::{ExposeSecret, SecretString};

#[derive(Debug, thiserror::Error)]
pub enum EmbedError {
    #[error("embedding request failed: {0}")]
    Request(String),
    #[error("embedding returned status {status}: {body}")]
    Status { status: u16, body: String },
    #[error("embedding parse error: {0}")]
    Parse(String),
}

#[async_trait]
pub trait EmbeddingClient: Send + Sync {
    /// Map `text` to a fixed-length vector. Similar text → similar vectors.
    async fn embed(&self, text: &str) -> Result<Vec<f32>, EmbedError>;
}

/// Deterministic, offline embedding: hash each token into a fixed-dim vector and
/// L2-normalize. Shared words → higher cosine similarity. Good enough for the
/// keyless demo and for tests; not as semantic as a real model.
#[derive(Debug, Clone)]
pub struct MockEmbeddingClient {
    dim: usize,
}

impl Default for MockEmbeddingClient {
    fn default() -> Self {
        Self { dim: 64 }
    }
}

#[async_trait]
impl EmbeddingClient for MockEmbeddingClient {
    async fn embed(&self, text: &str) -> Result<Vec<f32>, EmbedError> {
        let mut v = vec![0.0f32; self.dim];
        for tok in text
            .to_lowercase()
            .split(|c: char| !c.is_alphanumeric())
            .filter(|t| !t.is_empty())
        {
            // FNV-1a hash → bucket index (stable across runs/platforms).
            let mut h: u64 = 0xcbf29ce484222325;
            for b in tok.bytes() {
                h ^= b as u64;
                h = h.wrapping_mul(0x100000001b3);
            }
            let idx = (h as usize) % self.dim;
            v[idx] += 1.0;
        }
        let norm: f32 = v.iter().map(|x| x * x).sum::<f32>().sqrt();
        if norm > 0.0 {
            for x in &mut v {
                *x /= norm;
            }
        }
        Ok(v)
    }
}

/// OpenAI embeddings client (`/v1/embeddings`, `text-embedding-3-small` by
/// default). The key lives in a `SecretString` and is never logged.
#[derive(Debug, Clone)]
pub struct OpenAiEmbeddingClient {
    api_key: SecretString,
    model: String,
    base_url: String,
    client: reqwest::Client,
}

impl OpenAiEmbeddingClient {
    pub fn new(api_key: SecretString, model: impl Into<String>) -> Self {
        Self {
            api_key,
            model: model.into(),
            base_url: "https://api.openai.com/v1".to_string(),
            client: reqwest::Client::new(),
        }
    }

    pub fn with_base_url(mut self, base_url: impl Into<String>) -> Self {
        self.base_url = base_url.into();
        self
    }
}

#[async_trait]
impl EmbeddingClient for OpenAiEmbeddingClient {
    async fn embed(&self, text: &str) -> Result<Vec<f32>, EmbedError> {
        let body = serde_json::json!({ "model": self.model, "input": text });
        let resp = self
            .client
            .post(format!("{}/embeddings", self.base_url))
            .bearer_auth(self.api_key.expose_secret())
            .json(&body)
            .send()
            .await
            .map_err(|e| EmbedError::Request(e.to_string()))?;
        let status = resp.status();
        let text_body = resp
            .text()
            .await
            .map_err(|e| EmbedError::Request(e.to_string()))?;
        if !status.is_success() {
            return Err(EmbedError::Status {
                status: status.as_u16(),
                body: text_body,
            });
        }
        let v: serde_json::Value =
            serde_json::from_str(&text_body).map_err(|e| EmbedError::Parse(e.to_string()))?;
        let arr = v
            .get("data")
            .and_then(|d| d.get(0))
            .and_then(|d| d.get("embedding"))
            .and_then(|e| e.as_array())
            .ok_or_else(|| EmbedError::Parse("no embedding in response".to_string()))?;
        let out: Vec<f32> = arr
            .iter()
            .filter_map(|x| x.as_f64().map(|f| f as f32))
            .collect();
        if out.is_empty() {
            return Err(EmbedError::Parse("empty embedding".to_string()));
        }
        Ok(out)
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn mock_is_deterministic_and_similar_for_shared_words() {
        let m = MockEmbeddingClient::default();
        let a = m.embed("spooky haunted house").await.expect("embed a");
        let a2 = m.embed("spooky haunted house").await.expect("embed a2");
        let b = m.embed("spooky haunted mansion").await.expect("embed b");
        let c = m.embed("bright sunny beach").await.expect("embed c");
        assert_eq!(a, a2); // deterministic
        let sim_ab = crate::vector::cosine(&a, &b);
        let sim_ac = crate::vector::cosine(&a, &c);
        assert!(sim_ab > sim_ac); // shares 2 words vs 0
    }
}
