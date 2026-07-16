//! DreamCodeVR+ **admin / debug panel** (axum).
//!
//! A single-page web dashboard that visualises the whole pipeline live and lets a
//! super-admin tune it at runtime:
//! - **GET /** — the dashboard UI (embedded).
//! - **GET /events** — Server-Sent Events stream of [`PipelineEvent`]s (replay + live).
//! - **GET/POST /api/config** — read / update the live [`RuntimeConfig`].
//! - **GET /api/stats**, **/api/safety**, **/api/recent**, **/api/health**.
//! - **POST /api/validate** — run pasted C# through the real guardrail.
//! - **POST /api/redteam** — fire built-in malicious samples; prove they're blocked.
//! - **GET /api/profiles**, **/api/profile?peer=** — per-user RAG profiles.
//! - **POST /api/command**, **/api/sandbox** — optional backend hooks (run a real
//!   command / run code in the Mode-D sandbox), if the backend wired them.
//!
//! Mutating routes honour an optional admin token (`X-Admin-Token`).

use std::convert::Infallible;
use std::sync::Arc;

use axum::extract::{Query, State};
use axum::http::{HeaderMap, StatusCode};
use axum::response::sse::{Event, KeepAlive, Sse};
use axum::response::{Html, IntoResponse};
use axum::routing::{get, post};
use axum::{Json, Router};
use serde::Deserialize;
use tokio_stream::wrappers::BroadcastStream;
use tokio_stream::{Stream, StreamExt};

use dcvr_control::{ControlBus, PipelineEvent, RuntimeConfig};
use dcvr_csharp_policy::{validate_csharp_freeform, CsharpDecision};
use dcvr_personalization::PersonalizationStore;

/// Optional backend-provided actions (kept behind a trait so this crate doesn't
/// depend on the backend). Wired by `dreamcodevr-server`.
#[async_trait::async_trait]
pub trait AdminHooks: Send + Sync {
    /// Run a command through the real pipeline; returns a short result summary.
    async fn run_command(&self, peer: &str, command: &str) -> String;
    /// Compile + run untrusted C# in the Mode-D sandbox; returns the report.
    async fn run_sandbox(&self, code: &str) -> String;
}

/// Shared state for all handlers (cheap to clone).
#[derive(Clone)]
pub struct AdminState {
    pub bus: ControlBus,
    pub store: Option<Arc<dyn PersonalizationStore>>,
    pub hooks: Option<Arc<dyn AdminHooks>>,
    /// If set, mutating routes require header `X-Admin-Token: <token>`.
    pub token: Option<String>,
}

impl AdminState {
    pub fn new(bus: ControlBus) -> Self {
        Self {
            bus,
            store: None,
            hooks: None,
            token: None,
        }
    }
    pub fn with_store(mut self, store: Arc<dyn PersonalizationStore>) -> Self {
        self.store = Some(store);
        self
    }
    pub fn with_hooks(mut self, hooks: Arc<dyn AdminHooks>) -> Self {
        self.hooks = Some(hooks);
        self
    }
    pub fn with_token(mut self, token: Option<String>) -> Self {
        self.token = token.filter(|t| !t.is_empty());
        self
    }
}

const UI_HTML: &str = include_str!("ui.html");

/// Built-in malicious C# samples for the red-team button — each MUST be blocked,
/// plus one benign control that must pass (proves no false positives).
const REDTEAM: &[(&str, &str, bool)] = &[
    (
        "file delete (System.IO)",
        "using UnityEngine; using System.IO; public class GeneratedBehaviour : MonoBehaviour { void Start(){ File.Delete(\"/etc/passwd\"); } }",
        true,
    ),
    (
        "network exfiltration (System.Net)",
        "using UnityEngine; public class GeneratedBehaviour : MonoBehaviour { void Start(){ var c = new System.Net.WebClient(); } }",
        true,
    ),
    (
        "process spawn (System.Diagnostics)",
        "using UnityEngine; public class GeneratedBehaviour : MonoBehaviour { void Start(){ System.Diagnostics.Process.Start(\"sh\"); } }",
        true,
    ),
    (
        "reflection (.Assembly)",
        "using UnityEngine; public class GeneratedBehaviour : MonoBehaviour { void Start(){ var a = typeof(int).Assembly; } }",
        true,
    ),
    (
        "benign control (should PASS)",
        "using UnityEngine; public class GeneratedBehaviour : MonoBehaviour { void Start(){ var r = GetComponent<Renderer>(); if (r != null) r.material.color = Color.red; } }",
        false,
    ),
];

