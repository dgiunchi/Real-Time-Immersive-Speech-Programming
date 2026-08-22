const { NetworkId } = require("ubiq/ubiq/messaging");
const { MessageReader, ApplicationController } = require("ubiq-genie-components");
const {  CodeGenerationService, SpeechToTextService, FileServer } = require("ubiq-genie-services");
const fs = require("fs");
const nconf = require("nconf");
const path = require("path");
const { spawn } = require("child_process");
const { randomUUID } = require("crypto");
const { makeEnvelope } = require("../../../mcp/unity_scene_bridge/protocol");
const { toWireFormat } = require("../../../cache/protocol");
const { appendEvaluationEvent } = require("../../../evaluation/event_logger");
const { ArtifactLog } = require("../../../memory/artifact_log");

const STT_CONTROL_PREFIX = "__STT_CONTROL__:";
const DATA_DIR = "data";
const INPUT_FILE = `${DATA_DIR}/input.txt`;
const DEBUG_TRANSCRIPTS = ["1", "true", "yes"].includes(
    String(process.env.STUDY_DEBUG_TRANSCRIPTS || "").toLowerCase());

function terminateProcessTree(child) {
    if (!child || child.exitCode != null || child.killed) return;
    if (process.platform === "win32" && child.pid) {
        const killer = spawn("taskkill", ["/PID", String(child.pid), "/T", "/F"], {
            windowsHide: true,
            stdio: "ignore",
        });
        killer.once("error", () => {
            try { child.kill(); } catch (_) { /* process already exited */ }
        });
        return;
    }
    try { child.kill("SIGTERM"); } catch (_) { /* process already exited */ }
}

function ensureRuntimeDataFiles() {
    fs.mkdirSync(DATA_DIR, { recursive: true });
    if (!fs.existsSync(INPUT_FILE)) {
        fs.writeFileSync(INPUT_FILE, "");
    }
}

class CodeGeneration extends ApplicationController {
    constructor(configFile = "config.json") {
        super(configFile);
    }

    registerComponents() {
        this.agenticMode = (process.env.AGENTICXR_MODE || "legacy").toLowerCase() === "claude";
        ensureRuntimeDataFiles();

        // A FileServer to serve image files to clients
        this.components.fileServer = new FileServer(DATA_DIR);

        // A MessageReader to read audio data from peers based on fixed network ID
        this.components.audioReceiver = new MessageReader(this.scene, 98);

        // A SpeechToTextService to transcribe audio coming from peers
        this.components.transcriptionService = new SpeechToTextService(this.scene, nconf.get());

        // A CodeGenerationService to generate text based on text
        if (!this.agenticMode) {
            this.components.codeGenerationService = new CodeGenerationService(this.scene, nconf.get());
        }

        this.functionality = "";

        this.isGenerating = false;
        this.agenticTargets = new Map();
        this.agenticRuns = new Map();
        this.claudePingRuns = new Set();
        this.agenticCorrelations = new Map();
        this.studyIdentityBlocked = new Set();
        this.lastBaselineAttach = new Map(); // sessionId -> { correlationId, targetObjectId }
        // Unity's legacy path replies CodeAttachResult on the same channel the
        // CodeGenerated command goes out on - this is the baseline arm's
        // validated-execution acknowledgement (docs/study-logging-schema.md).
        this.components.legacyResultReader = new MessageReader(this.scene, 94);
        this.components.agenticControlReader = new MessageReader(this.scene, 100);
        this.lastAgenticActivityAt = new Map();
        this.idlePredictionRuns = new Map();
        this.lastIdlePredictionAt = new Map();
        this.baselinePending = null;
        this.agenticTurnTimeoutMs = Number(process.env.AGENTICXR_TURN_TIMEOUT_MS) || 180000;
        this.artifactLog = new ArtifactLog({ filePath: process.env.AGENTICXR_ARTIFACT_LOG });
        if (DEBUG_TRANSCRIPTS) {
            console.warn("[AgenticXR] STUDY_DEBUG_TRANSCRIPTS is enabled: verbatim speech may be written to local diagnostics");
        }
        this.idlePredictionEnabled = String(process.env.AGENTICXR_IDLE_PREDICTION_ENABLED || "false").toLowerCase() === "true";
        this.idlePredictionThresholdMs = Math.max(30000, Number(process.env.AGENTICXR_IDLE_PREDICTION_THRESHOLD_MS) || 60000);
        this.idlePredictionCooldownMs = Math.max(60000, Number(process.env.AGENTICXR_IDLE_PREDICTION_COOLDOWN_MS) || 300000);
        if (this.agenticMode && this.idlePredictionEnabled) {
            setInterval(() => this.runIdlePredictions(), 15000).unref();
        }

    }

