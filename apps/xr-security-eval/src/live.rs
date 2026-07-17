//! Live **static code-admission** verdicts for the streaming demo console.
//!
//! Every function here reuses the SAME real validator as the batch evaluator
//! (`crate::evaluate` / `dcvr_csharp_policy`). Nothing is compiled or executed;
//! we only decide *admission*. The per-violation "layer" (system-security vs
//! perceptual/XR) is derived WITHOUT touching `csharp-policy`, by diffing the
//! violation set produced under `CreativeFreedom` against `DeployHardened`:
//! a banned token that fires under both profiles is a system-security ban, one
//! that fires only under hardening is a perceptual/XR ban.

use std::collections::HashSet;
use std::time::Instant;

use dcvr_csharp_policy::{validate_csharp_freeform_profile, CsharpDecision, HardeningProfile};
use serde::Serialize;

use crate::{Attack, Defence};

/// Reject custom pasted C# larger than this before parsing (defence-in-depth on
/// top of the validator's own `TooLarge` check).
pub const MAX_CUSTOM_CSHARP_BYTES: usize = 20_000;

const BANNED_PREFIX: &str = "banned API/token referenced: ";

impl Defence {
    /// Parse a defence level from an API string (snake or kebab case).
    pub fn from_api(s: &str) -> Option<Defence> {
        match s.trim().to_ascii_lowercase().replace('-', "_").as_str() {
            "none" | "no_defence" | "no_defense" | "unvalidated" => Some(Defence::None),
            "security_only" | "security" | "creative_freedom" | "creativefreedom" => {
                Some(Defence::SecurityOnly)
            }
            "deploy_hardened" | "deployhardened" | "fully_hardened" | "hardened" => {
                Some(Defence::FullyHardened)
            }
            _ => None,
        }
    }

    /// Stable machine label used on the wire (matches the paper's profile names).
    pub fn api_label(self) -> &'static str {
        match self {
            Defence::None => "no_defence",
            Defence::SecurityOnly => "security_only",
            Defence::FullyHardened => "deploy_hardened",
        }
    }
}

/// Display family for an attack class (UI grouping / event `family`).
pub fn family_of(cls: &str) -> &'static str {
    match cls {
        "biometric" => "biometric",
        "positional" => "positional_motion",
        "surroundings" => "environment_imagery",
        "joystick" => "human_joystick",
        "chaperone" => "chaperone_boundary",
        "benign" => "benign",
        _ => "other",
    }
}

/// The two human-joystick payloads that pass static validation and are a
/// *documented* runtime-guardian case (bare `Camera.main.transform` motion,
/// lexically indistinguishable from legitimate authoring).
pub fn is_known_runtime_residual(id: &str) -> bool {
    id == "joy-05" || id == "joy-06"
}

fn reasons_for(csharp: &str, profile: HardeningProfile) -> (bool, Vec<String>) {
    let v = validate_csharp_freeform_profile(csharp, profile);
    (
        v.decision == CsharpDecision::Reject,
        v.violations.iter().map(|x| x.to_string()).collect(),
    )
}

/// Layer of one violation, using the CreativeFreedom reason-set to split
/// system-security bans (fire under both profiles) from perceptual/XR bans
/// (fire only under DeployHardened). Non-token findings are structural.
fn layer_of(reason: &str, security_set: &HashSet<String>) -> &'static str {
    if reason.starts_with(BANNED_PREFIX) {
        if security_set.contains(reason) {
            "system_security"
        } else {
            "perceptual_xr"
        }
    } else {
        "structural"
    }
}

/// Strip the `BannedToken` message prefix so the UI can show the bare token
/// (`WebCamTexture`); structural messages pass through unchanged.
fn token_of(reason: &str) -> String {
    reason
        .strip_prefix(BANNED_PREFIX)
        .unwrap_or(reason)
        .to_string()
}

/// Deterministic static code-admission verdict (no timing). This is the single
/// source of truth the streamed events and `POST /api/evaluate` both use.
#[derive(Debug, Clone, Serialize, PartialEq, Eq)]
pub struct AdmissionVerdict {
    /// `"reject"` (statically rejected before execution) or `"admit"`.
    pub decision: String,
    pub blocked: bool,
    /// Active defence profile (api label).
    pub profile: String,
    /// Offending tokens / structural messages (RAW text — callers rendering to
    /// HTML MUST escape; the JSON wire form is inherently safe).
    pub violations: Vec<String>,
    /// Dominant layer: `perceptual_xr` > `system_security` > `structural` > `none`.
    pub violation_layer: String,
    /// Per-violation layer, index-aligned with `violations`.
    pub violation_layers: Vec<String>,
}

