use std::process::Stdio;
use std::time::Instant;

use async_trait::async_trait;
use tokio::io::AsyncWriteExt;
use tokio::process::Command;
use tokio::time::timeout;

use crate::types::{ResourceLimits, SandboxJob, SandboxOutcome, SandboxReport};
use crate::SandboxRunner;

/// Offline stand-in (no execution). For queue/worker tests and keyless demos.
#[derive(Debug, Default, Clone)]
pub struct MockSandboxRunner;

#[async_trait]
impl SandboxRunner for MockSandboxRunner {
    async fn run(&self, job: SandboxJob, _limits: ResourceLimits) -> SandboxReport {
        SandboxReport {
            job_id: job.id,
            outcome: SandboxOutcome::Completed { exit_code: 0 },
            stdout: "mock: not executed".to_string(),
            stderr: String::new(),
            duration_ms: 0,
        }
    }
}

/// Runs a job by invoking a configured ISOLATION WRAPPER program (deployment:
/// `systemd-run --scope -p MemoryMax=.. -p CPUQuota=.. runsc run ...` / Firecracker).
///
/// Rust-enforced here: (1) WALL-CLOCK timeout + kill, (2) PROCESS-GROUP teardown
/// so a backgrounded grandchild cannot orphan past the job, (3) isolation FROM
/// the backend — runs on a separate worker host and returns ONLY a structured
/// [`SandboxReport`]. Environment is cleared (no secrets). NEVER panics / never
/// returns `Err`. NOT enforced here (delegated to the wrapper): CPU/memory/pids
/// caps, seccomp, no-network, read-only rootfs, kernel isolation (Mode D).
/// A bare runner without a wrapper provides NO kernel isolation.
#[derive(Debug, Clone)]
pub struct ProcessSandboxRunner {
    program: String,
    args: Vec<String>,
}

impl ProcessSandboxRunner {
    pub fn new(program: impl Into<String>, args: Vec<String>) -> Self {
        Self {
            program: program.into(),
            args,
        }
    }
}

#[async_trait]
impl SandboxRunner for ProcessSandboxRunner {
    async fn run(&self, job: SandboxJob, limits: ResourceLimits) -> SandboxReport {
        let start = Instant::now();
        let mut cmd = Command::new(&self.program);
        cmd.args(&self.args)
            .env_clear() // no secrets reach the sandbox
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true);
        // Put the child in its own process group so we can tear down the whole
        // tree (including backgrounded grandchildren), not just the direct PID.
        #[cfg(unix)]
        cmd.process_group(0);

        let mut_child = cmd.spawn();
        let child = match mut_child {
            Ok(c) => c,
            Err(e) => {
                return report(
                    &job.id,
                    SandboxOutcome::SpawnError,
                    String::new(),
                    e.to_string(),
                    start,
                )
            }
        };
        let pid = child.id();

        let result = timeout(limits.wall_clock, child.wait_with_output()).await;

        // Sweep the entire process group (negative PGID) so any backgrounded
        // grandchild is killed even when the direct child exited cleanly.
        #[cfg(unix)]
        {
            if let Some(p) = pid {
                let _ = Command::new("kill")
                    .arg("-KILL")
                    .arg(format!("-{p}"))
                    .stdin(Stdio::null())
                    .stdout(Stdio::null())
                    .stderr(Stdio::null())
                    .status()
                    .await;
            }
        }
        #[cfg(not(unix))]
        let _ = pid;

        match result {
            Ok(Ok(output)) => {
                let outcome = match output.status.code() {
                    Some(code) => SandboxOutcome::Completed { exit_code: code },
                    None => SandboxOutcome::Killed, // terminated by signal
                };
                report(
                    &job.id,
                    outcome,
                    String::from_utf8_lossy(&output.stdout).to_string(),
                    String::from_utf8_lossy(&output.stderr).to_string(),
                    start,
                )
            }
            Ok(Err(e)) => report(
                &job.id,
                SandboxOutcome::SpawnError,
                String::new(),
                e.to_string(),
                start,
            ),
            Err(_) => report(
                &job.id,
                SandboxOutcome::TimedOut,
                String::new(),
                "wall-clock timeout; sandbox process group killed".to_string(),
                start,
            ),
        }
    }
}

