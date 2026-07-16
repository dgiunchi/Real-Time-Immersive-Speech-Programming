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

// Unit 1: a hung Layer-1 screen classifier must NOT stall the router. The router
// holds a global Mutex across this await (server side), so an unbounded screen call
// would wedge every peer. screen_intent is fail-OPEN, so a timeout maps to `None`
// and generation proceeds — bounded by llm_timeout, never the classifier's hang.
struct HangingScreenLlm;
#[async_trait::async_trait]
impl dcvr_llm_client::LlmClient for HangingScreenLlm {
    async fn generate_plan(
        &self,
        request_id: &str,
        transcript: &str,
    ) -> Result<dcvr_behaviour_dsl::ActionPlan, dcvr_llm_client::LlmError> {
        // Delegate to the deterministic mock so a valid command is approved.
        dcvr_llm_client::LlmClient::generate_plan(
            &dcvr_llm_client::MockLlmClient,
            request_id,
            transcript,
        )
        .await
    }
    async fn screen_intent(
        &self,
        _request_id: &str,
        _command: &str,
    ) -> Result<dcvr_llm_client::IntentVerdict, dcvr_llm_client::LlmError> {
        // Hangs far longer than any test timeout. If it EVER returned it would flag
        // malicious — so a non-neutralized outcome proves the timeout (not the
        // verdict) let generation proceed.
        tokio::time::sleep(std::time::Duration::from_secs(30)).await;
        Ok(dcvr_llm_client::IntentVerdict {
            malicious: true,
            category: "would-block".to_string(),
            reason: "would-block".to_string(),
        })
    }
}

#[tokio::test]
async fn hung_screen_classifier_does_not_stall_the_router() {
    use std::time::Duration;
    let mut router = Router::new();
    // llm_timeout is 100 ms; the screen sleeps 30 s. An unbounded screen would block
    // the router for 30 s; the 3 s outer guard proves it does not.
    let res = tokio::time::timeout(
        Duration::from_secs(3),
        router.process_text_dual(
            "p",
            "make this cube red",
            &HangingScreenLlm,
            &dcvr_roslyn_client::MockRoslynAnalyzer,
            Duration::from_millis(100),
        ),
    )
    .await;
    let out = res.expect("router must not hang when the Layer-1 screen classifier hangs");
    // Screen timed out -> fail-open None -> generation proceeded (NOT neutralized).
    assert_eq!(out.decision, Decision::ApproveActionPlan);
    assert!(
        out.caught_reason.is_none(),
        "a timed-out fail-open screen must not neutralize the command"
    );
}

// Unit 1: a hung RAG embedder must NOT stall augment/record either. Same pattern:
// the embed await runs under the router lock and must fail open (no context) on a
// bounded timeout. with_rag_embed_timeout lets us drive it without a 30 s wait.
struct HangingEmbedder;
#[async_trait::async_trait]
impl dcvr_personalization::EmbeddingClient for HangingEmbedder {
    async fn embed(&self, _text: &str) -> Result<Vec<f32>, dcvr_personalization::EmbedError> {
        tokio::time::sleep(std::time::Duration::from_secs(30)).await;
        Ok(vec![0.0; 8])
    }
}

#[tokio::test]
async fn hung_rag_embedder_does_not_stall_the_router() {
    use dcvr_control::{ControlBus, RuntimeConfig};
    use dcvr_personalization::{
        InMemoryStore, MemoryRecord, PeerData, PersonalizationStore, Personalizer,
    };
    use std::sync::Arc;
    use std::time::Duration;

    let store: Arc<dyn PersonalizationStore> = Arc::new(InMemoryStore::default());
    // Seed a LIKED memory so Personalizer::context() actually reaches embed() on the
    // AUGMENT path — it short-circuits (never embeds) when there is no liked memory.
    // This makes the test exercise BOTH the augment AND record embed timeouts, so
    // removing EITHER wrap makes it hang (verified). Precomputed embedding, so seeding
    // needs no (hung) embedder.
    store.save(
        "p",
        &PeerData {
            memories: vec![MemoryRecord {
                seq: 1,
                command: "make it red".to_string(),
                result: "ok".to_string(),
                liked: Some(true),
                embedding: vec![0.1; 8],
            }],
            next_seq: 1,
            ..Default::default()
        },
    );
    let personalizer = Arc::new(Personalizer::new(store, Arc::new(HangingEmbedder)));
    let bus = ControlBus::new(RuntimeConfig {
        enable_rag: true,
        ..RuntimeConfig::default()
    });
    let mut router = Router::new()
        .with_bus(bus)
        .with_personalizer(personalizer)
        .with_rag_embed_timeout(Duration::from_millis(100)); // tiny bound for the test

    let res = tokio::time::timeout(
        Duration::from_secs(3),
        router.process_text_dual(
            "p",
            "make this cube red",
            &dcvr_llm_client::MockLlmClient,
            &dcvr_roslyn_client::MockRoslynAnalyzer,
            Duration::from_secs(5),
        ),
    )
    .await;
    let out = res.expect("router must not hang when the RAG embedder hangs");
    // Both embed awaits timed out -> fail-open (no context / skipped record) -> the
    // pipeline still produced a decision.
    assert_eq!(out.decision, Decision::ApproveActionPlan);
}

