"use strict";

const { createGoal, GLOBAL_MAX_ATTEMPTS, GLOBAL_MAX_WALL_TIME_MS } = require("./goal_schema");
const { GoalVerifier } = require("./goal_verifier");
const { GoalMemory } = require("./goal_memory");
const { checkModePolicy } = require("./mode_policy");

const TERMINAL_STATUSES = new Set(["completed", "cancelled", "killed"]);

class GoalLoopController {
    constructor({ artifactLog, verifier, now = () => Date.now() } = {}) {
        this.memory = new GoalMemory({ artifactLog });
        this.verifier = verifier || new GoalVerifier();
        this.now = now;
        this.killSwitch = this.memory.killSwitchState();
    }

    create(input) {
        const goal = createGoal(input);
        this.memory.saveGoal(goal, "goal_created");
        return goal;
    }

    get(goalId) {
        return this.memory.getGoal(goalId);
    }

    _log(goal, eventType, details = {}) {
        return this.memory.artifactLog.append({
            eventType,
            sessionId: goal.sessionId,
            correlationId: goal.correlationId,
            targetObjectId: goal.targetObjectId || null,
            goalId: goal.goalId,
            goalIteration: goal.currentIteration,
            verificationLevel: goal.verificationLevel,
            goalStatus: goal.status,
            ...details,
            at: this.now(),
        });
    }

    _exhausted(goal, at = this.now()) {
        const elapsed = (goal.startedAt == null ? 0 : at - goal.startedAt);
        return goal.currentIteration >= goal.maxAttempts || elapsed >= goal.maxWallTimeMs;
    }

    _escalate(goal, reason, eventType = "goal_escalated") {
        goal.status = "awaiting_human";
        goal.updatedAt = this.now();
        goal.escalationReason = reason;
        this._log(goal, eventType, { reasonCode: reason, boundExhausted: reason === "bound_exhausted" });
        this.memory.saveGoal(goal, "goal_state");
        return { goal, status: goal.status, escalated: true, reason };
    }

    async trigger(goalId, trigger, { execute, policy = {}, delayedResolution } = {}) {
        const goal = this.get(goalId);
        if (!goal) throw new Error(`unknown goal '${goalId}'`);
        if (TERMINAL_STATUSES.has(goal.status)) throw new Error(`goal '${goalId}' is ${goal.status}`);
        if (goal.status === "awaiting_human") return { goal, status: goal.status, escalated: true, reason: goal.escalationReason };
        if (this.killSwitch.active) {
            goal.status = "killed";
            goal.updatedAt = this.now();
            this._log(goal, "goal_killed", { reasonCode: this.killSwitch.reason || "global_kill_switch" });
            this.memory.saveGoal(goal, "goal_state");
            return { goal, status: "killed", killed: true };
        }
        if (this._exhausted(goal)) return this._escalate(goal, "bound_exhausted", "goal_bound_exhausted");
        if (!trigger || !["explicit_request", "system_opportunity", "context", "schedule", "delayed_signal"].includes(trigger.source)) {
            throw new Error("trigger source must be explicit_request, system_opportunity, context, schedule, or delayed_signal");
        }

        const mode = checkModePolicy({
            interactionMode: goal.interactionMode,
            authoringMode: goal.authoringMode,
            triggerSource: goal.triggerSource === "schedule" ? "system_opportunity" : goal.triggerSource,
            verificationLevel: goal.verificationLevel,
            speculative: goal.speculative,
            ...policy,
        });
        if (!mode.accepted) return this._escalate(goal, "mode_policy_rejected");
        if (mode.verificationRoute.requiresHuman && goal.authoringMode === "automatic") {
            return this._escalate(goal, "verification_requires_human");
        }

        goal.startedAt = goal.startedAt == null ? this.now() : goal.startedAt;
        goal.currentIteration += 1;
        goal.lastTrigger = { ...trigger, at: trigger.at || this.now() };
        goal.status = "running";
        goal.updatedAt = this.now();
        this._log(goal, "goal_triggered", { triggerSource: trigger.source, triggerId: trigger.id || null });
        this.memory.saveGoal(goal, "goal_state");

        if (typeof execute !== "function") throw new Error("goal trigger requires an execute harness callback");
        const startedAt = this.now();
        const evidence = await execute({ goal: { ...goal }, trigger: goal.lastTrigger });
        const executionDurationMs = this.now() - startedAt;
        this._log(goal, "goal_iteration_executed", { executionDurationMs });

        const verification = await this.verifier.verify(goal, {
            ...(evidence || {}),
            ...(delayedResolution ? { delayedResolution } : {}),
        });
        this._log(goal, "goal_verification_outcome", {
            verificationStatus: verification.status,
            verificationComplete: verification.complete === true,
            verificationPending: verification.pending === true,
        });

        if (verification.complete) {
            goal.status = "completed";
            goal.completedAt = this.now();
            goal.updatedAt = goal.completedAt;
            goal.terminationEvidence = verification;
            this._log(goal, "goal_terminated", {
                iterationsToCompletion: goal.currentIteration,
                totalWallTimeMs: goal.completedAt - goal.startedAt,
            });
            this.memory.saveGoal(goal, "goal_state");
            return { goal, status: "completed", verification };
        }
        if (verification.pending) {
            this.memory.enqueueDelayed(goal, verification.pendingEvaluation);
            goal.status = "waiting_delayed_ground_truth";
            goal.updatedAt = this.now();
            this.memory.saveGoal(goal, "goal_state");
            return { goal, status: goal.status, verification };
        }
        if (verification.needsHuman || verification.needsValidator) {
            return this._escalate(goal, verification.needsHuman ? "human_checkpoint_required" : "validator_required");
        }
        if (this._exhausted(goal)) return this._escalate(goal, "bound_exhausted", "goal_bound_exhausted");

        goal.status = "waiting_trigger";
        goal.updatedAt = this.now();
        this.memory.saveGoal(goal, "goal_state");
        return { goal, status: goal.status, verification };
    }