    definePipeline() {
        this.components.agenticControlReader.on("data", (data) => {
            let message;
            try { message = JSON.parse(data.message.toString()); }
            catch (_) { return; }
            if (!message) return;
            if (message.type === "CancelRequest") {
                this.cancelAgenticTurn(message.sessionId, message.correlationId, "xr_cancel_button");
                return;
            }
            if (message.type === "TrialReset") {
                let payload = message.payload;
                if (typeof payload === "string") {
                    try { payload = JSON.parse(payload); }
                    catch (_) { payload = {}; }
                }
                if (!payload || payload.status !== "trial_reset") {
                    this.logArtifactEvent({
                        eventType: "trial_reset_failed",
                        sessionId: message.sessionId || null,
                        correlationId: message.correlationId || randomUUID(),
                        status: "error",
                        reasonCode: payload && payload.reasonCode || "reset_not_confirmed",
                    });
                    console.error("[AgenticXR] Unity did not confirm a complete trial reset; active artifacts remain locked");
                    return;
                }
                const suppliedIds = new Set(Array.isArray(payload && payload.artifactIds) ? payload.artifactIds : []);
                const active = this.artifactLog.activeArtifacts();
                const resetCorrelationId = message.correlationId || randomUUID();
                for (const artifact of active) {
                    if (suppliedIds.size > 0 && artifact.artifactId && !suppliedIds.has(artifact.artifactId)) continue;
                    this.logArtifactEvent({
                        eventType: "trial_reset",
                        sessionId: message.sessionId || null,
                        correlationId: resetCorrelationId,
                        targetObjectId: artifact.targetObjectId,
                        artifactId: artifact.artifactId || null,
                        operation: "rollback",
                        status: "rolled_back",
                        reason: "xr_trial_reset",
                    });
                }
                this.logArtifactEvent({
                    eventType: "trial_reset",
                    sessionId: message.sessionId || null,
                    correlationId: resetCorrelationId,
                    operation: "rollback",
                    status: "rolled_back",
                    reason: "xr_trial_reset_all",
                });
                console.log(`[AgenticXR] trial reset synchronized artifacts=${active.length}`);
            }
        });

        // Baseline attach acknowledgement: Unity reports whether the direct-apply
        // legacy attach compiled/attached and how long it took. Logged with the
        // pending baseline trial identity so the exporter derives the baseline
        // arm's validated-execution latency from the same envelope-pair rule as
        // the agentic arm (eventType artifactresult, status committed).
        this.components.legacyResultReader.on("data", (data) => {
            let message;
            try { message = JSON.parse(data.message.toString()); }
            catch (_) { return; }
            if (!message || message.type !== "CodeAttachResult") return;
            const sessionId = message.peer;
            const pending = this.lastBaselineAttach.get(sessionId);
            if (!pending) return;
            this.lastBaselineAttach.delete(sessionId);
            let payload = {};
            try { payload = JSON.parse(message.data || "{}"); }
            catch (_) { /* status stays unknown; logged as error below */ }
            const attached = payload.status === "attached";
            this.logStudyEvent({
                eventType: "artifactresult",
                sessionId,
                correlationId: pending.correlationId,
                targetObjectId: pending.targetObjectId,
                status: attached ? "committed" : "error",
                commitAttachDurationMs: Number.isFinite(payload.commitAttachDurationMs)
                    ? payload.commitAttachDurationMs : null,
                failureStage: attached ? null : "compile",
                reason: attached ? null : payload.error || "attach_failed",
                studySource: "baseline_runtime",
            });
        });

        // Step 1: When we receive audio data from a peer, split it into a peer UUID and PCM audio, and send it to the transcription service
        this.components.audioReceiver.on("data", (data) => {
            // Split the data into a peer_uuid (36 bytes) and audio data (rest)
            const peerUUID = data.message.subarray(0, 36).toString();
            const pcmChunk = Buffer.from(data.message.subarray(36, data.message.length));
            if (pcmChunk.length <= 64) {
                const control = pcmChunk.toString("utf8");
                if (control.startsWith(STT_CONTROL_PREFIX)) {
                    const actionWithTarget = control.slice(STT_CONTROL_PREFIX.length);
                    const separator = actionWithTarget.indexOf(":");
                    const action = separator >= 0 ? actionWithTarget.slice(0, separator) : actionWithTarget;
                    const targetObjectId = separator >= 0 ? actionWithTarget.slice(separator + 1) : null;
                    if (action === "start") {
                        this.studyIdentityBlocked.delete(peerUUID);
                        if (targetObjectId) this.agenticTargets.set(peerUUID, targetObjectId);
                        this.lastAgenticActivityAt.set(peerUUID, Date.now());
                        const correlationId = randomUUID();
                        this.agenticCorrelations.set(peerUUID, correlationId);
                        if (!this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "listening", "Listening to your request.")) return;
                        this.logArtifactEvent({
                            eventType: "continuous_assist_preempt_requested",
                            sessionId: peerUUID,
                            correlationId,
                            targetObjectId: targetObjectId || null,
                            reasonCode: "push_to_talk_started",
                        });
                        this.logEvaluation({ eventType: "recording_start", sessionId: peerUUID, correlationId, targetObjectId });
                        this.logStudyEvent({ eventType: "recording_start", sessionId: peerUUID, correlationId, targetObjectId,
                            studySource: "speech_to_text" });
                        this.components.transcriptionService.recordingStart(peerUUID);
                    } else if (action === "stop") {
                        this.lastAgenticActivityAt.set(peerUUID, Date.now());
                        const correlationId = this.agenticCorrelations.get(peerUUID);
                        if (!this.sendAgenticStatus(peerUUID, this.agenticTargets.get(peerUUID), correlationId, "transcribing", "Transcribing your request.")) return;
                        this.logEvaluation({ eventType: "recording_stop", sessionId: peerUUID, correlationId, targetObjectId: this.agenticTargets.get(peerUUID) });
                        this.logStudyEvent({ eventType: "recording_stop", sessionId: peerUUID, correlationId,
                            targetObjectId: this.agenticTargets.get(peerUUID), studySource: "speech_to_text" });
                        this.components.transcriptionService.recordingStop(peerUUID);
                    } else {
                        console.warn("Unknown STT control action from " + peerUUID + ": " + action);
                    }
                    return;
                }
            }

            const sent = this.components.transcriptionService.addAudioChunk(
                peerUUID,
                pcmChunk
            );

            // False means no active push-to-talk recording; normal outside left-trigger hold.
        });

