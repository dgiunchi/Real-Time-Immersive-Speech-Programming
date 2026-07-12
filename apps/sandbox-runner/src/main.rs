//! Live Mode-D containment demo: run untrusted payloads in a hardened Docker
//! sandbox (`DockerSandboxRunner`) and print the structured report. Proves the
//! security properties empirically — no network, read-only rootfs, runs as
//! nobody, and a runaway payload is killed on the wall-clock deadline. The
//! backend (this process) survives every case.
//!
//! * (default)   — shell payloads in `alpine` (fast containment proof).
//! * `--csharp`  — REAL generated C# compiled+run in the `.NET` job harness image.
use std::time::Duration;

use dcvr_sandbox::{DockerSandboxRunner, ResourceLimits, SandboxJob, SandboxRunner};

#[tokio::main(flavor = "current_thread")]
async fn main() {
    if std::env::args().any(|a| a == "--csharp") {
        run_csharp_demo().await;
        return;
    }
    run_shell_demo().await;
}

/// Optional alternative OCI runtime for the sandbox, from
/// `DCVR_SANDBOX_DOCKER_RUNTIME` (e.g. `runsc` for gVisor). Unset/blank = Docker
/// default, so the demo never requires gVisor to be installed.
fn docker_runtime() -> Option<String> {
    std::env::var("DCVR_SANDBOX_DOCKER_RUNTIME")
        .ok()
        .filter(|s| !s.trim().is_empty())
}

async fn run_one(
    name: &str,
    runner: &DockerSandboxRunner,
    id: &str,
    lang: &str,
    code: &str,
    secs: u64,
) {
    let job = SandboxJob {
        id: id.to_string(),
        language: lang.to_string(),
        code: code.to_string(),
    };
    let limits = ResourceLimits {
        wall_clock: Duration::from_secs(secs),
    };
    let report = runner.run(job, limits).await;
    println!(
        "[{name}] outcome={:?} duration={}ms\n    stdout: {}\n    stderr: {}",
        report.outcome,
        report.duration_ms,
        report.stdout.trim(),
        report.stderr.trim()
    );
}

async fn run_shell_demo() {
    // (name, shell payload, wall-clock seconds)
    let scenarios: &[(&str, &str, u64)] = &[
        ("benign", "echo OK; echo uid=$(id -u)", 10),
        (
            "read-only-rootfs",
            "touch /pwned 2>/dev/null && echo WROTE_TO_ROOTFS || echo READONLY_BLOCKED",
            10,
        ),
        (
            "no-network-exfil",
            "wget -T2 -qO- http://1.1.1.1 2>/dev/null && echo GOT_NETWORK || echo NETWORK_BLOCKED",
            10,
        ),
        (
            "runaway-infinite-loop",
            "echo looping; while true; do :; done",
            3,
        ),
    ];

    println!(
        "=== DreamCodeVR+ Mode-D sandbox containment demo (alpine, runtime={}) ===",
        docker_runtime().unwrap_or_else(|| "default".to_string())
    );
    for (i, (name, code, secs)) in scenarios.iter().enumerate() {
        let runner = DockerSandboxRunner::new("alpine:latest", vec!["sh".to_string()])
            .with_runtime(docker_runtime());
        run_one(name, &runner, &format!("demo{i}"), "sh", code, *secs).await;
    }
    println!("=== backend (this process) survived all cases ===");
}

/// Compile + execute REAL generated C# inside the hardened `.NET` job-harness
/// container (image `dcvr-sandbox-harness:local`). Proves the production Mode-D
/// path: untrusted C# is compiled in-memory by Roslyn and run, fully isolated.
async fn run_csharp_demo() {
    // (name, C# source, wall-clock seconds). Roslyn compile + cold dotnet start
    // takes a few seconds, so benign cases get a generous deadline.
    let scenarios: &[(&str, &str, u64)] = &[
        (
            "benign-csharp",
            "using System; class P { static void Main(){ Console.WriteLine(\"hello from sandboxed C#\"); } }",
            40,
        ),
        (
            "csharp-runtime-exception",
            "class P { static void Main(){ throw new System.Exception(\"boom\"); } }",
            40,
        ),
        (
            "csharp-filesystem-write",
            "using System.IO; class P { static void Main(){ try { File.WriteAllText(\"/etc/pwned\",\"x\"); System.Console.WriteLine(\"WROTE_TO_HOST\"); } catch(System.Exception e){ System.Console.WriteLine(\"FS_BLOCKED: \"+e.GetType().Name); } } }",
            40,
        ),
        (
            "csharp-runaway-loop",
            "class P { static void Main(){ System.Console.WriteLine(\"looping\"); while(true){} } }",
            18,
        ),
    ];

    println!(
        "=== DreamCodeVR+ Mode-D: REAL C# in the hardened harness (runtime={}) ===",
        docker_runtime().unwrap_or_else(|| "default".to_string())
    );
    for (i, (name, code, secs)) in scenarios.iter().enumerate() {
        // empty entrypoint -> use the image's ENTRYPOINT (the .NET harness reads
        // C# from stdin). Roslyn compilation is memory-hungry, so raise the caps.
        let runner = DockerSandboxRunner::new("dcvr-sandbox-harness:local", Vec::new())
            .with_limits(512, "2.0", 256)
            .with_runtime(docker_runtime());
        run_one(name, &runner, &format!("cs{i}"), "csharp", code, *secs).await;
    }
    println!("=== backend survived; untrusted C# never touched the host ===");
}
