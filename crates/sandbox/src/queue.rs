use crate::types::{ResourceLimits, SandboxJob, SandboxOutcome, SandboxReport};
use crate::SandboxRunner;

/// Reports plus any jobs that exhausted retries (the dead-letter queue).
#[derive(Debug, Clone)]
pub struct WorkerResults {
    pub reports: Vec<SandboxReport>,
    pub dead_letter: Vec<SandboxJob>,
}

/// The disposable-worker loop. A failing/hanging job is retried up to
/// `max_retries`, then dead-lettered; the worker keeps going and the caller (the
/// backend) always receives a structured report for every job. Because the
/// worker runs on a SEPARATE host with a per-job sandbox, a sandbox escape/crash
/// cannot reach the backend — at worst one job is retried or dead-lettered.
#[derive(Debug)]
pub struct SandboxWorker<R: SandboxRunner> {
    runner: R,
    limits: ResourceLimits,
    max_retries: u32,
}

impl<R: SandboxRunner> SandboxWorker<R> {
    pub fn new(runner: R, limits: ResourceLimits, max_retries: u32) -> Self {
        Self {
            runner,
            limits,
            max_retries,
        }
    }

    /// NOTE: retry/dead-letter apply to WORKER-SIDE failures only (timeout, kill,
    /// spawn error). A job that runs to completion with a NON-ZERO exit code is a
    /// valid observed outcome (especially for the malicious-code study), so it is
    /// reported, not retried. `max_retries=N` yields up to N+1 total attempts.
    pub async fn process_all(&self, jobs: Vec<SandboxJob>) -> WorkerResults {
        let mut reports = Vec::new();
        let mut dead_letter = Vec::new();

        for job in jobs {
            let mut attempt = 0u32;
            let report = loop {
                attempt += 1;
                let r = self.runner.run(job.clone(), self.limits.clone()).await;
                if matches!(r.outcome, SandboxOutcome::Completed { .. })
                    || attempt > self.max_retries
                {
                    break r;
                }
            };
            let failed = !matches!(report.outcome, SandboxOutcome::Completed { .. });
            reports.push(report);
            if failed {
                dead_letter.push(job);
            }
        }

        WorkerResults {
            reports,
            dead_letter,
        }
    }
}
