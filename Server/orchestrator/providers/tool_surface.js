"use strict";

// The agent tool surface, read from the Unity Scene Bridge MCP server and
// rendered into the shapes different model providers expect.
//
// Why this exists: the Claude path gets its tools by pointing the Agent SDK at
// the MCP server, so the tool surface is never written down anywhere. A second
// provider cannot consume MCP directly, so without this it would need its own
// hand-written copy of the tool list. Two hand-maintained copies drift, and the
// moment they drift the backends are no longer running the same study, which is
// exactly what study/backend_equivalence.js exists to prevent.
//
// So every provider derives its tools from the one MCP server. The surface is
// read once, converted per provider, and fingerprinted so the comparison
// contract can assert that each backend was offered an identical surface.

const path = require("path");
const crypto = require("crypto");

const BRIDGE_SERVER_PATH = path.join(__dirname, "..", "..", "mcp", "unity_scene_bridge", "server.js");

// Environment the bridge needs forwarded, matching orchestrator/app.js.
const FORWARDED_ENV = ["AGENTICXR_EVALUATION_SOURCE", "AGENTICXR_EVALUATION_LOG", "AGENTICXR_ARTIFACT_LOG"];

function forwardedEnv() {
    return Object.fromEntries(
        FORWARDED_ENV.filter((name) => process.env[name]).map((name) => [name, process.env[name]])
    );
}

// Opens a real MCP session against the bridge and returns its advertised tools.
// MCP already publishes JSON Schema, so nothing is re-derived from the zod
// declarations and nothing can silently disagree with what the server accepts.
async function listBridgeTools({ serverPath = BRIDGE_SERVER_PATH } = {}) {
    const { Client } = await import("@modelcontextprotocol/sdk/client/index.js");
    const { StdioClientTransport } = await import("@modelcontextprotocol/sdk/client/stdio.js");

    const transport = new StdioClientTransport({
        command: "node",
        args: [serverPath],
        env: { ...process.env, ...forwardedEnv() },
    });
    const client = new Client({ name: "agenticxr-tool-surface", version: "1.0.0" });
    try {
        await client.connect(transport);
        const listed = await client.listTools();
        return (listed.tools || [])
            .map((tool) => ({
                name: tool.name,
                description: tool.description || "",
                inputSchema: tool.inputSchema || { type: "object", properties: {} },
            }))
            .sort((left, right) => left.name.localeCompare(right.name));
    } finally {
        await client.close().catch(() => {});
    }
}

// Sorts object keys recursively so a fingerprint depends on content rather than
// on key order, which JSON Schema does not guarantee.
function canonical(value) {
    if (Array.isArray(value)) return value.map(canonical);
    if (value && typeof value === "object") {
        return Object.keys(value).sort().reduce((out, key) => {
            out[key] = canonical(value[key]);
            return out;
        }, {});
    }
    return value;
}

// The number study/backend_equivalence.js compares across backends. It covers
// tool names and their full input schemas, so adding, removing, renaming or
// re-typing any tool changes it. Descriptions are included because they are part
// of what the model is told a tool does.
function toolSurfaceFingerprint(tools) {
    const payload = tools
        .map((tool) => ({ name: tool.name, description: tool.description, inputSchema: canonical(tool.inputSchema) }))
        .sort((left, right) => left.name.localeCompare(right.name));
    return crypto.createHash("sha256").update(JSON.stringify(payload)).digest("hex");
}

function toOpenAIFunctions(tools) {
    return tools.map((tool) => ({
        type: "function",
        function: { name: tool.name, description: tool.description, parameters: tool.inputSchema },
    }));
}

// Gemini rejects the JSON Schema keywords below, so they are stripped rather
// than passed through. Stripping is deliberate and asserted in the tests: it
// changes how a schema is expressed, never which tools exist or what they are
// named, so the surface stays comparable.
const GEMINI_UNSUPPORTED = new Set(["$schema", "additionalProperties", "default", "examples", "const", "$ref", "definitions", "$defs"]);

function stripForGemini(schema) {
    if (Array.isArray(schema)) return schema.map(stripForGemini);
    if (!schema || typeof schema !== "object") return schema;
    return Object.entries(schema).reduce((out, [key, value]) => {
        if (GEMINI_UNSUPPORTED.has(key)) return out;
        out[key] = stripForGemini(value);
        return out;
    }, {});
}

function toGeminiFunctionDeclarations(tools) {
    return tools.map((tool) => ({
        name: tool.name,
        description: tool.description,
        parameters: stripForGemini(tool.inputSchema),
    }));
}

// Providers are registered here so a new backend cannot be added without also
// declaring how it renders the surface, which keeps the fingerprint meaningful.
const PROVIDER_RENDERERS = Object.freeze({
    anthropic: (tools) => tools,
    openai: toOpenAIFunctions,
    google: toGeminiFunctionDeclarations,
});

function renderFor(providerId, tools) {
    const renderer = PROVIDER_RENDERERS[providerId];
    if (!renderer) {
        throw new Error(`unknown providerId '${providerId}'; register it in orchestrator/providers/tool_surface.js`);
    }
    return renderer(tools);
}

// Every rendering must preserve the set of tool names. A provider that silently
// drops a tool would still run, and would quietly be a different agent.
function assertRenderingPreservesNames(tools, rendered, providerId) {
    const before = tools.map((tool) => tool.name).sort();
    const after = rendered
        .map((entry) => (entry.function ? entry.function.name : entry.name))
        .sort();
    const missing = before.filter((name) => !after.includes(name));
    const added = after.filter((name) => !before.includes(name));
    return {
        ok: missing.length === 0 && added.length === 0,
        providerId,
        toolCount: before.length,
        missing,
        added,
    };
}

module.exports = {
    BRIDGE_SERVER_PATH,
    PROVIDER_RENDERERS,
    listBridgeTools,
    toolSurfaceFingerprint,
    toOpenAIFunctions,
    toGeminiFunctionDeclarations,
    renderFor,
    assertRenderingPreservesNames,
    canonical,
};
