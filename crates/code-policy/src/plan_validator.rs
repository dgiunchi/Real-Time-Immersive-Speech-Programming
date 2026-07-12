use dcvr_behaviour_dsl::bounds::{
    is_valid_hex_color, MAX_ACTIONS, MAX_PLAN_JSON_BYTES, MAX_SPAWN_COUNT,
    MAX_TOTAL_SPAWNED_PER_SESSION, MOVE_AMPLITUDE_MAX, MOVE_SPEED_MAX, PHYSICS_MASS_MAX,
    PHYSICS_MASS_MIN, ROTATE_DEG_PER_SEC_MAX, SCALE_MAX, SCALE_MIN, SUPPORTED_SCHEMA_VERSION,
};
use dcvr_behaviour_dsl::{Action, ActionPlan};
use serde::{Deserialize, Serialize};

use crate::decision::Decision;
use crate::errors::ValidationError;

/// Maximum accepted JSON container-nesting depth (VAL-02). A deeply-nested plan
/// can exhaust the stack inside `serde_json`'s recursive-descent parser before
/// any bound is checked, so we count `{`/`[` depth in a single non-recursive
/// byte scan BEFORE parsing and fail closed past this limit. A legitimate
/// action plan nests only a handful of levels deep, so 32 is generous.
pub const MAX_JSON_NESTING_DEPTH: usize = 32;

/// The outcome of validating a plan: a decision plus every violation found and
/// the number of primitives the plan would spawn (for session accounting).
///
/// `target_peer` is an OPTIONAL per-recipient routing hint (R2): when set, a
/// Unity client applies this decision only if the value equals its own peer id
/// (null/empty = broadcast). It is `None` here — R2 populates it downstream.
/// `skip_serializing_if`/`default` keep previously-emitted JSON valid.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PolicyOutcome {
    pub decision: Decision,
    pub violations: Vec<ValidationError>,
    pub spawned_in_plan: u32,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub target_peer: Option<String>,
}

/// Validate an already-parsed plan. Fail-closed: any violation -> `RejectUnsafe`.
pub fn validate_plan(plan: &ActionPlan) -> PolicyOutcome {
    let mut violations = Vec::new();

    if plan.schema_version != SUPPORTED_SCHEMA_VERSION {
        violations.push(ValidationError::UnsupportedSchemaVersion {
            expected: SUPPORTED_SCHEMA_VERSION.to_string(),
            found: plan.schema_version.clone(),
        });
    }
    if plan.request_id.trim().is_empty() {
        violations.push(ValidationError::EmptyRequestId);
    }
    if plan.actions.is_empty() {
        violations.push(ValidationError::EmptyActions);
    }
    if plan.actions.len() > MAX_ACTIONS {
        violations.push(ValidationError::TooManyActions {
            count: plan.actions.len(),
            max: MAX_ACTIONS,
        });
    }

    let mut spawned: u32 = 0;
    for action in &plan.actions {
        check_action(action, &mut violations, &mut spawned);
    }
    // A single plan may not exceed the whole-session spawn budget (cumulative
    // per-session enforcement is in the router; this bounds one plan).
    if spawned > MAX_TOTAL_SPAWNED_PER_SESSION {
        violations.push(ValidationError::SpawnBudgetExceeded {
            count: spawned,
            max: MAX_TOTAL_SPAWNED_PER_SESSION,
        });
    }

    let decision = if violations.is_empty() {
        Decision::ApproveActionPlan
    } else {
        Decision::RejectUnsafe
    };
    PolicyOutcome {
        decision,
        violations,
        spawned_in_plan: spawned,
        target_peer: None,
    }
}

/// Maximum container-nesting depth reached by `input`, scanning string literals
/// so a `{`/`[` inside a JSON string (or escaped quote) is not miscounted. Pure,
/// non-recursive, allocation-free, and total (never panics). Returns the deepest
/// `{`/`[` nesting observed; malformed brackets are left to the parser.
fn max_json_nesting_depth(input: &str) -> usize {
    let mut depth: usize = 0;
    let mut max: usize = 0;
    let mut in_string = false;
    let mut escaped = false;
    for &b in input.as_bytes() {
        if in_string {
            if escaped {
                escaped = false;
            } else if b == b'\\' {
                escaped = true;
            } else if b == b'"' {
                in_string = false;
            }
            continue;
        }
        match b {
            b'"' => in_string = true,
            b'{' | b'[' => {
                depth = depth.saturating_add(1);
                if depth > max {
                    max = depth;
                }
            }
            b'}' | b']' => depth = depth.saturating_sub(1),
            _ => {}
        }
    }
    max
}

