//! Perceptual / embodied red-team corpus (backend-enforceable subset).
//!
//! The perceptual/embodied guards (personal-space sphere, FOV-occlusion, overlap,
//! cumulative drift) live in the DEPLOYABLE Mode-C Unity executor and are exercised
//! there by `GuardrailSelfTest.cs`. **Mode A is the original full-freedom baseline**
//! (arbitrary creative C#, incl. animation via `Time.time`), gated by `csharp-policy`
//! only against genuine SYSTEM access (file / net / reflection / process). This file
//! covers the backend-enforceable, plan-level defences.
//!
//! References: Krauß et al. (2024); Tseng et al. (2022); Casey et al. (2021);
//! Lee et al. (2021); Ishii et al. (2016); Valluripally et al. (2020).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_behaviour_dsl::bounds::MOVE_MAX_TOTAL_DISTANCE;
use dcvr_code_policy::{validate_json, Decision, ValidationError};

/// VAL-02 allocation / parse DoS via deeply-nested plan JSON (Valluripally 2020
/// availability leg). The depth guard fails closed BEFORE serde's recursive descent.
#[test]
fn redteam_deeply_nested_plan_json_rejected() {
    let deep = format!("{}{}", "[".repeat(256), "]".repeat(256));
    let outcome = validate_json(&deep);
    assert_eq!(outcome.decision, Decision::RejectUnsafe);
    assert!(outcome
        .violations
        .iter()
        .any(|v| matches!(v, ValidationError::NestingTooDeep { .. })));
}

/// Drift-cap contract (Casey 2021 human-joystick; Ishii 2016): the cumulative-travel
/// cap that bounds a `move=once` action is shared with the Unity executor
/// (`ProtocolModels.MoveMaxTotalDistance`); the parity test guards their equality.
#[test]
fn redteam_move_total_distance_cap_present() {
    assert_eq!(MOVE_MAX_TOTAL_DISTANCE, 10.0);
}
