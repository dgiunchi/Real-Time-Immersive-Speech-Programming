//! LLM client abstraction for DreamCodeVR+ (Phase 2/3).
//!
//! [`LlmClient::generate_plan`] is the action-plan fast path. [`LlmClient::generate_dual`]
//! (Phase 3) additionally returns a matching `csharp_candidate` (preserving the
//! original paper's C# generation), which `dcvr-csharp-policy` then validates.
//! [`MockLlmClient`] is offline/deterministic; [`OpenAiLlmClient`] calls a real
//! endpoint. A model-produced plan/C# is ALWAYS re-validated downstream.

mod error;
mod mock;
mod openai;
mod template;

pub use error::LlmError;
pub use mock::{mock_generate, MockLlmClient};
pub use openai::OpenAiLlmClient;
pub use template::template_csharp;

use async_trait::async_trait;
use dcvr_behaviour_dsl::ActionPlan;
use std::sync::{OnceLock, RwLock};

/// Live, admin-tunable generation knobs for the GPT-5 / o-series reasoning models.
/// Set from the admin panel's `RuntimeConfig` (see `set_llm_tuning`) and read per
/// request by the OpenAI client, so quality<->latency can be tuned WITHOUT a restart.
#[derive(Debug, Clone)]
pub struct LlmTuning {
    /// `minimal | low | medium | high` — the dominant latency/quality lever.
    pub reasoning_effort: String,
    /// `default | low | medium | high` — `default` leaves the model default (richer).
    pub verbosity: String,
    /// Output-token budget (reasoning tokens count against it). 0 = do not send.
    pub max_completion_tokens: u32,
}

impl Default for LlmTuning {
    fn default() -> Self {
        // Best-quality default the user asked for: high reasoning, default verbosity.
        Self {
            reasoning_effort: "high".to_string(),
            verbosity: "default".to_string(),
            max_completion_tokens: 8000,
        }
    }
}

fn tuning_cell() -> &'static RwLock<LlmTuning> {
    static CELL: OnceLock<RwLock<LlmTuning>> = OnceLock::new();
    CELL.get_or_init(|| RwLock::new(LlmTuning::default()))
}

/// Update the live tuning (called by the router from the admin-panel `RuntimeConfig`).
pub fn set_llm_tuning(t: LlmTuning) {
    if let Ok(mut w) = tuning_cell().write() {
        *w = t;
    }
}

/// The current live tuning (read per request by the OpenAI client).
pub fn current_llm_tuning() -> LlmTuning {
    tuning_cell().read().map(|r| r.clone()).unwrap_or_default()
}

/// Dual generation result: the safety-authoritative plan plus an optional
/// matching C# candidate (research/dev mode only).
#[derive(Debug, Clone)]
pub struct Generation {
    pub plan: ActionPlan,
    pub csharp_candidate: Option<String>,
}

/// Verdict from the dedicated LLM SECURITY CLASSIFIER (Layer 1), which inspects the
/// raw command semantically BEFORE generation — catching intents a keyword filter
/// misses (device camera/mic access, tap automation, memory reads, exfiltration, …).
#[derive(Debug, Clone)]
pub struct IntentVerdict {
    pub malicious: bool,
    /// short label, e.g. "camera-access" / "keylogger" / "safe".
    pub category: String,
    pub reason: String,
}

#[async_trait]
pub trait LlmClient: Send + Sync {
    /// Action-plan only (the Quest-safe fast path).
    async fn generate_plan(
        &self,
        request_id: &str,
        transcript: &str,
    ) -> Result<ActionPlan, LlmError>;

    /// Plan + optional matching C# candidate. Default: plan only (no C#).
    async fn generate_dual(
        &self,
        request_id: &str,
        transcript: &str,
    ) -> Result<Generation, LlmError> {
        Ok(Generation {
            plan: self.generate_plan(request_id, transcript).await?,
            csharp_candidate: None,
        })
    }

    /// Layer-1 SECURITY SCREEN of the raw command, BEFORE generation. A real client
    /// overrides this with an LLM classification call; the default (offline/mock) is
    /// "safe" and relies on the keyword pre-filter + the downstream C# validator.
    async fn screen_intent(
        &self,
        request_id: &str,
        command: &str,
    ) -> Result<IntentVerdict, LlmError> {
        let _ = (request_id, command);
        Ok(IntentVerdict {
            malicious: false,
            category: "safe".to_string(),
            reason: String::new(),
        })
    }
}
