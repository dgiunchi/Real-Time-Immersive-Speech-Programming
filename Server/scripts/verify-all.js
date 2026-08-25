"use strict";

// One command that runs every check this machine can run, and says plainly which
// passed. Node suites, the mock integration, the synthetic pilot, task readiness,
// and the Unity runtime compile-and-attach smoke test.
//
// Nothing here needs an API key, a headset, or a network beyond localhost.
//
//   npm run verify:all              run everything available
//   npm run verify:all -- --node    skip Unity (fast, a few seconds)
//
// Exits non-zero if anything fails, so it can gate a commit or a handover.

const { spawnSync } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { editorPath, installLooksComplete } = require("./verify-scene-determinism");

const SERVER = path.join(__dirname, "..");
const UNITY_PROJECT = path.join(SERVER, "..", "Unity");
const PROJECT_VERSION = path.join(UNITY_PROJECT, "ProjectSettings", "ProjectVersion.txt");

const results = [];

function record(name, ok, detail) {
    results.push({ name, ok, detail });
    const mark = ok === null ? "SKIP" : ok ? "PASS" : "FAIL";
    console.log(`  ${mark.padEnd(4)}  ${name}${detail ? `  (${detail})` : ""}`);
}

function runNode(name, args) {
    const started = Date.now();
    const result = spawnSync(process.execPath, args, { cwd: SERVER, encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
    const output = `${result.stdout || ""}${result.stderr || ""}`;
    const assertions = [...output.matchAll(/\((\d+) assertions/g)].reduce((sum, m) => sum + Number(m[1]), 0);
    record(name, result.status === 0, `${assertions ? `${assertions} assertions, ` : ""}${((Date.now() - started) / 1000).toFixed(1)}s`);
    return result.status === 0;
}

function pinnedEditorVersion() {
    const match = /m_EditorVersion:\s*(\S+)/.exec(fs.readFileSync(PROJECT_VERSION, "utf8"));
    return match ? match[1] : null;
}

function runUnitySmokeTest() {
    const pinned = pinnedEditorVersion();
    const override = process.env.AGENTICXR_UNITY_VERSION_OVERRIDE;
    const binary = editorPath(pinned);

    if (!fs.existsSync(binary)) {
        record("unity runtime compile and attach", null,
            `no usable editor for ${pinned}; set AGENTICXR_UNITY_VERSION_OVERRIDE to an installed one`);
        return null;
    }
    if (!override && !installLooksComplete(path.dirname(path.dirname(path.dirname(binary))))) {
        record("unity runtime compile and attach", null, "the installed editor looks incomplete");
        return null;
    }

    const logFile = path.join(fs.mkdtempSync(path.join(os.tmpdir(), "agenticxr-verify-")), "smoke.log");
    const started = Date.now();
    // Output goes to a log file, never a pipe, so Unity's real exit status survives.
    const result = spawnSync(binary, [
        "-batchmode", "-nographics", "-quit",
        "-projectPath", UNITY_PROJECT,
        "-executeMethod", "AgenticRuntimeCompilerSmokeTest.Run",
        "-logFile", logFile,
    ], { stdio: ["ignore", "ignore", "ignore"], timeout: 30 * 60 * 1000 });

    const log = fs.existsSync(logFile) ? fs.readFileSync(logFile, "utf8") : "";
    const passed = /\[AgenticRuntimeCompilerSmokeTest\] PASS \((\d+) checks\)/.exec(log);
    const ok = result.status === 0 && Boolean(passed);
    record("unity runtime compile and attach", ok,
        `${passed ? `${passed[1]} checks, ` : ""}${((Date.now() - started) / 1000).toFixed(0)}s${ok ? "" : `, see ${logFile}`}`);
    if (override) console.log(`        note: ran on ${override}, not the pinned ${pinned}`);
    return ok;
}

function main() {
    const nodeOnly = process.argv.includes("--node");
    console.log("AgenticXR verification\n");

    console.log("Node");
    const npm = process.platform === "win32" ? "npm.cmd" : "npm";
    for (const [name, script] of [
        ["deterministic suites", "test"],
        ["mock integration", "test:integration"],
    ]) {
        const started = Date.now();
        const result = spawnSync(npm, ["run", "--silent", script], { cwd: SERVER, encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
        const output = `${result.stdout || ""}${result.stderr || ""}`;
        const assertions = [...output.matchAll(/\((\d+) assertions/g)].reduce((sum, m) => sum + Number(m[1]), 0);
        record(name, result.status === 0, `${assertions ? `${assertions} assertions, ` : ""}${((Date.now() - started) / 1000).toFixed(1)}s`);
    }

    runNode("synthetic 24 participant pilot", ["study/pilot_harness.js"]);
    runNode("static task readiness", ["-e",
        "const r=require('./study/task_readiness').validateTaskReadiness(); if(!r.ok){console.error(JSON.stringify(r).slice(0,600));process.exit(1);} console.log(`(${r.checks.length} checks)`);"]);

    if (!nodeOnly) {
        console.log("\nUnity");
        runUnitySmokeTest();
    } else {
        console.log("\nUnity\n  SKIP  --node was passed");
    }

    const failed = results.filter((item) => item.ok === false);
    const skipped = results.filter((item) => item.ok === null);
    console.log(`\n${failed.length === 0 ? "ALL CHECKS PASSED" : `${failed.length} CHECK(S) FAILED`}` +
        `${skipped.length ? `, ${skipped.length} skipped` : ""}`);
    process.exit(failed.length === 0 ? 0 : 1);
}

if (require.main === module) main();
