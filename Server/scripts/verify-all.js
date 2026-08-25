"use strict";

// One command that runs every check this machine can run, and says plainly which
// passed. Node suites, the mock integration, the synthetic pilot, task readiness,
// and the Unity runtime compile-and-attach smoke test.
//
// Nothing here needs an API key, a headset, or a network beyond localhost.
//
//   npm run verify:all                    run everything available
//   npm run verify:all -- --node          Node only, about 50 seconds
//   npm run verify:all -- --unity-only    Unity only
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

function readEditorVersion(text) {
    const match = /m_EditorVersion:\s*(\S+)/.exec(text || "");
    return match ? match[1] : null;
}

function pinnedEditorVersion() {
    return readEditorVersion(fs.readFileSync(PROJECT_VERSION, "utf8"));
}

// Opening the project with a newer editor silently rewrites ProjectVersion.txt,
// so "the pinned version" quietly becomes whatever was last used. Every check
// afterwards then reports success against the wrong editor, and committing that
// file loses the pin for everyone. Compare the working tree against what is
// committed and say so loudly.
function committedEditorVersion() {
    const shown = spawnSync("git", ["show", "HEAD:Unity/ProjectSettings/ProjectVersion.txt"],
        { cwd: SERVER, encoding: "utf8" });
    return shown.status === 0 ? readEditorVersion(shown.stdout) : null;
}

function runUnitySmokeTest() {
    const pinned = pinnedEditorVersion();
    const committed = committedEditorVersion();
    const override = process.env.AGENTICXR_UNITY_VERSION_OVERRIDE;

    if (committed && pinned !== committed) {
        record("unity editor pin is intact", false,
            `ProjectVersion.txt says ${pinned} but ${committed} is committed; a newer editor rewrote it. ` +
            "Restore it with: git checkout -- Unity/ProjectSettings/ProjectVersion.txt");
        return false;
    }
    record("unity editor pin is intact", true, `${pinned}`);

    const binary = editorPath(pinned);

    // .../<editor>/Unity.app/Contents/MacOS/Unity -> .../<editor>
    const editorDir = process.platform === "darwin"
        ? path.dirname(path.dirname(path.dirname(path.dirname(binary))))
        : path.dirname(path.dirname(binary));

    if (!fs.existsSync(binary)) {
        record("unity runtime compile and attach", null,
            `editor ${override || pinned} is not installed; install it, or set AGENTICXR_UNITY_VERSION_OVERRIDE to one that is`);
        return null;
    }
    if (!installLooksComplete(editorDir)) {
        record("unity runtime compile and attach", null,
            `the install at ${editorDir} is incomplete; reinstall it through Unity Hub`);
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

// A skipped check is not a passed check. Reporting "all checks passed" when
// everything was skipped is the same false confidence this script exists to
// prevent elsewhere, so a skip is always named and never counted as success.
function summarise() {
    const failed = results.filter((item) => item.ok === false);
    const passed = results.filter((item) => item.ok === true);
    const skipped = results.filter((item) => item.ok === null);

    console.log("");
    if (failed.length > 0) {
        console.log(`${failed.length} CHECK(S) FAILED, ${passed.length} passed${skipped.length ? `, ${skipped.length} skipped` : ""}`);
    } else if (passed.length === 0) {
        console.log(`NOTHING WAS VERIFIED: every check was skipped (${skipped.length})`);
    } else if (skipped.length > 0) {
        console.log(`${passed.length} passed, ${skipped.length} SKIPPED, so this is not a full verification`);
        for (const item of skipped) console.log(`  skipped: ${item.name} (${item.detail})`);
    } else {
        console.log(`ALL ${passed.length} CHECKS PASSED`);
    }
    // A skip is not a failure, but a run where nothing ran must not exit 0.
    process.exit(failed.length === 0 && passed.length > 0 ? 0 : 1);
}

function main() {
    const nodeOnly = process.argv.includes("--node");
    const unityOnly = process.argv.includes("--unity-only");
    console.log("AgenticXR verification\n");

    if (unityOnly) {
        console.log("Unity");
        runUnitySmokeTest();
        summarise();
    }

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

    summarise();
}

if (require.main === module) main();
