use std::collections::HashSet;

use dcvr_behaviour_dsl::{Action, ActionPlan};

use crate::verdict::CsharpViolation;

/// Capability tokens each action legitimately needs in generated C#.
fn capabilities_for(action: &Action) -> &'static [&'static str] {
    match action {
        Action::SetColor { .. } => &["color"],
        Action::SetScale { .. } => &["localScale"],
        Action::Move { .. } => &["position", "Translate"],
        Action::Rotate { .. } => &["Rotate", "rotation"],
        Action::SpawnPrimitive { .. } => &["CreatePrimitive", "Instantiate"],
        Action::SetPhysics { .. } => &["Rigidbody", "mass", "useGravity"],
    }
}

/// The universe of capability tokens we detect. If a token appears in the C# but
/// the plan authorizes none of its owning actions, the C# is doing MORE than the
/// plan said -> inconsistency.
const CAPABILITY_UNIVERSE: &[&str] = &[
    "color",
    "localScale",
    "position",
    "Translate",
    "Rotate",
    "rotation",
    "CreatePrimitive",
    "Instantiate",
    "Rigidbody",
    "mass",
    "useGravity",
];

/// Heuristic consistency check: the C# candidate must exercise no capability that
/// the action plan does not authorize. Substring-based and intentionally
/// over-approximate (fail-closed); the action plan remains the safety source of
/// truth, and the sandbox is the behavioural backstop.
pub fn consistency_check(plan: &ActionPlan, csharp: &str) -> Vec<CsharpViolation> {
    let mut allowed: HashSet<&str> = HashSet::new();
    for action in &plan.actions {
        for token in capabilities_for(action) {
            allowed.insert(token);
        }
    }

    let mut violations = Vec::new();
    for cap in CAPABILITY_UNIVERSE {
        if csharp.contains(cap) && !allowed.contains(cap) {
            violations.push(CsharpViolation::InconsistentCapability((*cap).to_string()));
        }
    }
    violations.sort_by(|a, b| format!("{a:?}").cmp(&format!("{b:?}")));
    violations.dedup();
    violations
}
