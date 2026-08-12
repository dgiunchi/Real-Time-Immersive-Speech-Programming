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
        // Live toggle: the admin panel can disable dispatch without a restart.
        if services.mode_a_live() {
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
        match &outcome.csharp {
            Some(cs) if cs.approved => {
                // DISPATCH to the headset (Mode A): send the validated C# to the Quest
                // over the SAME NID-94 `{type,peer,data}` path the audio flow uses, so a
                // typed admin-panel command actually BUILDS in VR instead of only being
                // validated. Target the last headset that issued a command; an empty
                // `peer` broadcasts to every connected client.
                let mut delivered = false;
                if self.services.mode_a_live() {
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
                        "validated C#"
                    } else {
                        "bounded action plan (Mode C)"
                    };
                    format!(
                        "{:?} — approved ({} chars) → SENT to the headset as {how} ✓",
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

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

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