pub fn router(state: AdminState) -> Router {
    Router::new()
        .route("/", get(index))
        .route("/events", get(events))
        .route("/api/config", get(get_config).post(post_config))
        .route("/api/stats", get(get_stats))
        .route("/api/safety", get(get_safety))
        .route("/api/recent", get(get_recent))
        .route("/api/report", get(get_report))
        .route("/api/health", get(get_health))
        .route("/api/validate", post(post_validate))
        .route("/api/redteam", post(post_redteam))
        .route("/api/profiles", get(get_profiles))
        .route("/api/profile", get(get_profile))
        .route("/api/profile/delete", post(post_profile_delete))
        .route("/api/command", post(post_command))
        .route("/api/sandbox", post(post_sandbox))
        .with_state(state)
}

/// Bind and serve the admin panel until the process exits. Fail-closed: refuses to
/// bind a non-loopback interface unless an admin token is configured.
pub async fn serve(addr: std::net::SocketAddr, state: AdminState) -> std::io::Result<()> {
    if !bind_allowed(addr.ip(), state.token.is_some()) {
        return Err(std::io::Error::new(
            std::io::ErrorKind::PermissionDenied,
            format!(
                "refusing to bind the admin panel to non-loopback {addr} without an admin token; \
                 set DCVR_ADMIN_TOKEN (fail-closed)"
            ),
        ));
    }
    let listener = tokio::net::TcpListener::bind(addr).await?;
    eprintln!("[admin] panel on http://{addr}");
    axum::serve(listener, router(state)).await
}

/// Constant-time byte comparison (no early-exit on the first mismatch) so admin
/// token verification does not leak the token through response timing (attack A122).
/// A length difference short-circuits (only the length, never the bytes, can leak).
fn ct_eq(a: &[u8], b: &[u8]) -> bool {
    if a.len() != b.len() {
        return false;
    }
    let mut diff = 0u8;
    for (x, y) in a.iter().zip(b.iter()) {
        diff |= x ^ y;
    }
    diff == 0
}

fn authed(st: &AdminState, headers: &HeaderMap) -> bool {
    match &st.token {
        None => true,
        Some(tok) => headers
            .get("x-admin-token")
            .and_then(|v| v.to_str().ok())
            .map(|got| ct_eq(got.as_bytes(), tok.as_bytes()))
            .unwrap_or(false),
    }
}

/// Whether binding the admin panel to `ip` is permitted. Loopback is always fine;
/// any other (publicly reachable) interface requires an admin token to be set —
/// otherwise we refuse to start (fail-closed). Closes the "admin panel exposed
/// off-loopback without a token" exposure (attack A122, the family's only true P0).
pub fn bind_allowed(ip: std::net::IpAddr, has_token: bool) -> bool {
    ip.is_loopback() || has_token
}

async fn index() -> Html<&'static str> {
    Html(UI_HTML)
}

fn to_event(ev: PipelineEvent) -> Result<Event, Infallible> {
    Ok(Event::default()
        .json_data(&ev)
        .unwrap_or_else(|_| Event::default().data("{}")))
}

async fn events(
    State(st): State<AdminState>,
) -> Sse<impl Stream<Item = Result<Event, Infallible>>> {
    let replay = tokio_stream::iter(st.bus.recent()).map(to_event);
    let live = BroadcastStream::new(st.bus.subscribe())
        .filter_map(|r| r.ok())
        .map(to_event);
    Sse::new(replay.chain(live)).keep_alive(KeepAlive::default())
}

async fn get_config(State(st): State<AdminState>) -> Json<RuntimeConfig> {
    Json(st.bus.config())
}

async fn post_config(
    State(st): State<AdminState>,
    headers: HeaderMap,
    Json(patch): Json<serde_json::Value>,
) -> impl IntoResponse {
    if !authed(&st, &headers) {
        return (StatusCode::UNAUTHORIZED, "unauthorized").into_response();
    }
    // PARTIAL / MERGE update: the panel (and any client) may send just the field(s)
    // it changed — e.g. `{ "llm_reasoning_effort": "low" }`. We overlay the patch onto
    // the CURRENT config so unspecified fields keep their value, instead of demanding
    // the whole struct (which returned 422 "missing field ..." and made Apply fail).
    let mut base = match serde_json::to_value(st.bus.config()) {
        Ok(v) => v,
        Err(e) => {
            return (
                StatusCode::INTERNAL_SERVER_ERROR,
                format!("config serialize: {e}"),
            )
                .into_response()
        }
    };
    if let (Some(b), Some(p)) = (base.as_object_mut(), patch.as_object()) {
        for (k, v) in p {
            b.insert(k.clone(), v.clone());
        }
    }
    match serde_json::from_value::<RuntimeConfig>(base) {
        Ok(cfg) => {
            st.bus.set_config(cfg);
            Json(st.bus.config()).into_response()
        }
        Err(e) => (StatusCode::BAD_REQUEST, format!("bad config field: {e}")).into_response(),
    }
}

