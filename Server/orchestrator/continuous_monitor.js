"use strict";

// Long-lived observer built from the existing SceneBridgeClient + SharedMemory path.
// It does not apply changes. A threshold crossing may start the normal orchestrator,
// which still passes Validator, mode policy, Proposal Gate, and Unity consent.

const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");
const nconf = require("nconf");
const { SceneBridgeClient } = require("../mcp/unity_scene_bridge/scene_bridge_client");
const { SharedMemory } = require("../memory");

const configPath = process.argv[2] || path.join(__dirname, "..", "mcp", "unity_scene_bridge", "config.json");
nconf.file(configPath);
const config = nconf.get();
if (!config || !config.roomGuid || !config.roomserver) {
    throw new Error(`Invalid or missing continuous-monitor config: ${configPath}`);
}

process.env.AGENTICXR_ACTIVITY_MONITOR_PRIMARY = "true";
const bridge = new SceneBridgeClient(config);
const memory = new SharedMemory({ artifactLogPath: process.env.AGENTICXR_ARTIFACT_LOG });
memory.attach(bridge);
const running = new Map();
const speculativeRunning = new Map();
const assistEnabled = String(process.env.AGENTICXR_CONTINUOUS_ASSIST_ENABLED || "false").toLowerCase() === "true";
// Anticipation-driven preparation shares the speculation opt-in with idle
// prediction - both generate candidates ahead of need and both spend API credit.
const speculationEnabled = String(process.env.AGENTICXR_IDLE_PREDICTION_ENABLED || "false").toLowerCase() === "true";
const allowDuringStudy = String(process.env.AGENTICXR_STUDY_ALLOW_CONTINUOUS_ASSIST || "false").toLowerCase() === "true";
const allowSpeculationDuringStudy = String(process.env.AGENTICXR_STUDY_ALLOW_SPECULATION || "false").toLowerCase() === "true";
const turnTimeoutMs = Math.max(30000, Number(process.env.AGENTICXR_CONTINUOUS_ASSIST_TIMEOUT_MS) || 120000);

function appendSuppressed(opportunity, reason) {
    memory.artifactLog.append({
        eventType: "activity_assist_suppressed",
        ...opportunity,
        correlationId: opportunity.triggerId,
        reasonCode: reason,
    });
}

