//! Localhost visible demo of the XR **static code-admission** evaluation.
//!
//! Serves a self-contained page with a LIVE console: fire the corpus (or your
//! own pasted C#) through the REAL DreamCodeVR+ validator and watch each verdict
//! stream in — rejected-before-execution vs admitted-by-static-validator — with
//! the real per-call admission latency. Nothing is compiled or executed; this is
//! admission control only. Offline, deterministic, loopback-only.
//!
//! The API + handlers live in `xr_security_eval::server` (so they are testable
//! in-process); this binary only binds the loopback socket and serves them.
//!
//! Run:  cargo run -p xr-security-eval --bin xr-security-demo   (then open the URL)

use xr_security_eval::server::app;

#[tokio::main]
async fn main() {
    let app = app();
    let port: u16 = std::env::var("XR_DEMO_PORT")
        .ok()
        .and_then(|s| s.parse().ok())
        .unwrap_or(7979);
    // Loopback only — this is a local presentation demo, never exposed off-host.
    let addr = std::net::SocketAddr::from(([127, 0, 0, 1], port));
    let listener = tokio::net::TcpListener::bind(addr)
        .await
        .expect("bind loopback");
    println!("\n  XR code-admission console  ->  http://127.0.0.1:{port}\n  (Ctrl+C to stop)\n");
    axum::serve(listener, app).await.expect("serve");
}
