"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");
const { deriveImplicitBinaries } = require("./rubric_scoring");

const REVEALING_KEYS = new Set(["participantId", "trialId", "condition", "conditionAlias", "candidateTarget", "h4Arm", "arm", "armLabel"]);
const REVEALING_TOKENS = ["full", "baseline", "nodryrun", "noDryRun", "best-of-3"];

function containsRevealingToken(value) {
    const lower = String(value).toLowerCase();
    return REVEALING_TOKENS.some((token) => lower.includes(token.toLowerCase()));
}

function stripRevealing(value) {
    if (Array.isArray(value)) return value.map(stripRevealing).filter((entry) => entry !== undefined);
    if (value && typeof value === "object") {
        const output = {};
        for (const [key, entry] of Object.entries(value)) {
            if (REVEALING_KEYS.has(key) || containsRevealingToken(key)) continue;
            const stripped = stripRevealing(entry);
            if (stripped !== undefined) output[key] = stripped;
        }
        return output;
    }
    if (typeof value === "string" && containsRevealingToken(value)) return undefined;
    return value;
}

function assertBlindedPacket(packet) {
    const serialized = JSON.stringify(packet);
    for (const key of REVEALING_KEYS) if (Object.prototype.hasOwnProperty.call(packet, key))
        throw new Error(`rater packet leaks ${key}`);
    for (const token of REVEALING_TOKENS) if (serialized.toLowerCase().includes(token.toLowerCase()))
        throw new Error(`rater packet leaks condition token '${token}'`);
    return true;
}

function createRaterPacket({ trial, material, mappingFilePath, randomBytes = crypto.randomBytes }) {
    if (!trial || !trial.participantId || !trial.trialId) throw new Error("trial identity is required for the private mapping");
    if (!mappingFilePath) throw new Error("a separate private mappingFilePath is required");
    const codingId = `RC-${randomBytes(16).toString("hex")}`;
    const packet = stripRevealing({ codingId, taskId: trial.taskId, taskVariant: trial.taskVariant,
        conditionBlinded: true, raterPseudonym: null, material });
    assertBlindedPacket(packet);
    fs.mkdirSync(path.dirname(mappingFilePath), { recursive: true });
    fs.appendFileSync(mappingFilePath, JSON.stringify({ codingId, participantId: trial.participantId,
        trialId: trial.trialId, createdAtUtc: new Date().toISOString() }) + "\n");
    return packet;
}

function createImplicitRaterPacket({ trial, evidence, observableMaterial, mappingFilePath, randomBytes = crypto.randomBytes }) {
    const computed = deriveImplicitBinaries(evidence, false);
    const packet = createRaterPacket({ trial, material: {
        codingInstruction: "Judge only whether the response is a plausible thing to try from the observable context.",
        observableMaterial,
    }, mappingFilePath, randomBytes });
    return {
        packet,
        prefilledLogDerived: { grounded: computed.grounded, inEnvelope: computed.inEnvelope, timely: computed.timely },
        raterField: "contextuallyAdmissible",
    };
}

function cohensKappa(raterA, raterB) {
    if (!Array.isArray(raterA) || !Array.isArray(raterB) || raterA.length === 0 || raterA.length !== raterB.length)
        throw new Error("Cohen's kappa requires two non-empty equal-length rating arrays");
    const categories = [...new Set([...raterA, ...raterB])];
    const observed = raterA.reduce((count, value, index) => count + (value === raterB[index] ? 1 : 0), 0) / raterA.length;
    const expected = categories.reduce((sum, category) => {
        const pa = raterA.filter((value) => value === category).length / raterA.length;
        const pb = raterB.filter((value) => value === category).length / raterB.length;
        return sum + pa * pb;
    }, 0);
    const kappa = expected === 1 ? (observed === 1 ? 1 : null) : (observed - expected) / (1 - expected);
    return { kappa, observedAgreement: observed, expectedAgreement: expected,
        reliability: kappa !== null && kappa >= 0.6 ? "reportable" : "unreliable-do-not-adjudicate" };
}

module.exports = { REVEALING_KEYS, REVEALING_TOKENS, stripRevealing, assertBlindedPacket,
    createRaterPacket, createImplicitRaterPacket, cohensKappa };
