//! Validator tests (required tests 6, 7, 8, 10, 11, 12, 13, 15, 16, 17).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_code_policy::{validate_json, validate_plan, Decision, ValidationError};

fn plan(actions_json: &str) -> String {
    format!(
        r#"{{"schema_version":"1.0","request_id":"r1","target":"selected_object","actions":{actions_json}}}"#
    )
}

// Test 15 / 6: a valid plan is approved.
#[test]
fn valid_plan_approved() {
    let out = validate_json(&plan(r##"[{"type":"set_color","color":"#00FF00"}]"##));
    assert_eq!(out.decision, Decision::ApproveActionPlan);
    assert!(out.violations.is_empty());
}

// Test 7: a valid move is approved.
#[test]
fn valid_move_approved() {
    let out = validate_json(&plan(
        r#"[{"type":"move","axis":"y","mode":"oscillate","speed":1.0,"amplitude":1.0}]"#,
    ));
    assert_eq!(out.decision, Decision::ApproveActionPlan);
}

// Test 8: empty actions rejected.
#[test]
fn empty_actions_rejected() {
    let out = validate_json(&plan("[]"));
    assert_eq!(out.decision, Decision::RejectUnsafe);
    assert!(out.violations.contains(&ValidationError::EmptyActions));
}

// Test 10: spawn count over 8 rejected.
#[test]
fn spawn_count_over_max_rejected() {
    let out = validate_json(&plan(
        r#"[{"type":"spawn_primitive","shape":"cube","count":9999,"parent":"target"}]"#,
    ));
    assert_eq!(out.decision, Decision::RejectUnsafe);
    assert!(out
        .violations
        .iter()
        .any(|e| matches!(e, ValidationError::SpawnCountOutOfRange { .. })));
}

// Test 11: scale over 5 rejected.
#[test]
fn scale_over_max_rejected() {
    let out = validate_json(&plan(r#"[{"type":"set_scale","value":1000.0}]"#));
    assert_eq!(out.decision, Decision::RejectUnsafe);
    assert!(out
        .violations
        .iter()
        .any(|e| matches!(e, ValidationError::ScaleOutOfRange { .. })));
}

// Test 12: speed over 5 rejected.
#[test]
fn speed_over_max_rejected() {
    let out = validate_json(&plan(
        r#"[{"type":"move","axis":"x","mode":"once","speed":99.0}]"#,
    ));
    assert_eq!(out.decision, Decision::RejectUnsafe);
    assert!(out
        .violations
        .iter()
        .any(|e| matches!(e, ValidationError::MoveSpeedOutOfRange { .. })));
}

// Test 13: invalid schema version rejected.
#[test]
fn invalid_schema_version_rejected() {
    let json = r##"{"schema_version":"9.9","request_id":"r1","target":"selected_object","actions":[{"type":"set_color","color":"#FFFFFF"}]}"##;
    let out = validate_json(json);
    assert_eq!(out.decision, Decision::RejectUnsafe);
    assert!(out
        .violations
        .iter()
        .any(|e| matches!(e, ValidationError::UnsupportedSchemaVersion { .. })));
}

// Test 16: an unsafe plan returns RejectUnsafe.
#[test]
fn unsafe_plan_rejected() {
    let out = validate_json(&plan(r#"[{"type":"set_color","color":"not-a-color"}]"#));
    assert_eq!(out.decision, Decision::RejectUnsafe);
}

// Test 17: errors are clear and structured (carry the offending value).
#[test]
fn errors_are_structured() {
    let out = validate_json(&plan(r#"[{"type":"set_scale","value":1000.0}]"#));
    match out.violations.first() {
        Some(ValidationError::ScaleOutOfRange { value, max, .. }) => {
            assert_eq!(*value, 1000.0);
            assert_eq!(*max, 5.0);
        }
        other => panic!("expected structured ScaleOutOfRange, got {other:?}"),
    }
}

// Non-finite values are rejected (fail-closed against NaN/Infinity).
#[test]
fn non_finite_value_rejected() {
    let json = r#"{"schema_version":"1.0","request_id":"r1","target":"selected_object","actions":[{"type":"set_scale","value":1e9999}]}"#;
    // serde_json parses 1e9999 as Infinity-or-error; either way we must not approve.
    assert_eq!(validate_json(json).decision, Decision::RejectUnsafe);
}

// Approve iff no violations (the core invariant), checked directly on a model.
#[test]
fn approve_iff_no_violations() {
    use dcvr_behaviour_dsl::{Action, ActionPlan, Target};
    let p = ActionPlan {
        schema_version: "1.0".to_string(),
        request_id: "r1".to_string(),
        target: Target::SelectedObject,
        actions: vec![Action::SetColor {
            color: "#123456".to_string(),
        }],
    };
    let out = validate_plan(&p);
    assert_eq!(
        out.violations.is_empty(),
        out.decision == Decision::ApproveActionPlan
    );
}

// Review fix: a single plan may not exceed the session spawn budget (64).
#[test]
fn per_plan_spawn_budget_enforced() {
    let one = r#"{"type":"spawn_primitive","shape":"cube","count":8,"parent":"target"}"#;
    let actions = std::iter::repeat_n(one, 16).collect::<Vec<_>>().join(",");
    let json = format!(
        r#"{{"schema_version":"1.0","request_id":"r","target":"scene_root","actions":[{actions}]}}"#
    );
    let out = validate_json(&json); // 16 x 8 = 128 > 64
    assert_eq!(out.decision, Decision::RejectUnsafe);
    assert!(out
        .violations
        .iter()
        .any(|e| matches!(e, ValidationError::SpawnBudgetExceeded { .. })));
}

// Review fix: oversized JSON is rejected BEFORE allocating/parsing.
#[test]
fn oversized_json_rejected_before_parse() {
    let big = format!(
        r#"{{"schema_version":"1.0","request_id":"r","target":"scene_root","actions":[{}]}}"#,
        "0,".repeat(100_000)
    );
    let out = validate_json(&big);
    assert_eq!(out.decision, Decision::RejectUnsafe);
    assert!(out
        .violations
        .iter()
        .any(|e| matches!(e, ValidationError::PayloadTooLarge { .. })));
}

// VAL-02: deeply-nested JSON is rejected BEFORE parse (anti stack-exhaustion DoS).
#[test]
fn deeply_nested_json_rejected_before_parse() {
    // ~200 nested arrays — well past the depth-32 guard, but tiny in bytes so it
    // passes the size check and would otherwise hit serde_json's recursive parser.
    let depth = 200usize;
    let nested = format!("{}{}", "[".repeat(depth), "]".repeat(depth));
    let json = format!(
        r#"{{"schema_version":"1.0","request_id":"r","target":"scene_root","actions":{nested}}}"#
    );
    let out = validate_json(&json);
    assert_eq!(out.decision, Decision::RejectUnsafe);
    assert!(out
        .violations
        .iter()
        .any(|e| matches!(e, ValidationError::NestingTooDeep { .. })));
}

// A normally-nested, otherwise-valid plan is NOT tripped by the depth guard.
#[test]
fn shallow_nesting_not_rejected_for_depth() {
    let out = validate_json(&plan(r##"[{"type":"set_color","color":"#00FF00"}]"##));
    assert!(out
        .violations
        .iter()
        .all(|e| !matches!(e, ValidationError::NestingTooDeep { .. })));
    assert_eq!(out.decision, Decision::ApproveActionPlan);
}

// Brackets inside a JSON string must not be counted as nesting depth.
#[test]
fn brackets_in_string_do_not_count_as_depth() {
    let many = "[".repeat(100);
    let out = validate_json(&plan(&format!(
        r#"[{{"type":"set_color","color":"{many}"}}]"#
    )));
    // It is rejected (bad colour), but NOT for nesting.
    assert!(out
        .violations
        .iter()
        .all(|e| !matches!(e, ValidationError::NestingTooDeep { .. })));
}

// R2 routing contract: `target_peer` is `None` by default and is OMITTED from
// serialized JSON (skip_serializing_if), so existing wire JSON stays valid;
// JSON without the key still deserializes (default).
#[test]
fn target_peer_defaults_to_none_and_is_omitted_from_json() {
    let out = validate_json(&plan(r##"[{"type":"set_color","color":"#00FF00"}]"##));
    assert_eq!(out.target_peer, None);

    let json = serde_json::to_string(&out).unwrap();
    assert!(
        !json.contains("target_peer"),
        "target_peer must be omitted when None: {json}"
    );

    let back: dcvr_code_policy::PolicyOutcome = serde_json::from_str(&json).unwrap();
    assert_eq!(back.target_peer, None);
    assert_eq!(back.decision, out.decision);
}
