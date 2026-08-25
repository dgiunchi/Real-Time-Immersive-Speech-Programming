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
const { start: startDiscovery } = require("./discovery-responder");

const UBIQ_PORT = 8009;

try {
    startDiscovery({ ubiqPort: UBIQ_PORT });
} catch (error) {
    console.log(`[discovery] unavailable, continuing without it: ${error.message}`);
}

// Loaded after discovery so a discovery failure cannot stop the room server.
require(path.join(__dirname, "..", "node_modules", "ubiq", "app.js"));

// The room server only relays. StudyDebugLauncher sends its trial registration
// on Ubiq channel 100 and waits for Node to acknowledge, and it is the Unity
// Scene Bridge that joins the room and answers. Without it the headset connects
// to the room and then times out with "Server did not acknowledge the trial",
// which reads like a networking problem and is not one.
//
// Started as a child process rather than required, because the bridge chdir's
// into its own directory and speaks MCP on stdio, neither of which should happen
// inside this process.
const { spawn } = require("child_process");

setTimeout(() => {
    const bridge = spawn(process.execPath, [path.join(__dirname, "start-unity-scene-bridge.js")], {
        cwd: path.join(__dirname, ".."),
        env: process.env,
        stdio: ["ignore", "pipe", "pipe"],
    });
    bridge.stdout.on("data", (chunk) => process.stdout.write(`[bridge] ${chunk}`));
    bridge.stderr.on("data", (chunk) => process.stdout.write(`[bridge] ${chunk}`));
    bridge.on("exit", (code) => console.log(`[bridge] exited with code ${code}`));
    process.on("exit", () => { try { bridge.kill(); } catch { /* already gone */ } });
    // Delayed so the room server is listening before the bridge tries to join it;
    // the bridge joins an existing room and does not host one.
}, 2000);