        // Step 2: When we receive a transcription from the transcription service, send it to the image generation service
        this.components.transcriptionService.on("response", (data, identifier, timing = {}) => {
            // roomClient.peers is a Map of all peers in the room
            // Get the peer with the given identifier
            const peer = this.roomClient.peers.get(identifier);
            const peerName = peer ? peer.properties.get("ubiq.samples.social.name") : identifier;

            var response = data.toString();
            var threshold = 10;

            if (response.length != 0 && response.length > threshold) {
                // Remove all newlines from the response
                response = response.replace(/(\r\n|\n|\r)/gm, "");
                
                if (DEBUG_TRANSCRIPTS) {
                    ensureRuntimeDataFiles();
                    fs.appendFileSync(INPUT_FILE, response);
                    console.warn(`[AgenticXR] debug transcript appended characters=${response.length}`);
                }

                if (response.startsWith(">")) response = response.slice(1);
                if (response.trim()) {
                    const transcript = response.trim();
                    this.logStudyEvent({
                        eventType: "transcript_ready",
                        sessionId: identifier,
                        correlationId: this.agenticCorrelations.get(identifier),
                        targetObjectId: this.agenticTargets.get(identifier),
                        transcriptCharacters: transcript.length,
                        audioDurationMs: Number.isFinite(timing.audioDurationMs) ? timing.audioDurationMs : null,
                        transcriptionDurationMs: Number.isFinite(timing.transcriptionDurationMs) ? timing.transcriptionDurationMs : null,
                        studySource: "speech_to_text",
                    });
                    if (this.agenticMode) {
                        this.lastAgenticActivityAt.set(identifier, Date.now());
                        this.logEvaluation({
                            eventType: "transcript_ready",
                            sessionId: identifier,
                            correlationId: this.agenticCorrelations.get(identifier),
                            targetObjectId: this.agenticTargets.get(identifier),
                            transcriptCharacters: transcript.length,
                        });
                        this.sendAgenticStatus(
                            identifier,
                            this.agenticTargets.get(identifier),
                            this.agenticCorrelations.get(identifier),
                            "heard",
                            `Request transcribed (${transcript.length} characters).`
                        );
                        if (/^(cancel|stop)(\s+(request|generation|claude))?[.!?]*$/i.test(transcript)) {
                            this.cancelAgenticTurn(identifier, null, "voice_cancel");
                        } else if (/^(claude|cloud)\s+ping[.!?]*$/i.test(transcript)) {
                            this.runClaudePing(identifier);
                        } else {
                            this.startAgenticTurn(transcript, identifier);
                        }
                    } else if (this.isGenerating == false) {
                        this.isGenerating = true;
                        const correlationId = this.agenticCorrelations.get(identifier) || randomUUID();
                        const targetObjectId = this.agenticTargets.get(identifier) || null;
                        this.baselinePending = { sessionId: identifier, correlationId, targetObjectId };
                        this.logStudyEvent({
                            eventType: "intent_captured",
                            sessionId: identifier,
                            correlationId,
                            targetObjectId,
                            studySource: "baseline_runtime",
                        });
                        console.log(`[AgenticXR] baseline transcript ready peer=${peerName} characters=${response.length}`);

                        // this.components.textToSpeechService.sendToChildProcess("default", response + "\n");
                        this.components.codeGenerationService.sendToChildProcess("default", response + "\n");
                    }
                }
                response = "";
            }
        });

