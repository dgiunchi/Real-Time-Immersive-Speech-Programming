"use strict";

// API-free live transport smoke test for the in-headset study launcher.
// It starts the real CodeGeneration room/runtime, joins as a tiny mock Unity
// peer, sends StudyTrialStartRequest on channel 100, and requires the same
// StudyTrialConfigured acknowledgement Unity consumes on channel 97.

const assert = require("assert");
const fs = require("fs");
const path = require("path");
const { spawn, spawnSync } = require("child_process");
const { NetworkScene, NetworkId, UbiqTcpConnection } = require("ubiq/ubiq");
const { RoomClient } = require("ubiq/components");
const { fromWireFormat } = require("../cache/protocol");

const root = path.resolve(__dirname, "..");
const artifactLogPath = path.join(root, "evaluation", "data", "debug-transport-test.jsonl");
fs.mkdirSync(path.dirname(artifactLogPath), { recursive: true });
fs.rmSync(artifactLogPath, { force: true });

const runtime = spawn(process.execPath, ["scripts/start-agenticxr.js"], {
    cwd: root,
    windowsHide: true,
    env: {
        ...process.env,
        // Startup requires a configured credential, but this smoke test never
        // starts an agent turn and therefore never sends an API request.
        ANTHROPIC_API_KEY: "offline-transport-test",
        AGENTICXR_MONITOR_ENABLED: "false",
        AGENTICXR_HYBRID_STUDY_RUNTIME: "false",
        AGENTICXR_ARTIFACT_LOG: artifactLogPath,
    },
    stdio: ["ignore", "pipe", "pipe"],
});

let runtimeOutput = "";
runtime.stdout.on("data", (chunk) => { runtimeOutput += chunk.toString(); });
runtime.stderr.on("data", (chunk) => { runtimeOutput += chunk.toString(); });

function stopTree(child) {
    if (!child || child.exitCode != null) return;
    if (process.platform === "win32") {
        spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], {
            windowsHide: true,
            stdio: "ignore",
        });
    } else {
        try { child.kill("SIGTERM"); } catch (_) { /* already stopped */ }
    }
}

function waitForRuntime(timeoutMs = 15000) {
    return new Promise((resolve, reject) => {
        const startedAt = Date.now();
        const timer = setInterval(() => {
            if (/Added RoomServer port 8009/.test(runtimeOutput)) {
                clearInterval(timer);
                resolve();
            } else if (runtime.exitCode != null) {
                clearInterval(timer);
                reject(new Error(`runtime exited ${runtime.exitCode}: ${runtimeOutput}`));
            } else if (Date.now() - startedAt > timeoutMs) {
                clearInterval(timer);
                reject(new Error(`runtime startup timed out: ${runtimeOutput}`));
            }
        }, 50);
    });
}

async function main() {
    await waitForRuntime();
    const scene = new NetworkScene();
    const rooms = new RoomClient(scene);
    const configured = new Promise((resolve, reject) => {
        const timeout = setTimeout(() => reject(new Error("StudyTrialConfigured acknowledgement timed out")), 10000);
        scene.register({
            networkId: new NetworkId(97),
            processMessage(message) {
                try {
                    const envelope = fromWireFormat(message.toObject());
                    if (envelope.type !== "StudyTrialConfigured") return;
                    clearTimeout(timeout);
                    resolve(envelope);
                } catch (error) {
                    clearTimeout(timeout);
                    reject(error);
                }
            },
        });
    });

    const joined = new Promise((resolve) => rooms.on("OnJoinedRoom", resolve));
    scene.addConnection(UbiqTcpConnection("localhost", 8009));
    rooms.join("6765c52b-3ad6-4fb0-9030-2c9a05dc4731");
    await Promise.race([
        joined,
        new Promise((_, reject) => setTimeout(() => reject(new Error("mock Unity room join timed out")), 10000)),
    ]);

    const correlationId = "debug-transport-config-1";
    scene.send(100, {
        type: "StudyTrialStartRequest",
        sessionId: "debug-L1-transport",
        correlationId,
        payload: {
            participantId: "DEBUG",
            sessionId: "debug-L1-transport",
            trialId: "debug-L1-A-transport",
            taskId: "L1-proactive",
            interactionMode: "L1",
            taskVariant: "A",
            condition: "agenticxr_verification",
            conditionAlias: "full",
            candidateTarget: 0,
        },
    });

    const reply = await configured;
    assert.strictEqual(reply.correlationId, correlationId);
    assert.strictEqual(reply.payload.status, "configured");
    assert.strictEqual(reply.payload.trialId, "debug-L1-A-transport");
    const records = fs.readFileSync(artifactLogPath, "utf8").split(/\r?\n/).filter(Boolean).map(JSON.parse);
    assert.ok(records.some((entry) => entry.eventType === "study_trial_started" &&
        entry.trialId === "debug-L1-A-transport"), "runtime did not durably register the trial");
    console.log("[debug_trial_transport_test] PASS (channel 100 request -> channel 97 acknowledgement)");
}

main().catch((error) => {
    console.error(`[debug_trial_transport_test] FAIL: ${error.message}`);
    process.exitCode = 1;
}).finally(() => stopTree(runtime));
