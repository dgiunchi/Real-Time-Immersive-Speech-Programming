"use strict";

const crypto = require("crypto");
const pin = require("./model_pin.v1.json");
const { SYSTEM_PROMPT, MODEL_ID, CANDIDATE_COUNT } = require("../orchestrator/app");

function runtimeSystemPromptHash() {
    return crypto.createHash("sha256").update(SYSTEM_PROMPT).digest("hex");
}

function validateModelPin({ liveVersionString = process.env.AGENTICXR_MODEL_VERSION } = {}) {
    const checks = [
        { id: "model-id", ok: MODEL_ID === pin.modelId, expected: pin.modelId, actual: MODEL_ID },
        { id: "model-version", ok: liveVersionString === pin.modelVersionString,
            expected: pin.modelVersionString, actual: liveVersionString || "missing AGENTICXR_MODEL_VERSION" },
        { id: "system-prompt-hash", ok: runtimeSystemPromptHash() === pin.systemPromptHash,
            expected: pin.systemPromptHash, actual: runtimeSystemPromptHash() },
        { id: "candidate-count-default", ok: CANDIDATE_COUNT === pin.candidateCountDefault,
            expected: pin.candidateCountDefault, actual: CANDIDATE_COUNT },
    ];
    return { ok: checks.every((check) => check.ok), pin, checks };
}

function trialModelPin() {
    const checked = validateModelPin();
    if (!checked.ok) throw new Error(`model pin mismatch: ${checked.checks.filter((item) => !item.ok).map((item) => item.id).join(", ")}`);
    return {
        modelId: pin.modelId,
        modelVersionString: pin.modelVersionString,
        systemPromptHash: pin.systemPromptHash,
        candidateCountDefault: pin.candidateCountDefault,
        toolsetVersion: pin.toolsetVersion,
    };
}

module.exports = { pin, runtimeSystemPromptHash, validateModelPin, trialModelPin };
