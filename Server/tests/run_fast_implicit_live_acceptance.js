"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");

require("../scripts/load-local-env");

const root = path.resolve(__dirname, "..");
const outputDir = path.join(root, "evaluation", "data", "fast-live-acceptance");
const evaluationLog = path.join(outputDir, "runtime-events.jsonl");
const artifactLog = path.join(outputDir, "artifact-log.jsonl");
const children = new Set();

function wait(ms) { return new Promise((resolve) => setTimeout(resolve, ms)); }

function start(args, label, env = process.env) {
    const child = spawn(process.execPath, args, { cwd: root, env, stdio: "inherit", windowsHide: true });
    children.add(child);
    child.once("exit", () => children.delete(child));
    child.label = label;
    return child;
}

function run(args, env) {
    return new Promise((resolve, reject) => {
        const child = start(args, args[0], env);
        child.once("exit", (code) => code === 0 ? resolve() : reject(new Error(`${args[0]} exited ${code}`)));
    });
}

function cleanup() {
    for (const child of children) {
        try { child.kill(); } catch (_) { /* Already stopped. */ }
    }
}

(async () => {
    if (!process.env.ANTHROPIC_API_KEY) throw new Error("ANTHROPIC_API_KEY is required for live acceptance.");
    fs.rmSync(outputDir, { recursive: true, force: true });
    fs.mkdirSync(outputDir, { recursive: true });
    const env = {
        ...process.env,
        AGENTICXR_EVALUATION_SOURCE: "fast-live-acceptance",
        AGENTICXR_EVALUATION_LOG: evaluationLog,
        AGENTICXR_ARTIFACT_LOG: artifactLog,
        AGENTICXR_INTERACTION_MODE: "L1",
        AGENTICXR_TRIGGER_SOURCE: "system_opportunity",
        AGENTICXR_EXPERIENCE_MODE: "training",
        AGENTICXR_FAST_IMPLICIT_DIRECT: "true",
        AGENTICXR_FAST_IMPLICIT_BUDGET_MS: "55000",
        AGENTICXR_MODEL_ID: process.env.AGENTICXR_MODEL_ID || "claude-sonnet-4-6",
        AGENTICXR_MODEL_VERSION: process.env.AGENTICXR_MODEL_ID || "claude-sonnet-4-6",
    };

    start(["node_modules/ubiq/app.js", "config/default.json"], "room server", env);
    await wait(2500);
    start(["mcp/unity_scene_bridge/mock_unity_peer.js"], "mock Unity peer", env);
    await wait(2500);

    const startedAt = Date.now();
    await run([
        "orchestrator/app.js",
        "Ground one tool/tray pair and show the bounded reversible guidance cue.",
        "study-l1-a-tool-2",
        "fast-live-session",
        "fast-live-correlation",
    ], env);
    const wallTimeMs = Date.now() - startedAt;
    assert.ok(wallTimeMs < 60000, `fast live acceptance exceeded 60 seconds (${wallTimeMs}ms)`);

    const records = fs.readFileSync(evaluationLog, "utf8").trim().split(/\r?\n/).map(JSON.parse);
    const result = records.find((record) => record.eventType === "orchestrator_result" &&
        record.orchestratorRoute === "direct-fast-implicit" && record.subtype === "success");
    assert.ok(result, "direct fast implicit success was not logged");
    assert.equal(result.sourceObjectId, "study-l1-a-tool-2");
    assert.equal(result.destinationObjectId, "study-l1-a-tray-1");
    assert.ok(result.latencyMs < 60000, `instrumented latency exceeded 60 seconds (${result.latencyMs}ms)`);
    console.log(`[fast_live_acceptance] PASS wall=${wallTimeMs}ms instrumented=${result.latencyMs}ms`);
})().catch((error) => {
    console.error(`[fast_live_acceptance] FAIL: ${error.message}`);
    process.exitCode = 1;
}).finally(cleanup);
