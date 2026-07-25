"use strict";

const { randomUUID } = require("crypto");

const VERIFICATION_LEVELS = Object.freeze({
    DETERMINISTIC: 1,
    RULES_CONSTRAINTS: 2,
    DELAYED_GROUND_TRUTH: 3,
    LLM_AS_JUDGE: 4,
    HUMAN_CHECKPOINT: 5,
});

const VERIFICATION_LEVEL_NAMES = Object.freeze({
    1: "deterministic",
    2: "rules_constraints",
    3: "delayed_ground_truth",
    4: "llm_as_judge",
    5: "human_checkpoint",
});

const GLOBAL_MAX_ATTEMPTS = 10;
const GLOBAL_MAX_WALL_TIME_MS = 15 * 60 * 1000;
const GOAL_ID_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;

function positiveInteger(value, name) {
    if (!Number.isInteger(value) || value < 1) throw new Error(`${name} must be a positive integer`);
    return value;
}

function validatePredicate(level, predicate) {
    if (!predicate || typeof predicate !== "object") throw new Error("terminationPredicate is required");
    if (level === VERIFICATION_LEVELS.DETERMINISTIC &&
        !["field_equals", "all_true"].includes(predicate.type)) {
        throw new Error("deterministic goals require field_equals or all_true");
    }
    if (level === VERIFICATION_LEVELS.RULES_CONSTRAINTS) {
        if (predicate.type !== "threshold") throw new Error("rules goals require a threshold predicate");
        if (!["<", "<=", "==", ">=", ">"].includes(predicate.operator) || !Number.isFinite(predicate.value)) {
            throw new Error("threshold predicate requires operator and numeric value");
        }
    }
    if (level === VERIFICATION_LEVELS.DELAYED_GROUND_TRUTH &&
        (predicate.type !== "delayed_signal" || !predicate.signal)) {
        throw new Error("delayed goals require a delayed_signal predicate");
    }
    if (level === VERIFICATION_LEVELS.LLM_AS_JUDGE &&
        (predicate.type !== "judge_score" || !Number.isFinite(predicate.minimumScore))) {
        throw new Error("LLM-judge goals require a judge_score predicate");
    }
    if (level === VERIFICATION_LEVELS.HUMAN_CHECKPOINT && predicate.type !== "human_approval") {
        throw new Error("human-checkpoint goals require human_approval");
    }
    return predicate;
}

function createGoal(input = {}) {
    const now = Number.isFinite(input.createdAt) ? input.createdAt : Date.now();
    const verificationLevel = Number(input.verificationLevel);
    if (!VERIFICATION_LEVEL_NAMES[verificationLevel]) throw new Error("verificationLevel must be an integer from 1 to 5");
    const goalId = input.goalId || `goal-${randomUUID()}`;
    if (!GOAL_ID_PATTERN.test(goalId)) throw new Error("goalId must be a 1-128 character safe identifier");
    for (const field of ["objective", "sessionId", "correlationId"]) {
        if (typeof input[field] !== "string" || !input[field].trim()) throw new Error(`${field} is required`);
    }
    const requestedAttempts = positiveInteger(input.maxAttempts || 3, "maxAttempts");
    const requestedWallTime = positiveInteger(input.maxWallTimeMs || 120000, "maxWallTimeMs");
    return {
        schemaVersion: "1.0",
        goalId,
        objective: input.objective.trim(),
        sessionId: input.sessionId,
        correlationId: input.correlationId,
        targetObjectId: input.targetObjectId || null,
        artifactVersion: input.artifactVersion || null,
        rollbackPointer: input.rollbackPointer || null,
        interactionMode: input.interactionMode || "L4",
        authoringMode: input.authoringMode || "semi_auto_confirm",
        triggerSource: input.triggerSource || "explicit_request",
        verificationLevel,
        verificationLevelName: VERIFICATION_LEVEL_NAMES[verificationLevel],
        terminationPredicate: validatePredicate(verificationLevel, input.terminationPredicate),
        maxAttempts: Math.min(requestedAttempts, GLOBAL_MAX_ATTEMPTS),
        maxWallTimeMs: Math.min(requestedWallTime, GLOBAL_MAX_WALL_TIME_MS),
        currentIteration: Number.isInteger(input.currentIteration) ? Math.max(0, input.currentIteration) : 0,
        status: input.status || "pending",
        createdAt: now,
        startedAt: Number.isFinite(input.startedAt) ? input.startedAt : null,
        updatedAt: now,
        lastTrigger: input.lastTrigger || null,
        speculative: input.speculative === true,
        predictedFrom: input.predictedFrom || null,
        sceneEpoch: input.sceneEpoch || null,
        snapshotId: input.snapshotId || null,
        objectRevision: input.objectRevision ?? null,
    };
}

function validateGoal(goal) {
    return createGoal(goal);
}

module.exports = {
    VERIFICATION_LEVELS,
    VERIFICATION_LEVEL_NAMES,
    GLOBAL_MAX_ATTEMPTS,
    GLOBAL_MAX_WALL_TIME_MS,
    createGoal,
    validateGoal,
};
