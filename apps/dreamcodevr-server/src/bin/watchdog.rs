//! `watchdog` — external liveness supervisor for the DreamCodeVR+ backend (Phase 5).
//!
//! Spawns the backend, then on a timer drives a DEADLINE-BOUNDED end-to-end probe (a
//! real synthetic utterance that must produce a NID-94 decision). A wedged pipeline
//! keeps the process alive, so this — not a "process alive?" check — is what detects
//! a stall. After N consecutive unresponsive probes it restarts the child, with
//! exponential backoff. The restart/backoff decision core lives (and is unit-tested)
//! in `dreamcodevr_server::watchdog`; this binary is the process + network wiring,
//! exercised on-device (it needs a running backend + room server), not in the cargo
//! gate.
//!
//! Config (env):
//!   DCVR_WATCH_CMD          program to run (default: sibling `dreamcodevr-server`)
//!   DCVR_WATCH_ADDR         backend TCP address to probe (default 127.0.0.1:9098)
//!   DCVR_WATCH_INTERVAL_MS  seconds between probes (default 15000)
//!   DCVR_WATCH_DEADLINE_MS  probe deadline (default 8000)
//!   DCVR_WATCH_FAILURES     consecutive failures before restart (default 3)
//!   DCVR_WATCH_BACKOFF_MS   base restart backoff (default 1000)
//!   DCVR_WATCH_BACKOFF_MAX_MS  max restart backoff (default 60000)
#![allow(clippy::print_stdout)]

use std::time::Duration;

use dcvr_protocol::{
    decode_frame, encode_frame, join_peer_payload, NID_AUDIO_INPUT, NID_SELECTED_OBJECT,
};
use dreamcodevr_server::watchdog::{Action, Backoff, Probe, WatchdogPolicy, WatchdogState};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;

/// NID-94 backend-output channel id — a probe is healthy once it sees one.
const NID_BACKEND_OUTPUT_B: u32 = 94;
/// A fixed, valid 36-char synthetic peer id for the probe.
const PROBE_PEER: &str = "00000000-0000-4000-8000-000000000001";

fn env_u64(key: &str, default: u64) -> u64 {
    std::env::var(key)
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(default)
}

fn backend_command() -> Vec<String> {
    if let Ok(cmd) = std::env::var("DCVR_WATCH_CMD") {
        return cmd.split_whitespace().map(String::from).collect();
    }
    // Default: the `dreamcodevr-server` binary sitting next to this watchdog binary.
    let sibling = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.join("dreamcodevr-server")))
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_else(|| "dreamcodevr-server".to_string());
    vec![sibling]
}

fn spawn_backend(cmd: &[String]) -> std::io::Result<tokio::process::Child> {
    let (prog, args) = cmd
        .split_first()
        .ok_or_else(|| std::io::Error::new(std::io::ErrorKind::InvalidInput, "empty command"))?;
    tokio::process::Command::new(prog)
        .args(args)
        .kill_on_drop(true) // if the watchdog dies, don't orphan the backend
        .spawn()
}

/// One deadline-bounded end-to-end probe: connect, send a synthetic selection + typed
/// command, and wait for a NID-94 decision. Any outcome other than "saw a decision in
/// time" is `Unresponsive`.
async fn probe(addr: &str, deadline: Duration) -> Probe {
    match tokio::time::timeout(deadline, probe_inner(addr)).await {
        Ok(Ok(true)) => Probe::Healthy,
        _ => Probe::Unresponsive,
    }
}

async fn probe_inner(addr: &str) -> std::io::Result<bool> {
    let mut stream = TcpStream::connect(addr).await?;
    let to_io =
        |e: dcvr_protocol::ProtocolError| std::io::Error::new(std::io::ErrorKind::InvalidData, e);
    let nid93 = encode_frame(
        NID_SELECTED_OBJECT,
        &join_peer_payload(PROBE_PEER, b"Cube:DefaultMaterial"),
    )
    .map_err(to_io)?;
    let nid98 = encode_frame(
        NID_AUDIO_INPUT,
        &join_peer_payload(PROBE_PEER, b"watchdog liveness ping"),
    )
    .map_err(to_io)?;
    stream.write_all(&nid93).await?;
    stream.write_all(&nid98).await?;

    let mut buf: Vec<u8> = Vec::new();
    let mut tmp = [0u8; 4096];
    loop {
        // Drain any complete frames; a NID-94 means the pipeline produced a decision.
        while let Ok(d) = decode_frame(&buf) {
            let consumed = d.consumed;
            if d.frame.network_id.b == NID_BACKEND_OUTPUT_B {
                return Ok(true);
            }
            buf.drain(0..consumed);
        }
        let n = stream.read(&mut tmp).await?;
        if n == 0 {
            return Ok(false); // closed without a decision
        }
        buf.extend_from_slice(&tmp[..n]);
    }
}

#[tokio::main]
async fn main() {
    let cmd = backend_command();
    let addr = std::env::var("DCVR_WATCH_ADDR").unwrap_or_else(|_| "127.0.0.1:9098".to_string());
    let interval = Duration::from_millis(env_u64("DCVR_WATCH_INTERVAL_MS", 15_000));
    let deadline = Duration::from_millis(env_u64("DCVR_WATCH_DEADLINE_MS", 8_000));
    let policy = WatchdogPolicy {
        failures_before_restart: env_u64("DCVR_WATCH_FAILURES", 3) as u32,
    };
    let mut backoff = Backoff::new(
        Duration::from_millis(env_u64("DCVR_WATCH_BACKOFF_MS", 1_000)),
        Duration::from_millis(env_u64("DCVR_WATCH_BACKOFF_MAX_MS", 60_000)),
        2,
    );
    let mut state = WatchdogState::default();

    eprintln!(
        "[watchdog] supervising {cmd:?}; probing {addr} every {interval:?} (deadline {deadline:?})"
    );
    let mut child = match spawn_backend(&cmd) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("[watchdog] failed to start backend: {e}");
            std::process::exit(1);
        }
    };

    loop {
        tokio::time::sleep(interval).await;

        // If the child has already exited on its own, restart it immediately.
        if let Ok(Some(status)) = child.try_wait() {
            eprintln!("[watchdog] backend exited on its own ({status}); restarting");
            let delay = backoff.next_delay();
            tokio::time::sleep(delay).await;
            match spawn_backend(&cmd) {
                Ok(c) => child = c,
                Err(e) => eprintln!("[watchdog] respawn failed: {e}"),
            }
            continue;
        }

        let outcome = probe(&addr, deadline).await;
        match state.observe(outcome, &policy) {
            Action::Continue => {
                if outcome == Probe::Healthy {
                    backoff.reset();
                }
            }
            Action::Restart => {
                eprintln!(
                    "[watchdog] backend unresponsive for {} consecutive probes; restarting",
                    policy.failures_before_restart
                );
                let _ = child.kill().await;
                let _ = child.wait().await;
                let delay = backoff.next_delay();
                eprintln!("[watchdog] backing off {delay:?} before respawn");
                tokio::time::sleep(delay).await;
                match spawn_backend(&cmd) {
                    Ok(c) => child = c,
                    Err(e) => eprintln!("[watchdog] respawn failed: {e}"),
                }
            }
        }
    }
}
