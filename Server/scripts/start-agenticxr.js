"use strict";

const path = require("path");
const { spawn } = require("child_process");

if (!process.env.ANTHROPIC_API_KEY) {
    console.error("[AgenticXR] ANTHROPIC_API_KEY is missing. Set it in this terminal before starting; do not put it in config.json.");
    process.exit(1);
}

process.env.AGENTICXR_MODE = "claude";
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
