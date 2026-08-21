"use strict";

const path = require("path");
const { spawn } = require("child_process");

require("./load-local-env");

if (!process.env.ANTHROPIC_API_KEY) {
    console.error("[AgenticXR] ANTHROPIC_API_KEY is missing. Set it in this terminal before starting; do not put it in config.json.");
    process.exit(1);
}

process.env.AGENTICXR_MODE = "claude";
// Keep live acceptance turns responsive. Registered study trials still override
// this per turn through their explicit candidateTarget (for example N=3).
if (!process.env.AGENTICXR_CANDIDATE_COUNT) process.env.AGENTICXR_CANDIDATE_COUNT = "1";
if (!process.env.AGENTICXR_TURN_TIMEOUT_MS) process.env.AGENTICXR_TURN_TIMEOUT_MS = "300000";
console.log(`[AgenticXR] runtime config candidates=${process.env.AGENTICXR_CANDIDATE_COUNT} timeoutMs=${process.env.AGENTICXR_TURN_TIMEOUT_MS}`);
let monitor = null;
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
const stopMonitor = () => {
    if (monitor && monitor.exitCode == null && !monitor.killed) monitor.kill();
};
process.once("SIGINT", () => { stopMonitor(); process.exit(0); });
process.once("SIGTERM", () => { stopMonitor(); process.exit(0); });
process.once("exit", stopMonitor);
const appDir = path.resolve(__dirname, "..", "samples", "apps", "code_runtime_generator");
process.chdir(appDir);
const { CodeGeneration } = require(path.join(appDir, "app.js"));
new CodeGeneration().start();
