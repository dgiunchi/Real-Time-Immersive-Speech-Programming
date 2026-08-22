"use strict";

const { StudySessionMachine, loadInteractionContract } = require("./study_session_machine");

const SUPPORTED_MODES = new Set(Object.keys(loadInteractionContract().modes));
const TERMINAL_DECISIONS = new Map([
    ["approve", "approved"], ["approved", "approved"],
    ["reject", "rejected"], ["rejected", "rejected"],
    ["timeout", "timed_out"], ["undo", "undone"], ["cancel", "cancelled"],
]);

function requiredIdentifier(value, name) {
    if (typeof value !== "string" || !value.trim()) throw new Error(`${name} is required`);
    return value;
}

class InteractionSessionStore {
    constructor({ now = () => Date.now() } = {}) {
        this.now = now;
        this.machine = new StudySessionMachine({ now });
        this.bySession = this.machine.sessions;
    }

    begin({ sessionId, mode, correlationId, targetObjectId = null, artifactId = null }) {
        requiredIdentifier(sessionId, "sessionId");
        requiredIdentifier(correlationId, "correlationId");
        if (!SUPPORTED_MODES.has(mode)) throw new Error(`unsupported interaction mode '${mode}'`);
        const existing = this.bySession.get(sessionId);
        if (existing && !existing.terminal) {
            if (existing.mode !== mode) throw new Error("cannot change interaction mode inside an active chain");
            if (targetObjectId && existing.targetObjectId && targetObjectId !== existing.targetObjectId) {
                throw new Error("cannot change target object inside an active chain");
            }
            return this.snapshot(existing);
        }
        const state = this.machine.create({ sessionId, mode, correlationId, targetObjectId, artifactId });
        return this.snapshot(state);
    }

    active(sessionId) {
        const state = this.bySession.get(sessionId);
        return state && !state.terminal ? this.snapshot(state) : null;
    }

    correlationFor(sessionId) {
        const active = this.active(sessionId);
        return active ? active.correlationId : null;
    }

    sessionForCorrelation(correlationId) {
        for (const [sessionId, state] of this.bySession.entries()) {
            if (!state.terminal && state.correlationId === correlationId) return sessionId;
        }
        return null;
    }

    recordUtterance({ sessionId, text }) {
        const state = this.requireActive(sessionId);
        const utterance = typeof text === "string" ? text.trim() : "";
        if (!utterance) throw new Error("utterance text is required");
        if (["L1", "L2"].includes(state.mode)) throw new Error(`${state.mode} does not accept participant speech`);
        this.machine.assertUtteranceAllowed(state, state.utterances.length + 1);
        const maximum = this.machine.graphs.get(state.mode).mode.maximumParticipantUtterances || Infinity;
        state.utterances.push(utterance);
        let action;
        if (state.mode === "L3") {
            if (state.utterances.length === 1) {
                this.machine.transitionState(state, "clarification_required", "request_received", { correlationId: state.correlationId });
                this.machine.transitionState(state, "awaiting_answer", "clarification_requested", { correlationId: state.correlationId });
                action = "request_clarification";
            } else {
                this.machine.transitionState(state, "resolved", "answer_received", { correlationId: state.correlationId });
                action = "execute_resolved_request";
            }
        } else if (state.mode === "L5") {
            if (state.utterances.length === 1) {
                this.machine.transitionState(state, "planning", "request_received", { correlationId: state.correlationId });
                this.machine.transitionState(state, "awaiting_revision", "proposal_previewed", { correlationId: state.correlationId });
                action = "propose_initial";
            } else {
                this.machine.transitionState(state, "revising", "revision_received", { correlationId: state.correlationId });
                this.machine.transitionState(state,
                    state.revisionCount >= this.machine.graphs.get(state.mode).mode.requiredRevisionCount
                        ? "awaiting_decision" : "awaiting_revision",
                    state.revisionCount >= this.machine.graphs.get(state.mode).mode.requiredRevisionCount
                        ? "revision_complete" : "revision_requested",
                    { correlationId: state.correlationId });
                action = "revise_artifact";
            }
        } else {
            if (state.state === "awaiting_request") {
                this.machine.transitionState(state, "proposing", "request_received");
            } else {
                this.machine.transitionState(state, "proposing", "revision_received");
            }
            this.machine.transitionState(state, "awaiting_decision", "proposal_previewed");
            action = state.revisionCount ? "revise_proposal" : "propose_initial";
        }
        return { ...this.snapshot(state), action, promptContext: [...state.utterances] };
    }

