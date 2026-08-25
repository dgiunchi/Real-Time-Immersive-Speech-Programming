"use strict";

// Runs the two build determinism gate that docs/PHYSICAL_EXECUTABILITY_GATE.md
// requires as automated evidence.
//
// The gate says two consecutive batch mode builds must be byte identical. Doing
// that by hand is easy to get wrong in a way that produces a false PASS:
//
//   1. Unity's exit status is lost if its output is piped, so a build that never
//      launched reports success.
//   2. If the build did not run, the scene file is simply unchanged, so hashing
//      it before and after "matches" and the gate appears to pass.
//   3. A different 6000.3.x editor will happily open the project and produce a
//      scene, but that scene is not evidence for the pinned method version.
//
// This script fails closed on all three. It checks Unity's real exit code, it
// deletes the scene so a non running build cannot masquerade as a stable one,
// and it refuses a mismatched editor unless explicitly overridden, in which case
// the result is marked as not gate valid.

const { spawnSync } = require("child_process");
const crypto = require("crypto");
const fs = require("fs");
const os = require("os");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const UNITY_PROJECT = path.join(REPO_ROOT, "Unity");
const SCENE_PATH = path.join(UNITY_PROJECT, "Assets", "Scenes", "AgenticXRStudy.unity");
const PROJECT_VERSION = path.join(UNITY_PROJECT, "ProjectSettings", "ProjectVersion.txt");
const BUILD_METHOD = "AgenticXRStudySceneBuilder.BuildStudyScene";

function pinnedEditorVersion() {
    const text = fs.readFileSync(PROJECT_VERSION, "utf8");
    const match = /m_EditorVersion:\s*(\S+)/.exec(text);
    if (!match) throw new Error(`cannot read m_EditorVersion from ${PROJECT_VERSION}`);
    return match[1];
}

function editorBase() {
    if (process.platform === "darwin") return "/Applications/Unity/Hub/Editor";
    if (process.platform === "win32") return "C:\\Program Files\\Unity\\Hub\\Editor";
    return path.join(os.homedir(), "Unity/Hub/Editor");
}

function binaryFor(dir) {
    if (process.platform === "darwin") return path.join(dir, "Unity.app/Contents/MacOS/Unity");
    if (process.platform === "win32") return path.join(dir, "Editor", "Unity.exe");
    return path.join(dir, "Editor", "Unity");
}

// Unity Hub installs the Apple Silicon editor under a "-arm64" suffixed directory,
// so the pinned version can be present under more than one directory name. They
// are the same editor version; only the installed modules differ. Prefer a variant
// that actually has build modules, since an editor missing the project's build
// target can prompt a platform switch on open.
function installLooksComplete(dir) {
    if (process.platform !== "darwin") return true;
    const frameworks = path.join(dir, "Unity.app/Contents/Frameworks");
    try {
        return fs.readdirSync(frameworks).includes("libRadeonRays.dylib");
    } catch {
        return false;
    }
}

function editorPath(version) {
    const base = editorBase();
    const candidates = [version, `${version}-arm64`, `${version}-x86_64`]
        .map((name) => path.join(base, name))
        .filter((dir) => fs.existsSync(binaryFor(dir)))
        // An interrupted Hub download leaves the binary and the modules in place
        // while the editor frameworks are incomplete. Unity then dies at dynamic
        // link time before it can write a log file, which reads as a null exit
        // status and an empty log rather than as a broken install. Skip those.
        .filter((dir) => installLooksComplete(dir));
    if (candidates.length === 0) return path.join(base, version, process.platform === "darwin" ? "Unity.app/Contents/MacOS/Unity" : "Unity");
    const withModules = candidates.find((dir) => {
        try {
            return fs.readdirSync(path.join(dir, "PlaybackEngines")).length > 0;
        } catch {
            return false;
        }
    });
    return binaryFor(withModules || candidates[0]);
}

function installedEditors() {
    const base = process.platform === "darwin"
        ? "/Applications/Unity/Hub/Editor"
        : process.platform === "win32"
            ? "C:\\Program Files\\Unity\\Hub\\Editor"
            : path.join(os.homedir(), "Unity/Hub/Editor");
    try {
        return fs.readdirSync(base).filter((name) => !name.startsWith("."));
    } catch {
        return [];
    }
}

function sha256(filePath) {
    return crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex");
}

function gitShaOfScene() {
    const shown = spawnSync("git", ["show", `HEAD:Unity/Assets/Scenes/AgenticXRStudy.unity`], {
        cwd: REPO_ROOT, encoding: "buffer", maxBuffer: 256 * 1024 * 1024,
    });
    if (shown.status !== 0) return null;
    return crypto.createHash("sha256").update(shown.stdout).digest("hex");
}

function restoreSceneFromGit() {
    return spawnSync("git", ["checkout", "--", "Unity/Assets/Scenes/AgenticXRStudy.unity"], {
        cwd: REPO_ROOT, encoding: "utf8",
    }).status === 0;
}