/// Classify a C# fragment at one defence level using the real validator.
/// Semantics match `crate::evaluate` exactly (same block decision).
pub fn admit(csharp: &str, defence: Defence) -> AdmissionVerdict {
    // CreativeFreedom reasons: used both as the SecurityOnly result and as the
    // reference set that distinguishes security bans from perceptual bans.
    let (sec_blocked, sec_reasons) = reasons_for(csharp, HardeningProfile::CreativeFreedom);
    let security_set: HashSet<String> = sec_reasons.iter().cloned().collect();

    let (blocked, reasons) = match defence {
        Defence::None => (false, Vec::new()),
        Defence::SecurityOnly => (sec_blocked, sec_reasons),
        Defence::FullyHardened => reasons_for(csharp, HardeningProfile::DeployHardened),
    };

    let violation_layers: Vec<String> = reasons
        .iter()
        .map(|r| layer_of(r, &security_set).to_string())
        .collect();
    let dominant = if violation_layers.iter().any(|l| l == "perceptual_xr") {
        "perceptual_xr"
    } else if violation_layers.iter().any(|l| l == "system_security") {
        "system_security"
    } else if !violation_layers.is_empty() {
        "structural"
    } else {
        "none"
    };
    let violations: Vec<String> = reasons.iter().map(|r| token_of(r)).collect();

    AdmissionVerdict {
        decision: if blocked { "reject" } else { "admit" }.to_string(),
        blocked,
        profile: defence.api_label().to_string(),
        violations,
        violation_layer: dominant.to_string(),
        violation_layers,
    }
}

/// `admit()` wrapped with a single monotonic-clock reading — the real per-call
/// admission latency the console shows (live observational timing, distinct from
/// the paper's controlled microbenchmark).
pub fn admit_timed(csharp: &str, defence: Defence) -> (AdmissionVerdict, f64) {
    let t = Instant::now();
    let v = admit(csharp, defence);
    let us = t.elapsed().as_nanos() as f64 / 1000.0;
    (v, (us * 10.0).round() / 10.0)
}

/// Ground-truth classification for a *corpus* payload, independent of the
/// defence level (what the payload is known to be).
pub fn expected_classification(id: &str, should_block: bool) -> &'static str {
    if !should_block {
        "benign_admitted"
    } else if is_known_runtime_residual(id) {
        "known_runtime_residual"
    } else {
        "blocked_pre_execution"
    }
}

/// Honest, display-ready verdict label. Never claims runtime safety from a
/// static admission.
fn verdict_label(blocked: bool, defence: Defence, expected: &str) -> &'static str {
    if blocked {
        return "REJECTED BEFORE EXECUTION";
    }
    match defence {
        Defence::None => "ADMITTED WITHOUT VALIDATION",
        _ => match expected {
            "benign_admitted" => "BENIGN PAYLOAD ADMITTED",
            "known_runtime_residual" => "ADMITTED — KNOWN RUNTIME-GUARDIAN CASE",
            // Custom paste or any unexpected admit: state admission only.
            _ => "ADMITTED BY STATIC VALIDATOR",
        },
    }
}

/// One streamed console event. `#[serde(flatten)]` folds the verdict fields
/// (decision/violations/…) to the top level to match the agreed wire schema.
#[derive(Debug, Clone, Serialize)]
pub struct LiveEvent {
    pub run_id: String,
    pub index: usize,
    pub total: usize,
    pub payload_id: String,
    pub family: String,
    pub spoken_command: String,
    pub effect: String,
    pub defence: String,
    #[serde(flatten)]
    pub verdict: AdmissionVerdict,
    pub latency_us: f64,
    pub expected_classification: String,
    pub verdict_label: String,
    /// True when the code was admitted but its runtime effect is NOT established
    /// (custom paste, or an admitted malicious case) — drives the UI caveat.
    pub runtime_unverified: bool,
    /// The C# source (RAW — the browser renders it via `textContent`).
    pub csharp: String,
}