    recordDecision({ sessionId, decision }) {
        const state = this.requireActive(sessionId);
        const normalized = String(decision || "").toLowerCase();
        if (normalized === "revise") {
            if (!new Set(["L4", "L5"]).has(state.mode)) throw new Error("revise is available only for L4/L5");
            this.machine.transitionState(state, "revising", "revise", { correlationId: state.correlationId });
            return { ...this.snapshot(state), action: "await_revision_utterance" };
        }
        const terminalState = TERMINAL_DECISIONS.get(normalized);
        if (!terminalState) throw new Error(`unsupported decision '${decision}'`);
        const decisionEvent = new Map([
            ["approve", "approve"], ["approved", "approve"], ["reject", "reject"], ["rejected", "reject"],
            ["timeout", "timeout"], ["undo", "undo"], ["cancel", "cancel"],
        ]).get(normalized);
        this.machine.transitionState(state, terminalState, decisionEvent,
            { correlationId: state.correlationId });
        return { ...this.snapshot(state), action: "close_chain" };
    }

    recordArtifact({ sessionId, artifactId, previousArtifactId = null }) {
        const state = this.requireActive(sessionId);
        requiredIdentifier(artifactId, "artifactId");
        this.machine.bindArtifact(state, artifactId, previousArtifactId);
        return this.snapshot(state);
    }

    startL1Opportunity({ sessionId, riskScore, localOnly, reversible, persistent, artifactCount = 1 }) {
        const state = this.requireActive(sessionId);
        if (state.mode !== "L1") throw new Error("system opportunity belongs to an L1 chain");
        this.machine.transitionState(state, "opportunity_detected", "opportunity_detected");
        const limits = this.machine.graphs.get("L1").mode.automaticConstraints;
        const eligible = Number.isFinite(riskScore) && riskScore < limits.maximumRiskScoreExclusive &&
            localOnly === limits.localOnly && reversible === limits.reversible && persistent === limits.persistent &&
            artifactCount <= limits.maximumArtifacts;
        this.machine.transitionState(state, eligible ? "executing" : "declined", eligible ? "execute" : "decline");
        return { ...this.snapshot(state), action: eligible ? "execute_system_opportunity" : "decline_opportunity" };
    }

    markL1Applied(sessionId) {
        const state = this.requireActive(sessionId);
        if (state.mode !== "L1" || state.state !== "executing") throw new Error("L1 is not executing");
        this.machine.transitionState(state, "applied", "apply");
        return this.snapshot(state);
    }

    startL2Context({ sessionId, eventType = "region_entered", riskScore, localOnly, reversible,
        persistent, artifactCount = 1 }) {
        const state = this.requireActive(sessionId);
        if (state.mode !== "L2") throw new Error("context trigger belongs to an L2 chain");
        if (eventType !== "region_entered") throw new Error("L2 requires a distinct region_entered event");
        this.machine.transitionState(state, "region_entered", "region_entered");
        this.machine.transitionState(state, "opportunity_detected", "opportunity_detected");
        const limits = this.machine.graphs.get("L2").mode.automaticConstraints;
        const eligible = Number.isFinite(riskScore) && riskScore < limits.maximumRiskScoreExclusive &&
            localOnly === limits.localOnly && reversible === limits.reversible && persistent === limits.persistent &&
            artifactCount <= limits.maximumArtifacts;
        this.machine.transitionState(state, eligible ? "executing" : "declined", eligible ? "execute" : "decline");
        return { ...this.snapshot(state), action: eligible ? "execute_context_opportunity" : "decline_opportunity" };
    }

    markL2Applied(sessionId) {
        const state = this.requireActive(sessionId);
        if (state.mode !== "L2" || state.state !== "executing") throw new Error("L2 is not executing");
        this.machine.transitionState(state, "applied", "apply");
        return this.snapshot(state);
    }

    assertCommitAllowed(sessionId) {
        return this.machine.assertCommitAllowed(this.bySession.get(sessionId));
    }

    reset(sessionId) { return this.machine.reset(sessionId); }

    requireActive(sessionId) {
        const state = this.bySession.get(sessionId);
        if (!state || state.terminal) throw new Error(`no active interaction chain for '${sessionId}'`);
        return state;
    }

    snapshot(state) {
        return {
            sessionId: state.sessionId, mode: state.mode, correlationId: state.correlationId,
            targetObjectId: state.targetObjectId, artifactId: state.artifactId,
            previousArtifactId: state.previousArtifactId, state: state.state,
            utteranceCount: state.utterances.length, revisionCount: state.revisionCount,
            createdAt: state.createdAt, updatedAt: state.updatedAt, terminal: state.terminal,
            transitionHistory: state.transitionHistory.map((transition) => ({ ...transition })),
        };
    }
}

module.exports = { InteractionSessionStore, SUPPORTED_MODES };
