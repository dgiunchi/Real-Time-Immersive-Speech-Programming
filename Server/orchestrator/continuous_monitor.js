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
const assistEnabled = String(process.env.AGENTICXR_CONTINUOUS_ASSIST_ENABLED || "false").toLowerCase() === "true";
const allowDuringStudy = String(process.env.AGENTICXR_STUDY_ALLOW_CONTINUOUS_ASSIST || "false").toLowerCase() === "true";
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
    if (!assistEnabled) {
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
    if (!allowDuringStudy && memory.artifactLog.getStudyContext({ sessionId: opportunity.sessionId })) {
        appendSuppressed(opportunity, "active_study_trial");
        return;
    }

    const experience = memory.experienceContext.get(opportunity.sessionId);
    const objective = `Assess whether the current ${experience && experience.mode || "unspecified"} activity ` +
        `would benefit from a reversible, local assistance for ${opportunity.targetObjectId}. ` +
        `Context signals: ${opportunity.signalTypes.join(", ")}. Do nothing if no useful low-risk assistance exists.`;
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
            AGENTICXR_TRIGGER_SOURCE: "context",
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
    });
    memory.artifactLog.append({
        eventType: "continuous_assist_started",
        ...opportunity,
        correlationId: opportunity.triggerId,
        experienceMode: experience && experience.mode || "unspecified",
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

function readRecentControlEvents() {
    const filePath = memory.artifactLog.filePath;
    if (!fs.existsSync(filePath)) return [];
    return fs.readFileSync(filePath, "utf8").split(/\r?\n/).filter(Boolean).slice(-200).flatMap((line) => {
        try { return [JSON.parse(line)]; } catch (_) { return []; }
    });
}

setInterval(() => {
    if (!running.size) return;
    const events = readRecentControlEvents();
    for (const [sessionId, run] of running) {
        const preempt = [...events].reverse().find((entry) =>
            entry.eventType === "continuous_assist_preempt_requested" &&
            entry.sessionId === sessionId &&
            (entry.loggedAt || entry.at || 0) > run.startedAt);
        if (!preempt) continue;
        clearTimeout(run.watchdog);
        if (run.child.exitCode == null && !run.child.killed) run.child.kill();
        memory.artifactLog.append({
            eventType: "continuous_assist_preempted",
            sessionId,
            correlationId: run.correlationId,
            reasonCode: "explicit_user_activity",
        });
    }
}, 500).unref();

function stop() {
    for (const run of running.values()) {
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
