"use strict";

// Goal state and delayed evaluations are persisted as typed records in the existing
// temporal ArtifactLog. There is intentionally no second goal database.

class GoalMemory {
    constructor({ artifactLog } = {}) {
        if (!artifactLog) throw new Error("GoalMemory requires the existing ArtifactLog");
        this.artifactLog = artifactLog;
    }

    _records() {
        return this.artifactLog.history({ limit: Number.MAX_SAFE_INTEGER }).reverse();
    }

    saveGoal(goal, eventType = "goal_state") {
        return this.artifactLog.append({
            eventType,
            sessionId: goal.sessionId,
            correlationId: goal.correlationId,
            targetObjectId: goal.targetObjectId || null,
            goalId: goal.goalId,
            goal: { ...goal },
            goalStatus: goal.status,
            goalIteration: goal.currentIteration,
            verificationLevel: goal.verificationLevel,
            at: goal.updatedAt || Date.now(),
        });
    }

    getGoal(goalId) {
        const latest = [...this._records()].reverse().find((entry) =>
            entry.goalId === goalId && entry.goal && String(entry.eventType).startsWith("goal_"));
        return latest ? { ...latest.goal } : null;
    }

    listGoals({ sessionId, statuses } = {}) {
        const latest = new Map();
        for (const entry of this._records()) {
            if (!entry.goalId || !entry.goal || !String(entry.eventType).startsWith("goal_")) continue;
            latest.set(entry.goalId, entry.goal);
        }
        return [...latest.values()].filter((goal) =>
            (!sessionId || goal.sessionId === sessionId) &&
            (!statuses || statuses.includes(goal.status)));
    }

    killSwitchState() {
        const latest = [...this._records()].reverse().find((entry) =>
            entry.eventType === "goal_global_kill_switch");
        return latest ? { active: latest.status === "active", reason: latest.reasonCode || null } :
            { active: false, reason: null };
    }

    saveKillSwitch({ active, reason }) {
        return this.artifactLog.append({
            eventType: "goal_global_kill_switch",
            status: active ? "active" : "cleared",
            reasonCode: reason || null,
        });
    }

    enqueueDelayed(goal, pendingEvaluation) {
        const queuedAt = Date.now();
        this.artifactLog.append({
            eventType: "goal_delayed_evaluation_pending",
            sessionId: goal.sessionId,
            correlationId: goal.correlationId,
            targetObjectId: goal.targetObjectId || null,
            goalId: goal.goalId,
            verificationLevel: goal.verificationLevel,
            pendingEvaluation,
            queuedAt,
            at: queuedAt,
        });
        return { goalId: goal.goalId, ...pendingEvaluation, queuedAt, status: "pending" };
    }

    pendingDelayed(goalId) {
        let pending = null;
        for (const entry of this._records()) {
            if (entry.goalId !== goalId) continue;
            if (entry.eventType === "goal_delayed_evaluation_pending") pending = {
                goalId,
                ...entry.pendingEvaluation,
                queuedAt: entry.queuedAt || entry.loggedAt,
                status: "pending",
            };
            if (entry.eventType === "goal_delayed_evaluation_resolved") pending = null;
        }
        return pending;
    }

    resolveDelayed(goal, { signal, value, at = Date.now() } = {}) {
        const pending = this.pendingDelayed(goal.goalId);
        if (!pending) throw new Error(`goal '${goal.goalId}' has no pending delayed evaluation`);
        if (signal !== pending.signal) throw new Error(`expected delayed signal '${pending.signal}'`);
        const resolutionLatencyMs = at - pending.queuedAt;
        this.artifactLog.append({
            eventType: "goal_delayed_evaluation_resolved",
            sessionId: goal.sessionId,
            correlationId: goal.correlationId,
            targetObjectId: goal.targetObjectId || null,
            goalId: goal.goalId,
            verificationLevel: goal.verificationLevel,
            signal,
            value,
            resolutionLatencyMs,
            at,
        });
        return { signal, value, at, resolutionLatencyMs };
    }

    verifiedHumanDecisionAfterEscalation(goal, decisionCorrelationId) {
        const eventAt = (entry) => entry.at || entry.timestamp || entry.loggedAt || 0;
        const records = [...this._records()].reverse();
        const escalation = records.find((entry) =>
            entry.goalId === goal.goalId &&
            ["goal_escalated", "goal_bound_exhausted"].includes(entry.eventType));
        if (!escalation) return null;
        const decision = records.find((entry) =>
            entry.sessionId === goal.sessionId &&
            eventAt(entry) > eventAt(escalation) &&
            (entry.eventType === "user_decision:approved" ||
                (entry.eventType === "intent_captured" &&
                    decisionCorrelationId &&
                    entry.correlationId === decisionCorrelationId &&
                    entry.correlationId !== goal.correlationId)));
        return decision ? {
            verified: true,
            eventType: decision.eventType,
            correlationId: decision.correlationId,
        } : null;
    }

    saveSpeculativeCandidate(goal, candidate) {
        return this.artifactLog.append({
            eventType: "speculative_candidate_prepared",
            sessionId: goal.sessionId,
            correlationId: goal.correlationId,
            targetObjectId: goal.targetObjectId || null,
            goalId: goal.goalId,
            speculative: true,
            candidateId: candidate.candidateId,
            candidateSetId: candidate.candidateSetId || null,
            status: candidate.status,
            validationState: candidate.validationState,
            simulationStatus: candidate.simulationStatus,
            riskScore: candidate.riskScore,
            sceneEpoch: goal.sceneEpoch,
            snapshotId: goal.snapshotId,
            objectRevision: goal.objectRevision,
            preparedArtifact: candidate.preparedArtifact || null,
            at: candidate.preparedAt || Date.now(),
        });
    }

    speculativeCandidates({ sessionId, targetObjectId } = {}) {
        return this._records().filter((entry) =>
            entry.eventType === "speculative_candidate_prepared" &&
            (!sessionId || entry.sessionId === sessionId) &&
            (!targetObjectId || entry.targetObjectId === targetObjectId));
    }
}

module.exports = { GoalMemory };