// Runs Unity and returns its real exit status. Output goes to a log file rather
// than a pipe, precisely so the status is not swallowed.
function runBuild(unityBinary, logFile) {
    const result = spawnSync(unityBinary, [
        "-batchmode", "-nographics", "-quit",
        "-projectPath", UNITY_PROJECT,
        "-executeMethod", BUILD_METHOD,
        "-logFile", logFile,
    ], { encoding: "utf8", stdio: ["ignore", "ignore", "ignore"], timeout: 30 * 60 * 1000 });
    return {
        status: result.status,
        timedOut: result.error && result.error.code === "ETIMEDOUT",
        error: result.error ? String(result.error.message) : null,
    };
}

function compileErrors(logFile) {
    if (!fs.existsSync(logFile)) return null;
    const text = fs.readFileSync(logFile, "utf8");
    return (text.match(/error CS\d+/g) || []).length;
}

function main() {
    const args = process.argv.slice(2);
    const allowMismatch = args.includes("--allow-version-mismatch");
    const logDir = fs.mkdtempSync(path.join(os.tmpdir(), "agenticxr-determinism-"));

    const pinned = pinnedEditorVersion();
    const binary = editorPath(pinned);
    const checks = [];
    const result = { ok: false, gateValid: false, pinnedEditorVersion: pinned, logDir, checks };

    const editorPresent = fs.existsSync(binary);
    checks.push({
        id: "pinned-editor-installed",
        ok: editorPresent,
        expected: binary,
        actual: editorPresent ? "present" : `missing; installed: ${installedEditors().join(", ") || "none"}`,
    });

    if (!editorPresent && !allowMismatch) {
        result.remedy = `Install Unity ${pinned} through Unity Hub. Another 6000.3.x will open the project but its output is not evidence for this method version. Re-run, or pass --allow-version-mismatch to produce a non gate valid result.`;
        console.log(JSON.stringify(result, null, 2));
        process.exit(1);
    }

    const committedSha = gitShaOfScene();
    const beforeSha = fs.existsSync(SCENE_PATH) ? sha256(SCENE_PATH) : null;

    // Delete the scene so a build that does not run cannot pass by leaving the
    // previous file in place.
    if (fs.existsSync(SCENE_PATH)) fs.unlinkSync(SCENE_PATH);

    const buildOne = runBuild(binary, path.join(logDir, "build1.log"));
    checks.push({
        id: "build-1-exit-status",
        ok: buildOne.status === 0,
        expected: 0,
        actual: buildOne.timedOut ? "timed out after 30m" : (buildOne.error || buildOne.status),
    });
    checks.push({
        id: "build-1-produced-scene",
        ok: fs.existsSync(SCENE_PATH),
        expected: "scene written",
        actual: fs.existsSync(SCENE_PATH) ? "written" : "absent",
    });

    if (!fs.existsSync(SCENE_PATH)) {
        const restored = restoreSceneFromGit();
        checks.push({ id: "scene-restored-after-failure", ok: restored, expected: "restored from git", actual: restored ? "restored" : "FAILED, restore by hand" });
        result.remedy = `Build 1 did not produce a scene. Inspect ${path.join(logDir, "build1.log")}.`;
        console.log(JSON.stringify(result, null, 2));
        process.exit(1);
    }

    const firstSha = sha256(SCENE_PATH);
    const errorsOne = compileErrors(path.join(logDir, "build1.log"));
    checks.push({ id: "build-1-no-compile-errors", ok: errorsOne === 0, expected: 0, actual: errorsOne });

    fs.unlinkSync(SCENE_PATH);
    const buildTwo = runBuild(binary, path.join(logDir, "build2.log"));
    checks.push({
        id: "build-2-exit-status",
        ok: buildTwo.status === 0,
        expected: 0,
        actual: buildTwo.timedOut ? "timed out after 30m" : (buildTwo.error || buildTwo.status),
    });

    if (!fs.existsSync(SCENE_PATH)) {
        const restored = restoreSceneFromGit();
        checks.push({ id: "build-2-produced-scene", ok: false, expected: "scene written", actual: "absent" });
        checks.push({ id: "scene-restored-after-failure", ok: restored, expected: "restored from git", actual: restored ? "restored" : "FAILED, restore by hand" });
        result.remedy = `Build 2 did not produce a scene. Inspect ${path.join(logDir, "build2.log")}.`;
        console.log(JSON.stringify(result, null, 2));
        process.exit(1);
    }

    const secondSha = sha256(SCENE_PATH);

    checks.push({ id: "two-builds-byte-identical", ok: firstSha === secondSha, expected: firstSha, actual: secondSha });
    checks.push({
        id: "matches-committed-scene",
        ok: committedSha === null ? false : secondSha === committedSha,
        expected: committedSha || "no committed scene found",
        actual: secondSha,
    });

    result.shas = { committed: committedSha, beforeRun: beforeSha, build1: firstSha, build2: secondSha };
    result.ok = checks.every((check) => check.ok);
    result.gateValid = result.ok && editorPresent && !allowMismatch;
    if (!result.gateValid && result.ok) {
        result.note = "Builds agree, but the editor version was overridden, so this is not valid gate evidence.";
    }

    console.log(JSON.stringify(result, null, 2));
    process.exit(result.ok ? 0 : 1);
}

if (require.main === module) main();

module.exports = { pinnedEditorVersion, editorPath, installedEditors, sha256 };
