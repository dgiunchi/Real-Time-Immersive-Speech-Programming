"use strict";

const fs = require("fs");
const path = require("path");

require("./load-local-env");

const serverRoot = path.resolve(__dirname, "..");
const samplesRoot = path.join(serverRoot, "samples");
const venvPython = process.platform === "win32"
    ? path.join(samplesRoot, "venv", "Scripts", "python.exe")
    : path.join(samplesRoot, "venv", "bin", "python");
const venvConfig = path.join(samplesRoot, "venv", "pyvenv.cfg");

const checks = [];
const agenticMode = (process.env.AGENTICXR_MODE || "legacy").toLowerCase() === "claude";

function addCheck(ok, title, details) {
    checks.push({ ok, title, details });
}

function pathExists(target) {
    return fs.existsSync(target);
}

function canResolve(moduleName) {
    try {
        require.resolve(moduleName, { paths: [serverRoot] });
        return true;
    } catch (error) {
        return false;
    }
}

addCheck(
    canResolve("ubiq/ubiq"),
    "Node dependency `ubiq`",
    [
        "The server bootstraps the local room server from `node_modules/ubiq/app.js`.",
        "This repository now vendors the historical UCL-VR `Node` package under `Server/vendor/ubiq`.",
        "If `npm install` did not materialize `node_modules/ubiq`, remove `node_modules` and reinstall from the `Server` folder."
    ]
);

addCheck(
    canResolve("nconf"),
    "Node dependency `nconf`",
    [
        "Sample apps and `components/application.js` call `require(\"nconf\")` directly.",
        "This package must exist at the top-level `Server/node_modules`."
    ]
);

if (agenticMode) {
    addCheck(
        canResolve("@anthropic-ai/claude-agent-sdk") &&
            canResolve("@modelcontextprotocol/sdk/server/mcp.js") &&
            canResolve("zod"),
        "Agentic orchestration dependencies",
        [
            "Requires the Claude Agent SDK, MCP SDK, and Zod from Server/package.json.",
            "Run npm install from Server if any package is missing."
        ]
    );
    addCheck(
        Boolean(process.env.ANTHROPIC_API_KEY),
        "Environment variable `ANTHROPIC_API_KEY`",
        [
            "Required by the Claude Agent SDK orchestrator.",
            "Set it only in the terminal that starts the server; do not put it in config.json."
        ]
    );
} else {
    addCheck(
        pathExists(venvPython) && pathExists(venvConfig),
        "Python virtual environment",
        [
            "Required only by the legacy OpenAI comparison path.",
            "Create it under Server/samples/venv and install samples/requirements.txt."
        ]
    );
    addCheck(
        Boolean(process.env.OPENAI_API_KEY),
        "Environment variable `OPENAI_API_KEY`",
        ["Required by the legacy OpenAI code-generation comparison path."]
    );
}

if (agenticMode) {
    const bridgeConfigPath = path.join(serverRoot, "mcp", "unity_scene_bridge", "config.json");
    const runtimeConfigPath = path.join(serverRoot, "samples", "apps", "code_runtime_generator", "config.json");
    let matchingRoom = false;
    try {
        const bridgeConfig = JSON.parse(fs.readFileSync(bridgeConfigPath, "utf8"));
        const runtimeConfig = JSON.parse(fs.readFileSync(runtimeConfigPath, "utf8"));
        matchingRoom = Boolean(bridgeConfig.roomGuid) &&
            bridgeConfig.roomGuid === runtimeConfig.roomGuid &&
            bridgeConfig.roomserver && runtimeConfig.roomserver &&
            bridgeConfig.roomserver.tcp.port === runtimeConfig.roomserver.tcp.port;
    } catch (_) { matchingRoom = false; }
    addCheck(
        matchingRoom,
        "Ubiq runtime/bridge room configuration",
        [
            "The speech runtime and continuous scene bridge must use the same roomGuid and TCP port.",
            "Current expected TCP port is 8009."
        ]
    );
}

addCheck(
    Boolean(process.env.STT_HTTP_URL),
    "Environment variable `STT_HTTP_URL`",
    [
        "Required for speech input. The server deliberately has no hardcoded remote fallback.",
        "Set it to a Faster Whisper-compatible /stt/transcribe endpoint reachable from this PC."
    ]
);

const failed = checks.filter((check) => !check.ok);

console.log("Local setup check for Server");
console.log("");

for (const check of checks) {
    console.log(`${check.ok ? "[OK]" : "[MISSING]"} ${check.title}`);
    for (const line of check.details) {
        console.log(`  - ${line}`);
    }
    console.log("");
}

if (failed.length > 0) {
    console.error(`Setup is incomplete: ${failed.length} check(s) failed.`);
    process.exit(1);
}

console.log("Setup looks complete.");
