//! Liveness watchdog for the backend (Phase 5, external supervisor).
//!
//! The failure mode this guards is subtle: a wedged pipeline (a stuck lock, a hung
//! provider call that slipped past the per-step timeouts) keeps the *process* alive,
//! so a plain "is it running?" check is useless — and the admin `/api/health` route
//! only echoes config. The watchdog must therefore drive a DEADLINE-BOUNDED,
//! end-to-end probe (a real synthetic request that must produce a decision) and
//! restart the child only after repeated unresponsiveness, with exponential backoff
//! so a crash-loop can't spin.
//!
//! This module is the PURE decision core — backoff + the restart policy — and is
//! unit-tested here. The live probe (a bounded TCP round-trip) and the process
//! spawn/kill live in the `watchdog` binary, which needs a running backend and is
//! exercised on-device, not in the cargo gate.

use std::time::Duration;

/// Result of one liveness probe.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Probe {
    /// The backend answered a synthetic request within the deadline.
    Healthy,
    /// No valid answer within the deadline (wedged, crashed, or unreachable).
    Unresponsive,
}

/// What the supervisor should do after folding in a probe.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Action {
    /// Keep watching; the child is (still) considered alive.
    Continue,
    /// Restart the child (kill, back off, respawn).
    Restart,
}

/// Exponential backoff between restarts, reset when the child becomes healthy so a
/// backend that recovers doesn't keep paying an ever-growing delay.
#[derive(Debug, Clone)]
pub struct Backoff {
    base: Duration,
    max: Duration,
    factor: u32,
    attempt: u32,
}

impl Backoff {
    pub fn new(base: Duration, max: Duration, factor: u32) -> Self {
        Self {
            base,
            max,
            factor: factor.max(1),
            attempt: 0,
        }
    }

    /// The delay to wait before the NEXT restart, growing geometrically each call and
    /// clamped to `max`. Saturating throughout, so it never panics or wraps.
    pub fn next_delay(&mut self) -> Duration {
        // Clamp the exponent so `factor^attempt` can't overflow before saturating.
        let mult = self.factor.saturating_pow(self.attempt.min(16));
        self.attempt = self.attempt.saturating_add(1);
        self.base.saturating_mul(mult).min(self.max)
    }

    /// Return to the base delay (call after a healthy probe).
    pub fn reset(&mut self) {
        self.attempt = 0;
    }
}

/// Restart after this many CONSECUTIVE unresponsive probes.
#[derive(Debug, Clone)]
pub struct WatchdogPolicy {
    pub failures_before_restart: u32,
}

impl Default for WatchdogPolicy {
    fn default() -> Self {
        Self {
            failures_before_restart: 3,
        }
    }
}

/// Tracks consecutive failures and decides restart-vs-continue. A single transient
/// blip won't restart the child; a sustained wedge will.
#[derive(Debug, Default)]
pub struct WatchdogState {
    consecutive_failures: u32,
}

impl WatchdogState {
    pub fn consecutive_failures(&self) -> u32 {
        self.consecutive_failures
    }

    /// Fold in one probe outcome and decide. A healthy probe clears the failure
    /// streak; the Nth consecutive failure triggers a restart and resets the streak
    /// (the fresh child starts clean).
    pub fn observe(&mut self, outcome: Probe, policy: &WatchdogPolicy) -> Action {
        match outcome {
            Probe::Healthy => {
                self.consecutive_failures = 0;
                Action::Continue
            }
            Probe::Unresponsive => {
                self.consecutive_failures = self.consecutive_failures.saturating_add(1);
                if self.consecutive_failures >= policy.failures_before_restart.max(1) {
                    self.consecutive_failures = 0;
                    Action::Restart
                } else {
                    Action::Continue
                }
            }
        }
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    #[test]
    fn backoff_grows_geometrically_then_clamps() {
        let mut b = Backoff::new(Duration::from_secs(1), Duration::from_secs(20), 2);
        assert_eq!(b.next_delay(), Duration::from_secs(1)); // 1*2^0
        assert_eq!(b.next_delay(), Duration::from_secs(2)); // 1*2^1
        assert_eq!(b.next_delay(), Duration::from_secs(4)); // 1*2^2
        assert_eq!(b.next_delay(), Duration::from_secs(8)); // 1*2^3
        assert_eq!(b.next_delay(), Duration::from_secs(16)); // 1*2^4
        assert_eq!(b.next_delay(), Duration::from_secs(20)); // clamped
        assert_eq!(b.next_delay(), Duration::from_secs(20)); // stays clamped
    }

    #[test]
    fn backoff_reset_returns_to_base() {
        let mut b = Backoff::new(Duration::from_secs(1), Duration::from_secs(20), 2);
        let _ = b.next_delay();
        let _ = b.next_delay();
        b.reset();
        assert_eq!(
            b.next_delay(),
            Duration::from_secs(1),
            "reset returns to base"
        );
    }

    #[test]
    fn backoff_never_panics_on_extreme_attempts() {
        let mut b = Backoff::new(Duration::from_secs(5), Duration::from_secs(60), 10);
        for _ in 0..100 {
            let d = b.next_delay();
            assert!(d <= Duration::from_secs(60));
        }
    }

    #[test]
    fn restarts_only_after_consecutive_failures() {
        let policy = WatchdogPolicy {
            failures_before_restart: 3,
        };
        let mut s = WatchdogState::default();
        assert_eq!(s.observe(Probe::Unresponsive, &policy), Action::Continue); // 1
        assert_eq!(s.observe(Probe::Unresponsive, &policy), Action::Continue); // 2
        assert_eq!(s.observe(Probe::Unresponsive, &policy), Action::Restart); // 3 -> restart
        assert_eq!(s.consecutive_failures(), 0, "streak resets after a restart");
    }

    #[test]
    fn a_healthy_probe_clears_the_failure_streak() {
        let policy = WatchdogPolicy {
            failures_before_restart: 3,
        };
        let mut s = WatchdogState::default();
        s.observe(Probe::Unresponsive, &policy);
        s.observe(Probe::Unresponsive, &policy);
        assert_eq!(s.observe(Probe::Healthy, &policy), Action::Continue);
        assert_eq!(s.consecutive_failures(), 0);
        // ...so it now takes a fresh run of failures to restart, not just one more.
        assert_eq!(s.observe(Probe::Unresponsive, &policy), Action::Continue);
        assert_eq!(s.observe(Probe::Unresponsive, &policy), Action::Continue);
        assert_eq!(s.observe(Probe::Unresponsive, &policy), Action::Restart);
    }

    #[test]
    fn a_zero_threshold_is_treated_as_one() {
        let policy = WatchdogPolicy {
            failures_before_restart: 0,
        };
        let mut s = WatchdogState::default();
        assert_eq!(
            s.observe(Probe::Unresponsive, &policy),
            Action::Restart,
            "a misconfigured 0 must not mean 'never restart'"
        );
    }
}
