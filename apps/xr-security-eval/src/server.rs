//! The demo console HTTP server (run-based API + SSE), factored out of the
//! binary so it is testable in-process with `tower::ServiceExt::oneshot`.
//!
//! Nothing here compiles or executes payloads — it is static code-admission
//! only. Design notes:
//! * run-based: `POST /api/runs` mints a run, `GET /api/runs/{id}/events`
//!   streams it; runs are consumed once so two sweeps can never mix events;
//! * loopback-only binding is enforced by the caller (`bin/demo.rs`);
//! * a small body cap, an oversize-C# pre-check, a bounded SSE channel, and a
//!   pending-run cap bound resource use; the SSE task stops when the client
//!   disconnects (its `send` fails), so a dropped browser cannot leak work.

use std::collections::HashMap;
use std::convert::Infallible;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use axum::extract::{DefaultBodyLimit, Path, State};
use axum::http::StatusCode;
use axum::response::sse::{Event, KeepAlive, Sse};
use axum::response::{Html, IntoResponse};
use axum::routing::{get, post};
use axum::{Json, Router};
use serde::Deserialize;
use tokio_stream::wrappers::ReceiverStream;
use tokio_stream::Stream;

use dcvr_csharp_policy::semantic_screen;

use crate::live::{build_case_event, build_custom_event, build_summary, MAX_CUSTOM_CSHARP_BYTES};
use crate::{evaluate_attack, load_corpus, Attack, Defence};

const PAGE: &str = include_str!("bin/demo.html");

/// Max pending (created-but-not-yet-streamed) sweeps — a memory backstop.
const MAX_PENDING_RUNS: usize = 16;
/// Bounded SSE channel so a slow/gone client can't grow memory unboundedly.
const SSE_CHANNEL_CAP: usize = 64;
/// Reject natural-language advisory text longer than this.
const MAX_ADVISORY_CHARS: usize = 2_000;

struct RunSpec {
    defence: Defence,
    ids: Vec<String>,
}

/// Shared server state.
#[derive(Clone)]
pub struct AppState {
    attacks: Arc<Vec<Attack>>,
    runs: Arc<Mutex<HashMap<String, RunSpec>>>,
    counter: Arc<AtomicU64>,
    pace_ms: u64,
}

/// Build the router with the default pacing (env `XR_DEMO_PACE_MS`, else 90 ms).
pub fn app() -> Router {
    let pace = std::env::var("XR_DEMO_PACE_MS")
        .ok()
        .and_then(|s| s.parse().ok())
        .unwrap_or(90);
    app_with(pace)
}

/// Build the router with an explicit per-case pacing (tests use `0`).
pub fn app_with(pace_ms: u64) -> Router {
    let corpus = load_corpus();
    let state = AppState {
        attacks: Arc::new(corpus.attacks),
        runs: Arc::new(Mutex::new(HashMap::new())),
        counter: Arc::new(AtomicU64::new(1)),
        pace_ms,
    };
    Router::new()
        .route("/", get(|| async { Html(PAGE) }))
        .route("/api/results", get(results))
        .route("/api/evaluate", post(evaluate_one))
        .route("/api/runs", post(create_run))
        .route("/api/runs/:run_id/events", get(run_events))
        .route("/api/advisory", post(advisory))
        // Small body cap: the largest legitimate body is one ~20 KB C# fragment.
        .layer(DefaultBodyLimit::max(64 * 1024))
        .with_state(state)
}

/// Batch: every payload's verdict at all three defence levels (the oracle the
/// live stream is tested against).
async fn results(State(st): State<AppState>) -> Json<serde_json::Value> {
    let per_attack: Vec<_> = st.attacks.iter().map(evaluate_attack).collect();
    Json(serde_json::json!({ "per_attack": per_attack }))
}

#[derive(Deserialize)]
struct EvalReq {
    payload_id: Option<String>,
    csharp: Option<String>,
    defence: Option<String>,
}

/// Evaluate a single payload: a corpus id, or a pasted C# fragment.
async fn evaluate_one(State(st): State<AppState>, Json(req): Json<EvalReq>) -> impl IntoResponse {
    let defence = req
        .defence
        .as_deref()
        .and_then(Defence::from_api)
        .unwrap_or(Defence::FullyHardened);

    if let Some(id) = req.payload_id.as_deref().filter(|s| !s.is_empty()) {
        match st.attacks.iter().find(|a| a.id == id) {
            Some(a) => {
                let ev = build_case_event("single", 0, 1, a, defence);
                Json(ev).into_response()
            }
            None => (StatusCode::NOT_FOUND, "unknown payload_id").into_response(),
        }
    } else if let Some(csharp) = req.csharp.as_deref() {
        let csharp = csharp.trim();
        if csharp.is_empty() {
            return (StatusCode::BAD_REQUEST, "empty csharp").into_response();
        }
        if csharp.len() > MAX_CUSTOM_CSHARP_BYTES {
            return (StatusCode::PAYLOAD_TOO_LARGE, "csharp too large").into_response();
        }
        let ev = build_custom_event(csharp, defence);
        Json(ev).into_response()
    } else {
        (StatusCode::BAD_REQUEST, "need payload_id or csharp").into_response()
    }
}