async fn get_stats(State(st): State<AdminState>) -> impl IntoResponse {
    Json(st.bus.stats().snapshot())
}

async fn get_safety(State(st): State<AdminState>) -> impl IntoResponse {
    Json(st.bus.safety_log())
}

async fn get_recent(State(st): State<AdminState>) -> impl IntoResponse {
    Json(st.bus.recent())
}

/// A downloadable markdown session report (stats + safety log + recent events).
async fn get_report(State(st): State<AdminState>) -> impl IntoResponse {
    let s = st.bus.stats().snapshot();
    let mut md = String::from("# DreamCodeVR+ — session report\n\n## Stats\n");
    md.push_str(&format!(
        "- requests: {}\n- approved: {}\n- rejected: {}\n- malicious blocked: {}\n- neutralized: {}\n- LLM calls: {}\n- avg LLM ms: {}\n- est. cost USD: {}\n\n",
        s.requests, s.approved, s.rejected, s.malicious_blocked, s.neutralized, s.llm_calls, s.avg_llm_ms, s.est_cost_usd
    ));
    md.push_str("## Safety log\n");
    for ev in st.bus.safety_log() {
        md.push_str(&format!(
            "- {} — {}\n",
            ev.summary,
            ev.detail.replace('\n', " ")
        ));
    }
    md.push_str("\n## Recent events\n");
    for ev in st.bus.recent() {
        let d: String = ev.detail.chars().take(100).collect();
        md.push_str(&format!(
            "- {:?} ({} ms) {} — {}\n",
            ev.stage,
            ev.ms,
            ev.summary,
            d.replace('\n', " ")
        ));
    }
    (
        [(
            axum::http::header::CONTENT_TYPE,
            "text/markdown; charset=utf-8",
        )],
        md,
    )
}

async fn get_health(State(st): State<AdminState>) -> impl IntoResponse {
    let cfg = st.bus.config();
    Json(serde_json::json!({
        "ok": true,
        "model": cfg.model,
        "mode_a": cfg.enable_mode_a,
        "rag": cfg.enable_rag,
        "stt": cfg.enable_stt,
        "guardrails": cfg.enable_guardrails,
        "personalization_store": st.store.is_some(),
        "command_hook": st.hooks.is_some(),
    }))
}

#[derive(Deserialize)]
struct ValidateReq {
    csharp: String,
}

fn verdict_json(code: &str) -> serde_json::Value {
    let v = validate_csharp_freeform(code);
    serde_json::json!({
        "approved": v.decision == CsharpDecision::ApproveForResearch,
        "violations": v.violations.iter().map(|x| x.to_string()).collect::<Vec<_>>(),
        "chars": code.len(),
        "lines": code.lines().count(),
    })
}

async fn post_validate(Json(req): Json<ValidateReq>) -> impl IntoResponse {
    Json(verdict_json(&req.csharp))
}

async fn post_redteam() -> impl IntoResponse {
    let results: Vec<serde_json::Value> = REDTEAM
        .iter()
        .map(|(name, code, should_block)| {
            let v = validate_csharp_freeform(code);
            let blocked = v.decision != CsharpDecision::ApproveForResearch;
            serde_json::json!({
                "name": name,
                "should_block": should_block,
                "blocked": blocked,
                "correct": blocked == *should_block,
                "violations": v.violations.iter().map(|x| x.to_string()).collect::<Vec<_>>(),
            })
        })
        .collect();
    let all_correct = results
        .iter()
        .all(|r| r.get("correct").and_then(|c| c.as_bool()).unwrap_or(false));
    Json(serde_json::json!({ "all_correct": all_correct, "results": results }))
}

async fn get_profiles(State(st): State<AdminState>) -> impl IntoResponse {
    let peers = st
        .store
        .as_ref()
        .map(|s| s.list_peers())
        .unwrap_or_default();
    Json(peers)
}

#[derive(Deserialize)]
struct PeerQuery {
    peer: String,
}

async fn get_profile(
    State(st): State<AdminState>,
    Query(q): Query<PeerQuery>,
) -> impl IntoResponse {
    match &st.store {
        Some(s) => Json(s.load(&q.peer)).into_response(),
        None => (StatusCode::NOT_FOUND, "no personalization store").into_response(),
    }
}

