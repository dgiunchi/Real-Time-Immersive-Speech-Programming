//! Async `tokio` TCP server. Accepts the same Ubiq-like frames as Phase 1 and
//! preserves the NID 94 contract; the Unity/Quest client is unchanged.
use std::sync::Arc;
use std::time::Duration;

use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream};

use dcvr_config::Settings;
use dcvr_control::{ControlBus, PipelineEvent, RuntimeConfig, Stage};
use dcvr_llm_client::{LlmClient, MockLlmClient, OpenAiLlmClient};
use dcvr_observability::JsonlWriter;
use dcvr_personalization::{
    EmbeddingClient, FilePersonalizationStore, MockEmbeddingClient, OpenAiEmbeddingClient,
    PersonalizationStore, Personalizer,
};
use dcvr_protocol::{decode_frame, ProtocolError};
use dcvr_roslyn_client::{HttpRoslynAnalyzer, MockRoslynAnalyzer, RoslynAnalyzer};
use dcvr_stt_client::{HttpSttClient, MockSttClient, OpenAiSttClient, SmartSttClient, SttClient};
use secrecy::{ExposeSecret, SecretString};

use crate::app::{App, HandleResult, Services};

/// Build the STT/LLM service bundle from settings. With no endpoint/key
/// configured, the offline mock clients are selected (keyless local demo).
pub fn services_from_settings(settings: Settings) -> Services {
    // Phase-1 auth seam: built from the profile + keys before `settings` is
    // consumed by the client constructors below. Legacy => inert (byte-identical).
    let auth = std::sync::Arc::new(crate::auth_gate::ServerAuth::from_settings(&settings));
    // Capture values needed AFTER `settings` fields are moved into clients below.
    let model = settings.openai_model.clone();
    let perso_dir = settings.personalization_dir.clone();
    let embed_openai = settings.embed_openai;
    let embed_key = settings
        .openai_api_key
        .as_ref()
        .map(|k| SecretString::from(k.expose_secret().to_string()));
    let stt: Arc<dyn SttClient> = if settings.stt_openai {
        // OpenAI Whisper STT. Reuse the OpenAI key (re-wrapped so the LLM can also
        // take ownership of the original below). Wrapped in SmartSttClient so a TYPED
        // command (sent as the NID-98 payload by the demo) bypasses Whisper, while
        // real mic PCM still goes to Whisper.
        match &settings.openai_api_key {
            Some(key) => {
                let stt_key = SecretString::from(key.expose_secret().to_string());
                Arc::new(SmartSttClient::new(Arc::new(OpenAiSttClient::new(
                    stt_key,
                    settings.openai_stt_model.clone(),
                ))))
            }
            None => Arc::new(MockSttClient),
        }
    } else if let Some(url) = settings.stt_http_url.clone() {
        Arc::new(SmartSttClient::new(Arc::new(HttpSttClient::new(url))))
    } else {
        Arc::new(MockSttClient)
    };
    let llm: Arc<dyn LlmClient> = match settings.openai_api_key {
        Some(key) => {
            let mut client = OpenAiLlmClient::new(key, settings.openai_model);
            if let Some(base) = settings.openai_base_url {
                client = client.with_base_url(base);
            }
            Arc::new(client)
        }
        None => Arc::new(MockLlmClient),
    };
    let roslyn: Arc<dyn RoslynAnalyzer> = match settings.roslyn_url {
        Some(url) => Arc::new(HttpRoslynAnalyzer::new(url)),
        None => Arc::new(MockRoslynAnalyzer),
    };
    let cfg = RuntimeConfig {
        model,
        enable_mode_a: settings.mode_a,
        max_generations_per_min: settings.max_generations_per_min,
        personal_space_radius_m: settings.personal_space_radius_m,
        min_plan_interval_ms: settings.min_plan_interval_ms,
        comfort_rotate_max_deg_s: settings.comfort_rotate_max_deg_s,
        perceptual_hardening: settings.perceptual_hardening,
        ..RuntimeConfig::default()
    };
    let bus = ControlBus::new(cfg);
    let store: Arc<dyn PersonalizationStore> = Arc::new(FilePersonalizationStore::new(perso_dir));
    let embedder: Arc<dyn EmbeddingClient> = match (embed_openai, embed_key) {
        (true, Some(k)) => Arc::new(OpenAiEmbeddingClient::new(k, "text-embedding-3-small")),
        _ => Arc::new(MockEmbeddingClient::default()),
    };
    let personalizer = Arc::new(Personalizer::new(store.clone(), embedder));
    Services {
        stt,
        llm,
        stt_timeout: Duration::from_millis(settings.stt_timeout_ms),
        llm_timeout: Duration::from_millis(settings.llm_timeout_ms),
        csharp_research: settings.csharp_research_dev,
        mode_a: settings.mode_a,
        roslyn,
        bus,
        personalizer: Some(personalizer),
        personalization_store: Some(store),
        ubiq_sender: Arc::new(tokio::sync::RwLock::new(None)),
        last_client_peer: Arc::new(tokio::sync::RwLock::new(None)),
        auth,
    }
}

