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
    /// The model id to generate with. EMPTY = keep the one the client was built
    /// with (`OPENAI_MODEL`). Non-empty overrides it per request, so switching the
    /// admin panel's model dropdown takes effect without a restart.
    pub model: String,
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
            // Empty on purpose: defer to the client's configured model until the
            // admin panel actually pushes a choice.
            model: String::new(),
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

    /// Ask the model to fix C# that the compiler rejected, given the diagnostics.
    ///
    /// Generated code that does not compile is the ordinary case, not the exceptional
    /// one — the reliability ceiling for code-generating LLMs is well documented, and in
    /// live use here roughly a third to a half of substantial creative programs failed to
    /// build on the first attempt (`CS0029: cannot convert GameObject to Transform` being
    /// a representative example). The system failed closed correctly every time, which is
    /// right and also means the user simply saw nothing happen.
    ///
    /// **The repaired source is NOT trusted.** It re-enters the pipeline at the top: the
    /// lexical guardrail, then the semantic analyzer, then the compiler. A compiler
    /// diagnostic is a hint to the model, never a licence to skip validation — the whole
    /// point is that nothing reaches the device without passing the same gate twice over.
    ///
    /// Default: refuse. A client that cannot repair should not silently pretend to.
    async fn repair_csharp(
        &self,
        request_id: &str,
        source: &str,
        diagnostics: &str,
    ) -> Result<String, LlmError> {
        let _ = (request_id, source, diagnostics);
        Err(LlmError::Unsupported(
            "this client does not support repair".to_string(),
        ))
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
