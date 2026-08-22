"use strict";

const { EventJournal } = require("../cache/event_journal");
const { generateParticipantPlan, validateParticipantId } = require("./protocol");
const { StudySessionMachine } = require("./study_session_machine");
const taskManifest = require("./task_manifest.v1.json");

const BREAK_AFTER_COMPLETED_TRIALS = new Set([4, 7]);
const DEFAULT_BREAK_MINIMUM_MS = 5 * 60 * 1000;
const TIMEOUT_SECONDS_BY_MODE = Object.freeze(Object.fromEntries(
    taskManifest.tasks.map((task) => [task.interactionMode, task.timeoutSeconds])));

function normalizedPlan(plan) {
    return {
        schemaVersion: plan.schemaVersion,
        protocolId: plan.protocolId,
        methodVersion: plan.methodVersion,
        participantId: plan.participantId,
        assignment: plan.assignment,
        trials: plan.trials,
    };
}

function samePlan(left, right) {
    return JSON.stringify(normalizedPlan(left)) === JSON.stringify(normalizedPlan(right));
}

class StudySessionRunner {
    constructor({ participantId, plan = generateParticipantPlan(participantId), journal = new EventJournal(),
        machine = new StudySessionMachine(), now = () => Date.now(), breakMinimumMs = DEFAULT_BREAK_MINIMUM_MS,
        restoring = false, runMode = "researcher-dry-run", modelPin = null } = {}) {
        validateParticipantId(participantId);
        if (plan.participantId !== participantId || !samePlan(plan, generateParticipantPlan(participantId)))
            throw new Error("session plan must be the verbatim generateParticipantPlan assignment for this participant");
        this.participantId = participantId;
        this.plan = plan;
        this.journal = journal;
        this.machine = machine;
        this.now = now;
        this.breakMinimumMs = breakMinimumMs;
        this.runMode = runMode;
        this.isDryRun = runMode === "researcher-dry-run";
        this.modelPin = modelPin;
        this.journalSessionId = `${participantId}-study-runner`;
        this.phase = "consent_demographics";
        this.completedTrialCount = 0;
        this.currentTrial = null;
        this.interactionState = null;
        this.trainingCriteria = { undo: false, reject: false };
        this.breakStartedAt = null;
        this.batteryOutstanding = false;
        this.taskClock = null;
        if (!restoring) {
            this._append("study_plan_journalled", { plan: normalizedPlan(plan) });
            this._snapshot();
        }
    }

    _append(eventType, data) {
        return this.journal.append(this.journalSessionId, eventType, JSON.parse(JSON.stringify({
            ...data, runMode: this.runMode, isDryRun: this.isDryRun, modelPin: this.modelPin,
        })));
    }

    _transitionPhase(to, event) {
        const allowed = {
            consent_demographics: ["training"],
            training: ["ready_for_trial"],
            ready_for_trial: ["trial_active"],
            trial_active: ["questionnaire_paused", "post_trial_questionnaire"],
            questionnaire_paused: ["trial_active"],
            post_trial_questionnaire: ["ready_for_trial", "break", "final_desktop_battery"],
            break: ["ready_for_trial"],
            final_desktop_battery: ["complete"],
            complete: [],
        };
        if (!(allowed[this.phase] || []).includes(to))
            throw new Error(`undeclared session phase transition '${this.phase}' -> '${to}' via '${event}'`);
        const from = this.phase;
        this.phase = to;
        this._append("study_session_phase_transition", { from, to, event, at: this.now() });
        this._snapshot();
    }

    _snapshot() {
        this._append("study_session_runner_snapshot", {
            participantId: this.participantId,
            phase: this.phase,
            completedTrialCount: this.completedTrialCount,
            currentTrial: this.currentTrial,
            interactionState: this.interactionState,
            trainingCriteria: this.trainingCriteria,
            breakStartedAt: this.breakStartedAt,
            batteryOutstanding: this.batteryOutstanding,
            taskClock: this.taskClock,
            runMode: this.runMode,
            isDryRun: this.isDryRun,
            modelPin: this.modelPin,
            plan: normalizedPlan(this.plan),
        });
    }

    completeConsentAndDemographics() {
        this._transitionPhase("training", "consent_and_demographics_complete");
    }

    recordTrainingCriterion(criterion) {
        if (this.phase !== "training") throw new Error("training criteria can only be recorded during training");
        if (!Object.prototype.hasOwnProperty.call(this.trainingCriteria, criterion))
            throw new Error("training criterion must be undo or reject");
        if (!this.trainingCriteria[criterion]) {
            this.trainingCriteria[criterion] = true;
            this._append("study_training_criterion_met", { criterion, at: this.now() });
        }
        if (this.trainingCriteria.undo && this.trainingCriteria.reject)
            this._transitionPhase("ready_for_trial", "both_training_criteria_met");
        else this._snapshot();
    }