/// Serve forever: handle each connection in its own task (per-peer isolation).
pub async fn serve(listener: TcpListener, services: Services) -> std::io::Result<()> {
    loop {
        let (stream, _addr) = listener.accept().await?;
        let services = services.clone();
        tokio::spawn(async move {
            let mut app = App::new(JsonlWriter::new(std::io::stdout()), services);
            if let Err(e) = handle_connection(stream, &mut app).await {
                eprintln!("connection error: {e}");
            }
        });
    }
}

/// Accept exactly one connection, handle it, and return (used by tests; JSONL
/// discarded).
pub async fn serve_one(listener: TcpListener, services: Services) -> std::io::Result<()> {
    let (stream, _addr) = listener.accept().await?;
    let mut app = App::new(JsonlWriter::new(std::io::sink()), services);
    handle_connection(stream, &mut app).await
}

async fn handle_connection<W: std::io::Write>(
    mut stream: TcpStream,
    app: &mut App<W>,
) -> std::io::Result<()> {
    let mut buf: Vec<u8> = Vec::new();
    let mut tmp = [0u8; 4096];
    loop {
        loop {
            match decode_frame(&buf) {
                Ok(decoded) => {
                    let consumed = decoded.consumed;
                    match app.handle_frame(&decoded.frame).await {
                        Ok(HandleResult::Response(bytes)) => stream.write_all(&bytes).await?,
                        Ok(_) => {}
                        Err(e) => eprintln!("handle error: {e}"),
                    }
                    buf.drain(0..consumed);
                }
                Err(ProtocolError::Incomplete { .. }) => break,
                Err(e) => {
                    eprintln!("decode error: {e}");
                    buf.clear();
                    break;
                }
            }
        }
        let n = stream.read(&mut tmp).await?;
        if n == 0 {
            break; // peer closed
        }
        buf.extend_from_slice(&tmp[..n]);
    }
    Ok(())
}

use std::collections::HashMap;

use dcvr_command_router::{AudioOutcome, Router};
use dcvr_protocol::{split_peer_payload, NID_BACKEND_OUTPUT};
use dcvr_stt_client::AudioUtterance;
use dcvr_unity_transport::{TransportError, UbiqServicePeer};

use crate::app::backend_decision_json;

const STT_CONTROL_PREFIX: &str = "__STT_CONTROL__:";

/// Run as a Ubiq SERVICE PEER: connect to the RoomServer, JOIN the room, then
/// receive NID 98 audio (push-to-talk: __STT_CONTROL__:start, PCM chunks, stop)
/// and NID 93 selection, run the validated pipeline, and emit NID 94 decisions.
/// This is the real-Unity/Quest path (the standalone `serve` is for offline tests).
/// Safety cap on a single push-to-talk accumulation: if a client never sends the
/// "stop" control (crash / dropped frame), the buffer must not grow unbounded.
/// ~8 MiB ≈ 4 min of 16 kHz mono PCM — generous, but bounded (fail-closed).
const MAX_UTTERANCE_BYTES: usize = 8 * 1024 * 1024;