/// Runs an untrusted job inside a hardened Docker container — a concrete
/// realization of the "Axis-B isolation wrapper". The job's `code` is streamed to
/// the container's STDIN (the image entrypoint compiles+runs it). The container
/// has NO network, a memory cap, a CPU cap, a PID cap (anti fork-bomb), a
/// read-only rootfs (+ a small writable tmpfs), all Linux capabilities dropped,
/// `no-new-privileges`, and runs as `nobody`.
///
/// Rust-enforced here: WALL-CLOCK timeout. Because the Docker DAEMON owns the
/// container (not this CLI's process group), the timeout path additionally runs
/// `docker kill <name>` to actually stop the workload. `--rm` reaps it. Only a
/// structured [`SandboxReport`] crosses back to the backend. Never panics.
#[derive(Debug, Clone)]
pub struct DockerSandboxRunner {
    image: String,
    entrypoint: Vec<String>,
    memory_mb: u64,
    cpus: String,
    pids_limit: u64,
    /// Optional alternative OCI runtime passed as `--runtime <name>` (e.g. `runsc`
    /// for gVisor kernel isolation). `None` = the Docker default runtime (`runc`).
    runtime: Option<String>,
}

impl DockerSandboxRunner {
    /// `image` is the container image; `entrypoint` is the command run inside it
    /// (e.g. `["sh"]` for a shell image that reads a script from stdin, or empty
    /// to use the image's own ENTRYPOINT, like the .NET job harness).
    pub fn new(image: impl Into<String>, entrypoint: Vec<String>) -> Self {
        Self {
            image: image.into(),
            entrypoint,
            memory_mb: 256,
            cpus: "1.0".to_string(),
            pids_limit: 128,
            runtime: None,
        }
    }

    pub fn with_limits(mut self, memory_mb: u64, cpus: impl Into<String>, pids_limit: u64) -> Self {
        self.memory_mb = memory_mb;
        self.cpus = cpus.into();
        self.pids_limit = pids_limit;
        self
    }

    /// Set an alternative OCI runtime (e.g. `Some("runsc")` for gVisor). `None`
    /// (the default) uses Docker's default runtime, so the normal test/run path
    /// never requires gVisor to be installed.
    pub fn with_runtime(mut self, runtime: Option<String>) -> Self {
        self.runtime = runtime.filter(|r| !r.trim().is_empty());
        self
    }

    fn container_name(job_id: &str) -> String {
        // Docker names allow [a-zA-Z0-9][a-zA-Z0-9_.-]+ — strip anything else.
        let cleaned: String = job_id
            .chars()
            .filter(|c| c.is_ascii_alphanumeric())
            .collect();
        format!("dcvrsbx-{cleaned}")
    }

    /// Build the `docker run ...` argument vector (pure; unit-tested without Docker).
    fn build_args(&self, name: &str) -> Vec<String> {
        let mut a: Vec<String> = vec![
            "run".into(),
            "--rm".into(),
            "-i".into(),
            "--name".into(),
            name.into(),
            // --- isolation / hardening (the security property) ---
            "--network".into(),
            "none".into(), // no exfiltration
            "--memory".into(),
            format!("{}m", self.memory_mb),
            "--memory-swap".into(),
            format!("{}m", self.memory_mb), // no swap escape
            "--cpus".into(),
            self.cpus.clone(),
            "--pids-limit".into(),
            self.pids_limit.to_string(), // anti fork-bomb
            "--ulimit".into(),
            "nofile=256:256".into(), // bound open file descriptors (anti fd-exhaustion)
            "--ulimit".into(),
            "nproc=64:64".into(), // bound processes (defence-in-depth beside --pids-limit)
            "--read-only".into(), // immutable rootfs
            "--tmpfs".into(),
            "/tmp:rw,noexec,nosuid,size=64m".into(), // small writable scratch
            "--security-opt".into(),
            "no-new-privileges".into(),
            "--cap-drop".into(),
            "ALL".into(),
            "--user".into(),
            "65534:65534".into(), // nobody
            self.image.clone(),
        ];
        a.extend(self.entrypoint.iter().cloned());
        // Insert the optional alternative runtime (e.g. gVisor's `runsc`) right
        // after `run`, when configured. Absent by default.
        if let Some(rt) = &self.runtime {
            a.splice(1..1, ["--runtime".to_string(), rt.clone()]);
        }
        a
    }
}

#[async_trait]
impl SandboxRunner for DockerSandboxRunner {
    async fn run(&self, job: SandboxJob, limits: ResourceLimits) -> SandboxReport {
        let start = Instant::now();
        let name = Self::container_name(&job.id);
        let mut cmd = Command::new("docker");
        cmd.args(self.build_args(&name))
            .env_clear()
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true);