/// Erase one user's stored personalization data (GDPR Art. 17). Mutating, so it
/// honours the admin token when one is configured.
async fn post_profile_delete(
    State(st): State<AdminState>,
    headers: HeaderMap,
    Query(q): Query<PeerQuery>,
) -> impl IntoResponse {
    if !authed(&st, &headers) {
        return (StatusCode::UNAUTHORIZED, "unauthorized").into_response();
    }
    match &st.store {
        Some(s) => {
            let deleted = s.delete(&q.peer);
            Json(serde_json::json!({ "peer": q.peer, "deleted": deleted })).into_response()
        }
        None => (StatusCode::NOT_FOUND, "no personalization store").into_response(),
    }
}

#[derive(Deserialize)]
struct CommandReq {
    peer: Option<String>,
    command: String,
}

async fn post_command(
    State(st): State<AdminState>,
    headers: HeaderMap,
    Json(req): Json<CommandReq>,
) -> impl IntoResponse {
    if !authed(&st, &headers) {
        return (StatusCode::UNAUTHORIZED, "unauthorized").into_response();
    }
    match &st.hooks {
        Some(h) => {
            let peer = req.peer.unwrap_or_else(|| "admin-panel".to_string());
            let out = h.run_command(&peer, &req.command).await;
            Json(serde_json::json!({ "result": out })).into_response()
        }
        None => (StatusCode::SERVICE_UNAVAILABLE, "command runner not wired").into_response(),
    }
}

#[derive(Deserialize)]
struct SandboxReq {
    code: String,
}

async fn post_sandbox(
    State(st): State<AdminState>,
    headers: HeaderMap,
    Json(req): Json<SandboxReq>,
) -> impl IntoResponse {
    if !authed(&st, &headers) {
        return (StatusCode::UNAUTHORIZED, "unauthorized").into_response();
    }
    match &st.hooks {
        Some(h) => {
            let out = h.run_sandbox(&req.code).await;
            Json(serde_json::json!({ "report": out })).into_response()
        }
        None => (StatusCode::SERVICE_UNAVAILABLE, "sandbox runner not wired").into_response(),
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;
    use dcvr_control::RuntimeConfig;

    #[test]
    fn router_builds() {
        let st = AdminState::new(ControlBus::new(RuntimeConfig::default()));
        let _ = router(st);
    }

    #[test]
    fn redteam_samples_classify_correctly() {
        for (name, code, should_block) in REDTEAM {
            let v = validate_csharp_freeform(code);
            let blocked = v.decision != CsharpDecision::ApproveForResearch;
            assert_eq!(
                blocked, *should_block,
                "redteam sample misclassified: {name}"
            );
        }
    }

    #[test]
    fn ct_eq_matches_only_equal_bytes() {
        assert!(ct_eq(b"secret-token", b"secret-token"));
        assert!(ct_eq(b"", b""));
        assert!(!ct_eq(b"secret-token", b"secret-toke")); // length differs
        assert!(!ct_eq(b"secret-token", b"secret-tokeX")); // last byte differs
        assert!(!ct_eq(b"", b"x"));
    }

    #[test]
    fn bind_refused_off_loopback_without_token() {
        use std::net::{IpAddr, Ipv4Addr};
        let loopback = IpAddr::V4(Ipv4Addr::LOCALHOST);
        let public = IpAddr::V4(Ipv4Addr::UNSPECIFIED); // 0.0.0.0
        assert!(bind_allowed(loopback, false)); // loopback, no token -> OK
        assert!(bind_allowed(loopback, true));
        assert!(!bind_allowed(public, false)); // public, no token -> REFUSED
        assert!(bind_allowed(public, true)); // public + token -> OK
    }

    #[test]
    fn authed_open_without_token_but_enforced_when_set() {
        use axum::http::HeaderMap;
        let st_open = AdminState::new(ControlBus::new(RuntimeConfig::default()));
        let mut h = HeaderMap::new();
        assert!(authed(&st_open, &h), "no token configured -> open (legacy)");

        let st = AdminState::new(ControlBus::new(RuntimeConfig::default()))
            .with_token(Some("t0ken".to_string()));
        assert!(!authed(&st, &h), "token set, none provided -> denied");
        h.insert("x-admin-token", "wrong".parse().unwrap());
        assert!(!authed(&st, &h), "wrong token -> denied");
        h.insert("x-admin-token", "t0ken".parse().unwrap());
        assert!(authed(&st, &h), "correct token -> allowed");
    }
}
