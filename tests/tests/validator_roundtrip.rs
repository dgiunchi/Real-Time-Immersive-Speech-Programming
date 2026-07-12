//! Integration test: JSON -> validate -> decision (no sockets).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_code_policy::{validate_json, Decision};

#[test]
fn valid_plan_json_is_approved() {
    let json = r##"{"schema_version":"1.0","request_id":"r1","target":"selected_object","actions":[{"type":"set_color","color":"#FF0000"}]}"##;
    let out = validate_json(json);
    assert_eq!(out.decision, Decision::ApproveActionPlan);
    assert!(out.violations.is_empty());
}

#[test]
fn malformed_json_is_rejected() {
    let out = validate_json("{ not json");
    assert_eq!(out.decision, Decision::RejectUnsafe);
    assert!(!out.violations.is_empty());
}

#[test]
fn spawn_explosion_is_rejected() {
    let json = r#"{"schema_version":"1.0","request_id":"r1","target":"selected_object","actions":[{"type":"spawn_primitive","shape":"cube","count":9999,"parent":"target"}]}"#;
    assert_eq!(validate_json(json).decision, Decision::RejectUnsafe);
}
