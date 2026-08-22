"use strict";

const fs = require("fs");
const path = require("path");

const STUDY_ROOT = __dirname;
const PROTOCOL_PATH = path.join(STUDY_ROOT, "protocol.v1.json");
const QUESTIONNAIRES_PATH = path.join(STUDY_ROOT, "questionnaires.v1.json");
const RUBRICS_PATH = path.join(STUDY_ROOT, "rubrics.v1.json");
const PARTICIPANT_PATTERN = /^P\d{3}$/;
const EXPLICIT_TASK_ORDERS = Object.freeze([
    ["L3", "L4", "L5"],
    ["L3", "L5", "L4"],
    ["L4", "L3", "L5"],
    ["L4", "L5", "L3"],
    ["L5", "L3", "L4"],
    ["L5", "L4", "L3"],
]);

function readJson(filePath) {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
}

function loadProtocol() {
    return readJson(PROTOCOL_PATH);
}

function validateParticipantId(participantId) {
    if (typeof participantId !== "string" || !PARTICIPANT_PATTERN.test(participantId)) {
        throw new Error("participantId must match P followed by exactly three digits (for example P001)");
    }
    return participantId;
}

function participantOrdinal(participantId) {
    validateParticipantId(participantId);
    const ordinal = Number(participantId.slice(1));
    if (ordinal < 1) throw new Error("participantId P000 is reserved and cannot be used");
    return ordinal;
}

function generateParticipantPlan(participantId) {
    const protocol = loadProtocol();
    const ordinal = participantOrdinal(participantId);
    const zeroBased = ordinal - 1;
    // Balancing block, orthogonal to EXPLICIT_TASK_ORDERS[zeroBased % 6].
    // Condition order and H4 arm must not be keyed on the permuted task position:
    // period-2 factors degenerate against the period-6 permutation.
    const balancingBlock = Math.floor(zeroBased / 6) % 2;
    const byMode = new Map(protocol.tasks.map((task) => [task.interactionMode, task]));
    const canonicalModeOrder = ["L1", "L2", "L3", "L4", "L5"];
    const modeOrder = ["L1", "L2", ...EXPLICIT_TASK_ORDERS[zeroBased % EXPLICIT_TASK_ORDERS.length]];
    const h4ByMode = balancingBlock === 0 ? { L4: 1, L5: 3 } : { L4: 3, L5: 1 };
    const trials = [];

    for (const [taskIndex, mode] of modeOrder.entries()) {
        const task = byMode.get(mode);
        const canonicalModeIndex = canonicalModeOrder.indexOf(mode);
        const aliases = [...task.conditionPair];
        if ((balancingBlock + canonicalModeIndex) % 2 === 1) aliases.reverse();
        const variants = (Math.floor(zeroBased / 3) + canonicalModeIndex) % 2 === 0
            ? [...protocol.design.taskVariants]
            : [...protocol.design.taskVariants].reverse();
        for (const [conditionIndex, conditionAlias] of aliases.entries()) {
            const condition = protocol.conditions[conditionAlias];
            const sequenceIndex = trials.length + 1;
            const isFullH4 = conditionAlias === "full" && task.h4Eligible === true;
            const candidateTarget = isFullH4 ? h4ByMode[mode] : null;
            trials.push({
                protocolId: protocol.protocolId,
                methodVersion: protocol.methodVersion,
                participantId,
                sequenceIndex,
                trialId: `T${String(sequenceIndex).padStart(2, "0")}`,
                sessionId: `${participantId}-S${String(sequenceIndex).padStart(2, "0")}`,
                blockId: taskIndex < 2 ? "implicit-fixed" : "explicit-counterbalanced",
                taskId: task.taskId,
                interactionMode: mode,
                taskVariant: variants[conditionIndex],
                conditionAlias,
                condition: condition.runtimeCondition,
                candidateTarget,
                h4Arm: isFullH4 ? (candidateTarget === 1 ? "single" : "best-of-3") : "not-applicable",
                hypotheses: task.tests,
            });
        }
    }

    if (trials.length !== protocol.design.trialsPerParticipant) {
        throw new Error(`protocol generated ${trials.length} trials, expected ${protocol.design.trialsPerParticipant}`);
    }
    const h4 = trials.filter((trial) => trial.h4Arm !== "not-applicable").map((trial) => trial.candidateTarget).sort();
    if (JSON.stringify(h4) !== JSON.stringify(protocol.design.h4CandidateCounts)) {
        throw new Error(`H4 assignment must contain ${protocol.design.h4CandidateCounts.join(" and ")}`);
    }
    return {
        schemaVersion: "1.0",
        protocolId: protocol.protocolId,
        methodVersion: protocol.methodVersion,
        participantId,
        generatedAtUtc: new Date().toISOString(),
        assignment: {
            explicitTaskOrder: modeOrder.slice(2),
            h4CandidateByMode: h4ByMode,
            taskVariantByTrial: Object.fromEntries(trials.map((trial) => [trial.trialId, trial.taskVariant])),
        },
        trials,
    };
}

module.exports = {
    STUDY_ROOT,
    PROTOCOL_PATH,
    QUESTIONNAIRES_PATH,
    RUBRICS_PATH,
    PARTICIPANT_PATTERN,
    EXPLICIT_TASK_ORDERS,
    loadProtocol,
    validateParticipantId,
    generateParticipantPlan,
};
