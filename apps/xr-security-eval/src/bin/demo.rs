//! Localhost visible demo of the XR attack/defence evaluation.
//!
//! Serves a single self-contained page that lets you browse each attack and SEE it
//! "succeed" with no defence vs being BLOCKED by the REAL DreamCodeVR+ backend. All
//! verdicts are computed live by the same validator the eval CLI uses. Offline,
//! deterministic, loopback-only.
//!
//! Run:  cargo run -p xr-security-eval --bin xr-security-demo   (then open the printed URL)

use axum::{response::Html, routing::get, Json, Router};
use xr_security_eval::{evaluate_attack, load_corpus};

const PAGE: &str = include_str!("demo.html");

#[tokio::main]
async fn main() {
    let app = Router::new()
        .route("/", get(|| async { Html(PAGE) }))
        .route("/api/results", get(results));
    let port: u16 = std::env::var("XR_DEMO_PORT")
        .ok()
        .and_then(|s| s.parse().ok())
        .unwrap_or(7979);
    // Loopback only — this is a local presentation demo, never exposed off-host.
    let addr = std::net::SocketAddr::from(([127, 0, 0, 1], port));
    let listener = tokio::net::TcpListener::bind(addr)
        .await
        .expect("bind loopback");
    println!("\n  XR attack/defence demo  →  http://127.0.0.1:{port}\n  (Ctrl+C to stop)\n");
    axum::serve(listener, app).await.expect("serve");
}

/// Compute every attack's verdict at all three defence levels and return them as JSON.
async fn results() -> Json<serde_json::Value> {
    let corpus = load_corpus();
    let per_attack: Vec<_> = corpus.attacks.iter().map(evaluate_attack).collect();
    Json(serde_json::json!({ "per_attack": per_attack }))
}