        this.components.transcriptionService.on("transcription_error", ({ peerUUID, message, audioDurationMs }) => {
            const correlationId = this.agenticCorrelations.get(peerUUID) || randomUUID();
            const targetObjectId = this.agenticTargets.get(peerUUID) || null;
            this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "failed",
                "Speech transcription failed. Please try again.");
            this.logStudyEvent({
                eventType: "transcription_failed",
                sessionId: peerUUID,
                correlationId,
                targetObjectId,
                audioDurationMs,
                status: "error",
                reasonCode: "stt_failure",
                studySource: "speech_to_text",
            });
        });

        // Step 3: When we receive a response from the text generation service, send it to the text to speech service
        if (this.components.codeGenerationService) this.components.codeGenerationService.on("response", (data, identifier) => {
            var response = data.toString();
            //console.log("Received text generation response from child process " + identifier);
            if (response.startsWith(">")) {
                console.log(" -> Code:: " + response);
                response = response.slice(1);
                
                this.scene.send(94, {
                        type: "CodeGenerated",
                        peer: identifier,
                        data: response,
                    });
                if (this.baselinePending) {
                    this.logStudyEvent({
                        ...this.baselinePending,
                        eventType: "propose_artifact",
                        status: "sent_unvalidated",
                        operation: "create",
                        studySource: "baseline_runtime",
                    });
                    // Remember the trial identity so Unity's CodeAttachResult can
                    // close this turn with a validated-execution event.
                    this.lastBaselineAttach.set(this.baselinePending.sessionId, {
                        correlationId: this.baselinePending.correlationId,
                        targetObjectId: this.baselinePending.targetObjectId,
                    });
                    this.baselinePending = null;
                }
                this.isGenerating = false;
            }
        });
    }

    async runClaudePing(peerUUID) {
        const targetObjectId = this.agenticTargets.get(peerUUID) || null;
        const correlationId = this.agenticCorrelations.get(peerUUID) || randomUUID();
        if (this.claudePingRuns.has(peerUUID)) {
            this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "failed", "A Claude ping is already running.");
            return;
        }

        this.claudePingRuns.add(peerUUID);
        const startedAt = Date.now();
        this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "claude_ping", "Claude ping sent.");
        try {
            const { query } = await import("@anthropic-ai/claude-agent-sdk");
            let result = null;
            for await (const message of query({
                prompt: "Reply exactly PONG.",
                options: {
                    model: "sonnet",
                    maxTurns: 1,
                    permissionMode: "bypassPermissions",
                    cwd: path.resolve(__dirname, "../../.."),
                },
            })) {
                if (message.type === "result") result = message;
            }

            const elapsedMs = Date.now() - startedAt;
            if (result && result.subtype === "success") {
                const reply = String(result.result || "PONG").trim().slice(0, 120);
                this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "claude_replied",
                    `Claude replied in ${(elapsedMs / 1000).toFixed(1)}s: ${reply}`);
                this.logEvaluation({ eventType: "claude_ping_success", sessionId: peerUUID, correlationId, elapsedMs });
            } else {
                this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "failed", "Claude ping returned without a successful result.");
                this.logEvaluation({ eventType: "claude_ping_failed", sessionId: peerUUID, correlationId, elapsedMs });
            }
        } catch (error) {
            const elapsedMs = Date.now() - startedAt;
            console.error(`[AgenticXR] Claude ping failed: ${error.message}`);
            this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "failed", "Claude ping failed: " + error.message);
            this.logEvaluation({ eventType: "claude_ping_failed", sessionId: peerUUID, correlationId, elapsedMs, error: error.message });
        } finally {
            this.claudePingRuns.delete(peerUUID);
        }
    }

    cancelAgenticTurn(peerUUID, requestedCorrelationId, source = "user_cancelled") {
        const run = this.agenticRuns.get(peerUUID);
        const correlationId = requestedCorrelationId || (run && run.correlationId) ||
            this.agenticCorrelations.get(peerUUID) || randomUUID();
        const targetObjectId = this.agenticTargets.get(peerUUID) || null;
        if (!run || run.child.exitCode != null) {
            this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "cancelled", "There is no active request to cancel.");
            return false;
        }
        if (requestedCorrelationId && requestedCorrelationId !== run.correlationId) {
            this.sendAgenticStatus(peerUUID, targetObjectId, requestedCorrelationId, "failed", "That request is no longer active.");
            return false;
        }

        run.cancelled = true;
        clearTimeout(run.watchdog);
        this.sendAgenticStatus(peerUUID, targetObjectId, run.correlationId, "cancelled", "Request cancelled.");
        this.logEvaluation({
            eventType: "turn_cancelled",
            sessionId: peerUUID,
            correlationId: run.correlationId,
            targetObjectId,
            reason: source,
        });
        terminateProcessTree(run.child);
        return true;
    }

    startAgenticTurn(intent, peerUUID) {
        if (this.studyIdentityBlocked.has(peerUUID)) return;
        const targetObjectId = this.agenticTargets.get(peerUUID);
        const correlationId = this.agenticCorrelations.get(peerUUID) || randomUUID();
        this.agenticCorrelations.set(peerUUID, correlationId);
        let studyContext;
        try {
            studyContext = this.artifactLog.claimRuntimeSession({
                runtimeSessionId: peerUUID,
                correlationId,
                studySource: "ubiq_peer",
            });
        } catch (error) {
            this.failClosedStudyIdentity(peerUUID, targetObjectId, correlationId, error);
            return;
        }
        const canonicalSessionId = studyContext ? studyContext.sessionId : peerUUID;
        this.artifactLog.append({
            eventType: "continuous_assist_preempt_requested",
            sessionId: peerUUID,
            correlationId,
            targetObjectId: targetObjectId || null,
            reasonCode: "explicit_user_request",
        });
        const idleRun = this.idlePredictionRuns.get(peerUUID);
        if (idleRun) {
            clearTimeout(idleRun.watchdog);
            if (idleRun.child.exitCode == null && !idleRun.child.killed) idleRun.child.kill();
            this.idlePredictionRuns.delete(peerUUID);
            this.artifactLog.append({
                eventType: "idle_prediction_preempted",
                sessionId: peerUUID,
                correlationId: idleRun.correlationId,
                targetObjectId,
                reason: "explicit_user_request",
                speculative: true,
            });
        }
        if (!targetObjectId) {
            console.warn(`[AgenticXR] No selected stable object was supplied by Unity for peer ${peerUUID}; ignoring transcript.`);
            this.sendAgenticStatus(peerUUID, null, correlationId, "failed", "No authorable object was selected.");
            this.logEvaluation({ eventType: "turn_rejected", sessionId: peerUUID, correlationId, reason: "missing_target" });
            return;
        }
        if (this.agenticRuns.has(peerUUID)) {
            console.warn(`[AgenticXR] Claude is already handling a request for peer ${peerUUID}; ignoring overlapping transcript.`);
            this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "failed", "A previous request is still running.");
            this.logEvaluation({ eventType: "turn_rejected", sessionId: peerUUID, correlationId, targetObjectId, reason: "overlapping_turn" });
            this.logStudyEvent({
                eventType: "interruption",
                sessionId: peerUUID,
                correlationId,
                targetObjectId,
                reasonCode: "overlapping_user_input",
            });
            return;
        }
        const orchestrator = path.resolve(__dirname, "../../../orchestrator/app.js");
        console.log(`[AgenticXR] starting Claude turn peer=${peerUUID} correlationId=${correlationId} target=${targetObjectId} intentCharacters=${intent.length}`);
        this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "thinking", "Claude is grounding and validating your request.");
        this.logEvaluation({ eventType: "turn_started", sessionId: peerUUID, correlationId, targetObjectId });
        this.logStudyEvent({
            eventType: "intent_captured",
            sessionId: peerUUID,
            correlationId,
            targetObjectId,
            studySource: "code_runtime_generator",
        });
        // Per-trial H4 configuration: the registered trial's candidateTarget (N=1
        // vs. N>1) reaches the orchestrator turn through its environment.
        const turnEnv = studyContext && Number.isInteger(studyContext.candidateTarget)
            ? { ...process.env, AGENTICXR_CANDIDATE_COUNT: String(studyContext.candidateTarget) }
            : process.env;
        const child = spawn(process.execPath, [orchestrator, intent, targetObjectId, canonicalSessionId, correlationId], {
            cwd: path.resolve(__dirname, "../../.."),
            env: turnEnv,
            stdio: "inherit",
            windowsHide: true,
        });
        const watchdog = setTimeout(() => {
            if (child.exitCode != null || child.killed) return;
            const run = this.agenticRuns.get(peerUUID);
            if (run) run.timedOut = true;
            console.error(`[AgenticXR] turn watchdog expired after ${this.agenticTurnTimeoutMs}ms correlationId=${correlationId}`);
            this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "failed", "The agent timed out. Please try again.");
            this.logEvaluation({ eventType: "turn_timeout", sessionId: peerUUID, correlationId, targetObjectId, timeoutMs: this.agenticTurnTimeoutMs });
            terminateProcessTree(child);
        }, this.agenticTurnTimeoutMs);
        this.agenticRuns.set(peerUUID, { child, watchdog, correlationId, cancelled: false, timedOut: false });
        child.on("error", (err) => {
            console.error(`[AgenticXR] failed to start Claude turn: ${err.message}`);
            clearTimeout(watchdog);
            this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "failed", "The agent process could not start.");
            this.logEvaluation({ eventType: "turn_process_error", sessionId: peerUUID, correlationId, targetObjectId, error: err.message });
            this.agenticRuns.delete(peerUUID);
        });
        child.on("exit", (code, signal) => {
            clearTimeout(watchdog);
            const run = this.agenticRuns.get(peerUUID);
            console.log(`[AgenticXR] Claude turn finished peer=${peerUUID} correlationId=${correlationId} exitCode=${code} signal=${signal || "none"}`);
            if (code !== 0 && !(run && (run.cancelled || run.timedOut)))
                this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "failed", "The agent stopped before completing the request.");
            this.logEvaluation({ eventType: "turn_process_exit", sessionId: peerUUID, correlationId, targetObjectId, exitCode: code, signal: signal || null });
            this.agenticRuns.delete(peerUUID);
            this.agenticCorrelations.delete(peerUUID);
            this.lastAgenticActivityAt.set(peerUUID, Date.now());
        });
    }

    runIdlePredictions() {
        if (!this.idlePredictionEnabled || !process.env.ANTHROPIC_API_KEY) return;
        const now = Date.now();
        for (const [sessionId, targetObjectId] of this.agenticTargets.entries()) {
            if (!targetObjectId || this.agenticRuns.has(sessionId) || this.idlePredictionRuns.has(sessionId)) continue;
            if (this.artifactLog.getStudyContext({ sessionId }) &&
                String(process.env.AGENTICXR_STUDY_ALLOW_SPECULATION || "false").toLowerCase() !== "true") continue;
            const lastActivity = this.lastAgenticActivityAt.get(sessionId) || now;
            const lastPrediction = this.lastIdlePredictionAt.get(sessionId) || 0;
            if (now - lastActivity < this.idlePredictionThresholdMs ||
                now - lastPrediction < this.idlePredictionCooldownMs) continue;
            this.startIdlePrediction(sessionId, targetObjectId);
        }
    }

    startIdlePrediction(sessionId, targetObjectId) {
        const correlationId = `idle-prediction-${randomUUID()}`;
        const orchestrator = path.resolve(__dirname, "../../../orchestrator/app.js");
        const objective = "Prepare likely reversible, local next-step candidates for the current object and activity context.";
        const child = spawn(process.execPath, [orchestrator, objective, targetObjectId, sessionId, correlationId], {
            cwd: path.resolve(__dirname, "../../.."),
            env: { ...process.env, AGENTICXR_SPECULATIVE_ONLY: "true" },
            stdio: "inherit",
            windowsHide: true,
        });
        const watchdog = setTimeout(() => {
            if (child.exitCode == null && !child.killed) child.kill();
        }, Math.min(this.agenticTurnTimeoutMs, 120000));
        this.idlePredictionRuns.set(sessionId, { child, watchdog, correlationId });
        this.lastIdlePredictionAt.set(sessionId, Date.now());
        this.artifactLog.append({
            eventType: "idle_prediction_triggered",
            sessionId,
            correlationId,
            targetObjectId,
            triggerSource: "schedule",
            speculative: true,
        });
        child.once("exit", (code) => {
            clearTimeout(watchdog);
            this.idlePredictionRuns.delete(sessionId);
            this.artifactLog.append({
                eventType: "idle_prediction_finished",
                sessionId,
                correlationId,
                targetObjectId,
                status: code === 0 ? "prepared" : "failed",
                speculative: true,
            });
        });
    }

    sendAgenticStatus(sessionId, targetObjectId, correlationId, state, detail) {
        if (!sessionId || !correlationId) return false;
        let studyContext;
        try {
            studyContext = this.artifactLog.claimRuntimeSession({
                runtimeSessionId: sessionId,
                correlationId,
                studySource: "ubiq_peer",
            });
        } catch (error) {
            this.failClosedStudyIdentity(sessionId, targetObjectId, correlationId, error);
            return false;
        }
        const canonicalSessionId = studyContext ? studyContext.sessionId : sessionId;
        const envelope = makeEnvelope({
            type: "AgentStatus",
            sessionId: canonicalSessionId,
            correlationId,
            originAgent: "code_runtime_generator",
            targetObjectId: targetObjectId || null,
            payload: { state, detail },
        });
        this.scene.send(97, toWireFormat(envelope));
        this.logStudyEvent({
            eventType: "agent_status_sent",
            sessionId,
            correlationId,
            targetObjectId: targetObjectId || null,
            status: state,
            studySource: "code_runtime_generator",
        });
        return !this.studyIdentityBlocked.has(sessionId);
    }

    logEvaluation(event) {
        try { appendEvaluationEvent(event); }
        catch (error) { console.error(`[AgenticXR] evaluation log error: ${error.message}`); }
    }

    logStudyEvent(event) {
        try {
            const runtimeSessionId = event && event.sessionId;
            const context = this.artifactLog.claimRuntimeSession({
                runtimeSessionId,
                correlationId: event && event.correlationId,
                studySource: event && event.studySource || "code_runtime_generator",
            });
            if (!context) return null;
            return this.artifactLog.appendStudyEvent(event);
        } catch (error) {
            console.error(`[AgenticXR] study log error: ${error.message}`);
            this.failClosedStudyIdentity(event && event.sessionId, event && event.targetObjectId,
                event && event.correlationId, error);
            return null;
        }
    }

    failClosedStudyIdentity(sessionId, targetObjectId, correlationId, error) {
        if (sessionId) this.studyIdentityBlocked.add(sessionId);
        const run = sessionId ? this.agenticRuns.get(sessionId) : null;
        if (run && run.child) terminateProcessTree(run.child);
        if (sessionId && correlationId) {
            const envelope = makeEnvelope({
                type: "AgentStatus",
                sessionId,
                correlationId,
                originAgent: "code_runtime_generator",
                targetObjectId: targetObjectId || null,
                payload: {
                    state: "failed",
                    detail: "Study logging identity failed. The request was stopped; ask the researcher to check the active trial.",
                },
            });
            this.scene.send(97, toWireFormat(envelope));
        }
        console.error(`[AgenticXR] request stopped because study identity could not be guaranteed: ${error.message}`);
    }

    logArtifactEvent(event) {
        try {
            const context = this.artifactLog.claimRuntimeSession({
                runtimeSessionId: event && event.sessionId,
                correlationId: event && event.correlationId,
                studySource: event && event.studySource || "code_runtime_generator",
            });
            return context
                ? this.artifactLog.appendStudyEvent(event)
                : this.artifactLog.append(event);
        } catch (error) {
            console.error(`[AgenticXR] artifact log error: ${error.message}`);
            this.failClosedStudyIdentity(event && event.sessionId, event && event.targetObjectId,
                event && event.correlationId, error);
            return null;
        }
    }
}

module.exports = { CodeGeneration };

if (require.main === module) {
    const app = new CodeGeneration();
    app.start();
}