pub async fn run_ubiq_peer(
    addr: &str,
    room_guid: &str,
    services: Services,
) -> Result<(), TransportError> {
    use std::sync::Arc;
    use tokio::sync::Mutex;

    let mut peer = UbiqServicePeer::connect_and_join(addr, room_guid).await?;
    eprintln!("dreamcodevr-server: joined Ubiq room {room_guid} at {addr} (service peer)");

    // Shared, briefly-locked state. Per-utterance processing runs in spawned tasks
    // so the receive loop NEVER blocks on STT/LLM — one peer can no longer stall
    // another's audio. (Processing serialises on the router lock; true per-peer
    // STT/LLM parallelism is a documented future refinement.)
    let mut base_router = Router::new().with_bus(services.bus.clone());
    if let Some(p) = &services.personalizer {
        base_router = base_router.with_personalizer(p.clone());
    }
    let router = Arc::new(Mutex::new(base_router));
    let jsonl = Arc::new(Mutex::new(JsonlWriter::new(std::io::stdout())));
    let sender = peer.sender();
    // Publish the live sender so the admin panel's MANUAL COMMAND box can dispatch
    // generated code to the headset (build it in VR), exactly like the audio path does.
    *services.ubiq_sender.write().await = Some(sender.clone());
    // Per-peer push-to-talk accumulation buffers (touched only by the recv loop).
    let mut accum: HashMap<String, Vec<u8>> = HashMap::new();

    while let Some(frame) = peer.recv().await {
        let pp = match split_peer_payload(&frame.payload) {
            Ok(pp) => pp,
            Err(_) => continue,
        };
        match frame.network_id.b {
            93 => {
                let selected = String::from_utf8_lossy(&pp.body).to_string();
                router
                    .lock()
                    .await
                    .set_selected_object(&pp.peer_uuid, selected);
            }
            98 => {
                if pp.body.len() <= 64 {
                    if let Ok(text) = std::str::from_utf8(&pp.body) {
                        if let Some(action) = text.strip_prefix(STT_CONTROL_PREFIX) {
                            match action {
                                "start" => {
                                    accum.insert(pp.peer_uuid.clone(), Vec::new());
                                }
                                "stop" => {
                                    let bytes = accum.remove(&pp.peer_uuid).unwrap_or_default();
                                    if !bytes.is_empty() {
                                        spawn_utterance(
                                            router.clone(),
                                            jsonl.clone(),
                                            sender.clone(),
                                            services.clone(),
                                            pp.peer_uuid.clone(),
                                            bytes,
                                        );
                                    }
                                }
                                other => eprintln!("[ubiq-peer] unknown STT control: {other}"),
                            }
                            continue;
                        }
                    }
                }
                let peer_uuid = pp.peer_uuid;
                let buf = accum.entry(peer_uuid.clone()).or_default();
                buf.extend_from_slice(&pp.body);
                if buf.len() > MAX_UTTERANCE_BYTES {
                    let bytes = accum.remove(&peer_uuid).unwrap_or_default();
                    eprintln!(
                        "[ubiq-peer] utterance for {peer_uuid} exceeded {MAX_UTTERANCE_BYTES} \
                         bytes without a stop control; auto-finalizing"
                    );
                    spawn_utterance(
                        router.clone(),
                        jsonl.clone(),
                        sender.clone(),
                        services.clone(),
                        peer_uuid,
                        bytes,
                    );
                }
            }
            95 => {
                // Personalization feedback (👍/👎). Body: JSON {"liked":bool} or "like"/"dislike".
                if let Some(p) = &services.personalizer {
                    let liked = std::str::from_utf8(&pp.body)
                        .ok()
                        .and_then(|t| serde_json::from_str::<serde_json::Value>(t).ok())
                        .and_then(|v| v.get("liked").and_then(|b| b.as_bool()))
                        .or_else(|| {
                            let t = String::from_utf8_lossy(&pp.body).to_lowercase();
                            if t.contains("dislike") {
                                Some(false)
                            } else if t.contains("like") {
                                Some(true)
                            } else {
                                None
                            }
                        });
                    if let Some(liked) = liked {
                        p.feedback(&pp.peer_uuid, liked).await;
                        services.bus.publish(PipelineEvent::new(
                            "feedback",
                            &pp.peer_uuid,
                            Stage::Info,
                            if liked {
                                "👍 user liked the last result"
                            } else {
                                "👎 user disliked the last result"
                            },
                        ));
                    }
                }
            }
            96 => {
                // Unity reports the Mode-A runtime compile result -> show it in the panel.
                let v = std::str::from_utf8(&pp.body)
                    .ok()
                    .and_then(|t| serde_json::from_str::<serde_json::Value>(t).ok());
                let ok = v
                    .as_ref()
                    .and_then(|v| v.get("ok").and_then(|b| b.as_bool()))
                    .unwrap_or(false);
                let ms = v
                    .as_ref()
                    .and_then(|v| v.get("ms").and_then(|m| m.as_u64()))
                    .unwrap_or(0);
                let err = v
                    .as_ref()
                    .and_then(|v| v.get("error").and_then(|e| e.as_str()))
                    .unwrap_or_default()
                    .to_string();
                services.bus.publish(
                    PipelineEvent::new(
                        "compile",
                        &pp.peer_uuid,
                        Stage::Compile,
                        if ok {
                            "compiled the C# at runtime ✓".to_string()
                        } else {
                            format!("compile FAILED: {err}")
                        },
                    )
                    .detail(err)
                    .ok(ok)
                    .ms(ms),
                );
            }
            _ => {}
        }
    }
    eprintln!("dreamcodevr-server: Ubiq connection closed.");
    Ok(())
}

