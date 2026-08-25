"use strict";

// Proves that every provider is offered the same agent tool surface, read from
// the live MCP bridge rather than from a hand-written list.

const assert = require("assert");
const path = require("path");
const net = require("net");
const { spawn } = require("child_process");
const surface = require("../orchestrator/providers/tool_surface");

const SERVER_ROOT = path.join(__dirname, "..");

// The bridge joins an existing Ubiq room rather than hosting one, so the test
// brings a room server up itself, the same way tests/run_mock_integration.js
// does. Without this the bridge exits before it can advertise its tools.
function startRoomServer() {
    return spawn(process.execPath, ["node_modules/ubiq/app.js", "config/default.json"],
        { cwd: SERVER_ROOT, stdio: "ignore", windowsHide: true });
}

function waitForPort(port, timeoutMs = 15000) {
    const deadline = Date.now() + timeoutMs;
    return new Promise((resolve, reject) => {
        const attempt = () => {
            const socket = net.connect({ host: "127.0.0.1", port }, () => {
                socket.end();
                resolve();
            });
            socket.on("error", () => {
                socket.destroy();
                if (Date.now() > deadline) reject(new Error(`port ${port} did not open`));
                else setTimeout(attempt, 250);
            });
        };
        attempt();
    });
}

let assertions = 0;
function check(condition, message) {
    assert.ok(condition, `FAILED: ${message}`);
    assertions += 1;
}

(async () => {
    const roomServer = startRoomServer();
    process.on("exit", () => roomServer.kill());
    let tools;
    try {
        await waitForPort(8009);
        tools = await surface.listBridgeTools();
    } catch (error) {
        roomServer.kill();
        throw error;
    }

    check(tools.length > 0, "the MCP bridge advertises at least one tool");
    check(tools.length >= 20, `the bridge advertises the expected agent surface (got ${tools.length})`);
    check(new Set(tools.map((t) => t.name)).size === tools.length, "tool names are unique");
    for (const tool of tools) {
        check(typeof tool.name === "string" && tool.name.length > 0, `tool has a name`);
        check(tool.inputSchema && typeof tool.inputSchema === "object", `${tool.name} publishes an input schema`);
    }

    // Tools the study depends on must be present by name, so a rename is caught
    // here rather than by a backend silently losing a capability.
    for (const required of ["query_scene", "propose_artifact", "simulate_artifact", "request_commit", "get_person_policy"]) {
        check(tools.some((t) => t.name === required), `surface includes ${required}`);
    }

    const fingerprint = surface.toolSurfaceFingerprint(tools);
    check(/^[0-9a-f]{64}$/.test(fingerprint), "fingerprint is a sha256 hex digest");
    check(surface.toolSurfaceFingerprint([...tools].reverse()) === fingerprint,
        "fingerprint is order independent");

    // Key order inside a schema must not change the fingerprint, since JSON
    // Schema does not guarantee it.
    const reordered = tools.map((tool) => ({
        ...tool,
        inputSchema: Object.fromEntries(Object.entries(tool.inputSchema).reverse()),
    }));
    check(surface.toolSurfaceFingerprint(reordered) === fingerprint,
        "fingerprint ignores schema key order");

    // Tamper detection: any real change to the surface must move the fingerprint.
    const renamed = tools.map((t, i) => (i === 0 ? { ...t, name: `${t.name}_renamed` } : t));
    check(surface.toolSurfaceFingerprint(renamed) !== fingerprint, "renaming a tool changes the fingerprint");
    const dropped = tools.slice(1);
    check(surface.toolSurfaceFingerprint(dropped) !== fingerprint, "dropping a tool changes the fingerprint");
    const retyped = tools.map((t, i) => (i === 0 ? { ...t, inputSchema: { type: "object", properties: { injected: { type: "string" } } } } : t));
    check(surface.toolSurfaceFingerprint(retyped) !== fingerprint, "changing a tool schema changes the fingerprint");

    // Every registered provider must render the identical set of tool names.
    for (const providerId of Object.keys(surface.PROVIDER_RENDERERS)) {
        const rendered = surface.renderFor(providerId, tools);
        check(rendered.length === tools.length, `${providerId} renders every tool`);
        const preserved = surface.assertRenderingPreservesNames(tools, rendered, providerId);
        check(preserved.ok, `${providerId} preserves tool names (missing ${preserved.missing}, added ${preserved.added})`);
    }

    // OpenAI shape.
    const openai = surface.toOpenAIFunctions(tools);
    check(openai.every((entry) => entry.type === "function"), "openai entries declare type function");
    check(openai.every((entry) => entry.function && entry.function.parameters), "openai entries carry parameters");

    // Gemini shape: unsupported JSON Schema keywords are stripped, but only
    // those. Names and the presence of parameters must survive.
    const gemini = surface.toGeminiFunctionDeclarations(tools);
    check(gemini.every((entry) => typeof entry.name === "string"), "gemini entries carry a name");
    check(gemini.every((entry) => entry.parameters && typeof entry.parameters === "object"), "gemini entries carry parameters");
    const serialisedGemini = JSON.stringify(gemini);
    for (const keyword of ["$schema", "additionalProperties", "$ref"]) {
        check(!serialisedGemini.includes(`"${keyword}"`), `gemini rendering strips ${keyword}`);
    }

    // An unregistered provider must be refused rather than silently defaulting.
    let refused = false;
    try {
        surface.renderFor("mistral", tools);
    } catch {
        refused = true;
    }
    check(refused, "an unregistered provider is refused rather than defaulted");

    roomServer.kill();
    console.log(`[provider_tool_surface_test] PASS (${assertions} assertions, ${tools.length} tools)`);
})().catch((error) => {
    console.error(error);
    process.exit(1);
});
