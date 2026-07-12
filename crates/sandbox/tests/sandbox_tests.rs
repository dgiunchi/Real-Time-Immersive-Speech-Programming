//! Sandbox isolation-mechanics tests: wall-clock kill, output capture, spawn
//! failure, and "backend survives a poison job" (retry + dead-letter).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use async_trait::async_trait;
use dcvr_sandbox::{
    MockSandboxRunner, ProcessSandboxRunner, ResourceLimits, SandboxJob, SandboxOutcome,
    SandboxReport, SandboxRunner, SandboxWorker,
};

fn job(id: &str) -> SandboxJob {
    SandboxJob {
        id: id.to_string(),
        language: "csharp".to_string(),
        code: "// code".to_string(),
    }
}

#[tokio::test]
async fn mock_runner_completes() {
    let r = MockSandboxRunner
        .run(job("1"), ResourceLimits::default())
        .await;
    assert!(matches!(
        r.outcome,
        SandboxOutcome::Completed { exit_code: 0 }
    ));
}

#[tokio::test]
async fn process_runner_captures_output() {
    let runner = ProcessSandboxRunner::new("sh", vec!["-c".to_string(), "echo hello".to_string()]);
    let r = runner
        .run(
            job("2"),
            ResourceLimits {
                wall_clock: Duration::from_secs(5),
            },
        )
        .await;
    assert!(
        matches!(r.outcome, SandboxOutcome::Completed { exit_code: 0 }),
        "{:?}",
        r.outcome
    );
    assert!(r.stdout.contains("hello"));
}

#[tokio::test]
async fn process_runner_kills_on_wall_clock_timeout() {
    // A hanging job must be killed promptly and reported as TimedOut.
    let runner = ProcessSandboxRunner::new("sh", vec!["-c".to_string(), "sleep 10".to_string()]);
    let r = runner
        .run(
            job("3"),
            ResourceLimits {
                wall_clock: Duration::from_millis(300),
            },
        )
        .await;
    assert_eq!(r.outcome, SandboxOutcome::TimedOut);
    assert!(
        r.duration_ms < 3000,
        "should be killed promptly, was {}ms",
        r.duration_ms
    );
}

#[tokio::test]
async fn process_runner_spawn_error_is_reported_not_panicked() {
    let runner = ProcessSandboxRunner::new("definitely-not-a-real-binary-xyz-123", vec![]);
    let r = runner.run(job("4"), ResourceLimits::default()).await;
    assert_eq!(r.outcome, SandboxOutcome::SpawnError);
}

/// Runner that fails any job whose id contains "poison" (models a crash-looping
/// payload) and completes the rest.
struct FlakyRunner;

#[async_trait]
impl SandboxRunner for FlakyRunner {
    async fn run(&self, job: SandboxJob, _limits: ResourceLimits) -> SandboxReport {
        let outcome = if job.id.contains("poison") {
            SandboxOutcome::SpawnError
        } else {
            SandboxOutcome::Completed { exit_code: 0 }
        };
        SandboxReport {
            job_id: job.id,
            outcome,
            stdout: String::new(),
            stderr: String::new(),
            duration_ms: 0,
        }
    }
}

#[tokio::test]
async fn worker_survives_poison_job_and_dead_letters_it() {
    let worker = SandboxWorker::new(FlakyRunner, ResourceLimits::default(), 1);
    let results = worker
        .process_all(vec![job("good-1"), job("poison-1"), job("good-2")])
        .await;
    // The backend gets a report for every job and keeps running.
    assert_eq!(results.reports.len(), 3);
    // Only the poison job is dead-lettered.
    assert_eq!(results.dead_letter.len(), 1);
    assert_eq!(results.dead_letter[0].id, "poison-1");
}

// Review fix: a backgrounded grandchild must NOT survive the job (process-group
// sweep), even when the direct child exits cleanly and immediately.
#[cfg(unix)]
#[tokio::test]
async fn process_group_sweep_kills_backgrounded_grandchild() {
    let marker = std::env::temp_dir().join(format!("dcvr_orphan_{}.marker", std::process::id()));
    let _ = std::fs::remove_file(&marker);
    let m = marker.display().to_string();
    // Parent backgrounds a grandchild that writes the marker after 3s, then exits 0.
    let script = format!("( sleep 3; echo x > '{m}' ) >/dev/null 2>&1 & echo started");
    let runner = ProcessSandboxRunner::new("sh", vec!["-c".to_string(), script]);
    let r = runner
        .run(
            job("orphan"),
            ResourceLimits {
                wall_clock: Duration::from_secs(5),
            },
        )
        .await;
    assert!(
        matches!(r.outcome, SandboxOutcome::Completed { .. }),
        "{:?}",
        r.outcome
    );
    // Wait past the grandchild's 3s timer; if not swept it would create the marker.
    tokio::time::sleep(Duration::from_millis(3500)).await;
    assert!(
        !marker.exists(),
        "backgrounded grandchild survived process-group sweep (orphan leak)"
    );
    let _ = std::fs::remove_file(&marker);
}
