"use strict";

const path = require("path");
const { spawn } = require("child_process");

require("./load-local-env");

if (!process.env.ANTHROPIC_API_KEY) {
    console.error("[AgenticXR] ANTHROPIC_API_KEY is missing. Set it in this terminal before starting; do not put it in config.json.");
    process.exit(1);
}

process.env.AGENTICXR_MODE = "claude";
process.env.AGENTICXR_HYBRID_STUDY_RUNTIME = process.env.AGENTICXR_HYBRID_STUDY_RUNTIME || "true";
// The orchestrator refuses live execution unless the operator-reported model
// version exactly matches its pinned model. Keep both defaults in one launch
// path so implicit L1/L2 turns do not stall after context detection.
process.env.AGENTICXR_MODEL_ID = process.env.AGENTICXR_MODEL_ID || "claude-sonnet-4-6";
process.env.AGENTICXR_MODEL_VERSION = process.env.AGENTICXR_MODEL_VERSION || process.env.AGENTICXR_MODEL_ID;
// Keep live acceptance turns responsive. Registered study trials still override
// this per turn through their explicit candidateTarget (for example N=3).
if (!process.env.AGENTICXR_CANDIDATE_COUNT) process.env.AGENTICXR_CANDIDATE_COUNT = "1";
if (!process.env.AGENTICXR_TURN_TIMEOUT_MS) process.env.AGENTICXR_TURN_TIMEOUT_MS = "300000";
// Implicit L1/L2 uses its own monitor watchdog. Keep it aligned with the live
// turn timeout so a successful validation at ~120s is not killed just before
// rank/propose completes.
if (!process.env.AGENTICXR_CONTINUOUS_ASSIST_TIMEOUT_MS) {
    process.env.AGENTICXR_CONTINUOUS_ASSIST_TIMEOUT_MS = process.env.AGENTICXR_TURN_TIMEOUT_MS;
}
console.log(`[AgenticXR] runtime config candidates=${process.env.AGENTICXR_CANDIDATE_COUNT} timeoutMs=${process.env.AGENTICXR_TURN_TIMEOUT_MS}`);
let monitor = null;
let monitorTimer = null;
const startMonitor = () => {
if (String(process.env.AGENTICXR_MONITOR_ENABLED || "true").toLowerCase() !== "false") {
    monitor = spawn(process.execPath, [
        path.resolve(__dirname, "..", "orchestrator", "continuous_monitor.js"),
    ], {
        cwd: path.resolve(__dirname, ".."),
        env: process.env,
        stdio: "inherit",
        windowsHide: true,
    });
    monitor.once("exit", (code) => {
        if (code !== 0) console.error(`[AgenticXR] continuous monitor exited with code ${code}`);
    });
}
};
const stopMonitor = () => {
    if (monitorTimer) clearTimeout(monitorTimer);
    if (monitor && monitor.exitCode == null && !monitor.killed) monitor.kill();
};
process.once("SIGINT", () => { stopMonitor(); process.exit(0); });
process.once("SIGTERM", () => { stopMonitor(); process.exit(0); });
process.once("exit", stopMonitor);
const appDir = path.resolve(__dirname, "..", "samples", "apps", "code_runtime_generator");
process.chdir(appDir);
const { CodeGeneration } = require(path.join(appDir, "app.js"));
new CodeGeneration().start();
// The monitor connects to the RoomServer owned by CodeGeneration. Starting it
// first caused a nondeterministic ECONNREFUSED race on Windows.
monitorTimer = setTimeout(startMonitor, 1500);