/// Build the streamed event for a corpus payload at one defence level.
pub fn build_case_event(
    run_id: &str,
    index: usize,
    total: usize,
    a: &Attack,
    defence: Defence,
) -> LiveEvent {
    let (verdict, latency_us) = admit_timed(&a.csharp, defence);
    let expected = expected_classification(&a.id, a.should_block);
    let runtime_unverified =
        !verdict.blocked && defence != Defence::None && expected != "benign_admitted"; // residual OR any admitted malicious
    LiveEvent {
        run_id: run_id.to_string(),
        index,
        total,
        payload_id: a.id.clone(),
        family: family_of(&a.cls).to_string(),
        spoken_command: a.command.clone(),
        effect: a.effect.clone(),
        defence: defence.api_label().to_string(),
        verdict_label: verdict_label(verdict.blocked, defence, expected).to_string(),
        runtime_unverified,
        latency_us,
        expected_classification: expected.to_string(),
        csharp: a.csharp.clone(),
        verdict,
    }
}

/// Build the event for a custom pasted C# fragment (no corpus ground truth).
pub fn build_custom_event(csharp: &str, defence: Defence) -> LiveEvent {
    let (verdict, latency_us) = admit_timed(csharp, defence);
    let runtime_unverified = !verdict.blocked && defence != Defence::None;
    LiveEvent {
        run_id: "single".to_string(),
        index: 0,
        total: 1,
        payload_id: "custom".to_string(),
        family: "custom".to_string(),
        spoken_command: "(pasted C#)".to_string(),
        effect: String::new(),
        defence: defence.api_label().to_string(),
        verdict_label: verdict_label(verdict.blocked, defence, "unknown_custom").to_string(),
        runtime_unverified,
        latency_us,
        expected_classification: "unknown_custom".to_string(),
        csharp: csharp.to_string(),
        verdict,
    }
}

fn percentile(sorted: &[f64], q: f64) -> f64 {
    if sorted.is_empty() {
        return 0.0;
    }
    let idx = (((sorted.len() - 1) as f64) * q).round() as usize;
    sorted[idx]
}

/// Build the terminal `completed` summary event for a sweep.
pub fn build_summary(run_id: &str, total: usize, rejected: usize, latencies: &[f64]) -> LiveEvent2 {
    let mut sorted = latencies.to_vec();
    sorted.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
    let mean = if sorted.is_empty() {
        0.0
    } else {
        sorted.iter().sum::<f64>() / sorted.len() as f64
    };
    LiveEvent2 {
        kind: "completed".to_string(),
        run_id: run_id.to_string(),
        total,
        rejected,
        admitted: total - rejected,
        median_latency_us: (percentile(&sorted, 0.50) * 10.0).round() / 10.0,
        mean_latency_us: (mean * 10.0).round() / 10.0,
        p95_latency_us: (percentile(&sorted, 0.95) * 10.0).round() / 10.0,
    }
}

/// The terminal summary event (`type: "completed"`).
#[derive(Debug, Clone, Serialize)]
pub struct LiveEvent2 {
    #[serde(rename = "type")]
    pub kind: String,
    pub run_id: String,
    pub total: usize,
    pub rejected: usize,
    pub admitted: usize,
    pub median_latency_us: f64,
    pub mean_latency_us: f64,
    pub p95_latency_us: f64,
}

/// Fully evaluate a sweep and return the ordered wire events
/// (`[case, case, …, completed]`) as JSON — the oracle used by both the SSE
/// handler's parity tests and any non-streaming caller.
pub fn collect_run(
    attacks: &[Attack],
    run_id: &str,
    ids: &[String],
    defence: Defence,
) -> Vec<serde_json::Value> {
    let total = ids.len();
    let mut out = Vec::with_capacity(total + 1);
    let mut latencies = Vec::with_capacity(total);
    let mut rejected = 0usize;
    for (index, id) in ids.iter().enumerate() {
        let Some(a) = attacks.iter().find(|a| &a.id == id) else {
            continue;
        };
        let ev = build_case_event(run_id, index, total, a, defence);
        if ev.verdict.blocked {
            rejected += 1;
        }
        latencies.push(ev.latency_us);
        out.push(serde_json::to_value(&ev).expect("serialize LiveEvent"));
    }
    let summary = build_summary(run_id, total, rejected, &latencies);
    out.push(serde_json::to_value(&summary).expect("serialize summary"));
    out
}
