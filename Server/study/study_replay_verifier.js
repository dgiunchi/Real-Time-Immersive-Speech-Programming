"use strict";

const {
    loadInteractionContract,
    expandTransitions,
    analyzeInteractionContract,
} = require("./study_session_machine");

const IDENTITY_FIELDS = Object.freeze([
    "participantId", "sessionId", "trialId", "condition", "taskId", "interactionMode",
]);

function eventTime(event) {
    if (Number.isFinite(event.at)) return event.at;
    if (Number.isFinite(event.timestamp)) return event.timestamp;
    const parsed = Date.parse(event.timestampUtc || "");
    return Number.isFinite(parsed) ? parsed : NaN;
}

function replayStudyJournal({ events, plans, contract = loadInteractionContract() }) {
    if (!Array.isArray(events)) throw new Error("events must be an array");
    const planList = Array.isArray(plans) ? plans : [plans].filter(Boolean);
    const planByParticipant = new Map(planList.map((plan) => [plan.participantId, plan]));
    const findings = [];
    const fail = (id, detail, event = null) => findings.push({ id, detail, event });
    const activeBySession = new Map();
    const activeGlobal = new Set();
    const starts = new Map();
    const ends = new Map();
    const partial = new Set();
    const lastTimeByCorrelation = new Map();
    const lastStateByCorrelation = new Map();
    const interrupted = new Map();
    const visitedStates = new Map();
    const visitedTransitions = new Map();
    const transitionSets = new Map();

    for (const [mode, definition] of Object.entries(contract.modes)) {
        visitedStates.set(mode, new Set());
        visitedTransitions.set(mode, new Set());
        transitionSets.set(mode, new Set(expandTransitions(definition)
            .map((transition) => `${transition.from}|${transition.to}|${transition.event}`)));
    }

    for (const event of events) {
        const at = eventTime(event);
        if (!event.correlationId || !Number.isFinite(at)) continue;
        const previous = lastTimeByCorrelation.get(event.correlationId);
        if (Number.isFinite(previous) && at < previous) {
            fail("correlation-time-monotonic", `${event.correlationId}: ${at} < ${previous}`, event);
        }
        lastTimeByCorrelation.set(event.correlationId, at);
    }
    lastTimeByCorrelation.clear();

    const ordered = events.map((event, index) => ({ event, index, at: eventTime(event) }))
        .sort((left, right) => (left.at - right.at) || (left.index - right.index));
    for (const { event, at } of ordered) {
        const key = event.participantId && event.trialId ? `${event.participantId}|${event.trialId}` : null;
        if (event.eventType === "study_trial_started") {
            if (activeGlobal.size) fail("one-active-trial", `opened ${event.trialId} while ${[...activeGlobal].join(",")} active`, event);
            if (starts.has(key)) fail("duplicate-trial-start", key, event);
            starts.set(key, event);
            activeBySession.set(event.sessionId, event);
            activeGlobal.add(key);
            if (visitedStates.has(event.interactionMode)) visitedStates.get(event.interactionMode)
                .add(contract.modes[event.interactionMode].initialState);
            continue;
        }

        const active = activeBySession.get(event.sessionId);
        if (event.studyEvent) {
            for (const field of IDENTITY_FIELDS) if (!event[field]) fail("measured-event-identity", `${event.eventType} missing ${field}`, event);
            if (!active && event.eventType !== "study_trial_ended") fail("measured-event-outside-trial", event.eventType, event);
            if (active) {
                if (event.trialId !== active.trialId) fail("event-trial-mismatch", `${event.trialId} != ${active.trialId}`, event);
                if (event.condition !== active.condition) fail("condition-changed", `${event.condition} != ${active.condition}`, event);
            }
        } else if (active && event.correlationId) {
            fail("runtime-event-unjoined", `${event.eventType || "unknown"} occurred during ${active.trialId}`, event);
        }

        if (event.eventType === "study_trial_partial" || event.eventType === "study_trial_interrupted") {
            if (key) partial.add(key);
        }
        if (event.eventType === "study_session_interrupted") interrupted.set(event.participantId, event);
        if (event.eventType === "study_session_resumed") {
            const prior = interrupted.get(event.participantId);
            const plan = planByParticipant.get(event.participantId);
            if (!prior || !plan || event.assignmentFingerprint !== prior.assignmentFingerprint ||
                event.assignmentFingerprint !== JSON.stringify(plan.assignment)) {
                fail("resume-assignment-mismatch", event.participantId, event);
            }
            interrupted.delete(event.participantId);
        }

        if (event.fromState && event.toState && event.transitionEvent) {
            const mode = event.interactionMode;
            const transitionKey = `${event.fromState}|${event.toState}|${event.transitionEvent}`;
            if (!transitionSets.has(mode) || !transitionSets.get(mode).has(transitionKey)) {
                fail("undeclared-transition", `${mode}:${transitionKey}`, event);
            } else {
                visitedStates.get(mode).add(event.fromState);
                visitedStates.get(mode).add(event.toState);
                visitedTransitions.get(mode).add(transitionKey);
            }
            const previousState = lastStateByCorrelation.get(event.correlationId);
            if (previousState && previousState !== event.fromState) {
                fail("transition-chain-discontinuity", `${previousState} != ${event.fromState}`, event);
            }
            lastStateByCorrelation.set(event.correlationId, event.toState);
        }

        if (event.eventType === "study_trial_ended") {
            if (!active) fail("trial-ended-without-start", key, event);
            if (ends.has(key)) fail("duplicate-trial-completion", key, event);
            ends.set(key, event);
            activeBySession.delete(event.sessionId);
            activeGlobal.delete(key);
        }
    }

    for (const [key] of starts) {
        if (!ends.has(key) && !partial.has(key)) fail("partial-trial-unmarked", key);
    }
    for (const plan of planList) {
        const planned = plan.trials.map((trial) => trial.trialId);
        const journalled = [...starts.values()].filter((event) => event.participantId === plan.participantId)
            .sort((left, right) => left.sequenceIndex - right.sequenceIndex).map((event) => event.trialId);
        if (JSON.stringify(planned) !== JSON.stringify(journalled)) {
            fail("planned-journalled-trials-match", `${plan.participantId}: planned=${planned.join(",")} journalled=${journalled.join(",")}`);
        }
        const planByTrial = new Map(plan.trials.map((trial) => [trial.trialId, trial]));
        for (const started of starts.values()) {
            if (started.participantId !== plan.participantId) continue;
            const plannedTrial = planByTrial.get(started.trialId);
            if (!plannedTrial || plannedTrial.condition !== started.condition || plannedTrial.taskId !== started.taskId) {
                fail("planned-condition-sequence", `${plan.participantId}:${started.trialId}`, started);
            }
        }
    }
    for (const participantId of interrupted.keys()) fail("interrupted-session-unresolved", participantId);

    const graph = analyzeInteractionContract(contract);
    const coverage = {};
    for (const [mode, definition] of Object.entries(contract.modes)) {
        const states = visitedStates.get(mode);
        const transitions = visitedTransitions.get(mode);
        const dead = new Set(graph.modes[mode].deadEnds);
        coverage[mode] = {
            unvisitedStates: definition.states.filter((state) => !states.has(state))
                .map((state) => ({ state, classification: dead.has(state) ? "dead" : "untested" })),
            unvisitedTransitions: expandTransitions(definition)
                .filter((transition) => !transitions.has(`${transition.from}|${transition.to}|${transition.event}`))
                .map((transition) => ({ ...transition, classification: "untested" })),
        };
    }
    return {
        ok: findings.length === 0,
        findings,
        coverage,
        coverageComplete: Object.values(coverage).every((mode) =>
            mode.unvisitedStates.length === 0 && mode.unvisitedTransitions.length === 0),
        trialCount: starts.size,
    };
}

module.exports = { IDENTITY_FIELDS, eventTime, replayStudyJournal };
