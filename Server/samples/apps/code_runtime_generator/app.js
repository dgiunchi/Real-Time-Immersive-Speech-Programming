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
        this.agenticCorrelations = new Map();
        this.lastAgenticActivityAt = new Map();
        this.idlePredictionRuns = new Map();
        this.lastIdlePredictionAt = new Map();
        this.baselinePending = null;
        this.agenticTurnTimeoutMs = Number(process.env.AGENTICXR_TURN_TIMEOUT_MS) || 180000;
        this.artifactLog = new ArtifactLog({ filePath: process.env.AGENTICXR_ARTIFACT_LOG });
        this.idlePredictionEnabled = String(process.env.AGENTICXR_IDLE_PREDICTION_ENABLED || "false").toLowerCase() === "true";
        this.idlePredictionThresholdMs = Math.max(30000, Number(process.env.AGENTICXR_IDLE_PREDICTION_THRESHOLD_MS) || 60000);
        this.idlePredictionCooldownMs = Math.max(60000, Number(process.env.AGENTICXR_IDLE_PREDICTION_COOLDOWN_MS) || 300000);
        if (this.agenticMode && this.idlePredictionEnabled) {
            setInterval(() => this.runIdlePredictions(), 15000).unref();
        }

    }

    definePipeline() {
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
                        if (targetObjectId) this.agenticTargets.set(peerUUID, targetObjectId);
                        this.lastAgenticActivityAt.set(peerUUID, Date.now());
                        const correlationId = randomUUID();
                        this.agenticCorrelations.set(peerUUID, correlationId);
                        this.artifactLog.append({
                            eventType: "continuous_assist_preempt_requested",
                            sessionId: peerUUID,
                            correlationId,
                            targetObjectId: targetObjectId || null,
                            reasonCode: "push_to_talk_started",
                        });
                        this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "listening", "Listening to your request.");
                        this.logEvaluation({ eventType: "recording_start", sessionId: peerUUID, correlationId, targetObjectId });
                        this.components.transcriptionService.recordingStart(peerUUID);
                    } else if (action === "stop") {
                        this.lastAgenticActivityAt.set(peerUUID, Date.now());
                        const correlationId = this.agenticCorrelations.get(peerUUID);
                        this.sendAgenticStatus(peerUUID, this.agenticTargets.get(peerUUID), correlationId, "transcribing", "Transcribing your request.");
                        this.logEvaluation({ eventType: "recording_stop", sessionId: peerUUID, correlationId, targetObjectId: this.agenticTargets.get(peerUUID) });
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
        this.components.transcriptionService.on("response", (data, identifier) => {
            // roomClient.peers is a Map of all peers in the room
            // Get the peer with the given identifier
            const peer = this.roomClient.peers.get(identifier);
            const peerName = peer ? peer.properties.get("ubiq.samples.social.name") : identifier;

            var response = data.toString();
            var threshold = 10;

            if (response.length != 0 && response.length > threshold) {
                // Remove all newlines from the response
                response = response.replace(/(\r\n|\n|\r)/gm, "");
                
                ensureRuntimeDataFiles();
                fs.appendFileSync(INPUT_FILE, response);
                console.log(`File ${INPUT_FILE} appended successfully.`);

                if (response.startsWith(">")) response = response.slice(1);
                if (response.trim()) {
                    if (this.agenticMode) {
                        this.lastAgenticActivityAt.set(identifier, Date.now());
                        this.logEvaluation({
                            eventType: "transcript_ready",
                            sessionId: identifier,
                            correlationId: this.agenticCorrelations.get(identifier),
                            targetObjectId: this.agenticTargets.get(identifier),
                            transcriptCharacters: response.trim().length,
                        });
                        this.startAgenticTurn(response.trim(), identifier);
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
                        console.log(peerName + " -> Agent:: " + response);

                        // this.components.textToSpeechService.sendToChildProcess("default", response + "\n");
                        this.components.codeGenerationService.sendToChildProcess("default", response + "\n");
                    }
                }
                response = "";
            }
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
                    this.baselinePending = null;
                }
                this.isGenerating = false;
            }
        });
    }

    startAgenticTurn(intent, peerUUID) {
        const targetObjectId = this.agenticTargets.get(peerUUID);
        const correlationId = this.agenticCorrelations.get(peerUUID) || randomUUID();
        this.agenticCorrelations.set(peerUUID, correlationId);
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
        console.log(`[AgenticXR] starting Claude turn peer=${peerUUID} correlationId=${correlationId} target=${targetObjectId} intent=${JSON.stringify(intent)}`);
        this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "thinking", "Claude is grounding and validating your request.");
        this.logEvaluation({ eventType: "turn_started", sessionId: peerUUID, correlationId, targetObjectId });
        this.logStudyEvent({
            eventType: "intent_captured",
            sessionId: peerUUID,
            correlationId,
            targetObjectId,
            studySource: "code_runtime_generator",
        });
        const child = spawn(process.execPath, [orchestrator, intent, targetObjectId, peerUUID, correlationId], {
            cwd: path.resolve(__dirname, "../../.."),
            env: process.env,
            stdio: "inherit",
            windowsHide: true,
        });
        const watchdog = setTimeout(() => {
            if (child.exitCode != null || child.killed) return;
            console.error(`[AgenticXR] turn watchdog expired after ${this.agenticTurnTimeoutMs}ms correlationId=${correlationId}`);
            this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "failed", "The agent timed out. Please try again.");
            this.logEvaluation({ eventType: "turn_timeout", sessionId: peerUUID, correlationId, targetObjectId, timeoutMs: this.agenticTurnTimeoutMs });
            child.kill();
        }, this.agenticTurnTimeoutMs);
        this.agenticRuns.set(peerUUID, { child, watchdog, correlationId });
        child.on("error", (err) => {
            console.error(`[AgenticXR] failed to start Claude turn: ${err.message}`);
            clearTimeout(watchdog);
            this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "failed", "The agent process could not start.");
            this.logEvaluation({ eventType: "turn_process_error", sessionId: peerUUID, correlationId, targetObjectId, error: err.message });
            this.agenticRuns.delete(peerUUID);
        });
        child.on("exit", (code, signal) => {
            clearTimeout(watchdog);
            console.log(`[AgenticXR] Claude turn finished peer=${peerUUID} correlationId=${correlationId} exitCode=${code} signal=${signal || "none"}`);
            if (code !== 0) this.sendAgenticStatus(peerUUID, targetObjectId, correlationId, "failed", "The agent stopped before completing the request.");
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
        if (!sessionId || !correlationId) return;
        const envelope = makeEnvelope({
            type: "AgentStatus",
            sessionId,
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
    }

    logEvaluation(event) {
        try { appendEvaluationEvent(event); }
        catch (error) { console.error(`[AgenticXR] evaluation log error: ${error.message}`); }
    }

    logStudyEvent(event) {
        try {
            if (!this.artifactLog.getStudyContext(event)) return;
            this.artifactLog.appendStudyEvent(event);
        } catch (error) {
            console.error(`[AgenticXR] study log error: ${error.message}`);
        }
    }
}

module.exports = { CodeGeneration };

if (require.main === module) {
    const app = new CodeGeneration();
    app.start();
}
