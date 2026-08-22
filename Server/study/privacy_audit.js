"use strict";

const fs = require("fs");
const path = require("path");

const DEFAULT_ROOT = path.resolve(__dirname, "..");

function auditTranscriptPrivacy({ serverRoot = DEFAULT_ROOT } = {}) {
    const files = {
        runtime: path.join(serverRoot, "samples", "apps", "code_runtime_generator", "app.js"),
        stt: path.join(serverRoot, "samples", "services", "speech_to_text", "service.js"),
        orchestrator: path.join(serverRoot, "orchestrator", "app.js"),
    };
    const source = Object.fromEntries(Object.entries(files).map(([name, file]) => [name, fs.readFileSync(file, "utf8")]));
    const checks = [
        { id: "runtime-debug-gate", ok: source.runtime.includes("if (DEBUG_TRANSCRIPTS)") &&
            source.runtime.includes("debug transcript appended characters=") },
        { id: "runtime-default-sanitised", ok: source.runtime.includes("baseline transcript ready peer=${peerName} characters=${response.length}") },
        { id: "generated-output-gate", ok: source.runtime.includes('if (DEBUG_TRANSCRIPTS) console.log(" -> Code:: " + response)') &&
            source.runtime.includes("generated model output characters=${response.length}") },
        { id: "stt-debug-gate", ok: source.stt.includes("if (debugTranscripts)") &&
            source.stt.includes("characters=${responseText.length}") },
        { id: "orchestrator-echo-gate", ok: source.orchestrator.includes("if (DEBUG_TRANSCRIPTS) console.log(`[router] ${block.text.trim()}`)") &&
            source.orchestrator.includes("model output received characters=${block.text.trim().length}") },
        { id: "orchestrator-intent-gate", ok: source.orchestrator.includes("if (DEBUG_TRANSCRIPTS) console.log(`[orchestrator] intent:") &&
            source.orchestrator.includes("intent received characters=${intent.length}") },
        { id: "no-raw-transcript-journal-field", ok: !/transcript(?:Text|Wording)\s*:/.test(source.runtime) },
    ];
    return { ok: checks.every((check) => check.ok), checks, files };
}

module.exports = { auditTranscriptPrivacy };