        let mut child = match cmd.spawn() {
            Ok(c) => c,
            Err(e) => {
                return report(
                    &job.id,
                    SandboxOutcome::SpawnError,
                    String::new(),
                    format!("docker spawn failed (is Docker installed/running?): {e}"),
                    start,
                )
            }
        };

        // Stream the untrusted code into the container's stdin, then close it.
        if let Some(mut sin) = child.stdin.take() {
            let _ = sin.write_all(job.code.as_bytes()).await;
            let _ = sin.shutdown().await;
        }

        match timeout(limits.wall_clock, child.wait_with_output()).await {
            Ok(Ok(output)) => {
                let outcome = match output.status.code() {
                    Some(code) => SandboxOutcome::Completed { exit_code: code },
                    None => SandboxOutcome::Killed,
                };
                report(
                    &job.id,
                    outcome,
                    String::from_utf8_lossy(&output.stdout).to_string(),
                    String::from_utf8_lossy(&output.stderr).to_string(),
                    start,
                )
            }
            Ok(Err(e)) => report(
                &job.id,
                SandboxOutcome::SpawnError,
                String::new(),
                e.to_string(),
                start,
            ),
            Err(_) => {
                // The daemon owns the container — actually stop it.
                let _ = Command::new("docker")
                    .args(["kill", &name])
                    .stdin(Stdio::null())
                    .stdout(Stdio::null())
                    .stderr(Stdio::null())
                    .status()
                    .await;
                report(
                    &job.id,
                    SandboxOutcome::TimedOut,
                    String::new(),
                    "wall-clock timeout; container killed (docker kill)".to_string(),
                    start,
                )
            }
        }
    }
}

fn report(
    job_id: &str,
    outcome: SandboxOutcome,
    stdout: String,
    stderr: String,
    start: Instant,
) -> SandboxReport {
    SandboxReport {
        job_id: job_id.to_string(),
        outcome,
        stdout,
        stderr,
        duration_ms: start.elapsed().as_millis() as u64,
    }
}

#[cfg(test)]
mod docker_args_tests {
    use super::DockerSandboxRunner;

    #[test]
    fn container_name_is_sanitized() {
        assert_eq!(
            DockerSandboxRunner::container_name("abc-123-DEF_x"),
            "dcvrsbx-abc123DEFx"
        );
    }

    #[test]
    fn hardening_flags_present() {
        let runner = DockerSandboxRunner::new("alpine:latest", vec!["sh".to_string()]);
        let args = runner.build_args("dcvrsbx-demo");
        let joined = args.join(" ");
        for flag in [
            "--network none",
            "--memory 256m",
            "--memory-swap 256m",
            "--cpus 1.0",
            "--pids-limit 128",
            "--ulimit nofile=256:256",
            "--ulimit nproc=64:64",
            "--read-only",
            "no-new-privileges",
            "--cap-drop ALL",
            "--user 65534:65534",
        ] {
            assert!(joined.contains(flag), "missing hardening flag: {flag}");
        }
        assert!(joined.ends_with("alpine:latest sh"));
    }

    #[test]
    fn default_runtime_omits_flag() {
        let runner = DockerSandboxRunner::new("alpine:latest", vec!["sh".to_string()]);
        assert!(
            !runner
                .build_args("dcvrsbx-demo")
                .join(" ")
                .contains("--runtime"),
            "default runner must NOT pass --runtime (no gVisor required)"
        );
    }

    #[test]
    fn runsc_runtime_included() {
        let runner = DockerSandboxRunner::new("alpine:latest", vec!["sh".to_string()])
            .with_runtime(Some("runsc".to_string()));
        let joined = runner.build_args("dcvrsbx-demo").join(" ");
        assert!(
            joined.contains("--runtime runsc"),
            "runsc runtime must add `--runtime runsc`: {joined}"
        );
        // still well-formed: runtime sits after `run`, image + entrypoint stay last
        assert!(joined.starts_with("run --runtime runsc"));
        assert!(joined.ends_with("alpine:latest sh"));
    }

    #[test]
    fn blank_runtime_is_ignored() {
        let runner = DockerSandboxRunner::new("alpine:latest", vec!["sh".to_string()])
            .with_runtime(Some("  ".to_string()));
        assert!(!runner.build_args("d").join(" ").contains("--runtime"));
    }
}
