//! Automated XR privacy & manipulation attack/defence evaluation.
//!
//! Runs a curated corpus of malicious (and benign) speech-to-C# commands through the
//! REAL DreamCodeVR+ validator (`dcvr_csharp_policy`) at three defence levels and reports
//! how many attacks succeed with no defence vs how many the backend blocks. Everything
//! runs locally — no Quest, no network. This is the reusable core; `main.rs` is the CLI
//! and `bin/demo.rs` is the localhost demo, both built on it.

use dcvr_csharp_policy::{validate_csharp_freeform_profile, CsharpDecision, HardeningProfile};
use serde::{Deserialize, Serialize};

/// The corpus, embedded at build time so the binary is self-contained.
pub const CORPUS_JSON: &str = include_str!("../attacks/corpus.json");

/// The three defence levels evaluated.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Defence {
    /// Original DreamCodeVR: unvalidated C# runs — every attack succeeds.
    None,
    /// `HardeningProfile::CreativeFreedom` — system-access bans only.
    SecurityOnly,
    /// `HardeningProfile::DeployHardened` — + perceptual / XR-device bans.
    FullyHardened,
}

impl Defence {
    pub fn label(self) -> &'static str {
        match self {
            Defence::None => "no-defence",
            Defence::SecurityOnly => "security-only",
            Defence::FullyHardened => "fully-hardened",
        }
    }
    pub const ALL: [Defence; 3] = [Defence::None, Defence::SecurityOnly, Defence::FullyHardened];
}

#[derive(Debug, Clone, Deserialize, Serialize)]
pub struct Attack {
    pub id: String,
    pub cls: String,
    pub command: String,
    pub csharp: String,
    pub effect: String,
    pub should_block: bool,
}

#[derive(Debug, Deserialize)]
pub struct Corpus {
    pub attacks: Vec<Attack>,
}

/// Parse the embedded corpus.
pub fn load_corpus() -> Corpus {
    serde_json::from_str(CORPUS_JSON).expect("embedded corpus.json is valid")
}

/// Result of running one payload through one defence level.
#[derive(Debug, Clone, Serialize)]
pub struct Outcome {
    /// The malicious code was stopped before it could run.
    pub blocked: bool,
    /// The banned tokens / reasons the validator reported (empty at `Defence::None`).
    pub reasons: Vec<String>,
}

/// Run one C# payload through the real validator at the given defence level.
pub fn evaluate(csharp: &str, defence: Defence) -> Outcome {
    match defence {
        // No validation is applied — the code would compile and run, so the attack lands.
        Defence::None => Outcome {
            blocked: false,
            reasons: vec![],
        },
        Defence::SecurityOnly | Defence::FullyHardened => {
            let profile = if defence == Defence::SecurityOnly {
                HardeningProfile::CreativeFreedom
            } else {
                HardeningProfile::DeployHardened
            };
            let verdict = validate_csharp_freeform_profile(csharp, profile);
            Outcome {
                blocked: verdict.decision == CsharpDecision::Reject,
                reasons: verdict.violations.iter().map(|v| v.to_string()).collect(),
            }
        }
    }
}

/// Per-attack row across all three defence levels (for the report + demo).
#[derive(Debug, Clone, Serialize)]
pub struct AttackReport {
    pub id: String,
    pub cls: String,
    pub command: String,
    pub csharp: String,
    pub effect: String,
    pub should_block: bool,
    /// `blocked` at [none, security, hardened].
    pub blocked: [bool; 3],
    pub reasons_security: Vec<String>,
    pub reasons_hardened: Vec<String>,
}

pub fn evaluate_attack(a: &Attack) -> AttackReport {
    let sec = evaluate(&a.csharp, Defence::SecurityOnly);
    let hard = evaluate(&a.csharp, Defence::FullyHardened);
    AttackReport {
        id: a.id.clone(),
        cls: a.cls.clone(),
        command: a.command.clone(),
        csharp: a.csharp.clone(),
        effect: a.effect.clone(),
        should_block: a.should_block,
        blocked: [false, sec.blocked, hard.blocked],
        reasons_security: sec.reasons,
        reasons_hardened: hard.reasons,
    }
}
