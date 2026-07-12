//! Shared **runtime control plane** for DreamCodeVR+.
//!
//! One cheaply-clonable [`ControlBus`] is created by the backend and shared with
//! both the request pipeline (which reads live [`RuntimeConfig`] and *publishes*
//! [`PipelineEvent`]s) and the admin panel (which *edits* the config and
//! *subscribes* to the events). Kept dependency-light (no web framework) so the
//! pipeline crates can depend on it without pulling in axum.

use std::collections::VecDeque;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, RwLock};

use serde::{Deserialize, Serialize};
use tokio::sync::broadcast;

/// Live-tunable knobs the admin panel can change at runtime; the pipeline reads a
/// fresh snapshot per request, so changes take effect on the next command.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RuntimeConfig {
    /// Max generated-C# size (chars) the validator will accept.
    pub max_csharp_chars: usize,
    /// Max generated-C# lines the validator will accept.
    pub max_csharp_lines: usize,
    /// Soft cap on objects a build may create (advised to the model).
    pub max_objects: usize,
    pub enable_stt: bool,
    pub enable_rag: bool,
    pub enable_mode_a: bool,
    pub enable_guardrails: bool,
    /// OpenAI model id used for generation (e.g. `gpt-5.5`, `gpt-4.1-nano`).
    pub model: String,
    /// How many liked memories RAG may retrieve.
    pub rag_top_k: usize,
    /// Anti-flood: max generated plans a single peer may produce per minute
    /// (sliding-window rate limit, SOC-03/PE-02).
    pub max_generations_per_min: u32,
    /// Per-recipient comfort: radius (metres) of a user's personal space that
    /// spawned/animated geometry should respect.
    pub personal_space_radius_m: f64,
    /// Anti-strobe: minimum gap (ms) between two accepted plans from one peer
    /// (=> <=3 plans/sec at the default, WCAG 2.3.1 flash-rate guard).
    pub min_plan_interval_ms: u64,
    /// Anti-vection: hard clamp on rotation magnitude (deg/sec) applied to any
    /// produced/validated plan.
    pub comfort_rotate_max_deg_s: f64,
    /// DEPLOY-TIME perceptual hardening (Mode B). When false (DEFAULT) the creative
    /// C# path keeps FULL freedom — only system-access APIs are banned. When true,
    /// the validator ADDITIONALLY bans the perceptual/embodied-attack API surface
    /// (XR rig / camera-view / haptics / second-display / scene-scan) for a
    /// shippable embodied-safe build. Opt-in; never limits creation by default.
    #[serde(default)]
    pub perceptual_hardening: bool,
    /// LLM reasoning effort for GPT-5 / o-series (minimal | low | medium | high).
    /// The dominant quality<->latency lever; tunable live from the admin panel.
    #[serde(default = "default_reasoning_effort")]
    pub llm_reasoning_effort: String,
    /// LLM verbosity (default | low | medium | high). `default` keeps the model
    /// default (richer builds); lower it to trade richness for speed.
    #[serde(default = "default_verbosity")]
    pub llm_verbosity: String,
    /// Output-token budget for generation (reasoning tokens count against it).
    #[serde(default = "default_max_completion_tokens")]
    pub llm_max_completion_tokens: u32,
}

fn default_reasoning_effort() -> String {
    // `medium`, not `high`: on gpt-5.5 (a reasoning model) `high` can spend the WHOLE
    // completion budget on hidden reasoning and return EMPTY content (parsed as EOF ->
    // "llm_unavailable" -> the command is wrongly rejected), and it is also very slow
    // (~100s/command). `medium` keeps quality while leaving room for the output.
    "medium".to_string()
}
fn default_verbosity() -> String {
    "default".to_string()
}
fn default_max_completion_tokens() -> u32 {
    // Generous ceiling so reasoning tokens + the emitted plan/C# both fit. 8000 was too
    // small for a reasoning model and caused empty completions on richer builds.
    24000
}

impl Default for RuntimeConfig {
    fn default() -> Self {
        Self {
            max_csharp_chars: 16384,
            max_csharp_lines: 400,
            max_objects: 40,
            enable_stt: true,
            enable_rag: true,
            enable_mode_a: false,
            enable_guardrails: true,
            model: "gpt-5.5".to_string(),
            rag_top_k: 1,
            max_generations_per_min: 30,
            personal_space_radius_m: 0.5,
            min_plan_interval_ms: 334,
            comfort_rotate_max_deg_s: 120.0,
            // FREEDOM DEFAULT: perceptual hardening OFF — full creative C# freedom.
            perceptual_hardening: false,
            // Sensible defaults: medium reasoning (see default_reasoning_effort), default verbosity — all user-tunable.
            llm_reasoning_effort: default_reasoning_effort(),
            llm_verbosity: default_verbosity(),
            llm_max_completion_tokens: default_max_completion_tokens(),
        }
    }
}