/// Process one finalised utterance in its own task and emit NID 94. Keeps the
/// receive loop free to keep accumulating audio for every peer concurrently.
#[allow(clippy::too_many_arguments)]
fn spawn_utterance(
    router: std::sync::Arc<tokio::sync::Mutex<Router>>,
    jsonl: std::sync::Arc<tokio::sync::Mutex<JsonlWriter<std::io::Stdout>>>,
    sender: dcvr_unity_transport::PeerSender,
    services: Services,
    peer_id: String,
    bytes: Vec<u8>,
) {
    tokio::spawn(async move {
        // Remember which headset just issued a command so a subsequent MANUAL COMMAND
        // from the admin panel is delivered to the same client (NID-94 `peer`).
        *services.last_client_peer.write().await = Some(peer_id.clone());
        let audio = AudioUtterance::new_16k_mono(bytes);
        // Mode A and Mode B both need the dual path (to generate + validate the C#).
        let need_csharp = services.csharp_research || services.mode_a;
        let outcome = {
            let mut r = router.lock().await;
            if need_csharp {
                r.process_audio_dual(
                    &peer_id,
                    audio,
                    services.stt.as_ref(),
                    services.llm.as_ref(),
                    services.roslyn.as_ref(),
                    services.stt_timeout,
                    services.llm_timeout,
                )
                .await
            } else {
                r.process_audio(
                    &peer_id,
                    audio,
                    services.stt.as_ref(),
                    services.llm.as_ref(),
                    services.stt_timeout,
                    services.llm_timeout,
                )
                .await
            }
        };
        {
            let mut j = jsonl.lock().await;
            let _ = j.write_event(&outcome.timing);
        }
        publish_pipeline_events(&services.bus, &outcome);
        if services.mode_a {
            // Mode A (original DreamCodeVR): hand the VALIDATED generated C# to the
            // original Unity `CodeGenerationManager` (NID 94 `{type,peer,data}`) for
            // runtime RoslynCSharp compilation. Fail-closed: only send if it passed
            // the lexical + Roslyn validation (this is how DreamCodeVR+ makes the
            // original runtime-compile path safe).
            match &outcome.csharp {
                Some(cs) if cs.approved => {
                    let msg = serde_json::json!({
                        "type": "code",
                        "peer": peer_id,
                        "data": cs.candidate,
                    })
                    .to_string();
                    // Legacy: bytes unchanged. Hardened: Ed25519-signed envelope
                    // bound to peer+request+code-hash; fail-closed (do not send
                    // unsigned code) if signing is unavailable.
                    match services.auth.sign_nid94(
                        msg.as_bytes(),
                        &peer_id,
                        &outcome.request_id,
                        crate::auth_gate::now_unix(),
                    ) {
                        Ok(bytes) => {
                            let _ = sender.send(NID_BACKEND_OUTPUT, &bytes).await;
                        }
                        Err(e) => eprintln!(
                            "[mode-a] refusing to send unsigned NID-94 (fail-closed): {e}"
                        ),
                    }
                }
                Some(cs) => {
                    eprintln!(
                        "[mode-a] generated C# REJECTED by validator; not sent ({} violation(s)): {}",
                        cs.violations.len(),
                        cs.violations.join(" | ")
                    );
                }
                None => eprintln!("[mode-a] no C# candidate generated; nothing sent"),
            }
        } else {
            let json = backend_decision_json(&outcome).unwrap_or_else(|_| "{}".to_string());
            let _ = sender.send(NID_BACKEND_OUTPUT, json.as_bytes()).await;
        }
    });
}

