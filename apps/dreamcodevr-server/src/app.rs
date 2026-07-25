//! Application logic: turn decoded frames into validated decisions + responses.
use std::io::Write;
use std::sync::Arc;
use std::time::Duration;

use serde::Serialize;
use thiserror::Error;

use dcvr_behaviour_dsl::ActionPlan;
use dcvr_command_router::{AudioOutcome, Router};
use dcvr_control::ControlBus;
use dcvr_llm_client::LlmClient;
use dcvr_observability::{JsonlWriter, ObservabilityError};
use dcvr_personalization::{PersonalizationStore, Personalizer};
use dcvr_protocol::{
    encode_frame, split_peer_payload, NetworkFrame, ProtocolError, NID_AUDIO_INPUT,
    NID_BACKEND_OUTPUT, NID_SELECTED_OBJECT,
};
use dcvr_roslyn_client::RoslynAnalyzer;
use dcvr_stt_client::{AudioUtterance, SttClient};

#[derive(Debug, Error)]
pub enum AppError {
    #[error("protocol error: {0}")]
    Protocol(#[from] ProtocolError),
    #[error("observability error: {0}")]
    Observability(#[from] ObservabilityError),
    #[error("serialize error: {0}")]
    Serialize(String),
}

#[derive(Debug)]
pub enum HandleResult {
    Response(Vec<u8>),
    Handled,
    Ignored,
}

/// The NID 94 payload (mirrored by `unity/Runtime/ProtocolModels.cs`).
#[derive(Serialize)]
struct BackendResponse<'a> {
    #[serde(rename = "type")]
    kind: &'static str,
    request_id: &'a str,
    decision: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    action_plan: Option<&'a ActionPlan>,
    #[serde(skip_serializing_if = "Option::is_none")]
    csharp_candidate: Option<&'a str>,
    #[serde(skip_serializing_if = "Option::is_none")]
    csharp_decision: Option<&'static str>,
    errors: Vec<String>,
}

/// Shared, cheaply-clonable STT/LLM clients + timeouts. Built once and shared
/// across connection tasks via `Arc`.
#[derive(Clone)]
pub struct Services {
    pub stt: Arc<dyn SttClient>,
    pub llm: Arc<dyn LlmClient>,
    pub stt_timeout: Duration,
    pub llm_timeout: Duration,
    /// Optional overall per-utterance deadline (belt-and-suspenders umbrella over the
    /// per-step timeouts). `None` = disabled (default, legacy byte-identical); `Some`
    /// = the whole STT→LLM→validate block is bounded and fails closed on elapse.
    pub utterance_timeout: Option<Duration>,
    /// Max concurrent in-flight utterances per peer (backpressure vs task-flood DoS).
    pub max_inflight_per_peer: usize,
    /// Opt-in: give each peer its own router so peers don't serialise on one lock.
    /// Default false = one shared router (legacy byte-identical). Env `DCVR_PER_PEER_ROUTING`.
    pub per_peer_routing: bool,
    /// Dev/research only: route NID 98 through the validated-C# (Mode B) path.
    pub csharp_research: bool,
    /// Mode A: emit the validated generated C# (NID 94 `{type,peer,data}`) to the
    /// original Unity `CodeGenerationManager` for runtime RoslynCSharp compilation.
    pub mode_a: bool,
    pub roslyn: Arc<dyn RoslynAnalyzer>,
    /// Runtime control plane (admin-panel config + live event bus).
    pub bus: ControlBus,
    /// Personalization / RAG engine (None = disabled).
    pub personalizer: Option<Arc<Personalizer>>,
    /// The personalization store, shared with the admin panel for profile views.
    pub personalization_store: Option<Arc<dyn PersonalizationStore>>,
    /// Live Ubiq sender to the headset, set once `run_ubiq_peer` joins the room. Lets
    /// the admin panel's MANUAL COMMAND box DISPATCH generated code to the Quest (build
    /// it in VR), not merely validate it. `None` until a room connection exists.
    pub ubiq_sender: Arc<tokio::sync::RwLock<Option<dcvr_unity_transport::PeerSender>>>,
    /// The most recent headset peer that issued a command, so a manual command targets
    /// the same client the audio path would (NID-94 `peer` field). `None` until a
    /// headset has spoken.
    pub last_client_peer: Arc<tokio::sync::RwLock<Option<String>>>,
    /// Phase-1 authentication seam (spec §10). Inert in the `legacy` profile
    /// (byte-identical to today); signs NID-94 + verifies client envelopes in
    /// `hardened`/`test`. Shared read-only across connection tasks.
    pub auth: Arc<crate::auth_gate::ServerAuth>,
}

impl Services {
    /// Whether Mode A may dispatch validated C# to the headset **right now**.
    ///
    /// The startup setting (`DCVR_MODE_A`) is the CEILING; the admin panel's live
    /// `enable_mode_a` toggle can only narrow it. An operator must never be able to
    /// switch runtime code dispatch ON in a process that was started without it —
    /// that would widen the trust surface at runtime — but they must be able to
    /// switch it OFF instantly, which is the whole point of having the toggle.
    pub fn mode_a_live(&self) -> bool {
        self.mode_a && self.bus.config().enable_mode_a
    }
}

impl std::fmt::Debug for Services {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Services")
            .field("stt_timeout", &self.stt_timeout)
            .field("llm_timeout", &self.llm_timeout)
            .field("utterance_timeout", &self.utterance_timeout)
            .field("csharp_research", &self.csharp_research)
            .finish()
    }
}

