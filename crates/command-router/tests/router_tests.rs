//! Router/session tests (required tests 18, 19, 20).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_code_policy::Decision;
use dcvr_command_router::mock::mock_generate;
use dcvr_command_router::{PeerSession, Router, SessionState};

// Test 18: a request id is created and propagated into the outcome + timing.
#[test]
fn request_id_created_and_propagated() {
    let mut router = Router::new();
    let out = router.process_transcript("peer-1", "make this cube red");
    assert!(!out.request_id.is_empty());
    assert_eq!(out.request_id, out.timing.request_id);
    assert_eq!(out.plan.request_id, out.request_id);
}

// Test 19: session state transitions behave (valid path ok, invalid rejected).
#[test]
fn session_transitions_work() {
    let mut s = PeerSession::new("peer-1".to_string());
    assert_eq!(s.state, SessionState::Idle);
    assert!(s.transition_to(SessionState::Receiving).is_ok());
    assert!(s.transition_to(SessionState::Generating).is_ok());
    // Idle -> Executing is not a legal jump.
    let mut s2 = PeerSession::new("peer-2".to_string());
    assert!(s2.transition_to(SessionState::Executing).is_err());
    assert_eq!(s2.state, SessionState::Idle); // unchanged on rejection
}

// Test 20: mock generation is deterministic and intent-appropriate.
#[test]
fn mock_generation_deterministic() {
    let a = mock_generate("req-1", "make this cube red");
    let b = mock_generate("req-1", "make this cube red");
    assert_eq!(a, b);
    assert!(a.actions.iter().any(
        |act| matches!(act, dcvr_behaviour_dsl::Action::SetColor { color } if color == "#FF0000")
    ));
}

// The mock pipeline approves a simple, valid command end-to-end.
#[test]
fn process_transcript_approves_valid_command() {
    let mut router = Router::new();
    let out = router.process_transcript("peer-1", "make this cube red");
    assert_eq!(out.decision, Decision::ApproveActionPlan);
    assert_eq!(out.timing.action_count, 1);
}

// Phase 2: async pipeline approves a simple valid command (mock STT+LLM).
#[tokio::test]
async fn process_audio_happy_path_approves() {
    use dcvr_llm_client::MockLlmClient;
    use dcvr_stt_client::{AudioUtterance, MockSttClient};
    use std::time::Duration;
    let mut router = Router::new();
    let audio = AudioUtterance::new_16k_mono(b"make this cube red".to_vec());
    let out = router
        .process_audio(
            "peer-1",
            audio,
            &MockSttClient,
            &MockLlmClient,
            Duration::from_secs(5),
            Duration::from_secs(5),
        )
        .await;
    assert_eq!(out.decision, Decision::ApproveActionPlan);
    assert!(out.plan.is_some());
    assert!(out.error.is_none());
}

// Phase 2: empty audio is rejected fail-closed (no plan, error set).
#[tokio::test]
async fn process_audio_empty_audio_is_rejected() {
    use dcvr_llm_client::MockLlmClient;
    use dcvr_stt_client::{AudioUtterance, MockSttClient};
    use std::time::Duration;
    let mut router = Router::new();
    let out = router
        .process_audio(
            "peer-1",
            AudioUtterance::new_16k_mono(vec![]),
            &MockSttClient,
            &MockLlmClient,
            Duration::from_secs(5),
            Duration::from_secs(5),
        )
        .await;
    assert_eq!(out.decision, Decision::RejectUnsafe);
    assert!(out.plan.is_none());
    assert!(out.error.is_some());
}

// Phase 3: dual path returns a statically-validated (approved) C# candidate.
#[tokio::test]
async fn process_audio_dual_returns_validated_csharp() {
    use dcvr_llm_client::MockLlmClient;
    use dcvr_stt_client::{AudioUtterance, MockSttClient};
    use std::time::Duration;
    let mut router = Router::new();
    let audio = AudioUtterance::new_16k_mono(b"make this cube red and bigger".to_vec());
    let out = router
        .process_audio_dual(
            "p",
            audio,
            &MockSttClient,
            &MockLlmClient,
            &dcvr_roslyn_client::MockRoslynAnalyzer,
            Duration::from_secs(5),
            Duration::from_secs(5),
        )
        .await;
    assert_eq!(out.decision, Decision::ApproveActionPlan);
    let cs = out.csharp.expect("dual path returns csharp");
    assert!(
        cs.approved,
        "template c# should pass policy: {:?}",
        cs.violations
    );
}