/// Bump the lifetime stats for one processed utterance. The per-stage LIVE events
/// (transcript / prompt / C# / validation / safety) are published in real time by
/// the router as each stage completes, so this only updates counters.
fn publish_pipeline_events(bus: &ControlBus, o: &AudioOutcome) {
    use std::sync::atomic::Ordering::Relaxed;
    let stats = bus.stats();
    stats.requests.fetch_add(1, Relaxed);
    // A Layer-1 security CATCH is a malicious BLOCK, not an approval. The router
    // already bumped `neutralized`; surface it in the headline `malicious_blocked`
    // KPI too, and keep it OUT of `approved` (a neutralized request answers with a
    // harmless recolour whose `csharp.approved` is true, which would otherwise
    // inflate the approved count and leave "malicious blocked" reading 0).
    if o.caught_reason.is_some() {
        stats.malicious_blocked.fetch_add(1, Relaxed);
        return;
    }
    // Count an LLM call only when STT produced a transcript (i.e. the LLM actually ran).
    if o.transcript.is_some() {
        stats.llm_calls.fetch_add(1, Relaxed);
        stats.total_llm_ms.fetch_add(
            o.timing.t_validated.saturating_sub(o.timing.t_received),
            Relaxed,
        );
    }
    let (approved, violations): (bool, Vec<String>) = match &o.csharp {
        Some(cs) => (cs.approved, cs.violations.clone()),
        None => (
            o.timing.decision.contains("Approve"),
            o.timing.errors.clone(),
        ),
    };
    if approved {
        stats.approved.fetch_add(1, Relaxed);
    } else {
        stats.rejected.fetch_add(1, Relaxed);
    }
    let malicious = violations.iter().any(|v| {
        let l = v.to_lowercase();
        l.contains("system.")
            || l.contains("banned")
            || l.contains("process")
            || l.contains("reflection")
            || l.contains("webrequest")
            || l.contains("unsafe")
    });
    if malicious {
        stats.malicious_blocked.fetch_add(1, Relaxed);
    }
}