#[derive(Deserialize)]
struct RunReq {
    defence: Option<String>,
    payload_ids: Option<Vec<String>>,
}

/// Create a sweep run. Returns a run_id to open the SSE stream for.
async fn create_run(State(st): State<AppState>, Json(req): Json<RunReq>) -> impl IntoResponse {
    let defence = req
        .defence
        .as_deref()
        .and_then(Defence::from_api)
        .unwrap_or(Defence::FullyHardened);

    let ids: Vec<String> = match req.payload_ids {
        Some(list) if !list.is_empty() => {
            for id in &list {
                if !st.attacks.iter().any(|a| &a.id == id) {
                    return (StatusCode::BAD_REQUEST, format!("unknown payload_id: {id}"))
                        .into_response();
                }
            }
            list
        }
        _ => st.attacks.iter().map(|a| a.id.clone()).collect(),
    };
    let total = ids.len();

    let run_id = format!("run-{}", st.counter.fetch_add(1, Ordering::SeqCst));
    {
        let mut guard = st.runs.lock().expect("runs lock");
        if guard.len() >= MAX_PENDING_RUNS {
            return (StatusCode::TOO_MANY_REQUESTS, "too many pending runs").into_response();
        }
        guard.insert(run_id.clone(), RunSpec { defence, ids });
    }
    Json(serde_json::json!({ "run_id": run_id, "total_cases": total })).into_response()
}

fn valid_run_id(id: &str) -> bool {
    id.starts_with("run-") && id.len() > 4 && id[4..].bytes().all(|b| b.is_ascii_digit())
}

fn to_event<T: serde::Serialize>(v: &T) -> Result<Event, Infallible> {
    Ok(Event::default()
        .json_data(v)
        .unwrap_or_else(|_| Event::default().data("{}")))
}

/// SSE stream for a specific sweep. Consumed once: the run is removed on
/// connect, so it can neither be replayed nor mixed with another run.
async fn run_events(
    State(st): State<AppState>,
    Path(run_id): Path<String>,
) -> Result<Sse<impl Stream<Item = Result<Event, Infallible>>>, StatusCode> {
    if !valid_run_id(&run_id) {
        return Err(StatusCode::BAD_REQUEST);
    }
    let spec = {
        let mut guard = st.runs.lock().expect("runs lock");
        guard.remove(&run_id)
    };
    let Some(spec) = spec else {
        return Err(StatusCode::NOT_FOUND);
    };

    let (tx, rx) = tokio::sync::mpsc::channel::<Result<Event, Infallible>>(SSE_CHANNEL_CAP);
    let attacks = st.attacks.clone();
    let pace = st.pace_ms;
    let RunSpec { defence, ids } = spec;
    let total = ids.len();

    tokio::spawn(async move {
        let mut latencies: Vec<f64> = Vec::with_capacity(total);
        let mut rejected = 0usize;
        for (index, id) in ids.iter().enumerate() {
            let Some(a) = attacks.iter().find(|a| &a.id == id) else {
                continue;
            };
            let ev = build_case_event(&run_id, index, total, a, defence);
            if ev.verdict.blocked {
                rejected += 1;
            }
            latencies.push(ev.latency_us);
            // Err => the browser disconnected; stop (no leak, no unbounded work).
            if tx.send(to_event(&ev)).await.is_err() {
                return;
            }
            if pace > 0 {
                tokio::time::sleep(Duration::from_millis(pace)).await;
            }
        }
        let summary = build_summary(&run_id, total, rejected, &latencies);
        let _ = tx.send(to_event(&summary)).await;
    });

    Ok(Sse::new(ReceiverStream::new(rx)).keep_alive(KeepAlive::default()))
}

#[derive(Deserialize)]
struct AdvReq {
    command: Option<String>,
}

/// Natural-language INTENT ADVISORY lane. This is NOT the code-admission
/// decision — it runs the advisory-only semantic intent screen on plain text.
async fn advisory(Json(req): Json<AdvReq>) -> impl IntoResponse {
    let text = req.command.unwrap_or_default();
    let text = text.trim();
    if text.is_empty() {
        return (StatusCode::BAD_REQUEST, "empty command").into_response();
    }
    if text.chars().count() > MAX_ADVISORY_CHARS {
        return (StatusCode::PAYLOAD_TOO_LARGE, "command too long").into_response();
    }
    let advisories: Vec<serde_json::Value> = semantic_screen(text)
        .into_iter()
        .map(|a| {
            serde_json::json!({
                "cluster": a.cluster,
                "note": a.note,
                "confidence": a.confidence,
            })
        })
        .collect();
    Json(serde_json::json!({
        "lane": "intent_advisory",
        "authoritative": false,
        "note": "INTENT ADVISORY — NOT THE CODE-ADMISSION DECISION",
        "command": text,
        "advisories": advisories,
    }))
    .into_response()
}
