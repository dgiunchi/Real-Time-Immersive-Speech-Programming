use std::collections::VecDeque;

use crate::errors::RouterError;

/// The lifecycle of a single request within a peer session.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SessionState {
    Idle,
    Receiving,
    Transcribing,
    Generating,
    Validating,
    Executing,
    Failed,
}

/// Per-peer state. There is no global mutable state: the router owns a map of
/// these and passes `&mut` references, so peers cannot interfere with each other.
#[derive(Debug, Clone)]
pub struct PeerSession {
    pub peer_id: String,
    pub state: SessionState,
    pub selected_object: Option<String>,
    pub spawned_count: u32,
    /// Epoch-ms of recently *accepted* generations, oldest first. Used for the
    /// per-peer sliding-window rate limit (anti-flood, SOC-03/PE-02).
    accepts: VecDeque<u64>,
    /// Epoch-ms of the last accepted generation (anti-strobe interval gate).
    last_accept_ms: Option<u64>,
}

/// Why an incoming generation was admitted or dropped by the per-peer gates.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AdmitDecision {
    /// Within both gates; the generation may proceed (window updated).
    Allowed,
    /// Dropped because the per-minute generation rate limit was exceeded.
    RateLimited,
    /// Dropped because it arrived sooner than `min_plan_interval_ms` after the last.
    TooSoon,
}

impl PeerSession {
    pub fn new(peer_id: String) -> Self {
        Self {
            peer_id,
            state: SessionState::Idle,
            selected_object: None,
            spawned_count: 0,
            accepts: VecDeque::new(),
            last_accept_ms: None,
        }
    }

    /// Admission control for one incoming generation at wall-clock `now_ms`.
    ///
    /// Two independent per-peer gates (no global lock):
    /// * **min-plan-interval** — reject if `< min_interval_ms` since the last
    ///   accepted plan (anti-strobe / WCAG 2.3.1).
    /// * **rate limit** — a sliding 60s window may hold at most
    ///   `max_per_min` accepted plans (anti-flood, SOC-03/PE-02).
    ///
    /// On `Allowed` the window/last-accept bookkeeping is advanced; on a drop the
    /// state is left unchanged so a rejected spam attempt cannot reset the gate.
    /// A `max_per_min` of 0 disables the rate gate; `min_interval_ms` of 0
    /// disables the interval gate.
    pub fn admit_generation(
        &mut self,
        now_ms: u64,
        max_per_min: u32,
        min_interval_ms: u64,
    ) -> AdmitDecision {
        if min_interval_ms > 0 {
            if let Some(last) = self.last_accept_ms {
                if now_ms.saturating_sub(last) < min_interval_ms {
                    return AdmitDecision::TooSoon;
                }
            }
        }
        // Evict timestamps older than the 60s sliding window.
        while let Some(&front) = self.accepts.front() {
            if now_ms.saturating_sub(front) >= 60_000 {
                self.accepts.pop_front();
            } else {
                break;
            }
        }
        if max_per_min > 0 && self.accepts.len() as u32 >= max_per_min {
            return AdmitDecision::RateLimited;
        }
        self.accepts.push_back(now_ms);
        self.last_accept_ms = Some(now_ms);
        AdmitDecision::Allowed
    }

    /// Move to `next` if the transition is allowed; otherwise return an error
    /// and leave the state unchanged (fail-closed bookkeeping).
    pub fn transition_to(&mut self, next: SessionState) -> Result<(), RouterError> {
        if Self::is_valid_transition(self.state, next) {
            self.state = next;
            Ok(())
        } else {
            Err(RouterError::InvalidTransition {
                from: format!("{:?}", self.state),
                to: format!("{:?}", next),
            })
        }
    }

    fn is_valid_transition(from: SessionState, to: SessionState) -> bool {
        use SessionState::{
            Executing, Failed, Generating, Idle, Receiving, Transcribing, Validating,
        };
        matches!(
            (from, to),
            (Idle, Receiving)
                | (Receiving, Transcribing)
                | (Receiving, Generating) // Phase-1 mock skips real STT
                | (Transcribing, Generating)
                | (Generating, Validating)
                | (Validating, Executing)
                | (Executing, Idle)
                | (Failed, Idle)
                | (_, Failed)
        )
    }
}