/// Backend implementation of the admin panel's optional action hooks: run a typed
/// command through the REAL pipeline (the panel's manual command box), and a
/// sandbox pre-check. Holds its own router so admin commands don't touch a live
/// Unity connection's session state.
pub struct ServerHooks {
    services: Services,
    router: tokio::sync::Mutex<Router>,
}

impl ServerHooks {
    pub fn new(services: Services) -> Self {
        let mut router = Router::new().with_bus(services.bus.clone());
        if let Some(p) = &services.personalizer {
            router = router.with_personalizer(p.clone());
        }
        Self {
            services,
            router: tokio::sync::Mutex::new(router),
        }
    }
}

#[async_trait::async_trait]
impl dcvr_admin::AdminHooks for ServerHooks {
    async fn run_command(&self, peer: &str, command: &str) -> String {
        let outcome = {
            let mut r = self.router.lock().await;
            r.process_text_dual(
                peer,
                command,
                self.services.llm.as_ref(),
                self.services.roslyn.as_ref(),
                self.services.llm_timeout,
            )
            .await
        };
        publish_pipeline_events(&self.services.bus, &outcome);
        if let Some(reason) = &outcome.caught_reason {
            return format!(
                "🛡️ MALICIOUS INTENT CAUGHT & NEUTRALIZED — {reason}. \
                 No code was generated for the request; a harmless placeholder was sent instead."
            );
        }
        match &outcome.csharp {
            Some(cs) if cs.approved => {
                // DISPATCH to the headset (Mode A): send the validated C# to the Quest
                // over the SAME NID-94 `{type,peer,data}` path the audio flow uses, so a
                // typed admin-panel command actually BUILDS in VR instead of only being
                // validated. Target the last headset that issued a command; an empty
                // `peer` broadcasts to every connected client.
                let mut delivered = false;
                if self.services.mode_a {
                    if let Some(sender) = self.services.ubiq_sender.read().await.clone() {
                        let target = self
                            .services
                            .last_client_peer
                            .read()
                            .await
                            .clone()
                            .unwrap_or_default();
                        let msg = serde_json::json!({
                            "type": "code",
                            "peer": target,
                            "data": cs.candidate,
                        })
                        .to_string();
                        delivered = match self.services.auth.sign_nid94(
                            msg.as_bytes(),
                            &target,
                            &outcome.request_id,
                            crate::auth_gate::now_unix(),
                        ) {
                            Ok(bytes) => sender.send(NID_BACKEND_OUTPUT, &bytes).await.is_ok(),
                            Err(e) => {
                                eprintln!("[manual-cmd] refusing to send unsigned NID-94: {e}");
                                false
                            }
                        };
                    }
                }
                if delivered {
                    format!(
                        "{:?} — C# approved ({} chars) → SENT to the headset to build ✓",
                        outcome.decision,
                        cs.candidate.len()
                    )
                } else {
                    format!(
                        "{:?} — C# approved ({} chars); validated OK, but no headset is \
                         connected to build it",
                        outcome.decision,
                        cs.candidate.len()
                    )
                }
            }
            Some(cs) => format!(
                "{:?} — C# REJECTED: {}",
                outcome.decision,
                cs.violations.join("; ")
            ),
            None => format!("{:?} — no C# generated", outcome.decision),
        }
    }

    async fn run_sandbox(&self, code: &str) -> String {
        let v = dcvr_csharp_policy::validate_csharp_freeform(code);
        if v.decision != dcvr_csharp_policy::CsharpDecision::ApproveForResearch {
            return format!(
                "rejected before sandbox: {}",
                v.violations
                    .iter()
                    .map(|x| x.to_string())
                    .collect::<Vec<_>>()
                    .join("; ")
            );
        }
        "validated OK. Full isolated execution runs in the Mode-D sandbox (Docker + \
         dcvr-sandbox-harness; see scripts/sandbox-run-docker.sh / sandbox-run-gvisor.sh)."
            .to_string()
    }
}
