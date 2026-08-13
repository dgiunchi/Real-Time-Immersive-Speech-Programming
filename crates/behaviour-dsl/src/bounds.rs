//! Bound constants — the single source of truth for action-plan limits.
//!
//! These MUST be mirrored by the Unity client's defensive clamps
//! (`unity/Runtime/ProtocolModels.cs`) and by `docs/action-plan-v1.schema.json`.
//! The Rust backend is the authoritative validator; the client re-clamps.

/// Only this schema version is accepted in Phase 1.
pub const SUPPORTED_SCHEMA_VERSION: &str = "1.0";

/// Maximum number of actions in a single plan.
pub const MAX_ACTIONS: usize = 48;
/// Maximum `count` for a single `spawn_primitive` action.
pub const MAX_SPAWN_COUNT: u32 = 32;
/// Maximum primitives spawned across a whole session (enforced in router/Unity).
pub const MAX_TOTAL_SPAWNED_PER_SESSION: u32 = 512;
/// Maximum generated hierarchy depth (placeholder; enforced when nesting lands).
pub const MAX_HIERARCHY_DEPTH: u32 = 3;

pub const SCALE_MIN: f64 = 0.1;
pub const SCALE_MAX: f64 = 5.0;
pub const MOVE_SPEED_MAX: f64 = 5.0;
pub const MOVE_AMPLITUDE_MAX: f64 = 5.0;
/// Cumulative travel cap for a `move` with `mode = once` (metres). Enforced
/// client-side by the Unity mover (a `move=once` may not translate an object
/// more than this in total) and mirrored in `ProtocolModels.cs`
/// (`MoveMaxTotalDistance`).
pub const MOVE_MAX_TOTAL_DISTANCE: f64 = 10.0;
pub const ROTATE_DEG_PER_SEC_MAX: f64 = 360.0;
pub const PHYSICS_MASS_MIN: f64 = 0.1;
pub const PHYSICS_MASS_MAX: f64 = 100.0;

// --- Perceptual / embodied detection thresholds (TRACK U2) ---
// These are DISCLOSURE / MONITORING thresholds for the deployable Mode-C
// executor. They NEVER block free creation: when armed (opt-in) and exceeded,
// the executor emits a transparency event (and, for hard-occlusion cases,
// fail-closed rejection) — but creative builds are never restricted by them.
// Mirrored by `unity/Runtime/ProtocolModels.cs`.
//
// Cumulative real-world head displacement (metres) that, while a content-motion
// plan is active, is flagged as possible herding (Ishii 2016 optical marionette;
// Casey 2021 human joystick). MONITOR-ONLY — disclosed, not blocked.
pub const USER_DRIFT_DISCLOSURE_THRESHOLD_M: f64 = 1.0;
// Aggregate coherent optic-flow budget: fraction of generated objects that may
// translate coherently in the same direction within one plan before the plan is
// flagged as a vection/world-drift pattern (Casey 2021 slow-sine disorientation;
// Ishii 2016 stripes). MONITOR-ONLY.
pub const VECTION_COHERENT_FLOW_BUDGET: f64 = 0.75;
// AdCube-style spatial confinement (Lee 2021): when armed, generated content is
// expected to live within this radius (metres) of the generated root; content
// staged beyond it (e.g. blind-spot placement) is DISCLOSED. MONITOR-ONLY.
pub const CONFINEMENT_REGION_RADIUS_M: f64 = 8.0;
// Fujita 2023 shielding cue: a generated occluder whose depth differs from the
// object it occludes by more than this (metres) is flagged as a possible
// disguise/shield. MONITOR-ONLY (the user already sees it is AI-generated).
pub const FOCAL_DEPTH_MISMATCH_M: f64 = 0.25;

/// Max accepted action-plan JSON size (anti-allocation-DoS; checked before parse).
pub const MAX_PLAN_JSON_BYTES: usize = 256 * 1024;

// --- Placement and appearance bounds (expressiveness, Phase 2) ---
// These exist so a plan can BUILD something — place a block, tint a light — rather than
// only mutate one object. Every one is a number with a hard range: there is still no field
// anywhere in the grammar where a path, a socket or a namespace could be written, so the
// "unsafe is unrepresentable" property is untouched by widening what is expressible.
/// Placement envelope around the creation zone (metres, per axis).
pub const POSITION_ABS_MAX: f64 = 20.0;
/// Per-axis size of a spawned primitive (metres).
pub const SIZE_MIN: f64 = 0.02;
pub const SIZE_MAX: f64 = 10.0;
/// Light intensity and range.
pub const LIGHT_INTENSITY_MAX: f64 = 8.0;
pub const LIGHT_RANGE_MAX: f64 = 30.0;
/// Emissive strength, and how many lights one session may create (fill-rate guard).
pub const EMISSION_MAX: f64 = 6.0;
pub const MAX_LIGHTS_PER_SESSION: u32 = 12;
/// World-space text: bounded length, and no control characters (checked separately).
pub const MAX_TEXT_LEN: usize = 64;
pub const TEXT_SIZE_MIN: f64 = 0.02;
pub const TEXT_SIZE_MAX: f64 = 2.0;

/// Returns true iff `s` is a `#RRGGBB` hex colour. Pure, allocation-free, and
/// total (no panics): the `len == 7` check short-circuits before indexing.
pub fn is_valid_hex_color(s: &str) -> bool {
    let bytes = s.as_bytes();
    bytes.len() == 7 && bytes.first() == Some(&b'#') && bytes[1..].iter().all(u8::is_ascii_hexdigit)
}

#[cfg(test)]
mod tests {
    #![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
    use super::*;

    #[test]
    fn move_max_total_distance_is_ten() {
        // Shared contract: must equal ProtocolModels.cs `MoveMaxTotalDistance`.
        assert_eq!(MOVE_MAX_TOTAL_DISTANCE, 10.0);
    }

    #[test]
    fn perceptual_detection_thresholds_match_unity_contract() {
        // These MUST equal the mirrored values in unity/Runtime/ProtocolModels.cs.
        assert_eq!(USER_DRIFT_DISCLOSURE_THRESHOLD_M, 1.0);
        assert_eq!(VECTION_COHERENT_FLOW_BUDGET, 0.75);
        assert_eq!(CONFINEMENT_REGION_RADIUS_M, 8.0);
        assert_eq!(FOCAL_DEPTH_MISMATCH_M, 0.25);
    }
}