// Unit 4: an LLM that emits C# touching an unambiguous device API (OVRHaptics). The
// C# is otherwise a valid MonoBehaviour, so the ONLY thing that can change its
// approval is the perceptual-hardening profile the router selects from the bus.
struct HapticsCsharpLlm;
#[async_trait::async_trait]
impl dcvr_llm_client::LlmClient for HapticsCsharpLlm {
    async fn generate_plan(
        &self,
        request_id: &str,
        transcript: &str,
    ) -> Result<dcvr_behaviour_dsl::ActionPlan, dcvr_llm_client::LlmError> {
        dcvr_llm_client::LlmClient::generate_plan(
            &dcvr_llm_client::MockLlmClient,
            request_id,
            transcript,
        )
        .await
    }
    async fn generate_dual(
        &self,
        request_id: &str,
        transcript: &str,
    ) -> Result<dcvr_llm_client::Generation, dcvr_llm_client::LlmError> {
        let plan = dcvr_llm_client::LlmClient::generate_plan(
            &dcvr_llm_client::MockLlmClient,
            request_id,
            transcript,
        )
        .await?;
        Ok(dcvr_llm_client::Generation {
            plan,
            csharp_candidate: Some(
                "public class GeneratedBehaviour : MonoBehaviour { \
                 void Update() { OVRHaptics.RightChannel.Mix(default); } }"
                    .to_string(),
            ),
        })
    }
}

#[tokio::test]
async fn perceptual_hardening_bus_flag_flips_deployhardened_at_the_csharp_gate() {
    use dcvr_control::{ControlBus, RuntimeConfig};
    use std::time::Duration;

    // Default bus (perceptual_hardening = false) -> CreativeFreedom -> device API is
    // free (the freedom contract: hardening never restricts by default).
    let mut free = Router::new().with_bus(ControlBus::new(RuntimeConfig {
        enable_mode_a: true,
        ..RuntimeConfig::default()
    }));
    let out_free = free
        .process_text_dual(
            "p",
            "buzz the controller",
            &HapticsCsharpLlm,
            &dcvr_roslyn_client::MockRoslynAnalyzer,
            Duration::from_secs(5),
        )
        .await;
    let cs_free = out_free.csharp.expect("dual path returns csharp");
    assert!(
        cs_free.approved,
        "haptics C# is free under the default profile"
    );

    // Hardened bus (perceptual_hardening = true) -> DeployHardened -> device API is
    // rejected. Proves the env->Settings->RuntimeConfig->bus->hardening_profile()
    // chain actually reaches the C# gate (previously untested at router level).
    let mut hard = Router::new().with_bus(ControlBus::new(RuntimeConfig {
        enable_mode_a: true,
        perceptual_hardening: true,
        ..RuntimeConfig::default()
    }));
    let out_hard = hard
        .process_text_dual(
            "p",
            "buzz the controller",
            &HapticsCsharpLlm,
            &dcvr_roslyn_client::MockRoslynAnalyzer,
            Duration::from_secs(5),
        )
        .await;
    let cs_hard = out_hard.csharp.expect("dual path returns csharp");
    assert!(
        !cs_hard.approved,
        "once perceptual_hardening is on, the haptics device API must be rejected"
    );
    assert!(
        cs_hard
            .violations
            .iter()
            .any(|v| v.to_lowercase().contains("ovrhaptics") || v.contains("OVRHaptics")),
        "the rejection should name the perceptual device token: {:?}",
        cs_hard.violations
    );
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
