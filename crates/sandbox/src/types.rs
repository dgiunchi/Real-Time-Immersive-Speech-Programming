use std::time::Duration;

/// A unit of untrusted work to run in the sandbox (e.g. compile+run a generated
/// C# candidate). The backend never executes this in-process.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SandboxJob {
    pub id: String,
    pub language: String,
    pub code: String,
}

/// Resource bounds for one job. `wall_clock` is enforced by the Rust runner
/// (timeout + kill). CPU/memory/pids are enforced by the isolation wrapper
/// program (cgroups v2 / gVisor / microVM) in deployment — see `docs/SECURITY_MODEL.md` (Mode D).
#[derive(Debug, Clone)]
pub struct ResourceLimits {
    pub wall_clock: Duration,
    // CPU/memory/pids caps are intentionally absent from the Rust API: they are
    // enforced by the Axis-B isolation wrapper (cgroups v2 / gVisor / microVM),
    // not by this process. See docs/SECURITY_MODEL.md (Mode D).
}

impl Default for ResourceLimits {
    fn default() -> Self {
        Self {
            wall_clock: Duration::from_secs(10),
        }
    }
}

/// How a job ended.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SandboxOutcome {
    Completed { exit_code: i32 },
    TimedOut,
    Killed,
    SpawnError,
}

/// The ONLY thing that crosses back to the backend: a structured report. No live
/// handles, no process, no filesystem — just data.
#[derive(Debug, Clone)]
pub struct SandboxReport {
    pub job_id: String,
    pub outcome: SandboxOutcome,
    pub stdout: String,
    pub stderr: String,
    pub duration_ms: u64,
}