    startNextTrial() {
        if (this.phase !== "ready_for_trial") throw new Error("runner is not ready to start a trial");
        if (!(this.trainingCriteria.undo && this.trainingCriteria.reject))
            throw new Error("trial 1 is blocked until both undo and reject training criteria are met");
        const assigned = this.plan.trials[this.completedTrialCount];
        if (!assigned) throw new Error("no assigned trial remains");
        this.currentTrial = JSON.parse(JSON.stringify(assigned));
        this.interactionState = this.machine.create({ sessionId: assigned.sessionId, mode: assigned.interactionMode,
            correlationId: `${this.participantId}-${assigned.trialId}-root` });
        this.taskClock = null;
        this.batteryOutstanding = false;
        this._append("study_trial_assignment_dispatched", { assignment: this.currentTrial });
        this._transitionPhase("trial_active", "server_assignment_dispatched");
        return JSON.parse(JSON.stringify(this.currentTrial));
    }

    transitionInteraction(to, event, context = {}) {
        if (!this.interactionState) throw new Error("no active StudySessionMachine interaction");
        this.machine.transitionState(this.interactionState, to, event, context);
        this._append("interaction_state_transition", this.interactionState.transitionHistory.at(-1));
        this._snapshot();
        return this.interactionState;
    }

    markTaskCardDismissed() {
        if (this.phase !== "trial_active" || !this.currentTrial) throw new Error("no active trial card can be dismissed");
        this._append("study_task_card_dismissed", { trialId: this.currentTrial.trialId, at: this.now() });
    }

    markTaskT0() {
        if (this.phase !== "trial_active" || this.taskClock) throw new Error("task t0 requires one active unstarted trial");
        this.taskClock = { startedAt: this.now(), pauseStartedAt: null, excludedQuestionnaireMs: 0, endedAt: null };
        this._append("study_trial_t0", { trialId: this.currentTrial.trialId, at: this.taskClock.startedAt });
        this._snapshot();
    }

    markL2Trigger() {
        if (this.phase !== "trial_active" || !this.taskClock || this.taskClock.endedAt ||
            this.currentTrial.interactionMode !== "L2") throw new Error("L2 trigger requires an active L2 task clock");
        const at = this.now();
        this._append("study_l2_trigger", { trialId: this.currentTrial.trialId, at,
            triggerType: "region-entry-or-dwell-threshold" });
        return at;
    }

    beginQuestionnairePause() {
        if (this.phase !== "trial_active" || !this.taskClock || this.taskClock.endedAt)
            throw new Error("questionnaire pause requires a running t0-to-t1 clock");
        this.taskClock.pauseStartedAt = this.now();
        this._transitionPhase("questionnaire_paused", "immediate_proposal_item_presented");
    }

    endQuestionnairePause() {
        if (this.phase !== "questionnaire_paused" || this.taskClock.pauseStartedAt == null)
            throw new Error("no questionnaire pause is active");
        this.taskClock.excludedQuestionnaireMs += this.now() - this.taskClock.pauseStartedAt;
        this.taskClock.pauseStartedAt = null;
        this._transitionPhase("trial_active", "immediate_proposal_item_answered");
    }

    markTaskT1(arbitrationReason) {
        if (this.phase !== "trial_active" || !this.taskClock || this.taskClock.endedAt)
            throw new Error("task t1 requires a running, unpaused task clock");
        if (!["detector", "declared", "timeout"].includes(arbitrationReason)) throw new Error("invalid t1 arbitration reason");
        this.taskClock.endedAt = this.now();
        this.taskClock.totalTaskTimeMs = this.taskClock.endedAt - this.taskClock.startedAt - this.taskClock.excludedQuestionnaireMs;
        this.batteryOutstanding = true;
        this._append("study_trial_t1", { trialId: this.currentTrial.trialId, arbitrationReason,
            totalTaskTimeMs: this.taskClock.totalTaskTimeMs,
            excludedQuestionnaireMs: this.taskClock.excludedQuestionnaireMs, at: this.taskClock.endedAt });
        this._transitionPhase("post_trial_questionnaire", "task_t1_recorded");
    }

    completeInVrBattery() {
        if (this.phase !== "post_trial_questionnaire" || !this.batteryOutstanding)
            throw new Error("no post-trial in-VR battery is outstanding");
        this.batteryOutstanding = false;
        this.completedTrialCount += 1;
        this._append("study_in_vr_battery_complete", { trialId: this.currentTrial.trialId, at: this.now() });
        this.machine.reset(this.currentTrial.sessionId);
        this.currentTrial = null;
        this.interactionState = null;
        this.taskClock = null;
        if (BREAK_AFTER_COMPLETED_TRIALS.has(this.completedTrialCount)) {
            this.breakStartedAt = this.now();
            this.batteryOutstanding = true;
            this._transitionPhase("break", "scheduled_headset_off_break");
        } else if (this.completedTrialCount === this.plan.trials.length) {
            this.batteryOutstanding = true;
            this._transitionPhase("final_desktop_battery", "all_trials_complete");
        } else this._transitionPhase("ready_for_trial", "post_trial_battery_complete");
    }