/// Which pipeline stage an event reports.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Stage {
    Transcript,
    PromptSent,
    LlmReply,
    Validation,
    Compile,
    Safety,
    Error,
    Info,
}

/// One observable step of one request — streamed live to the admin panel (SSE).
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PipelineEvent {
    pub request_id: String,
    pub peer: String,
    pub stage: Stage,
    /// One-line headline (shown in the timeline).
    pub summary: String,
    /// Full content (transcript / prompt / C# / violations) — shown when expanded.
    pub detail: String,
    /// Stage duration in milliseconds (0 if instantaneous/not timed).
    pub ms: u64,
    pub ok: bool,
}

impl PipelineEvent {
    pub fn new(request_id: &str, peer: &str, stage: Stage, summary: impl Into<String>) -> Self {
        Self {
            request_id: request_id.to_string(),
            peer: peer.to_string(),
            stage,
            summary: summary.into(),
            detail: String::new(),
            ms: 0,
            ok: true,
        }
    }
    pub fn detail(mut self, d: impl Into<String>) -> Self {
        self.detail = d.into();
        self
    }
    pub fn ms(mut self, ms: u64) -> Self {
        self.ms = ms;
        self
    }
    pub fn ok(mut self, ok: bool) -> Self {
        self.ok = ok;
        self
    }
}

/// Lifetime counters (lock-free). Snapshot for JSON via [`Stats::snapshot`].
#[derive(Debug, Default)]
pub struct Stats {
    pub requests: AtomicU64,
    pub approved: AtomicU64,
    pub rejected: AtomicU64,
    pub neutralized: AtomicU64,
    pub malicious_blocked: AtomicU64,
    pub llm_calls: AtomicU64,
    pub total_llm_ms: AtomicU64,
    pub prompt_tokens: AtomicU64,
    pub completion_tokens: AtomicU64,
}

#[derive(Debug, Clone, Serialize)]
pub struct StatsSnapshot {
    pub requests: u64,
    pub approved: u64,
    pub rejected: u64,
    pub neutralized: u64,
    pub malicious_blocked: u64,
    pub llm_calls: u64,
    pub avg_llm_ms: u64,
    pub prompt_tokens: u64,
    pub completion_tokens: u64,
    /// Rough USD estimate from token counts (gpt-5.x order-of-magnitude rates).
    pub est_cost_usd: f64,
}

impl Stats {
    pub fn snapshot(&self) -> StatsSnapshot {
        let calls = self.llm_calls.load(Ordering::Relaxed);
        let total_ms = self.total_llm_ms.load(Ordering::Relaxed);
        let pt = self.prompt_tokens.load(Ordering::Relaxed);
        let ct = self.completion_tokens.load(Ordering::Relaxed);
        // Indicative rates ($/1M tokens); adjust per model. Input ~1.25, output ~10.
        let est = (pt as f64 / 1_000_000.0) * 1.25 + (ct as f64 / 1_000_000.0) * 10.0;
        StatsSnapshot {
            requests: self.requests.load(Ordering::Relaxed),
            approved: self.approved.load(Ordering::Relaxed),
            rejected: self.rejected.load(Ordering::Relaxed),
            neutralized: self.neutralized.load(Ordering::Relaxed),
            malicious_blocked: self.malicious_blocked.load(Ordering::Relaxed),
            llm_calls: calls,
            avg_llm_ms: total_ms.checked_div(calls).unwrap_or(0),
            prompt_tokens: pt,
            completion_tokens: ct,
            est_cost_usd: (est * 10000.0).round() / 10000.0,
        }
    }
}

/// The shared control plane handle. Clone freely (everything inside is `Arc`).
#[derive(Clone)]
pub struct ControlBus {
    config: Arc<RwLock<RuntimeConfig>>,
    tx: broadcast::Sender<PipelineEvent>,
    stats: Arc<Stats>,
    safety_log: Arc<Mutex<VecDeque<PipelineEvent>>>,
    recent: Arc<Mutex<VecDeque<PipelineEvent>>>,
}