memory.activity.on("assist_worthy", (opportunity) => {
    let studyContext = null;
    try {
        studyContext = memory.artifactLog.claimRuntimeSession({
            runtimeSessionId: opportunity.sessionId,
            correlationId: opportunity.triggerId,
            studySource: "continuous_monitor",
        });
    } catch (error) {
        appendSuppressed(opportunity, "study_identity_failed");
        console.error(`[continuous_monitor] study identity failed: ${error.message}`);
        return;
    }
    const debugImplicitStudy = studyContext && studyContext.runMode === "unity_debug_launcher" &&
        ["L1", "L2"].includes(studyContext.interactionMode);
    if (!assistEnabled && !debugImplicitStudy) {
        appendSuppressed(opportunity, "continuous_assist_disabled");
        return;
    }
    if (!process.env.ANTHROPIC_API_KEY) {
        appendSuppressed(opportunity, "anthropic_key_missing");
        return;
    }
    if (!opportunity.targetObjectId) {
        appendSuppressed(opportunity, "stable_target_missing");
        return;
    }
    if (running.has(opportunity.sessionId)) {
        appendSuppressed(opportunity, "continuous_assist_already_running");
        return;
    }
    if (!allowDuringStudy && studyContext && !debugImplicitStudy) {
        appendSuppressed(opportunity, "active_study_trial");
        return;
    }
    if (studyContext && !["L1", "L2"].includes(studyContext.interactionMode)) {
        appendSuppressed(opportunity, "implicit_trigger_outside_l1_l2");
        return;
    }

    const experience = memory.experienceContext.get(opportunity.sessionId);
    // Keep implicit study turns deliberately narrow. The scene context still
    // determines whether a cue is useful, but the live model is not asked to
    // explore unrelated objects or invent a complex interaction.
    const triggerSource = studyContext && studyContext.interactionMode === "L1" ? "system_opportunity" : "context";
    const objective = `Apply the fixed ${studyContext && studyContext.interactionMode || "L2"} visual guidance cue ` +
        `to ${opportunity.targetObjectId}. Signals: ${opportunity.signalTypes.join(", ")}. ` +
        "Make this target pulse continuously between cyan and magenta while scaling from its original size to 1.08x. " +
        "Restore its exact original color and scale when disabled or destroyed. Do not choose another behaviour.";
    bridge.sendAgentStatus({
        sessionId: opportunity.sessionId,
        correlationId: opportunity.triggerId,
        state: "context_detected",
        detail: "The agent noticed a possible assistance opportunity and is checking it safely.",
    });
    const orchestrator = path.join(__dirname, "app.js");
    const child = spawn(process.execPath, [
        orchestrator,
        objective,
        opportunity.targetObjectId,
        opportunity.sessionId,
        opportunity.triggerId,
    ], {
        cwd: path.join(__dirname, ".."),
        env: {
            ...process.env,
            AGENTICXR_TRIGGER_SOURCE: triggerSource,
            AGENTICXR_INTERACTION_MODE: studyContext && studyContext.interactionMode || "L2",
            AGENTICXR_EXPERIENCE_MODE: experience && experience.mode || "unspecified",
        },
        stdio: "inherit",
        windowsHide: true,
    });
    const watchdog = setTimeout(() => {
        if (child.exitCode == null && !child.killed) child.kill();
    }, turnTimeoutMs);
    running.set(opportunity.sessionId, {
        child,
        watchdog,
        correlationId: opportunity.triggerId,
        startedAt: Date.now(),
        trialId: studyContext && studyContext.trialId || null,
    });
    memory.artifactLog.append({
        eventType: "continuous_assist_started",
        ...opportunity,
        correlationId: opportunity.triggerId,
        experienceMode: experience && experience.mode || "unspecified",
        triggerSource,
    });
    child.once("exit", (code, signal) => {
        clearTimeout(watchdog);
        running.delete(opportunity.sessionId);
        memory.artifactLog.append({
            eventType: "continuous_assist_finished",
            ...opportunity,
            correlationId: opportunity.triggerId,
            status: code === 0 ? "finished" : "failed_or_preempted",
            exitCode: code,
            signal: signal || null,
        });
    });
});

