use std::collections::HashSet;

use dcvr_behaviour_dsl::{Action, ActionPlan};

use crate::verdict::CsharpViolation;

/// Capability tokens each action legitimately needs in generated C#.
fn capabilities_for(action: &Action) -> &'static [&'static str] {
    match action {
        // `material` belongs here as well as to SetMaterial. There is no way to set a
        // renderer's colour in Unity without going through `.material`, so when that token
        // entered the universe alongside SetMaterial, the canonical set-colour candidate
        // (`r.material.color = ...`) started failing its own consistency check. Granting
        // it widens nothing: the token only reaches the shared appearance surface that
        // SetColor is already authorised to write.
        Action::SetColor { .. } => &["color", "material"],
        Action::SetScale { .. } => &["localScale"],
        Action::Move { .. } => &["position", "Translate"],
        Action::Rotate { .. } => &["Rotate", "rotation"],
        Action::SpawnPrimitive { .. } => &["CreatePrimitive", "Instantiate"],
        Action::SetPhysics { .. } => &["Rigidbody", "mass", "useGravity"],
        Action::SetMaterial { .. } => &["material", "SetColor", "SetFloat"],
        Action::SpawnLight { .. } => &["Light", "intensity", "range"],
        Action::SpawnText { .. } => &["TextMesh", "text"],
        Action::Orbit { .. } => &["RotateAround", "Rotate"],
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
    "material",
    "SetColor",
    "SetFloat",
    "Light",
    "intensity",
    "range",
    "TextMesh",
    "text",
    "RotateAround",
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
