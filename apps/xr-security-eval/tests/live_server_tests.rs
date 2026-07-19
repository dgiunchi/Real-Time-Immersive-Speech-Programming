//! HTTP-boundary tests for the demo console server (checks 7–10 of the agreed
//! list, plus wire-level ordering). Driven in-process via `tower::oneshot` — no
//! sockets, no browser, no flakiness.

use axum::body::Body;
use axum::http::{Request, StatusCode};
use http_body_util::BodyExt;
use serde_json::{json, Value};
use tower::ServiceExt;
use xr_security_eval::server::app_with;

fn json_post(uri: &str, body: Value) -> Request<Body> {
    Request::builder()
        .method("POST")
        .uri(uri)
        .header("content-type", "application/json")
        .body(Body::from(body.to_string()))
        .unwrap()
}

fn get(uri: &str) -> Request<Body> {
    Request::builder()
        .method("GET")
        .uri(uri)
        .body(Body::empty())
        .unwrap()
}

async fn to_json(resp: axum::response::Response) -> Value {
    let bytes = resp.into_body().collect().await.unwrap().to_bytes();
    serde_json::from_slice(&bytes).unwrap()
}

/// Parse an `text/event-stream` body into its JSON `data:` events.
fn parse_sse(text: &str) -> Vec<Value> {
    text.split("\n\n")
        .filter_map(|block| {
            let data: String = block
                .lines()
                .filter_map(|l| l.strip_prefix("data:"))
                .map(|s| s.trim_start())
                .collect::<Vec<_>>()
                .join("\n");
            if data.is_empty() {
                None
            } else {
                serde_json::from_str(&data).ok()
            }
        })
        .collect()
}

struct RunInfo {
    run_id: String,
    total: usize,
}

async fn create_run(app: &axum::Router, defence: &str) -> RunInfo {
    let resp = app
        .clone()
        .oneshot(json_post("/api/runs", json!({ "defence": defence })))
        .await
        .unwrap();
    assert_eq!(resp.status(), StatusCode::OK);
    let v = to_json(resp).await;
    RunInfo {
        run_id: v["run_id"].as_str().unwrap().to_string(),
        total: v["total_cases"].as_u64().unwrap() as usize,
    }
}

async fn stream_run(app: &axum::Router, run_id: &str) -> Vec<Value> {
    let resp = app
        .clone()
        .oneshot(get(&format!("/api/runs/{run_id}/events")))
        .await
        .unwrap();
    assert_eq!(resp.status(), StatusCode::OK);
    let bytes = resp.into_body().collect().await.unwrap().to_bytes();
    parse_sse(std::str::from_utf8(&bytes).unwrap())
}

// (7) Oversize custom C# is rejected before parsing.
#[tokio::test]
async fn oversize_custom_csharp_is_rejected() {
    let app = app_with(0);
    let big = "x".repeat(20_001); // > MAX_CUSTOM_CSHARP_BYTES, < 64 KB body cap
    let resp = app
        .oneshot(json_post(
            "/api/evaluate",
            json!({ "csharp": big, "defence": "deploy_hardened" }),
        ))
        .await
        .unwrap();
    assert_eq!(resp.status(), StatusCode::PAYLOAD_TOO_LARGE);
}

// (7b) A body beyond the global cap is rejected by the body limit.
#[tokio::test]
async fn body_over_the_limit_is_rejected() {
    let app = app_with(0);
    let huge = "y".repeat(70_000); // > 64 KB DefaultBodyLimit
    let resp = app
        .oneshot(json_post("/api/evaluate", json!({ "csharp": huge })))
        .await
        .unwrap();
    assert_eq!(resp.status(), StatusCode::PAYLOAD_TOO_LARGE);
}

// (8) Script/HTML content round-trips as a JSON string on a JSON response — the
// API never emits it as HTML (the frontend renders via textContent).
#[tokio::test]
async fn script_content_roundtrips_as_json_not_html() {
    let app = app_with(0);
    let payload = "public class GeneratedBehaviour : MonoBehaviour { \
                   /* </script><img src=x onerror=alert(1)> */ void Start(){} }";
    let resp = app
        .oneshot(json_post(
            "/api/evaluate",
            json!({ "csharp": payload, "defence": "no_defence" }),
        ))
        .await
        .unwrap();
    assert_eq!(resp.status(), StatusCode::OK);
    let ct = resp.headers()["content-type"].to_str().unwrap().to_string();
    assert!(
        ct.starts_with("application/json"),
        "served as JSON, not HTML"
    );
    let v = to_json(resp).await;
    assert!(v["csharp"].as_str().unwrap().contains("</script>"));
}

// (2 over the wire + 10) Two runs stream in order and never mix events.
#[tokio::test]
async fn two_runs_stream_in_order_and_never_mix() {
    let app = app_with(0);
    let r1 = create_run(&app, "deploy_hardened").await;
    let r2 = create_run(&app, "security_only").await;
    assert_ne!(r1.run_id, r2.run_id);

    let ev1 = stream_run(&app, &r1.run_id).await;
    let ev2 = stream_run(&app, &r2.run_id).await;

    for (label, ev, info) in [("r1", &ev1, &r1), ("r2", &ev2, &r2)] {
        assert_eq!(ev.len(), info.total + 1, "{label}: N cases + summary");
        assert_eq!(ev.last().unwrap()["type"], "completed", "{label} completed");
        for (i, e) in ev[..info.total].iter().enumerate() {
            assert_eq!(e["index"].as_u64().unwrap() as usize, i, "{label} order");
            // every case event carries ITS OWN run_id — no cross-run mixing.
            assert_eq!(e["run_id"].as_str().unwrap(), info.run_id, "{label} run_id");
        }
        assert_eq!(ev.last().unwrap()["run_id"].as_str().unwrap(), info.run_id);
    }
}