// Anticipation (docs/code-implicit-proactive-showcase-2026-08-13.md §1): sustained
// directed attention toward a target predicts engagement BEFORE the assist
// threshold fires. Start a speculative orchestrator run that generates,
// validates, and dry-runs candidates and registers them pinned to the exact
// scene tuple - preparation only, never a proposal or commit. When the real
// trigger later fires, the normal turn consults select_speculative_candidate
// and still passes every validation, mode-policy, and consent gate.
memory.activity.on("predicted_engagement", (prediction) => {
    if (!speculationEnabled || !process.env.ANTHROPIC_API_KEY) return;
    if (!prediction.targetObjectId) return;
    if (running.has(prediction.sessionId) || speculativeRunning.has(prediction.sessionId)) return;
    if (!allowSpeculationDuringStudy && memory.artifactLog.getStudyContext({ sessionId: prediction.sessionId })) {
        memory.artifactLog.append({
            eventType: "idle_prediction_suppressed",
            sessionId: prediction.sessionId,
            correlationId: prediction.predictionId,
            targetObjectId: prediction.targetObjectId,
            reasonCode: "active_study_trial",
            speculative: true,
        });
        return;
    }
    const objective = `Predicted upcoming engagement with ${prediction.targetObjectId} ` +
        `(directed signals: ${prediction.signalTypes.join(", ")}). Environmental context - ` +
        `${memory.describeContext(prediction)}. Prepare reversible, local, clearly visible candidates ` +
        "this context would justify, for later adoption through the full normal pipeline.";
    const child = spawn(process.execPath, [
        path.join(__dirname, "app.js"),
        objective,
        prediction.targetObjectId,
        prediction.sessionId,
        prediction.predictionId,
    ], {
        cwd: path.join(__dirname, ".."),
        env: {
            ...process.env,
            AGENTICXR_SPECULATIVE_ONLY: "true",
            AGENTICXR_TRIGGER_SOURCE: "context",
        },
        stdio: "inherit",
        windowsHide: true,
    });
    const watchdog = setTimeout(() => {
        if (child.exitCode == null && !child.killed) child.kill();
    }, Math.min(turnTimeoutMs, 120000));
    speculativeRunning.set(prediction.sessionId, {
        child,
        watchdog,
        correlationId: prediction.predictionId,
        startedAt: Date.now(),
    });
    memory.artifactLog.append({
        eventType: "idle_prediction_triggered",
        sessionId: prediction.sessionId,
        correlationId: prediction.predictionId,
        targetObjectId: prediction.targetObjectId,
        triggerSource: "predicted_engagement",
        speculative: true,
    });
    child.once("exit", (code) => {
        clearTimeout(watchdog);
        speculativeRunning.delete(prediction.sessionId);
        memory.artifactLog.append({
            eventType: "idle_prediction_finished",
            sessionId: prediction.sessionId,
            correlationId: prediction.predictionId,
            targetObjectId: prediction.targetObjectId,
            status: code === 0 ? "prepared" : "failed",
            speculative: true,
        });
    });
});

function readRecentControlEvents() {
    const filePath = memory.artifactLog.filePath;
    if (!fs.existsSync(filePath)) return [];
    return fs.readFileSync(filePath, "utf8").split(/\r?\n/).filter(Boolean).slice(-200).flatMap((line) => {
        try { return [JSON.parse(line)]; } catch (_) { return []; }
    });
}

setInterval(() => {
    if (!running.size && !speculativeRunning.size) return;
    const events = readRecentControlEvents();
    for (const [pool, preemptEventType] of [
        [running, "continuous_assist_preempted"],
        [speculativeRunning, "idle_prediction_preempted"],
    ]) {
        for (const [sessionId, run] of pool) {
            const explicitPreempt = [...events].reverse().find((entry) =>
                entry.eventType === "continuous_assist_preempt_requested" &&
                entry.sessionId === sessionId &&
                (entry.loggedAt || entry.at || 0) > run.startedAt);
            const trialEnded = run.trialId && [...events].reverse().find((entry) =>
                entry.eventType === "study_trial_ended" &&
                entry.trialId === run.trialId &&
                (entry.loggedAt || entry.at || 0) > run.startedAt);
            const preempt = explicitPreempt || trialEnded;
            if (!preempt) continue;
            clearTimeout(run.watchdog);
            if (run.child.exitCode == null && !run.child.killed) run.child.kill();
            pool.delete(sessionId);
            memory.artifactLog.append({
                eventType: preemptEventType,
                sessionId,
                correlationId: run.correlationId,
                reasonCode: trialEnded ? "study_trial_ended" : "explicit_user_activity",
                ...(pool === speculativeRunning ? { speculative: true } : {}),
            });
        }
    }
}, 500).unref();

function stop() {
    for (const run of [...running.values(), ...speculativeRunning.values()]) {
        clearTimeout(run.watchdog);
        if (run.child.exitCode == null && !run.child.killed) run.child.kill();
    }
}
process.once("SIGINT", () => { stop(); process.exit(0); });
process.once("SIGTERM", () => { stop(); process.exit(0); });
process.once("exit", stop);
bridge.connect().then(() => {
    console.error(`[continuous_monitor] observing Ubiq activity; assistance=${assistEnabled ? "enabled" : "monitor-only"}`);
}).catch((error) => {
    console.error(`[continuous_monitor] failed to connect: ${error.message}`);
    process.exitCode = 1;
});