    recordDesktopBatteryComplete() {
        if (!["break", "final_desktop_battery"].includes(this.phase) || !this.batteryOutstanding)
            throw new Error("no desktop battery is outstanding");
        this.batteryOutstanding = false;
        this._append("study_desktop_battery_complete", { phase: this.phase, afterTrial: this.completedTrialCount, at: this.now() });
        this._snapshot();
    }

    advanceAfterBreak() {
        if (this.phase !== "break") throw new Error("runner is not at a break");
        if (this.batteryOutstanding) throw new Error("desktop break battery is still outstanding");
        const remaining = this.breakMinimumMs - (this.now() - this.breakStartedAt);
        if (remaining > 0) throw new Error(`enforced break has ${remaining}ms remaining`);
        this.breakStartedAt = null;
        this._transitionPhase("ready_for_trial", "minimum_break_elapsed_and_battery_complete");
    }

    finishDebrief() {
        if (this.phase !== "final_desktop_battery" || this.batteryOutstanding)
            throw new Error("final desktop battery must be complete before debrief completion");
        this._transitionPhase("complete", "debrief_complete");
    }

    operatorDisplay() {
        const assigned = this.currentTrial || this.plan.trials[Math.min(this.completedTrialCount, this.plan.trials.length - 1)];
        const elapsedMs = this.taskClock && !this.taskClock.endedAt
            ? this.now() - this.taskClock.startedAt - this.taskClock.excludedQuestionnaireMs -
                (this.taskClock.pauseStartedAt == null ? 0 : this.now() - this.taskClock.pauseStartedAt)
            : null;
        const timeoutSeconds = assigned ? TIMEOUT_SECONDS_BY_MODE[assigned.interactionMode] : null;
        return {
            trial: `${Math.min(this.completedTrialCount + 1, 10)} of 10`,
            taskId: assigned ? assigned.taskId : null,
            taskVariant: assigned ? assigned.taskVariant : null,
            elapsedMs,
            timeoutSeconds,
            batteryOutstanding: this.batteryOutstanding,
            nextAction: this.phase,
        };
    }

    static resume({ participantId, journal, machine = new StudySessionMachine(), now = () => Date.now(),
        breakMinimumMs = DEFAULT_BREAK_MINIMUM_MS } = {}) {
        const records = journal.backfill(`${participantId}-study-runner`, 0);
        const planRecord = records.find((record) => record.eventType === "study_plan_journalled");
        const snapshots = records.filter((record) => record.eventType === "study_session_runner_snapshot");
        if (!planRecord || snapshots.length === 0) throw new Error("cannot resume without a journalled plan and runner snapshot");
        const derived = generateParticipantPlan(participantId);
        const journalledPlan = planRecord.data.plan;
        if (!samePlan(derived, journalledPlan)) throw new Error("resume refused: journalled plan differs from re-derived participant assignment");
        const snapshot = snapshots.at(-1).data;
        if (!samePlan(derived, snapshot.plan)) throw new Error("resume refused: latest snapshot plan differs from re-derived participant assignment");
        const runner = new StudySessionRunner({ participantId, plan: derived, journal, machine, now, breakMinimumMs,
            restoring: true, runMode: snapshot.runMode || "researcher-dry-run", modelPin: snapshot.modelPin || null });
        runner.phase = snapshot.phase;
        runner.completedTrialCount = snapshot.completedTrialCount;
        runner.currentTrial = snapshot.currentTrial;
        runner.trainingCriteria = snapshot.trainingCriteria;
        runner.breakStartedAt = snapshot.breakStartedAt;
        runner.batteryOutstanding = snapshot.batteryOutstanding;
        runner.taskClock = snapshot.taskClock;
        if (snapshot.interactionState && runner.currentTrial) {
            runner.interactionState = machine.create({ sessionId: snapshot.interactionState.sessionId,
                mode: snapshot.interactionState.mode, correlationId: snapshot.interactionState.correlationId,
                targetObjectId: snapshot.interactionState.targetObjectId, artifactId: snapshot.interactionState.artifactId });
            runner.interactionState.utterances = JSON.parse(JSON.stringify(snapshot.interactionState.utterances || []));
            for (const transition of snapshot.interactionState.transitionHistory || [])
                machine.transitionState(runner.interactionState, transition.to, transition.event,
                    { correlationId: snapshot.interactionState.correlationId });
            if (runner.interactionState.revisionCount !== snapshot.interactionState.revisionCount)
                throw new Error("resume refused: revision evidence does not reproduce the snapshot revision count");
        }
        runner._append("study_session_resumed", { phase: runner.phase, completedTrialCount: runner.completedTrialCount, at: now() });
        runner._snapshot();
        return runner;
    }
}

module.exports = { BREAK_AFTER_COMPLETED_TRIALS, DEFAULT_BREAK_MINIMUM_MS, TIMEOUT_SECONDS_BY_MODE,
    normalizedPlan, samePlan, StudySessionRunner };