/// Parse-then-validate. A parse failure is itself a fail-closed `RejectUnsafe`.
pub fn validate_json(input: &str) -> PolicyOutcome {
    // Fail-closed BEFORE allocating: reject oversized payloads so a hostile plan
    // cannot force a multi-megabyte Vec<Action> allocation prior to the count check.
    if input.len() > MAX_PLAN_JSON_BYTES {
        return PolicyOutcome {
            decision: Decision::RejectUnsafe,
            violations: vec![ValidationError::PayloadTooLarge {
                len: input.len(),
                max: MAX_PLAN_JSON_BYTES,
            }],
            spawned_in_plan: 0,
            target_peer: None,
        };
    }
    // VAL-02: reject deeply-nested JSON BEFORE parse so a hostile plan cannot
    // exhaust the stack in serde_json's recursive descent. Fail closed.
    let depth = max_json_nesting_depth(input);
    if depth > MAX_JSON_NESTING_DEPTH {
        return PolicyOutcome {
            decision: Decision::RejectUnsafe,
            violations: vec![ValidationError::NestingTooDeep {
                depth,
                max: MAX_JSON_NESTING_DEPTH,
            }],
            spawned_in_plan: 0,
            target_peer: None,
        };
    }
    match ActionPlan::from_json(input) {
        Ok(plan) => validate_plan(&plan),
        Err(e) => PolicyOutcome {
            decision: Decision::RejectUnsafe,
            violations: vec![ValidationError::MalformedJson(e.to_string())],
            spawned_in_plan: 0,
            target_peer: None,
        },
    }
}

fn push_range(
    v: &mut Vec<ValidationError>,
    value: f64,
    ok: bool,
    make: impl Fn(f64) -> ValidationError,
) {
    if !value.is_finite() {
        v.push(ValidationError::NonFiniteValue);
    } else if !ok {
        v.push(make(value));
    }
}

fn check_action(action: &Action, v: &mut Vec<ValidationError>, spawned: &mut u32) {
    match action {
        Action::SetColor { color } => {
            if !is_valid_hex_color(color) {
                v.push(ValidationError::InvalidColor {
                    value: color.clone(),
                });
            }
        }
        Action::SetScale { value } => {
            push_range(
                v,
                *value,
                *value >= SCALE_MIN && *value <= SCALE_MAX,
                |val| ValidationError::ScaleOutOfRange {
                    value: val,
                    min: SCALE_MIN,
                    max: SCALE_MAX,
                },
            );
        }
        Action::Move {
            speed, amplitude, ..
        } => {
            push_range(
                v,
                *speed,
                *speed >= 0.0 && *speed <= MOVE_SPEED_MAX,
                |val| ValidationError::MoveSpeedOutOfRange {
                    value: val,
                    max: MOVE_SPEED_MAX,
                },
            );
            if let Some(a) = amplitude {
                push_range(v, *a, *a >= 0.0 && *a <= MOVE_AMPLITUDE_MAX, |val| {
                    ValidationError::MoveAmplitudeOutOfRange {
                        value: val,
                        max: MOVE_AMPLITUDE_MAX,
                    }
                });
            }
        }
        Action::Rotate { deg_per_sec, .. } => {
            push_range(
                v,
                *deg_per_sec,
                *deg_per_sec >= 0.0 && *deg_per_sec <= ROTATE_DEG_PER_SEC_MAX,
                |val| ValidationError::RotateRateOutOfRange {
                    value: val,
                    max: ROTATE_DEG_PER_SEC_MAX,
                },
            );
        }
        Action::SpawnPrimitive { count, .. } => {
            if *count == 0 || *count > MAX_SPAWN_COUNT {
                v.push(ValidationError::SpawnCountOutOfRange {
                    count: *count,
                    max: MAX_SPAWN_COUNT,
                });
            } else {
                *spawned = spawned.saturating_add(*count);
            }
        }
        Action::SetPhysics { mass, .. } => {
            if let Some(m) = mass {
                push_range(
                    v,
                    *m,
                    *m >= PHYSICS_MASS_MIN && *m <= PHYSICS_MASS_MAX,
                    |val| ValidationError::PhysicsMassOutOfRange {
                        value: val,
                        min: PHYSICS_MASS_MIN,
                        max: PHYSICS_MASS_MAX,
                    },
                );
            }
        }
    }
}
