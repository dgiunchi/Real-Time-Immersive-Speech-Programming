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
        start(["orchestrator/continuous_monitor.js"], "continuous activity monitor");
        await wait(1000);
        await run(["mcp/unity_scene_bridge/smoketest_client.mjs"]);
        const allArtifacts = readJsonLines(process.env.AGENTICXR_ARTIFACT_LOG);
        assert.ok(allArtifacts.some((entry) => entry.eventType === "activity_assist_triggered"),
            "long-lived monitor must turn the mock sensor stream into an activity trigger");
        assert.ok(allArtifacts.some((entry) =>
            entry.eventType === "activity_assist_suppressed" &&
            entry.reasonCode === "continuous_assist_disabled"),
        "monitor-only mode must not start a proactive model turn");
        const exported = buildStudyExports(allArtifacts);
        assert.strictEqual(exported.trialRows.length, 2, "mock pipeline must export exactly two study trials (one per condition arm)");
        const row = exported.trialRows.find((trial) => trial.condition === "agenticxr_verification");
        const bypassRow = exported.trialRows.find((trial) => trial.condition === "agenticxr_no_verification");
        assert.ok(row, "verification-arm trial row exists");
        assert.ok(bypassRow, "no-verification-arm trial row exists");
        for (const column of TRIAL_COLUMNS) assert.ok(Object.hasOwn(row, column), `missing study export column ${column}`);
        assert.strictEqual(row.participantId, "mock-participant-001");
        assert.strictEqual(row.taskCompletion, true);
        assert.ok(row.generatedArtifactCount >= 1);
        assert.ok(row.candidatesGenerated >= 3);
        assert.ok(Number.isFinite(row.validatedExecutionLatencyMs));
        assert.ok(Number.isFinite(row.memoryRetrievalLatencyMeanMs));
        assert.ok(row.agentStatusMessageCount >= 1);
        assert.ok(row.verificationApplyCount >= 1, "verification arm must carry dry-run apply evidence");
        assert.strictEqual(row.verificationBypassedCount, 0, "verification arm must have no bypassed dry-runs");
        // H2 contract: the same intent produced a committed proposal in both arms,
        // differing only in dry-run evidence and validation-state marking.
        assert.strictEqual(bypassRow.taskCompletion, true);
        assert.ok(Number.isFinite(bypassRow.validatedExecutionLatencyMs), "bypass arm still reaches validated execution");
        assert.strictEqual(bypassRow.verificationApplyCount, 0, "bypass arm must have no dry-run apply evidence");
        assert.ok(bypassRow.verificationBypassedCount >= 2, "bypass arm records skips on simulate and proposal");
        assert.strictEqual(bypassRow.candidateTargetCount, 1, "H4 N=1 switch is stamped on the bypass trial");
        assert.strictEqual(bypassRow.candidatesGenerated, 1);
        assert.strictEqual(bypassRow.selectedCandidateRank, 1, "the lone candidate's surfaced rank is logged");
        assert.ok(bypassRow.agentStatusMessageCount >= 1, "status visibility is unchanged in the bypass arm");
        console.log(`[mock_integration] study export PASS (${TRIAL_COLUMNS.length} trial columns, both condition arms)`);
        await run(["mcp/unity_scene_bridge/cache_test_flow.mjs"]);
        console.log("[mock_integration] PASS");
    } finally {
        cleanup();
    }
})().catch((error) => {
    console.error(`[mock_integration] FAIL: ${error.message}`);
    process.exitCode = 1;
});
