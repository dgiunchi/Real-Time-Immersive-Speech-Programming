//! Parse-layer tests (required tests 6, 7, 9, 14). Bound/policy tests live in
//! the `code-policy` crate; this crate only models and parses.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_behaviour_dsl::{Action, ActionPlan, Target};

// Test 6: a valid set_color plan parses.
#[test]
fn valid_set_color_parses() {
    let json = r##"{"schema_version":"1.0","request_id":"r1","target":"selected_object","actions":[{"type":"set_color","color":"#FF0000"}]}"##;
    let plan = ActionPlan::from_json(json).unwrap();
    assert_eq!(plan.target, Target::SelectedObject);
    assert_eq!(plan.actions.len(), 1);
    assert!(matches!(plan.actions[0], Action::SetColor { ref color } if color == "#FF0000"));
}

// Test 7: a valid move plan parses with optional amplitude.
#[test]
fn valid_move_parses() {
    let json = r#"{"schema_version":"1.0","request_id":"r1","target":"selected_object","actions":[{"type":"move","axis":"y","mode":"oscillate","speed":1.0,"amplitude":1.0}]}"#;
    let plan = ActionPlan::from_json(json).unwrap();
    assert!(
        matches!(plan.actions[0], Action::Move { speed, .. } if (speed - 1.0).abs() < f64::EPSILON)
    );
}

// Test 9: an unknown action `type` is rejected at parse time.
#[test]
fn unknown_action_rejected() {
    let json = r#"{"schema_version":"1.0","request_id":"r1","target":"selected_object","actions":[{"type":"launch_missiles"}]}"#;
    assert!(ActionPlan::from_json(json).is_err());
}

// Test 14: malformed JSON is rejected (fail-closed, no panic).
#[test]
fn malformed_json_rejected() {
    assert!(ActionPlan::from_json("{ this is not json").is_err());
}

// Unknown top-level fields are rejected (deny_unknown_fields).
#[test]
fn unknown_top_level_field_rejected() {
    let json = r#"{"schema_version":"1.0","request_id":"r1","target":"selected_object","actions":[],"evil":true}"#;
    assert!(ActionPlan::from_json(json).is_err());
}

// Round-trip: a parsed plan re-serialises and re-parses identically.
#[test]
fn json_roundtrip() {
    let json = r#"{"schema_version":"1.0","request_id":"r1","target":"scene_root","actions":[{"type":"set_scale","value":2.0}]}"#;
    let plan = ActionPlan::from_json(json).unwrap();
    let reparsed = ActionPlan::from_json(&plan.to_json().unwrap()).unwrap();
    assert_eq!(plan, reparsed);
}