// (10b) A run id is consumed once — a second stream of the same id is 404.
#[tokio::test]
async fn run_id_is_consumed_once() {
    let app = app_with(0);
    let r = create_run(&app, "deploy_hardened").await;
    let first = stream_run(&app, &r.run_id).await;
    assert_eq!(first.len(), r.total + 1);

    let resp = app
        .clone()
        .oneshot(get(&format!("/api/runs/{}/events", r.run_id)))
        .await
        .unwrap();
    assert_eq!(resp.status(), StatusCode::NOT_FOUND);

    // A malformed run id is a 400, not a panic.
    let bad = app
        .oneshot(get("/api/runs/not-a-run/events"))
        .await
        .unwrap();
    assert_eq!(bad.status(), StatusCode::BAD_REQUEST);
}

// (9) A client disconnecting mid-stream does not crash or wedge the server.
#[tokio::test]
async fn client_disconnect_midstream_does_not_crash_server() {
    let app = app_with(5); // small pacing so the stream is incremental
    let r = create_run(&app, "deploy_hardened").await;

    let resp = app
        .clone()
        .oneshot(get(&format!("/api/runs/{}/events", r.run_id)))
        .await
        .unwrap();
    assert_eq!(resp.status(), StatusCode::OK);
    // Read a single frame, then drop the body — simulates the browser going away.
    let mut body = resp.into_body();
    let _first = body.frame().await;
    drop(body);

    // The server still serves a fresh run to completion.
    let r2 = create_run(&app, "deploy_hardened").await;
    let ev = stream_run(&app, &r2.run_id).await;
    assert_eq!(ev.last().unwrap()["type"], "completed");
}

// The natural-language lane is advisory-only and clearly labelled as such.
#[tokio::test]
async fn advisory_lane_is_non_authoritative() {
    let app = app_with(0);
    let resp = app
        .oneshot(json_post(
            "/api/advisory",
            json!({ "command": "move the user slowly towards the wall" }),
        ))
        .await
        .unwrap();
    assert_eq!(resp.status(), StatusCode::OK);
    let v = to_json(resp).await;
    assert_eq!(v["authoritative"], false);
    assert!(v["note"]
        .as_str()
        .unwrap()
        .contains("NOT THE CODE-ADMISSION"));
}

// Unknown payload id is a clean 404.
#[tokio::test]
async fn unknown_payload_id_is_404() {
    let app = app_with(0);
    let resp = app
        .oneshot(json_post(
            "/api/evaluate",
            json!({ "payload_id": "nope-99", "defence": "deploy_hardened" }),
        ))
        .await
        .unwrap();
    assert_eq!(resp.status(), StatusCode::NOT_FOUND);
}

// The guardrail hot-swap (`POST /api/mode`) changes the server's default verdict
// live: a sweep that omits `defence` follows the current mode.
#[tokio::test]
async fn guardrail_hot_swap_changes_default_behaviour() {
    let app = app_with(0);

    // default is protected
    let m = to_json(app.clone().oneshot(get("/api/mode")).await.unwrap()).await;
    assert_eq!(m["mode"], "deploy_hardened");
    assert_eq!(m["guardrail_on"], true);

    // swap to bypass; a run with NO explicit defence now blocks nothing
    let sw = to_json(
        app.clone()
            .oneshot(json_post("/api/mode", json!({ "mode": "off" })))
            .await
            .unwrap(),
    )
    .await;
    assert_eq!(sw["mode"], "no_defence");
    assert_eq!(sw["guardrail_on"], false);

    let r = to_json(
        app.clone()
            .oneshot(json_post("/api/runs", json!({})))
            .await
            .unwrap(),
    )
    .await;
    let ev = stream_run(&app, r["run_id"].as_str().unwrap()).await;
    assert_eq!(
        ev.last().unwrap()["rejected"].as_u64().unwrap(),
        0,
        "bypass blocks nothing — attacks would reach the headset"
    );

    // swap back to protected; the same sweep now blocks 38/40
    app.clone()
        .oneshot(json_post("/api/mode", json!({ "mode": "on" })))
        .await
        .unwrap();
    let r2 = to_json(
        app.clone()
            .oneshot(json_post("/api/runs", json!({})))
            .await
            .unwrap(),
    )
    .await;
    let ev2 = stream_run(&app, r2["run_id"].as_str().unwrap()).await;
    assert_eq!(
        ev2.last().unwrap()["rejected"].as_u64().unwrap(),
        38,
        "protected blocks 38/40 (the paper's 95%)"
    );
}

#[tokio::test]
async fn invalid_guardrail_mode_is_400() {
    let app = app_with(0);
    let resp = app
        .oneshot(json_post("/api/mode", json!({ "mode": "banana" })))
        .await
        .unwrap();
    assert_eq!(resp.status(), StatusCode::BAD_REQUEST);
}
