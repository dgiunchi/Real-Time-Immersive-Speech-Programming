//! Observability tests (required tests 21, 22).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_observability::{JsonlWriter, TimingEvent};

fn sample() -> TimingEvent {
    TimingEvent {
        request_id: "req-1".to_string(),
        peer_id: "peer-1".to_string(),
        mode: "action_plan_fast".to_string(),
        decision: "ApproveActionPlan".to_string(),
        t_received: 1,
        t_validated: 2,
        t_sent: 3,
        validation_ms: 1,
        errors: vec![],
        action_count: 1,
        spawned_count: 0,
    }
}

// Test 21: a timing event serializes to a single JSONL line.
#[test]
fn timing_event_serializes_to_jsonl() {
    let mut buf: Vec<u8> = Vec::new();
    {
        let mut w = JsonlWriter::new(&mut buf);
        w.write_event(&sample()).unwrap();
    }
    let text = String::from_utf8(buf).unwrap();
    assert!(text.ends_with('\n'));
    assert_eq!(text.lines().count(), 1);
    assert!(text.contains("\"request_id\":\"req-1\""));
}

// Test 22: the event schema contains NO raw audio/transcript/secret fields.
#[test]
fn timing_event_has_no_sensitive_fields() {
    let json = serde_json::to_value(sample()).unwrap();
    let obj = json.as_object().unwrap();
    let allowed = [
        "request_id",
        "peer_id",
        "mode",
        "decision",
        "t_received",
        "t_validated",
        "t_sent",
        "validation_ms",
        "errors",
        "action_count",
        "spawned_count",
    ];
    for key in obj.keys() {
        assert!(
            allowed.contains(&key.as_str()),
            "unexpected field leaked: {key}"
        );
    }
    for forbidden in ["audio", "transcript", "pcm", "secret", "api_key", "image"] {
        assert!(
            !obj.contains_key(forbidden),
            "sensitive field present: {forbidden}"
        );
    }
}
