//! Isolated sandbox harness for the malicious-code research mode (DreamCodeVR+
//! Mode D, Phase 4).
//!
//! TWO-AXIS isolation (see `docs/SECURITY_MODEL.md`, Mode D):
//! * Axis A — isolation FROM the backend: the backend never runs untrusted code
//!   in-process; it hands a [`SandboxJob`] to a worker (on a separate host) that
//!   returns ONLY a [`SandboxReport`]. A sandbox crash/hang/escape costs at most
//!   one retried/dead-lettered job. Implemented + tested here ([`SandboxWorker`],
//!   wall-clock kill in [`ProcessSandboxRunner`]).
//! * Axis B — isolation of the CODE from the host kernel: gVisor/microVM +
//!   cgroups + seccomp + no-net + read-only rootfs, provided by the wrapper
//!   PROGRAM the [`ProcessSandboxRunner`] invokes (deployment-only; "Docker alone
//!   is not the security claim" — NIST SP 800-190).
//!
//! Nothing here ever panics or returns `Err`: failures become structured reports.

mod queue;
mod runner;
mod types;

pub use queue::{SandboxWorker, WorkerResults};
pub use runner::{DockerSandboxRunner, MockSandboxRunner, ProcessSandboxRunner};
pub use types::{ResourceLimits, SandboxJob, SandboxOutcome, SandboxReport};

use async_trait::async_trait;

#[async_trait]
pub trait SandboxRunner: Send + Sync {
    /// Run one job. MUST NOT panic and MUST NOT return a live handle — only a
    /// structured report crosses back to the backend.
    async fn run(&self, job: SandboxJob, limits: ResourceLimits) -> SandboxReport;
}
