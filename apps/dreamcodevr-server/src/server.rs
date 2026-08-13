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
use dcvr_stt_client::{
    AudioBounds, BoundedSttClient, HttpSttClient, MockSttClient, OpenAiSttClient, SmartSttClient,
    SttClient,
};
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
    let effort = settings.reasoning_effort.clone();
    let perso_dir = settings.personalization_dir.clone();
    let profile_enc = settings.profile_enc_key_hex.clone();
    let embed_openai = settings.embed_openai;
    let embed_key = settings
        .openai_api_key
        .as_ref()
        .map(|k| SecretString::from(k.expose_secret().to_string()));
    // Hardened profile: validate attacker-controlled NID-98 audio against AudioBounds
    // BEFORE it reaches a paid/slow backend. Composition MUST be Smart(Bounded(real))
    // so a short typed demo command still short-circuits in SmartSttClient before any
    // audio bound applies. Legacy takes the else-branch → byte-identical to today.
    let stt_hardened = settings.security_profile.is_hardened();
    let maybe_bound = |inner: Arc<dyn SttClient>| -> Arc<dyn SttClient> {
        if stt_hardened {
            Arc::new(BoundedSttClient::new(inner, AudioBounds::default()))
        } else {
            inner
        }
    };
    let stt: Arc<dyn SttClient> = if settings.stt_openai {
        // OpenAI Whisper STT. Reuse the OpenAI key (re-wrapped so the LLM can also
        // take ownership of the original below). Wrapped in SmartSttClient so a TYPED
        // command (sent as the NID-98 payload by the demo) bypasses Whisper, while
        // real mic PCM still goes to Whisper.
        match &settings.openai_api_key {
            Some(key) => {
                let stt_key = SecretString::from(key.expose_secret().to_string());
                Arc::new(SmartSttClient::new(maybe_bound(Arc::new(
                    OpenAiSttClient::new(stt_key, settings.openai_stt_model.clone()),
                ))))
            }
            None => Arc::new(MockSttClient),
        }
    } else if let Some(url) = settings.stt_http_url.clone() {
        Arc::new(SmartSttClient::new(maybe_bound(Arc::new(
            HttpSttClient::new(url),
        ))))
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
    // The measured generation configuration, applied before the first request rather than
    // waiting for the admin panel to push one. Printing it is useful; the key never is.
    eprintln!("[model] creative model = {model}  effort = {effort}");
    dcvr_llm_client::set_llm_tuning(dcvr_llm_client::LlmTuning {
        model: model.clone(),
        reasoning_effort: effort.clone(),
        verbosity: "default".to_string(),
        max_completion_tokens: 32000,
    });

    let cfg = RuntimeConfig {
        model,
        enable_mode_a: settings.mode_a,
        max_generations_per_min: settings.max_generations_per_min,
        personal_space_radius_m: settings.personal_space_radius_m,
        min_plan_interval_ms: settings.min_plan_interval_ms,
        comfort_rotate_max_deg_s: settings.comfort_rotate_max_deg_s,
        perceptual_hardening: settings.perceptual_hardening,
        // Age-adaptive coupling: a detected minor (or unknown = fail-safe) tightens the
        // code plane when age gating is on. Only the coarse is_minor bit crosses over.
        age_gating_enabled: settings.age_gating,
        age_is_minor: settings.age_band.is_minor(),
        ..RuntimeConfig::default()
    };
    let bus = ControlBus::new(cfg);
    let mut file_store = FilePersonalizationStore::new(perso_dir);
    if let Some(hex) = &profile_enc {
        file_store = file_store.with_encryption_key_hex(hex);
    }
    let store: Arc<dyn PersonalizationStore> = Arc::new(file_store);
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
        // 0 = disabled (legacy byte-identical); >0 = opt-in overall utterance bound.
        utterance_timeout: (settings.utterance_timeout_ms > 0)
            .then(|| Duration::from_millis(settings.utterance_timeout_ms)),
        max_inflight_per_peer: settings.max_inflight_per_peer,
        per_peer_routing: settings.per_peer_routing,
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
use dcvr_protocol::NID_BACKEND_OUTPUT;
use dcvr_stt_client::AudioUtterance;
use dcvr_unity_transport::{SessionSequence, TransportError, UbiqServicePeer};

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

/// How peers map to routers on the live Ubiq path. `Shared` (default, legacy
/// byte-identical) puts EVERY peer behind one `Mutex<Router>`, so utterances across
/// peers serialise on that single lock — unchanged from the original design.
/// `PerPeer` (opt-in via `DCVR_PER_PEER_ROUTING`) gives each peer its OWN
/// `Mutex<Router>`, so a slow STT/LLM call for one peer no longer blocks another
/// peer's pipeline. Correctness is preserved because a peer only ever touches its
/// own session, and the control bus + personalizer are internally synchronised and
/// shared by reference in both modes — only the lock granularity differs.
enum RouterRegistry {
    Shared(std::sync::Arc<tokio::sync::Mutex<Router>>),
    PerPeer {
        routers: std::sync::Arc<
            tokio::sync::Mutex<
                std::collections::HashMap<String, std::sync::Arc<tokio::sync::Mutex<Router>>>,
            >,
        >,
        build: std::sync::Arc<dyn Fn() -> Router + Send + Sync>,
    },
}

impl RouterRegistry {
    /// The `Mutex<Router>` that serves `peer`. `Shared` returns the one router for all
    /// peers (a cheap `Arc` clone — byte-identical to the original `router.clone()`);
    /// `PerPeer` returns that peer's router, creating it on first use under a brief lock
    /// of the (small) registry map. The same peer always maps to the same router, so its
    /// selected-object and per-peer rate/spawn state persist across utterances.
    async fn for_peer(&self, peer: &str) -> std::sync::Arc<tokio::sync::Mutex<Router>> {
        match self {
            RouterRegistry::Shared(r) => r.clone(),
            RouterRegistry::PerPeer { routers, build } => routers
                .lock()
                .await
                .entry(peer.to_string())
                .or_insert_with(|| std::sync::Arc::new(tokio::sync::Mutex::new(build())))
                .clone(),
        }
    }
}

pub async fn run_ubiq_peer(
    addr: &str,
    room_guid: &str,
    services: Services,
) -> Result<(), TransportError> {
    use std::sync::Arc;
    use tokio::sync::Mutex;

    let mut peer = UbiqServicePeer::connect_and_join(addr, room_guid).await?;
    eprintln!("dreamcodevr-server: joined Ubiq room {room_guid} at {addr} (service peer)");

    // Per-utterance processing runs in spawned tasks so the receive loop NEVER blocks
    // on STT/LLM. A per-peer router builder (captures the control bus + personalizer,
    // both cheaply shareable and internally synchronised) backs both routing modes.
    let build_router = {
        let bus = services.bus.clone();
        let personalizer = services.personalizer.clone();
        Arc::new(move || {
            let mut r = Router::new().with_bus(bus.clone());
            if let Some(p) = &personalizer {
                r = r.with_personalizer(p.clone());
            }
            r
        }) as Arc<dyn Fn() -> Router + Send + Sync>
    };
    // Off (default): ONE shared router — utterances serialise on its lock, byte-identical
    // to the original design. On (`DCVR_PER_PEER_ROUTING`): each peer gets its own router,
    // so a slow STT/LLM call for one peer no longer blocks another peer's pipeline.
    let registry = if services.per_peer_routing {
        eprintln!(
            "dreamcodevr-server: per-peer routing ENABLED (peers no longer serialise on one router lock)"
        );
        RouterRegistry::PerPeer {
            routers: Arc::new(Mutex::new(HashMap::new())),
            build: build_router,
        }
    } else {
        RouterRegistry::Shared(Arc::new(Mutex::new(build_router())))
    };
    let jsonl = Arc::new(Mutex::new(JsonlWriter::new(std::io::stdout())));
    let sender = peer.sender();
    // Publish the live sender so the admin panel's MANUAL COMMAND box can dispatch
    // generated code to the headset (build it in VR), exactly like the audio path does.
    *services.ubiq_sender.write().await = Some(sender.clone());
    // Per-peer push-to-talk accumulation buffers (touched only by the recv loop).
    let mut accum: HashMap<String, Vec<u8>> = HashMap::new();
    // Per-peer replay state for the incoming auth gate (used only in hardened mode).
    let mut seqs: HashMap<String, SessionSequence> = HashMap::new();
    // Per-peer in-flight backpressure (bounds concurrent utterance tasks per peer).
    let mut peer_sems: HashMap<String, Arc<tokio::sync::Semaphore>> = HashMap::new();

    while let Some(frame) = peer.recv().await {
        // Incoming authentication gate. Legacy: inert — this is byte-identical to
        // `split_peer_payload` (self-asserted uuid + body). Hardened: verify the
        // client HMAC envelope (identity + freshness + payload hash + domain +
        // strict-monotonic replay) and use the PROVEN peer id; an unverifiable frame
        // is dropped fail-closed. Incoming enforcement therefore activates the moment
        // the Unity client starts emitting envelopes; until then legacy is unchanged.
        let peer_key = match services.auth.incoming_peer_key(&frame.payload) {
            Some(k) => k,
            None => continue,
        };
        // Verify against a COPY of the peer's sequence and persist it only on success,
        // so an in-room peer flooding unverifiable frames with varying keys cannot grow
        // this map unboundedly (memory-exhaustion DoS). (Audit finding.)
        let mut seq = seqs.get(&peer_key).cloned().unwrap_or_default();
        let verified = match services.auth.verify_incoming(
            frame.network_id.b,
            &frame.payload,
            &mut seq,
            crate::auth_gate::now_unix(),
        ) {
            Ok(v) => v,
            Err(e) => {
                if services.auth.is_active() {
                    eprintln!(
                        "[auth] dropped unverifiable NID-{} frame: {e}",
                        frame.network_id.b
                    );
                }
                continue;
            }
        };
        seqs.insert(peer_key, seq);
        let peer_uuid = verified.peer_id;
        let body = verified.body;
        match frame.network_id.b {
            93 => {
                let selected = String::from_utf8_lossy(&body).to_string();
                registry
                    .for_peer(&peer_uuid)
                    .await
                    .lock()
                    .await
                    .set_selected_object(&peer_uuid, selected);
            }
            98 => {
                if body.len() <= 64 {
                    if let Ok(text) = std::str::from_utf8(&body) {
                        if let Some(action) = text.strip_prefix(STT_CONTROL_PREFIX) {
                            match action {
                                "start" => {
                                    accum.insert(peer_uuid.clone(), Vec::new());
                                }
                                "stop" => {
                                    let bytes = accum.remove(&peer_uuid).unwrap_or_default();
                                    if !bytes.is_empty() {
                                        match acquire_inflight(
                                            &mut peer_sems,
                                            &peer_uuid,
                                            services.max_inflight_per_peer,
                                        ) {
                                            Some(permit) => {
                                                let r = registry.for_peer(&peer_uuid).await;
                                                spawn_utterance(
                                                    r,
                                                    jsonl.clone(),
                                                    sender.clone(),
                                                    services.clone(),
                                                    peer_uuid.clone(),
                                                    bytes,
                                                    permit,
                                                );
                                            }
                                            None => eprintln!(
                                                "[ubiq-peer] peer {peer_uuid} at max in-flight \
                                                 utterances; dropping (backpressure)"
                                            ),
                                        }
                                    }
                                }
                                other => eprintln!("[ubiq-peer] unknown STT control: {other}"),
                            }
                            continue;
                        }
                    }
                }
                let buf = accum.entry(peer_uuid.clone()).or_default();
                buf.extend_from_slice(&body);
                if buf.len() > MAX_UTTERANCE_BYTES {
                    let bytes = accum.remove(&peer_uuid).unwrap_or_default();
                    eprintln!(
                        "[ubiq-peer] utterance for {peer_uuid} exceeded {MAX_UTTERANCE_BYTES} \
                         bytes without a stop control; auto-finalizing"
                    );
                    match acquire_inflight(
                        &mut peer_sems,
                        &peer_uuid,
                        services.max_inflight_per_peer,
                    ) {
                        Some(permit) => {
                            let r = registry.for_peer(&peer_uuid).await;
                            spawn_utterance(
                                r,
                                jsonl.clone(),
                                sender.clone(),
                                services.clone(),
                                peer_uuid,
                                bytes,
                                permit,
                            );
                        }
                        None => eprintln!(
                            "[ubiq-peer] peer {peer_uuid} at max in-flight utterances; \
                             dropping overflow (backpressure)"
                        ),
                    }
                }
            }
            95 => {
                // Personalization feedback (👍/👎). Body: JSON {"liked":bool} or "like"/"dislike".
                if let Some(p) = &services.personalizer {
                    let liked = std::str::from_utf8(&body)
                        .ok()
                        .and_then(|t| serde_json::from_str::<serde_json::Value>(t).ok())
                        .and_then(|v| v.get("liked").and_then(|b| b.as_bool()))
                        .or_else(|| {
                            let t = String::from_utf8_lossy(&body).to_lowercase();
                            if t.contains("dislike") {
                                Some(false)
                            } else if t.contains("like") {
                                Some(true)
                            } else {
                                None
                            }
                        });
                    if let Some(liked) = liked {
                        p.feedback(&peer_uuid, liked).await;
                        services.bus.publish(PipelineEvent::new(
                            "feedback",
                            &peer_uuid,
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
                let v = std::str::from_utf8(&body)
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
                        &peer_uuid,
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
/// Run one utterance's processing under an OPTIONAL overall deadline. `None` (the
/// legacy default) is byte-identical to running the future directly — it just wraps
/// the result in `Some`. `Some(d)` bounds the whole STT→LLM→validate block; on elapse
/// it returns `None` so the caller fails closed (sends nothing) instead of holding the
/// router lock indefinitely. Belt-and-suspenders over the per-step timeouts.
async fn bounded_utterance<F>(limit: Option<Duration>, fut: F) -> Option<F::Output>
where
    F: std::future::Future,
{
    match limit {
        Some(d) => tokio::time::timeout(d, fut).await.ok(),
        None => Some(fut.await),
    }
}

/// Per-peer backpressure: acquire one in-flight slot for `peer`, creating that peer's
/// semaphore (capacity `cap`) on first use. Returns `None` when the peer already has
/// `cap` concurrent utterances in flight, so the caller drops the new one fail-closed.
/// The generous default cap means a single push-to-talk speaker never hits it, so real
/// use is byte-identical; only a flood is bounded.
fn acquire_inflight(
    sems: &mut HashMap<String, Arc<tokio::sync::Semaphore>>,
    peer: &str,
    cap: usize,
) -> Option<tokio::sync::OwnedSemaphorePermit> {
    let sem = sems
        .entry(peer.to_string())
        .or_insert_with(|| Arc::new(tokio::sync::Semaphore::new(cap.max(1))))
        .clone();
    sem.try_acquire_owned().ok()
}

fn spawn_utterance(
    router: std::sync::Arc<tokio::sync::Mutex<Router>>,
    jsonl: std::sync::Arc<tokio::sync::Mutex<JsonlWriter<std::io::Stdout>>>,
    sender: dcvr_unity_transport::PeerSender,
    services: Services,
    peer_id: String,
    bytes: Vec<u8>,
    permit: tokio::sync::OwnedSemaphorePermit,
) {
    tokio::spawn(async move {
        // Hold the per-peer in-flight slot for this task's whole lifetime; it is released
        // (drop-safe) when the task completes or is cancelled.
        let _permit = permit;
        // Remember which headset just issued a command so a subsequent MANUAL COMMAND
        // from the admin panel is delivered to the same client (NID-94 `peer`).
        *services.last_client_peer.write().await = Some(peer_id.clone());
        let audio = AudioUtterance::new_16k_mono(bytes);
        // Mode A and Mode B both need the dual path (to generate + validate the C#).
        let need_csharp = services.csharp_research || services.mode_a;
        // Optional overall deadline (None = legacy, byte-identical). On elapse we fail
        // closed: log and return before any NID-94 send, so a wedged utterance can
        // never hold the shared router lock forever. tokio's Mutex does not poison on
        // the dropped guard, and the load-bearing counters move conservatively (rate
        // limit recorded before work; spawn budget only after success).
        let outcome = match bounded_utterance(services.utterance_timeout, async {
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
        })
        .await
        {
            Some(o) => o,
            None => {
                eprintln!(
                    "[utterance] processing exceeded the configured deadline; failing closed (nothing sent)"
                );
                return;
            }
        };
        {
            let mut j = jsonl.lock().await;
            let _ = j.write_event(&outcome.timing);
        }
        publish_pipeline_events(&services.bus, &outcome);

        // A deterministic object operation takes precedence over BOTH generation paths.
        // It is not a plan and not an assembly; the device carries it out against its own
        // registry. Checked first because a command that was answered without the model
        // has no plan and no C# to fall back to, and treating it as "nothing to send"
        // would make every fast-path command silently do nothing on the headset.
        if let Some(op) = &outcome.device_op {
            let msg = op.to_json(&peer_id);
            match services.auth.sign_nid94(
                msg.as_bytes(),
                &peer_id,
                &outcome.request_id,
                crate::auth_gate::now_unix(),
            ) {
                Ok(bytes) => {
                    let _ = sender.send(NID_BACKEND_OUTPUT, &bytes).await;
                    eprintln!("[device-op] {} (no AI call)", op.describe());
                }
                Err(e) => eprintln!("[device-op] refusing to send unsigned NID-94: {e}"),
            }
            return;
        }

        // Live toggle: the admin panel can disable dispatch without a restart.
        if services.mode_a_live() {
            // Mode A (original DreamCodeVR): hand the VALIDATED generated C# to the
            // original Unity `CodeGenerationManager` (NID 94 `{type,peer,data}`) for
            // runtime RoslynCSharp compilation. Fail-closed: only send if it passed
            // the lexical + Roslyn validation (this is how DreamCodeVR+ makes the
            // original runtime-compile path safe).
            match &outcome.csharp {
                Some(cs) if cs.approved => {
                    // Quest 3 cannot compile: IL2CPP is ahead-of-time and ships no C#
                    // compiler. So when the analyzer can compile, do it HERE and send the
                    // assembly; the device interprets IL, which is ordinary managed code
                    // and runs fine under AOT. Falls back to sending source for hosts that
                    // do have a runtime compiler (the Editor, a Mono sideload).
                    //
                    // The order matters and is deliberate: the source has ALREADY passed
                    // the lexical guardrail and the semantic analyzer at this point.
                    // Compilation is a delivery mechanism, never an approval.
                    let Some(msg) = mode_a_payload_with_repair(
                        services.roslyn.as_ref(),
                        Some(services.llm.as_ref()),
                        &outcome.request_id,
                        &cs.candidate,
                        &peer_id,
                        outcome.transcript.as_deref().unwrap_or(""),
                    )
                    .await
                    else {
                        return;
                    };
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

/// Build the NID-94 payload that carries approved Mode-A code to the headset.
///
/// Shared by the audio flow and the admin panel deliberately. These were separate copies
/// once and they drifted: the admin path kept sending source long after the audio path had
/// moved to shipping IL, so a typed command silently took a route the headset could not
/// execute. One builder means one answer to "what does Mode A put on the wire".
///
/// Returns `None` to mean SEND NOTHING. That is the fail-closed case, and it is the only
/// correct answer when the compiler rejects code the validator approved: the two disagree,
/// so the safe move is to build nothing rather than to guess which one was right.
///
/// `csharp` must already have passed the lexical guardrail and the semantic analyzer.
/// Compiling here is a delivery mechanism and confers no approval of its own.
/// As [`mode_a_payload`], with ONE repair attempt when the compiler rejects the code.
///
/// # Why a repair pass exists
///
/// Generated code that does not compile is the common case, not the exception. In live
/// use, roughly a third to a half of substantial creative programs failed to build first
/// time — `CS0029: cannot convert GameObject to Transform` is representative. The system
/// failed closed every time, which is correct, and also meant the user asked for a solar
/// system and watched nothing happen. Broad creative freedom that only works half the
/// time is not broad creative freedom.
///
/// # Why it does not weaken anything
///
/// The repaired source is treated as a fresh, untrusted generation. It goes back through
/// the FULL lexical guardrail before it is compiled again — the compiler's diagnostics are
/// a hint to the model, never a reason to skip a gate. If the repair introduces a banned
/// API it is refused exactly as a first attempt would be, and if it still does not compile
/// nothing is sent. The number of attempts is fixed at one: an unbounded repair loop is a
/// way to spend money and to let a model wander somewhere the first validation would not
/// have allowed.
async fn mode_a_payload_with_repair(
    roslyn: &dyn RoslynAnalyzer,
    llm: Option<&dyn LlmClient>,
    request_id: &str,
    csharp: &str,
    peer_id: &str,
    prompt: &str,
) -> Option<String> {
    /// How many times the model may be asked to fix its own code.
    ///
    /// Two, from measurement rather than taste. In live use a single attempt took one
    /// solar-system program from five compiler errors down to one — close enough that
    /// stopping there threw away almost-working code, and the user saw nothing appear.
    /// It stays small because each attempt is a paid round trip and because a model that
    /// has not converged in two tries is usually rewriting rather than repairing.
    const MAX_REPAIRS: usize = 2;

    let mut source = csharp.to_string();

    for attempt in 0..=MAX_REPAIRS {
        let compiled = match roslyn.compile(&source).await {
            Ok(c) => c,
            Err(e) => {
                // No compile service at all. Send source: a host that DOES have a runtime
                // compiler (the Editor, a 32-bit Mono sideload) can still run it, and one
                // that does not reports it cleanly rather than silently doing nothing.
                eprintln!("[mode-a] no compile service ({e}); sending source instead");
                return Some(
                    serde_json::json!({
                        "type": "code",
                        "peer": peer_id,
                        "data": source,
                    })
                    .to_string(),
                );
            }
        };

        if compiled.approved && compiled.assembly.is_some() {
            if attempt > 0 {
                eprintln!("[mode-a] repair #{attempt} compiled; sending IL to the headset");
            } else {
                eprintln!("[mode-a] compiled to assembly; sending IL to the headset");
            }
            return Some(assembly_message(
                &compiled.assembly.unwrap_or_default(),
                &source,
                peer_id,
                prompt,
            ));
        }

        let diagnostics = compiled.diagnostics.join(" | ");
        let Some(llm) = llm else {
            eprintln!("[mode-a] compile refused ({diagnostics}); nothing sent");
            return None;
        };
        if attempt == MAX_REPAIRS {
            eprintln!("[mode-a] still does not compile after {MAX_REPAIRS} repair(s) ({diagnostics}); nothing sent");
            return None;
        }

        eprintln!(
            "[mode-a] compile refused ({diagnostics}); repair {}/{MAX_REPAIRS}",
            attempt + 1
        );
        let fixed = match llm.repair_csharp(request_id, &source, &diagnostics).await {
            Ok(f) => f,
            Err(e) => {
                eprintln!("[mode-a] repair unavailable ({e}); nothing sent");
                return None;
            }
        };

        // THE GUARDRAIL RUNS AGAIN, on every attempt. This is the whole reason the repair
        // loop is safe to have: a repaired program is a NEW program and is admitted on
        // exactly the same terms as any other. Compiler diagnostics are a hint to the
        // model, never a licence to skip a gate.
        let verdict = dcvr_csharp_policy::validate_csharp_freeform(&fixed);
        if !verdict.violations.is_empty() {
            eprintln!(
                "[mode-a] repaired C# REJECTED by the guardrail; nothing sent ({})",
                verdict
                    .violations
                    .iter()
                    .map(|v| v.to_string())
                    .collect::<Vec<_>>()
                    .join("; ")
            );
            return None;
        }
        source = fixed;
    }

    None
}

fn assembly_message(assembly: &str, source: &str, peer_id: &str, prompt: &str) -> String {
    serde_json::json!({
        "type": "assembly",
        "peer": peer_id,
        "data": assembly,
        // What the user actually asked for. The device names the creation from this so it
        // can be addressed later ("delete the castle"); without it every group inherited
        // whatever text the client happened to be holding, and on device that turned out
        // to be the demo's default text box — so every creation was called "this cube red"
        // and nothing could be deleted by name.
        "prompt": prompt,
        // The source travels with the IL for two reasons that are not about execution:
        // the headset's disclosure panel shows the user what actually ran (A093), and the
        // client reads a composition hint from it. The device never compiles this string.
        "source": source,
    })
    .to_string()
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
    /// Send an approved bounded action plan to the headset. Returns whether it went.
    async fn dispatch_plan(&self, outcome: &AudioOutcome) -> bool {
        let Some(sender) = self.services.ubiq_sender.read().await.clone() else {
            return false;
        };
        let Ok(json) = crate::app::backend_decision_json(outcome) else {
            return false;
        };
        let target = self
            .services
            .last_client_peer
            .read()
            .await
            .clone()
            .unwrap_or_default();
        match self.services.auth.sign_nid94(
            json.as_bytes(),
            &target,
            &outcome.request_id,
            crate::auth_gate::now_unix(),
        ) {
            Ok(bytes) => sender.send(NID_BACKEND_OUTPUT, &bytes).await.is_ok(),
            Err(e) => {
                eprintln!("[manual-cmd] refusing to send unsigned NID-94: {e}");
                false
            }
        }
    }

    /// Tell the headset a request was refused, and why.
    ///
    /// Both block paths need this. The intent screen and the validator are different
    /// layers reaching the same verdict, and a wearer cannot distinguish "refused" from
    /// "nothing arrived" unless the refusal is delivered. No generated code is sent —
    /// that is the property being demonstrated — only the decision and its reason.
    async fn notify_headset_blocked(&self, outcome: &AudioOutcome, reason: &str) {
        let Some(sender) = self.services.ubiq_sender.read().await.clone() else {
            return;
        };
        let target = self
            .services
            .last_client_peer
            .read()
            .await
            .clone()
            .unwrap_or_default();
        let msg = serde_json::json!({
            "type": "BackendDecision",
            "request_id": outcome.request_id,
            "decision": format!("{:?}", outcome.decision),
            "errors": [],
            "caught_reason": reason,
        })
        .to_string();
        match self.services.auth.sign_nid94(
            msg.as_bytes(),
            &target,
            &outcome.request_id,
            crate::auth_gate::now_unix(),
        ) {
            Ok(bytes) => {
                let _ = sender.send(NID_BACKEND_OUTPUT, &bytes).await;
            }
            Err(e) => eprintln!("[manual-cmd] refusing to send unsigned NID-94: {e}"),
        }
    }

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
            // TELL THE HEADSET. Returning here without dispatching left the wearer with no
            // indication that anything happened: the block was visible only in the admin
            // panel on the laptop, so in the headset a refused attack and an ignored
            // command looked identical. The decision carries `caught_reason`, which the
            // client uses to raise the security barrier and name the stage that stopped it.
            //
            // Note this sends the NEUTRALIZED decision, not the attack: no generated code
            // is dispatched, which is exactly the property being demonstrated.
            if let Some(sender) = self.services.ubiq_sender.read().await.clone() {
                if let Ok(json) = crate::app::backend_decision_json(&outcome) {
                    let target = self
                        .services
                        .last_client_peer
                        .read()
                        .await
                        .clone()
                        .unwrap_or_default();
                    match self.services.auth.sign_nid94(
                        json.as_bytes(),
                        &target,
                        &outcome.request_id,
                        crate::auth_gate::now_unix(),
                    ) {
                        Ok(bytes) => {
                            let _ = sender.send(NID_BACKEND_OUTPUT, &bytes).await;
                        }
                        Err(e) => {
                            eprintln!("[manual-cmd] refusing to send unsigned NID-94: {e}");
                        }
                    }
                }
            }
            return format!(
                "🛡️ MALICIOUS INTENT CAUGHT & NEUTRALIZED — {reason}. \
                 No code was generated for the request; a harmless placeholder was sent instead."
            );
        }
        // Deterministic object operation — answered without the model (§23). Same
        // precedence as the audio path: the fast path produces no plan and no C#, so it
        // has to be dispatched before either of the generation branches decides there is
        // nothing to send.
        if let Some(op) = &outcome.device_op {
            let mut delivered = false;
            if let Some(sender) = self.services.ubiq_sender.read().await.clone() {
                let target = self
                    .services
                    .last_client_peer
                    .read()
                    .await
                    .clone()
                    .unwrap_or_default();
                let msg = op.to_json(&target);
                delivered = match self.services.auth.sign_nid94(
                    msg.as_bytes(),
                    &target,
                    &outcome.request_id,
                    crate::auth_gate::now_unix(),
                ) {
                    Ok(bytes) => sender.send(NID_BACKEND_OUTPUT, &bytes).await.is_ok(),
                    Err(e) => {
                        eprintln!("[device-op] refusing to send unsigned NID-94: {e}");
                        false
                    }
                };
            }
            return if delivered {
                format!(
                    "{} → SENT to the headset (deterministic, no AI call) ✓",
                    op.describe()
                )
            } else {
                format!(
                    "{} — resolved without the model, but no headset is connected",
                    op.describe()
                )
            };
        }

        // MODE C IS THE DELIVERABLE when Mode A is off — which is the default. The
        // pipeline generates BOTH an action plan
        // and a C# candidate; the plan is what ships to the headset, the candidate exists
        // so Mode B can be compared against it.
        //
        // Judging the whole request by the C# candidate therefore reports the wrong thing:
        // a live model produces a malformed or banned candidate for roughly one benign
        // build in three, and the request was being reported as REJECTED — and the headset
        // told it was blocked — while a perfectly good bounded plan had already been
        // dispatched and applied. The mock never showed this because it returns one fixed,
        // always-valid candidate.
        //
        // Nothing is weakened. The candidate is still validated, still rejected, and still
        // never dispatched; Mode A dispatch remains gated on `cs.approved` below. Only the
        // reported verdict changes, so it reflects the path that actually ran.
        if !self.services.mode_a_live() {
            if let Some(plan) = &outcome.plan {
                let delivered = self.dispatch_plan(&outcome).await;
                let note = match &outcome.csharp {
                    Some(cs) if !cs.approved => format!(
                        "  (Mode-B C# candidate rejected by the guardrail and NOT sent: {})",
                        cs.violations.join("; ")
                    ),
                    _ => String::new(),
                };
                return if delivered {
                    format!(
                        "{:?} — {} action(s) → SENT to the headset as a bounded action plan \
                         (Mode C) ✓{note}",
                        outcome.decision,
                        plan.actions.len()
                    )
                } else {
                    format!(
                        "{:?} — {} action(s) validated OK, but no headset is connected{note}",
                        outcome.decision,
                        plan.actions.len()
                    )
                };
            }
        }

        match &outcome.csharp {
            Some(cs) if cs.approved => {
                // DISPATCH to the headset (Mode A): send the validated C# to the Quest
                // over the SAME NID-94 `{type,peer,data}` path the audio flow uses, so a
                // typed admin-panel command actually BUILDS in VR instead of only being
                // validated. Target the last headset that issued a command; an empty
                // `peer` broadcasts to every connected client.
                let mut delivered = false;
                // What actually went on the wire, so the operator's reply says which of the
                // two Mode-A deliveries happened. "Sent as C#" when we sent IL would be a
                // small lie that costs an hour of debugging on the device.
                let mut mode_a_form = "validated C#";
                let mut compiler_refused = false;
                if self.services.mode_a_live() {
                    if let Some(sender) = self.services.ubiq_sender.read().await.clone() {
                        let target = self
                            .services
                            .last_client_peer
                            .read()
                            .await
                            .clone()
                            .unwrap_or_default();
                        // Same builder as the audio path: on a Quest 3 this compiles here
                        // and ships IL, because IL2CPP has no runtime compiler. None means
                        // the compiler refused code the validator approved — fail closed.
                        let msg = match mode_a_payload_with_repair(
                            self.services.roslyn.as_ref(),
                            Some(self.services.llm.as_ref()),
                            &outcome.request_id,
                            &cs.candidate,
                            &target,
                            outcome.transcript.as_deref().unwrap_or(""),
                        )
                        .await
                        {
                            Some(m) => {
                                if m.contains("\"type\":\"assembly\"") {
                                    mode_a_form =
                                        "server-compiled IL (Mode A, interpreted on device)";
                                }
                                m
                            }
                            None => {
                                compiler_refused = true;
                                String::new()
                            }
                        };
                        delivered = !msg.is_empty()
                            && match self.services.auth.sign_nid94(
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
                // MODE C dispatch. Without this a typed command was validated and then
                // dropped whenever Mode A was off — which is the DEFAULT and the only
                // configuration a Quest 3 can run, since IL2CPP has no runtime compiler.
                // The effect was that the safest, deployable path was the one that could
                // not be driven from the admin panel at all: the demo validated the
                // command and the headset never moved. Send the bounded action plan on
                // the same NID-94 route the audio flow uses.
                if !delivered && !self.services.mode_a_live() {
                    if let Some(sender) = self.services.ubiq_sender.read().await.clone() {
                        if let Ok(json) = crate::app::backend_decision_json(&outcome) {
                            let target = self
                                .services
                                .last_client_peer
                                .read()
                                .await
                                .clone()
                                .unwrap_or_default();
                            delivered = match self.services.auth.sign_nid94(
                                json.as_bytes(),
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
                }

                if delivered {
                    let how = if self.services.mode_a_live() {
                        mode_a_form
                    } else {
                        "bounded action plan (Mode C)"
                    };
                    format!(
                        "{:?} — approved ({} chars) → SENT to the headset as {how} ✓",
                        outcome.decision,
                        cs.candidate.len()
                    )
                } else if compiler_refused {
                    // Not a security block, and saying so matters: the guardrail approved
                    // this and the COMPILER disagreed. Almost always the model emitted C#
                    // that does not build, not an attack.
                    format!(
                        "{:?} — C# approved ({} chars) but the compiler rejected it; \
                         nothing was sent (fail-closed). See the backend log for diagnostics.",
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
            Some(cs) => {
                // A VALIDATOR rejection is a block too, and it also has to reach the
                // headset. Only the Layer-1 intent-screen path carried a reason to the
                // device, so a request refused by the guardrail rather than the screen
                // produced no on-device feedback at all — the wearer saw nothing happen
                // and could not tell refusal from a lost message.
                self.notify_headset_blocked(&outcome, &cs.violations.join("; "))
                    .await;
                format!(
                    "{:?} — C# REJECTED: {}",
                    outcome.decision,
                    cs.violations.join("; ")
                )
            }
            None => {
                self.notify_headset_blocked(&outcome, "request refused before generation")
                    .await;
                format!("{:?} — no C# generated", outcome.decision)
            }
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

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    // ---- Mode-A delivery -----------------------------------------------------
    //
    // These pin the three answers `mode_a_payload` can give, because the difference
    // between them is a security property, not a formatting detail: a Quest 3 can only
    // run IL, and "compiler disagreed with the validator" must never become "send it
    // anyway".

    use dcvr_roslyn_client::{CompiledAssembly, RoslynVerdict};

    /// Compiles to whatever it was constructed with.
    struct StubCompiler(Result<CompiledAssembly, ()>);

    #[async_trait::async_trait]
    impl RoslynAnalyzer for StubCompiler {
        async fn analyze(
            &self,
            _csharp: &str,
        ) -> Result<RoslynVerdict, dcvr_roslyn_client::RoslynError> {
            Ok(RoslynVerdict {
                approved: true,
                diagnostics: vec![],
            })
        }

        async fn compile(
            &self,
            _csharp: &str,
        ) -> Result<CompiledAssembly, dcvr_roslyn_client::RoslynError> {
            self.0.clone().map_err(|()| {
                dcvr_roslyn_client::RoslynError::Unavailable("no compile service".into())
            })
        }
    }

    #[tokio::test]
    async fn mode_a_sends_il_when_the_compiler_succeeds() {
        let stub = StubCompiler(Ok(CompiledAssembly {
            approved: true,
            assembly: Some("TVqQAAM=".to_string()),
            diagnostics: vec![],
        }));
        let msg =
            mode_a_payload_with_repair(&stub, None, "r1", "class C {}", "peer-1", "build a castle")
                .await
                .expect("approved code should be delivered");
        let v: serde_json::Value = serde_json::from_str(&msg).unwrap();
        assert_eq!(v["type"], "assembly");
        assert_eq!(v["data"], "TVqQAAM=");
        assert_eq!(v["peer"], "peer-1");
        // The source rides along for the on-device disclosure panel.
        assert_eq!(v["source"], "class C {}");
        // The device names the creation from this, so it must survive onto the wire.
        assert_eq!(v["prompt"], "build a castle");
    }

    #[tokio::test]
    async fn mode_a_sends_nothing_when_the_compiler_refuses() {
        // The guardrail approved this and the compiler did not. The two disagree, so the
        // only safe answer is to build nothing — NOT to fall back to source.
        let stub = StubCompiler(Ok(CompiledAssembly {
            approved: false,
            assembly: None,
            diagnostics: vec!["CS1002: ; expected".to_string()],
        }));
        assert!(
            mode_a_payload_with_repair(&stub, None, "r1", "class C {", "peer-1", "")
                .await
                .is_none()
        );
    }

    /// A repair that the compiler accepts but the GUARDRAIL rejects must send nothing.
    ///
    /// This is the property that makes the repair pass safe to have at all. The model is
    /// handed compiler diagnostics and asked to change its own code; if that were enough
    /// to get onto the device, the repair prompt would be a way to introduce anything the
    /// first validation would have refused. Repaired source is a new program and is
    /// admitted on exactly the same terms.
    #[tokio::test]
    async fn a_repair_that_violates_the_guardrail_is_refused() {
        struct RepairsIntoSomethingBanned;
        #[async_trait::async_trait]
        impl LlmClient for RepairsIntoSomethingBanned {
            async fn generate_plan(
                &self,
                _r: &str,
                _t: &str,
            ) -> Result<dcvr_behaviour_dsl::ActionPlan, dcvr_llm_client::LlmError> {
                Err(dcvr_llm_client::LlmError::EmptyResponse)
            }
            async fn repair_csharp(
                &self,
                _r: &str,
                _s: &str,
                _d: &str,
            ) -> Result<String, dcvr_llm_client::LlmError> {
                // Compiles fine. Reads the filesystem.
                Ok(
                    "using UnityEngine; using System.IO; public class GeneratedBehaviour : \
                    MonoBehaviour { void Start(){ File.ReadAllText(\"/etc/passwd\"); } }"
                        .to_string(),
                )
            }
        }

        let stub = StubCompiler(Ok(CompiledAssembly {
            approved: false,
            assembly: None,
            diagnostics: vec!["CS0029: cannot convert".to_string()],
        }));
        let out = mode_a_payload_with_repair(
            &stub,
            Some(&RepairsIntoSomethingBanned),
            "r1",
            "class C {}",
            "peer-1",
            "",
        )
        .await;
        assert!(
            out.is_none(),
            "a repaired program that violates the guardrail must never be sent"
        );
    }

    #[tokio::test]
    async fn mode_a_falls_back_to_source_only_when_there_is_no_compiler() {
        // Distinct from a refusal: nothing has judged the code here, and a host that DOES
        // have a runtime compiler (the Editor, a Mono sideload) can still run it.
        let stub = StubCompiler(Err(()));
        let msg =
            mode_a_payload_with_repair(&stub, None, "r1", "class C {}", "peer-1", "build a castle")
                .await
                .expect("no compiler should fall back to source");
        let v: serde_json::Value = serde_json::from_str(&msg).unwrap();
        assert_eq!(v["type"], "code");
        assert_eq!(v["data"], "class C {}");
    }

    // Unit 2: the umbrella is byte-identical when disabled (None just wraps in Some).
    #[tokio::test]
    async fn bounded_utterance_none_is_transparent_passthrough() {
        let out = bounded_utterance(None, async { 42u32 }).await;
        assert_eq!(out, Some(42));
    }

    // Unit 2: a fast future completes normally under a generous deadline.
    #[tokio::test]
    async fn bounded_utterance_fast_future_completes() {
        let out = bounded_utterance(Some(Duration::from_secs(5)), async { 7u32 }).await;
        assert_eq!(out, Some(7));
    }

    // Phase-4 backpressure: the per-peer in-flight cap bounds concurrent utterances,
    // is per-peer, releases a slot when a permit is dropped, and treats 0 as 1.
    #[test]
    fn acquire_inflight_caps_per_peer_and_releases_on_drop() {
        let mut sems: std::collections::HashMap<String, Arc<tokio::sync::Semaphore>> =
            std::collections::HashMap::new();
        let p1 = acquire_inflight(&mut sems, "a", 2);
        let p2 = acquire_inflight(&mut sems, "a", 2);
        assert!(p1.is_some() && p2.is_some(), "first two within cap");
        assert!(
            acquire_inflight(&mut sems, "a", 2).is_none(),
            "third exceeds the per-peer cap"
        );
        drop(p1);
        assert!(
            acquire_inflight(&mut sems, "a", 2).is_some(),
            "a slot frees when a permit is dropped (task completes)"
        );
        // A different peer has its own independent budget.
        assert!(acquire_inflight(&mut sems, "b", 2).is_some());
        // A misconfigured cap of 0 is treated as 1 (never "never admit").
        let mut s2 = std::collections::HashMap::new();
        assert!(acquire_inflight(&mut s2, "x", 0).is_some());
    }

    // Unit 2: a hung utterance fails closed (None) within the deadline, instead of
    // holding the router lock forever — proven by a 30 s future bounded to 50 ms.
    #[tokio::test]
    async fn bounded_utterance_hung_future_fails_closed() {
        let res = tokio::time::timeout(
            Duration::from_secs(3),
            bounded_utterance(Some(Duration::from_millis(50)), async {
                tokio::time::sleep(Duration::from_secs(30)).await;
                42u32
            }),
        )
        .await;
        assert_eq!(
            res.expect("the bound must fire well before the 3s guard"),
            None,
            "a hung utterance must fail closed (None), not block"
        );
    }

    // --- Phase-4 per-peer routing (DCVR_PER_PEER_ROUTING) --------------------------

    fn shared_registry() -> RouterRegistry {
        RouterRegistry::Shared(std::sync::Arc::new(tokio::sync::Mutex::new(Router::new())))
    }

    fn per_peer_registry() -> RouterRegistry {
        RouterRegistry::PerPeer {
            routers: std::sync::Arc::new(tokio::sync::Mutex::new(std::collections::HashMap::new())),
            build: std::sync::Arc::new(Router::new)
                as std::sync::Arc<dyn Fn() -> Router + Send + Sync>,
        }
    }

    // Legacy/off: every peer maps to the SAME router (byte-identical serialisation).
    #[tokio::test]
    async fn shared_registry_returns_one_router_for_all_peers() {
        let reg = shared_registry();
        let a = reg.for_peer("peer-a").await;
        let b = reg.for_peer("peer-b").await;
        assert!(
            Arc::ptr_eq(&a, &b),
            "shared mode must hand every peer one router"
        );
        // ...and because it is one lock, holding it for peer-a blocks peer-b (the
        // original serialised behaviour, preserved exactly when the flag is off).
        let _held = a.lock().await;
        assert!(
            b.try_lock().is_err(),
            "shared mode: one peer's work serialises every other peer (unchanged)"
        );
    }

    // On: distinct peers get distinct routers, so one peer's held lock does NOT block
    // another peer — this is the whole point of the refactor.
    #[tokio::test]
    async fn per_peer_registry_does_not_block_across_peers() {
        let reg = per_peer_registry();
        let a = reg.for_peer("peer-a").await;
        let b = reg.for_peer("peer-b").await;
        assert!(
            !Arc::ptr_eq(&a, &b),
            "per-peer mode must give distinct peers distinct routers"
        );
        let _held = a.lock().await; // peer-a busy in a long STT/LLM call...
        assert!(
            b.try_lock().is_ok(),
            "per-peer mode: peer-b must NOT be blocked by peer-a's in-flight work"
        );
    }

    // On: the SAME peer always maps to the SAME router, so its per-peer state
    // (selected object, rate-limit window, spawn budget) persists across utterances
    // and its own utterances still serialise (correct per-peer ordering).
    #[tokio::test]
    async fn per_peer_registry_is_stable_and_serial_for_one_peer() {
        let reg = per_peer_registry();
        let first = reg.for_peer("peer-a").await;
        let again = reg.for_peer("peer-a").await;
        assert!(
            Arc::ptr_eq(&first, &again),
            "same peer must reuse its own router"
        );
        let _held = first.lock().await;
        assert!(
            again.try_lock().is_err(),
            "one peer's concurrent utterances must still serialise (ordering preserved)"
        );
    }
}
