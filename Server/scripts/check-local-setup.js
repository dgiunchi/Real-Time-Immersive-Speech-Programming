"use strict";

const fs = require("fs");
const path = require("path");

const serverRoot = path.resolve(__dirname, "..");
const samplesRoot = path.join(serverRoot, "samples");
const venvPython = process.platform === "win32"
    ? path.join(samplesRoot, "venv", "Scripts", "python.exe")
    : path.join(samplesRoot, "venv", "bin", "python");
const venvConfig = path.join(samplesRoot, "venv", "pyvenv.cfg");

const checks = [];

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

addCheck(
    pathExists(venvPython) && pathExists(venvConfig),
    "Python virtual environment",
    [
        "The code generation and media services spawn Python from `Server/samples/venv`.",
        "For the trimmed `code_runtime_generator` flow, create it with `cd samples`, `py -3.10 -m venv .\\venv`, activate it, run `python -m pip install --upgrade pip setuptools wheel`, then `pip install -r requirements.txt`."
    ]
);

addCheck(
    Boolean(process.env.OPENAI_API_KEY),
    "Environment variable `OPENAI_API_KEY`",
    [
        "Required by the `code_runtime_generator` sample unless the key is hardcoded in config.",
        "Set it in the shell before `npm run start:code-runtime-generator`."
    ]
);

addCheck(
    Boolean(process.env.STT_HTTP_URL),
    "Environment variable `STT_HTTP_URL`",
    [
        "Recommended for local runs of the speech-to-text pipeline.",
        "The current code falls back to `http://130.136.2.161:50101/stt/transcribe`, which may be unreachable from your machine."
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
