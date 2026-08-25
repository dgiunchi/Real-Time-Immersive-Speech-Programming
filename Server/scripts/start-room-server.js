"use strict";

// Starts the Ubiq room server together with the LAN discovery responder, so a
// standalone headset on the same network finds this machine without anyone
// typing an address.
//
//   npm run start:room
//
// The discovery responder is best effort: if it cannot bind, the room server
// still runs and the headset can be pointed at a host explicitly.

const path = require("path");
require("./load-local-env");
const { start: startDiscovery } = require("./discovery-responder");

const UBIQ_PORT = 8009;

try {
    startDiscovery({ ubiqPort: UBIQ_PORT });
} catch (error) {
    console.log(`[discovery] unavailable, continuing without it: ${error.message}`);
}

// The room server is NOT started here. code_runtime_generator hosts its own on
// 8009 and 8010, so starting one first makes the second collide and die, which
// presents as a server that looks up while nothing answers a trial.

// The room server only relays. StudyDebugLauncher sends its trial registration
// on Ubiq channel 100 as a StudyTrialStartRequest and waits for Node to
// acknowledge it. That message type is handled in exactly one place,
// samples/apps/code_runtime_generator/app.js, not in the Unity Scene Bridge.
//
// Starting the bridge alone therefore produces a headset that joins the room and
// then times out with "Server did not acknowledge the trial within 10 seconds",
// which reads like a networking fault when nothing is listening for the message.
//
// Started as a child process rather than required, because the app chdir's into
// its own directory, which should not happen inside this process.
const { spawn } = require("child_process");

function startChild(label, script, extraEnv) {
    const child = spawn(process.execPath, [path.join(__dirname, script)], {
        cwd: path.join(__dirname, ".."),
        env: { ...process.env, ...extraEnv },
        stdio: ["ignore", "pipe", "pipe"],
    });
    child.stdout.on("data", (chunk) => process.stdout.write(`[${label}] ${chunk}`));
    child.stderr.on("data", (chunk) => process.stdout.write(`[${label}] ${chunk}`));
    child.on("exit", (code) => console.log(`[${label}] exited with code ${code}`));
    process.on("exit", () => { try { child.kill(); } catch { /* already gone */ } });
    return child;
}

// Delayed so the room server is listening first; these join an existing room and
// do not host one.
setTimeout(() => {
    // Speech capture needs an endpoint or the service refuses to start. Defaults
    // to the shared Faster Whisper host so a session does not require it to be
    // set by hand every time.
    const sttUrl = process.env.STT_HTTP_URL || "http://130.136.2.161:50101/stt/transcribe";

    // AgenticXR conditions are refused by the baseline runtime with "AgenticXR
    // conditions require npm run start:agenticxr", so start the agentic path when
    // a key is available and fall back to the baseline runtime when it is not.
    // Both host their own Ubiq room server, so only one may run.
    if (process.env.ANTHROPIC_API_KEY) {
        console.log("[startup] ANTHROPIC_API_KEY found, starting the AgenticXR runtime");
        startChild("agenticxr", "start-agenticxr.js", { STT_HTTP_URL: sttUrl });
    } else {
        console.log("[startup] no ANTHROPIC_API_KEY, starting the baseline runtime (AgenticXR conditions will be refused)");
        startChild("trials", "start-code-runtime-generator.js", { STT_HTTP_URL: sttUrl });
    }
}, 100);