// Review fix (privacy): external STT error DETAIL must never reach the outcome
// error, the NID 94 response, or the JSONL timing — only a fixed reason code.
struct LeakySttClient;
#[async_trait::async_trait]
impl dcvr_stt_client::SttClient for LeakySttClient {
    async fn transcribe(
        &self,
        _a: &dcvr_stt_client::AudioUtterance,
    ) -> Result<dcvr_stt_client::Transcript, dcvr_stt_client::SttError> {
        Err(dcvr_stt_client::SttError::Request(
            "SECRET_TRANSCRIPT_user_said_my_password".to_string(),
        ))
    }
}

#[tokio::test]
async fn stt_error_detail_does_not_leak_into_outcome_or_timing() {
    use dcvr_stt_client::AudioUtterance;
    use std::time::Duration;
    let mut router = Router::new();
    let out = router
        .process_audio(
            "p",
            AudioUtterance::new_16k_mono(b"hi".to_vec()),
            &LeakySttClient,
            &dcvr_llm_client::MockLlmClient,
            Duration::from_secs(5),
            Duration::from_secs(5),
        )
        .await;
    assert_eq!(out.decision, Decision::RejectUnsafe);
    assert_eq!(out.error.as_deref(), Some("stt_unavailable"));
    assert!(out
        .error
        .as_deref()
        .map(|e| !e.contains("SECRET"))
        .unwrap_or(true));
    assert!(out.timing.errors.iter().all(|e| !e.contains("SECRET")));
    assert!(out.plan.is_none());
}

// Review fix: cumulative per-session spawn budget (64) is enforced across plans.
struct SpawnyLlmClient {
    spawns: u32,
}
#[async_trait::async_trait]
impl dcvr_llm_client::LlmClient for SpawnyLlmClient {
    async fn generate_plan(
        &self,
        request_id: &str,
        _t: &str,
    ) -> Result<dcvr_behaviour_dsl::ActionPlan, dcvr_llm_client::LlmError> {
        use dcvr_behaviour_dsl::{Action, ParentRef, Shape, Target};
        let n = (self.spawns / 8) as usize;
        let actions = std::iter::repeat_with(|| Action::SpawnPrimitive {
            shape: Shape::Cube,
            count: 8,
            parent: ParentRef::Target,
        })
        .take(n)
        .collect();
        Ok(dcvr_behaviour_dsl::ActionPlan {
            schema_version: "1.0".to_string(),
            request_id: request_id.to_string(),
            target: Target::SceneRoot,
            actions,
        })
    }
}

#[tokio::test]
async fn session_spawn_budget_enforced_cumulatively() {
    use dcvr_stt_client::{AudioUtterance, MockSttClient};
    use std::time::Duration;
    let mut router = Router::new();
    let llm = SpawnyLlmClient { spawns: 40 };
    let a = || AudioUtterance::new_16k_mono(b"spawn".to_vec());
    let o1 = router
        .process_audio(
            "p",
            a(),
            &MockSttClient,
            &llm,
            Duration::from_secs(5),
            Duration::from_secs(5),
        )
        .await;
    assert_eq!(
        o1.decision,
        Decision::ApproveActionPlan,
        "first 40 within budget"
    );
    let o2 = router
        .process_audio(
            "p",
            a(),
            &MockSttClient,
            &llm,
            Duration::from_secs(5),
            Duration::from_secs(5),
        )
        .await;
    assert_eq!(
        o2.decision,
        Decision::RejectUnsafe,
        "cumulative 80 > 64 rejected"
    );
    assert!(o2
        .timing
        .errors
        .iter()
        .any(|e| e.contains("session_spawn_budget")));
}