    async resolveDelayed(goalId, resolution, options) {
        const goal = this.get(goalId);
        if (!goal) throw new Error(`unknown goal '${goalId}'`);
        const resolved = this.memory.resolveDelayed(goal, resolution);
        goal.status = "waiting_trigger";
        goal.updatedAt = this.now();
        this.memory.saveGoal(goal, "goal_state");
        return this.trigger(goalId, { source: "delayed_signal", id: resolution.signal, at: resolution.at }, {
            ...options,
            execute: options && options.execute || (async () => ({})),
            delayedResolution: resolved,
        });
    }

    continueAfterHuman(goalId, { approved, maxAttempts, maxWallTimeMs, humanDecision } = {}) {
        const goal = this.get(goalId);
        if (!goal) throw new Error(`unknown goal '${goalId}'`);
        if (goal.status !== "awaiting_human") throw new Error("goal is not awaiting a human");
        if (approved !== true) {
            goal.status = "cancelled";
            goal.updatedAt = this.now();
            this._log(goal, "goal_human_continuation_rejected");
            this.memory.saveGoal(goal, "goal_state");
            return goal;
        }
        if (!humanDecision || humanDecision.verified !== true) {
            throw new Error("goal continuation requires a verified human decision after escalation");
        }
        goal.maxAttempts = Math.min(
            Number.isInteger(maxAttempts) ? maxAttempts : goal.currentIteration + 1,
            GLOBAL_MAX_ATTEMPTS
        );
        goal.maxWallTimeMs = Math.min(
            Number.isInteger(maxWallTimeMs) ? maxWallTimeMs : goal.maxWallTimeMs,
            GLOBAL_MAX_WALL_TIME_MS
        );
        goal.status = "waiting_trigger";
        goal.escalationReason = null;
        goal.updatedAt = this.now();
        this._log(goal, "goal_human_continuation_approved", {
            humanDecisionEventType: humanDecision.eventType,
            humanDecisionCorrelationId: humanDecision.correlationId,
        });
        this.memory.saveGoal(goal, "goal_state");
        return goal;
    }

    activateKillSwitch(reason = "global_kill_switch") {
        this.killSwitch = { active: true, reason };
        this.memory.saveKillSwitch(this.killSwitch);
        return { ...this.killSwitch };
    }

    clearKillSwitch({ humanApproved } = {}) {
        if (humanApproved !== true) throw new Error("kill switch requires explicit human approval to clear");
        this.killSwitch = { active: false, reason: null };
        this.memory.saveKillSwitch(this.killSwitch);
        return { ...this.killSwitch };
    }

    async run(goalId, { triggerProvider, execute, policy } = {}) {
        if (typeof triggerProvider !== "function") throw new Error("run requires a triggerProvider");
        while (true) {
            const trigger = await triggerProvider(this.get(goalId));
            const result = await this.trigger(goalId, trigger, { execute, policy });
            if (result.status !== "waiting_trigger") return result;
        }
    }
}

module.exports = { GoalLoopController, TERMINAL_STATUSES };