/// Per-connection application state: a peer-session router, a JSONL timing sink,
/// and the shared services.
pub struct App<W: Write> {
    router: Router,
    jsonl: JsonlWriter<W>,
    services: Services,
}

impl<W: Write> App<W> {
    pub fn new(jsonl: JsonlWriter<W>, services: Services) -> Self {
        let mut router = Router::new().with_bus(services.bus.clone());
        if let Some(p) = &services.personalizer {
            router = router.with_personalizer(p.clone());
        }
        Self {
            router,
            jsonl,
            services,
        }
    }

    /// Dispatch a decoded frame. NID 93 stores selection context; NID 98 runs the
    /// async STT -> LLM -> validate pipeline and returns a NID 94 response.
    pub async fn handle_frame(&mut self, frame: &NetworkFrame) -> Result<HandleResult, AppError> {
        if frame.network_id == NID_SELECTED_OBJECT {
            let pp = split_peer_payload(&frame.payload)?;
            let selected = String::from_utf8_lossy(&pp.body).to_string();
            self.router.set_selected_object(&pp.peer_uuid, selected);
            Ok(HandleResult::Handled)
        } else if frame.network_id == NID_AUDIO_INPUT {
            let pp = split_peer_payload(&frame.payload)?;
            // NID 98 body = PCM audio. The mock STT decodes it as UTF-8 text, so a
            // keyless local demo works; the HTTP STT transcribes real PCM.
            let audio = AudioUtterance::new_16k_mono(pp.body);
            let outcome = if self.services.csharp_research {
                self.router
                    .process_audio_dual(
                        &pp.peer_uuid,
                        audio,
                        self.services.stt.as_ref(),
                        self.services.llm.as_ref(),
                        self.services.roslyn.as_ref(),
                        self.services.stt_timeout,
                        self.services.llm_timeout,
                    )
                    .await
            } else {
                self.router
                    .process_audio(
                        &pp.peer_uuid,
                        audio,
                        self.services.stt.as_ref(),
                        self.services.llm.as_ref(),
                        self.services.stt_timeout,
                        self.services.llm_timeout,
                    )
                    .await
            };
            self.jsonl.write_event(&outcome.timing)?;
            let json = backend_decision_json(&outcome)?;
            let bytes = encode_frame(NID_BACKEND_OUTPUT, json.as_bytes())?;
            Ok(HandleResult::Response(bytes))
        } else {
            Ok(HandleResult::Ignored)
        }
    }
}

/// Build the NID 94 BackendDecision JSON for an outcome (used by both the
/// standalone listener and the Ubiq service-peer path).
pub fn backend_decision_json(outcome: &AudioOutcome) -> Result<String, AppError> {
    let mut errors: Vec<String> = Vec::new();
    if let Some(e) = &outcome.error {
        errors.push(e.clone());
    }
    let (csharp_candidate, csharp_decision) = match &outcome.csharp {
        Some(r) => {
            if !r.approved {
                for v in &r.violations {
                    errors.push(format!("csharp: {v}"));
                }
            }
            (
                Some(r.candidate.as_str()),
                Some(if r.approved {
                    "ApproveCSharpResearchMode"
                } else {
                    "Reject"
                }),
            )
        }
        None => (None, None),
    };
    let resp = BackendResponse {
        kind: "BackendDecision",
        request_id: &outcome.request_id,
        decision: format!("{:?}", outcome.decision),
        action_plan: outcome.plan.as_ref(),
        csharp_candidate,
        csharp_decision,
        errors,
    };
    serde_json::to_string(&resp).map_err(|e| AppError::Serialize(e.to_string()))
}
