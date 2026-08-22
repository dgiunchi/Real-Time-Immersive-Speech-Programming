"use strict";

const fs = require("fs");
const path = require("path");

const STUDY_TRANSITION_CALLBACK = /^(?:StartStudyTrial|EndStudyTrial|AdvanceStudyTrial|ConfirmStudyTransition|ResetStudyTrial)$/;

function walk(directory, files = []) {
    if (!fs.existsSync(directory)) return files;
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const full = path.join(directory, entry.name);
        if (entry.isDirectory()) walk(full, files);
        else if (/\.(?:unity|prefab)$/i.test(entry.name)) files.push(full);
    }
    return files;
}

function scanStudyControls({ repositoryRoot } = {}) {
    const root = repositoryRoot || path.resolve(__dirname, "..", "..");
    const unityAssets = path.join(root, "Unity", "Assets");
    const files = walk(unityAssets);
    const callbacks = [];
    for (const file of files) {
        const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);
        for (let index = 0; index < lines.length; index += 1) {
            const match = lines[index].match(/m_MethodName:\s*(\S+)/);
            if (!match || !STUDY_TRANSITION_CALLBACK.test(match[1])) continue;
            const nearby = lines.slice(Math.max(0, index - 12), index + 1);
            const targetLine = [...nearby].reverse().find((line) => /m_Target:/.test(line)) || "unknown-target";
            callbacks.push({ file: path.relative(root, file), line: index + 1, method: match[1], target: targetLine.trim() });
        }
    }
    const counts = new Map();
    for (const callback of callbacks) {
        const key = `${callback.file}|${callback.target}|${callback.method}`;
        counts.set(key, (counts.get(key) || 0) + 1);
    }
    const multiplyBound = callbacks.filter((callback) =>
        counts.get(`${callback.file}|${callback.target}|${callback.method}`) > 1);
    const csharp = walkSources(path.join(unityAssets), []);
    const outOfBand = callbacks.filter((callback) => {
        const source = csharp.find((item) => item.text.includes(`${callback.method}(`));
        if (!source) return true;
        const at = source.text.indexOf(`${callback.method}(`);
        const methodRegion = source.text.slice(at, at + 2500);
        return !/StudySessionMachine|study session machine|interaction_state_transition/i.test(methodRegion);
    });
    return { ok: multiplyBound.length === 0 && outOfBand.length === 0,
        scannedFiles: files.length, callbacks, multiplyBound, outOfBand };
}

function walkSources(directory, files) {
    if (!fs.existsSync(directory)) return files;
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const full = path.join(directory, entry.name);
        if (entry.isDirectory()) walkSources(full, files);
        else if (/\.cs$/i.test(entry.name)) files.push({ file: full, text: fs.readFileSync(full, "utf8") });
    }
    return files;
}

module.exports = { STUDY_TRANSITION_CALLBACK, scanStudyControls };
