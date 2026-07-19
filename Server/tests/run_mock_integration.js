"use strict";

const path = require("path");
const { spawn } = require("child_process");

const root = path.resolve(__dirname, "..");
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
        await run(["mcp/unity_scene_bridge/cache_test_flow.mjs"]);
        console.log("[mock_integration] PASS");
    } finally {
        cleanup();
    }
})().catch((error) => {
    console.error(`[mock_integration] FAIL: ${error.message}`);
    process.exitCode = 1;
});
