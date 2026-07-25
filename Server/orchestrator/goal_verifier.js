"use strict";

const { VERIFICATION_LEVELS } = require("./goal_schema");

function fieldAt(object, path) {
    return String(path || "").split(".").filter(Boolean).reduce(
        (value, key) => value == null ? undefined : value[key],
        object
    );
}

function compare(actual, operator, expected) {
    if (!Number.isFinite(actual)) return false;
    if (operator === "<") return actual < expected;
    if (operator === "<=") return actual <= expected;
    if (operator === "==") return actual === expected;
    if (operator === ">=") return actual >= expected;
    if (operator === ">") return actual > expected;
    return false;
}

class GoalVerifier {
    constructor({ llmJudge, humanCheckpoint } = {}) {
        this.llmJudge = llmJudge;
        this.humanCheckpoint = humanCheckpoint;
    }

    async verify(goal, evidence = {}) {
        const predicate = goal.terminationPredicate;
        if (goal.verificationLevel === VERIFICATION_LEVELS.DETERMINISTIC) {
            const complete = predicate.type === "all_true"
                ? (predicate.fields || []).every((field) => fieldAt(evidence, field) === true)
                : fieldAt(evidence, predicate.field) === predicate.value;
            return { complete, status: complete ? "verified" : "not_verified", level: 1, evidence };
        }
        if (goal.verificationLevel === VERIFICATION_LEVELS.RULES_CONSTRAINTS) {
            const actual = Number(fieldAt(evidence, predicate.metric));
            const complete = compare(actual, predicate.operator, predicate.value);
            return { complete, status: complete ? "verified" : "constraint_not_met", level: 2, actual, expected: predicate.value };
        }
        if (goal.verificationLevel === VERIFICATION_LEVELS.DELAYED_GROUND_TRUTH) {
            const resolution = evidence.delayedResolution;
            if (!resolution || resolution.signal !== predicate.signal) {
                return {
                    complete: false,
                    pending: true,
                    status: "waiting_delayed_ground_truth",
                    level: 3,
                    pendingEvaluation: { signal: predicate.signal, expectedValue: predicate.value },
                };
            }
            const complete = predicate.value === undefined || resolution.value === predicate.value;
            return { complete, status: complete ? "verified" : "delayed_signal_not_met", level: 3, resolution };
        }
        if (goal.verificationLevel === VERIFICATION_LEVELS.LLM_AS_JUDGE) {
            if (typeof this.llmJudge !== "function") {
                return { complete: false, needsValidator: true, status: "validator_required", level: 4 };
            }
            const judgment = await this.llmJudge({ goal, evidence });
            if (!judgment || typeof judgment !== "object") {
                return { complete: false, needsValidator: true, status: "validator_required", level: 4 };
            }
            const score = Number(judgment && judgment.score);
            const complete = judgment && judgment.accepted === true && score >= predicate.minimumScore;
            return { complete, status: complete ? "verified" : "judge_not_satisfied", level: 4, score, judgment };
        }
        if (goal.verificationLevel === VERIFICATION_LEVELS.HUMAN_CHECKPOINT) {
            if (typeof this.humanCheckpoint !== "function") {
                return { complete: false, needsHuman: true, status: "human_checkpoint_required", level: 5 };
            }
            const decision = await this.humanCheckpoint({ goal, evidence });
            const complete = decision && decision.approved === true;
            return { complete, needsHuman: !complete, status: complete ? "verified" : "human_checkpoint_required", level: 5, decision };
        }
        throw new Error(`unsupported verification level ${goal.verificationLevel}`);
    }
}

module.exports = { GoalVerifier, fieldAt, compare };
