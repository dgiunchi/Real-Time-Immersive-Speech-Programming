"use strict";

const { randomUUID } = require("crypto");
const { createGoal, VERIFICATION_LEVELS } = require("./goal_schema");
const { GoalMemory } = require("./goal_memory");

const MAX_PREDICTIONS_PER_IDLE_WINDOW = 3;

function tokenize(text) {
    return new Set(String(text || "").toLowerCase().split(/[^a-z0-9]+/).filter((token) => token.length > 2));
}

function overlapScore(left, right) {
    const a = tokenize(left);
    const b = tokenize(right);
    if (!a.size || !b.size) return 0;
    let overlap = 0;
    for (const token of a) if (b.has(token)) overlap += 1;
    return overlap / Math.max(a.size, b.size);
}

class FutureGoalPredictor {
    constructor({ artifactLog, predictionProvider } = {}) {
        this.memory = new GoalMemory({ artifactLog });
        this.predictionProvider = predictionProvider;
    }

    fallbackPredictions(context) {
        const predictions = [];
        if (context.region && context.targetObjectId) {
            predictions.push({
                objective: `prepare local guidance for ${context.targetObjectId} in ${context.region}`,
                targetObjectId: context.targetObjectId,
                confidence: 0.65,
                rationaleCode: "stable_focus_and_region",
            });
        }
        if (context.experienceMode === "training" && context.targetObjectId) {
            predictions.push({
                objective: `prepare a reversible training cue for ${context.targetObjectId}`,
                targetObjectId: context.targetObjectId,
                confidence: 0.6,
                rationaleCode: "training_context",
            });
        }
        return predictions;
    }

    async prepareDuringIdle({
        sessionId,
        correlationId,
        context,
        isIdle,
        generateCandidate,
        simulateCandidate,
    } = {}) {
        if (isIdle !== true) return { status: "skipped_busy", prepared: [] };
        if (!sessionId || !correlationId || !context) throw new Error("idle prediction requires sessionId, correlationId, and context");
        if (typeof generateCandidate !== "function" || typeof simulateCandidate !== "function") {
            throw new Error("idle prediction requires generation and simulation harnesses");
        }
        const raw = typeof this.predictionProvider === "function"
            ? await this.predictionProvider(context)
            : this.fallbackPredictions(context);
        const predictions = (raw || []).filter((item) =>
            item && typeof item.objective === "string" && Number(item.confidence) >= 0.5
        ).slice(0, MAX_PREDICTIONS_PER_IDLE_WINDOW);
        const prepared = [];
        for (const prediction of predictions) {
            const goal = createGoal({
                goalId: `predicted-${randomUUID()}`,
                objective: prediction.objective,
                sessionId,
                correlationId,
                targetObjectId: prediction.targetObjectId || context.targetObjectId,
                interactionMode: "L4",
                authoringMode: "semi_auto_confirm",
                triggerSource: "schedule",
                verificationLevel: VERIFICATION_LEVELS.DETERMINISTIC,
                terminationPredicate: { type: "field_equals", field: "artifactCommitted", value: true },
                maxAttempts: 1,
                maxWallTimeMs: 30000,
                speculative: true,
                predictedFrom: prediction.rationaleCode || "prediction_provider",
                sceneEpoch: context.sceneEpoch,
                snapshotId: context.snapshotId,
                objectRevision: context.objectRevision,
            });
            this.memory.saveGoal(goal, "goal_predicted");
            const candidate = await generateCandidate({ goal, context, prediction });
            const simulation = await simulateCandidate({ goal, context, candidate });
            const accepted = candidate && simulation &&
                candidate.validationState === "accepted" &&
                simulation.status === "simulated";
            const stored = {
                candidateId: candidate && candidate.candidateId || `candidate-${randomUUID()}`,
                candidateSetId: candidate && candidate.candidateSetId || null,
                status: accepted ? "prepared" : "rejected",
                validationState: candidate && candidate.validationState || "unknown",
                simulationStatus: simulation && simulation.status || "unknown",
                riskScore: candidate && candidate.riskScore,
                preparedArtifact: accepted ? candidate.artifact : null,
                preparedAt: Date.now(),
                predictedObjective: goal.objective,
                confidence: prediction.confidence,
            };
            this.memory.saveSpeculativeCandidate(goal, stored);
            if (accepted) prepared.push({ goal, ...stored });
        }
        return { status: "prepared", prepared };
    }

    selectForActualGoal({ sessionId, correlationId, actualObjective, targetObjectId, sceneEpoch, snapshotId, objectRevision } = {}) {
        const candidates = this.memory.speculativeCandidates({ sessionId, targetObjectId })
            .filter((entry) =>
                entry.status === "prepared" &&
                entry.validationState === "accepted" &&
                entry.simulationStatus === "simulated" &&
                entry.sceneEpoch === sceneEpoch &&
                entry.snapshotId === snapshotId &&
                entry.objectRevision === objectRevision
            )
            .map((entry) => ({
                ...entry,
                objectiveFit: overlapScore(actualObjective,
                    (this.memory.getGoal(entry.goalId) || {}).objective),
            }))
            .filter((entry) => entry.objectiveFit >= 0.4)
            .sort((a, b) => b.objectiveFit - a.objectiveFit || b.loggedAt - a.loggedAt);
        if (!candidates.length) return { selected: null, reason: "no_fresh_verified_prediction" };
        const selected = candidates[0];
        // How far ahead of need the preparation ran - the study's anticipation
        // payoff measure (docs/code-implicit-proactive-showcase-2026-08-13.md §1).
        const preparedAt = Number.isFinite(selected.at) ? selected.at : selected.loggedAt;
        const speculativePreparationLeadTimeMs = Number.isFinite(preparedAt)
            ? Math.max(0, Date.now() - preparedAt) : null;
        this.memory.artifactLog.append({
            eventType: "speculative_candidate_adopted",
            sessionId,
            correlationId,
            targetObjectId,
            goalId: selected.goalId,
            candidateId: selected.candidateId,
            objectiveFit: selected.objectiveFit,
            speculativePreparationLeadTimeMs,
            speculative: true,
            status: "selected_for_normal_pipeline",
        });
        return {
            selected,
            reason: "fresh verified prediction matches actual goal",
            requiresNormalGates: true,
            mayCommitAutomatically: false,
        };
    }
}

module.exports = {
    FutureGoalPredictor,
    MAX_PREDICTIONS_PER_IDLE_WINDOW,
    overlapScore,
};
