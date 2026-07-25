"use strict";

const path = require("path");
const fs = require("fs");
const assert = require("assert");
const { spawn } = require("child_process");
const { TRIAL_COLUMNS, readJsonLines, buildStudyExports } = require("../evaluation/study_export");

const root = path.resolve(__dirname, "..");
process.env.AGENTICXR_EVALUATION_SOURCE = "mock";
process.env.AGENTICXR_EVALUATION_LOG = path.join(root, "evaluation", "data", "mock-integration-events.jsonl");
process.env.AGENTICXR_ARTIFACT_LOG = path.join(root, "evaluation", "data", "mock-study-artifacts.jsonl");
fs.mkdirSync(path.dirname(process.env.AGENTICXR_EVALUATION_LOG), { recursive: true });
fs.rmSync(process.env.AGENTICXR_EVALUATION_LOG, { force: true });
fs.rmSync(process.env.AGENTICXR_ARTIFACT_LOG, { force: true });
const children = new Set();

function start(args, label) {
    const child = spawn(process.execPath, args, { cwd: root, stdio: "inherit", windowsHide: true });
    children.add(child);
    child.once("exit", (code) => {
        children.delete(child);
        if (code && !process.exitCode) {
            console.error(`[mock_integration] ${label} exited early with ${code}`);
            process.exitCode = code;
        }
    });
    return child;
}

function run(args) {
    return new Promise((resolve, reject) => {
        const child = start(args, args[0]);
        child.once("exit", (code) => code === 0 ? resolve() : reject(new Error(`${args[0]} exited ${code}`)));
    });
}

function wait(ms) { return new Promise((resolve) => setTimeout(resolve, ms)); }
function cleanup() {
    for (const child of children) {
        try { child.kill(); } catch { /* already stopped */ }
    }
}

process.on("SIGINT", () => { cleanup(); process.exit(130); });
process.on("SIGTERM", () => { cleanup(); process.exit(143); });

(async () => {
    try {
        start(["node_modules/ubiq/app.js", "config/default.json"], "room server");
        await wait(2500);
        start(["mcp/unity_scene_bridge/mock_unity_peer.js"], "mock Unity peer");
        await wait(2500);
        await run(["mcp/unity_scene_bridge/smoketest_client.mjs"]);
        const exported = buildStudyExports(readJsonLines(process.env.AGENTICXR_ARTIFACT_LOG));
        assert.strictEqual(exported.trialRows.length, 1, "mock pipeline must export exactly one study trial");
        const row = exported.trialRows[0];
        for (const column of TRIAL_COLUMNS) assert.ok(Object.hasOwn(row, column), `missing study export column ${column}`);
        assert.strictEqual(row.participantId, "mock-participant-001");
        assert.strictEqual(row.taskCompletion, true);
        assert.ok(row.generatedArtifactCount >= 1);
        assert.ok(row.candidatesGenerated >= 3);
        assert.ok(Number.isFinite(row.validatedExecutionLatencyMs));
        assert.ok(Number.isFinite(row.memoryRetrievalLatencyMeanMs));
        assert.ok(row.agentStatusMessageCount >= 1);
        console.log(`[mock_integration] study export PASS (${TRIAL_COLUMNS.length} trial columns)`);
        await run(["mcp/unity_scene_bridge/cache_test_flow.mjs"]);
        console.log("[mock_integration] PASS");
    } finally {
        cleanup();
    }
})().catch((error) => {
    console.error(`[mock_integration] FAIL: ${error.message}`);
    process.exitCode = 1;
});