impl ControlBus {
    pub fn new(config: RuntimeConfig) -> Self {
        let (tx, _rx) = broadcast::channel(512);
        Self {
            config: Arc::new(RwLock::new(config)),
            tx,
            stats: Arc::new(Stats::default()),
            safety_log: Arc::new(Mutex::new(VecDeque::new())),
            recent: Arc::new(Mutex::new(VecDeque::new())),
        }
    }

    /// A snapshot of the current config (cheap clone).
    pub fn config(&self) -> RuntimeConfig {
        self.config.read().map(|c| c.clone()).unwrap_or_default()
    }

    /// Replace the whole config (admin "Apply").
    pub fn set_config(&self, cfg: RuntimeConfig) {
        if let Ok(mut w) = self.config.write() {
            *w = cfg;
        }
    }

    /// Mutate the config in place.
    pub fn update<F: FnOnce(&mut RuntimeConfig)>(&self, f: F) {
        if let Ok(mut w) = self.config.write() {
            f(&mut w);
        }
    }

    /// Publish a pipeline event (live SSE + ring buffers). Never blocks/fails.
    pub fn publish(&self, ev: PipelineEvent) {
        let _ = self.tx.send(ev.clone());
        if ev.stage == Stage::Safety {
            if let Ok(mut q) = self.safety_log.lock() {
                q.push_back(ev.clone());
                while q.len() > 200 {
                    q.pop_front();
                }
            }
        }
        if let Ok(mut q) = self.recent.lock() {
            q.push_back(ev);
            while q.len() > 200 {
                q.pop_front();
            }
        }
    }

    pub fn subscribe(&self) -> broadcast::Receiver<PipelineEvent> {
        self.tx.subscribe()
    }

    pub fn stats(&self) -> Arc<Stats> {
        self.stats.clone()
    }

    pub fn safety_log(&self) -> Vec<PipelineEvent> {
        self.safety_log
            .lock()
            .map(|q| q.iter().cloned().collect())
            .unwrap_or_default()
    }

    /// The most recent events (for a browser that connects mid-stream).
    pub fn recent(&self) -> Vec<PipelineEvent> {
        self.recent
            .lock()
            .map(|q| q.iter().cloned().collect())
            .unwrap_or_default()
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    #[test]
    fn comfort_knob_defaults_match_contract() {
        let c = RuntimeConfig::default();
        assert_eq!(c.max_generations_per_min, 30);
        assert_eq!(c.personal_space_radius_m, 0.5);
        assert_eq!(c.min_plan_interval_ms, 334);
        assert_eq!(c.comfort_rotate_max_deg_s, 120.0);
    }

    #[test]
    fn config_update_roundtrip() {
        let bus = ControlBus::new(RuntimeConfig::default());
        assert!(bus.config().enable_guardrails);
        bus.update(|c| {
            c.max_csharp_lines = 120;
            c.enable_rag = false;
        });
        assert_eq!(bus.config().max_csharp_lines, 120);
        assert!(!bus.config().enable_rag);
    }

    #[tokio::test]
    async fn subscribe_receives_published_events() {
        let bus = ControlBus::new(RuntimeConfig::default());
        let mut rx = bus.subscribe();
        bus.publish(PipelineEvent::new("r1", "p1", Stage::Transcript, "hello").ms(5));
        let ev = rx.recv().await.expect("event");
        assert_eq!(ev.stage, Stage::Transcript);
        assert_eq!(ev.ms, 5);
    }

    #[test]
    fn safety_events_go_to_safety_log() {
        let bus = ControlBus::new(RuntimeConfig::default());
        bus.publish(PipelineEvent::new("r1", "p1", Stage::Transcript, "x"));
        bus.publish(PipelineEvent::new(
            "r2",
            "p1",
            Stage::Safety,
            "blocked file delete",
        ));
        assert_eq!(bus.safety_log().len(), 1);
        assert_eq!(bus.recent().len(), 2);
    }

    #[test]
    fn stats_snapshot_avg_and_cost() {
        let bus = ControlBus::new(RuntimeConfig::default());
        let s = bus.stats();
        s.llm_calls.fetch_add(2, Ordering::Relaxed);
        s.total_llm_ms.fetch_add(600, Ordering::Relaxed);
        s.completion_tokens.fetch_add(1_000_000, Ordering::Relaxed);
        let snap = s.snapshot();
        assert_eq!(snap.avg_llm_ms, 300);
        assert!((snap.est_cost_usd - 10.0).abs() < 0.001); // 1M output tokens * $10/1M
    }
}
