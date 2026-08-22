"use strict";

const SUPPORTED_MODES = new Set(["L1", "L3", "L4", "L5"]);
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
        this.bySession = new Map();
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
        const state = {
            sessionId, mode, correlationId, targetObjectId, artifactId,
            previousArtifactId: null,
            state: mode === "L1" ? "observing" : "awaiting_request",
            utterances: [], revisionCount: 0,
            createdAt: this.now(), updatedAt: this.now(), terminal: false,
        };
        this.bySession.set(sessionId, state);
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

    recordUtterance({ sessionId, text }) {
        const state = this.requireActive(sessionId);
        const utterance = typeof text === "string" ? text.trim() : "";
        if (!utterance) throw new Error("utterance text is required");
        if (state.mode === "L1") throw new Error("L1 does not accept participant speech");
        const maximum = state.mode === "L3" ? 2 : state.mode === "L5" ? 3 : Infinity;
        if (state.utterances.length >= maximum) throw new Error(`${state.mode} utterance budget exhausted`);
        state.utterances.push(utterance);
        let action;
        if (state.mode === "L3") {
            if (state.utterances.length === 1) {
                state.state = "awaiting_answer";
                action = "request_clarification";
            } else {
                state.state = "resolved";
                action = "execute_resolved_request";
            }
        } else if (state.mode === "L5") {
            if (state.utterances.length === 1) {
                state.state = "planning";
                action = "propose_initial";
            } else {
                state.revisionCount += 1;
                state.state = state.utterances.length === maximum ? "awaiting_decision" : "awaiting_revision";
                action = "revise_artifact";
            }
        } else {
            state.state = "proposing";
            action = state.revisionCount ? "revise_proposal" : "propose_initial";
        }
        state.updatedAt = this.now();
        return { ...this.snapshot(state), action, promptContext: [...state.utterances] };
    }

    recordDecision({ sessionId, decision }) {
        const state = this.requireActive(sessionId);
        const normalized = String(decision || "").toLowerCase();
        if (normalized === "revise") {
            if (!new Set(["L4", "L5"]).has(state.mode)) throw new Error("revise is available only for L4/L5");
            state.revisionCount += 1;
            state.state = "revising";
            state.updatedAt = this.now();
            return { ...this.snapshot(state), action: "await_revision_utterance" };
        }
        const terminalState = TERMINAL_DECISIONS.get(normalized);
        if (!terminalState) throw new Error(`unsupported decision '${decision}'`);
        state.state = terminalState;
        state.terminal = true;
        state.updatedAt = this.now();
        return { ...this.snapshot(state), action: "close_chain" };
    }

    recordArtifact({ sessionId, artifactId }) {
        const state = this.requireActive(sessionId);
        requiredIdentifier(artifactId, "artifactId");
        state.previousArtifactId = state.artifactId;
        state.artifactId = artifactId;
        state.updatedAt = this.now();
        return this.snapshot(state);
    }

    startL1Opportunity({ sessionId, riskScore, localOnly, reversible, persistent, artifactCount = 1 }) {
        const state = this.requireActive(sessionId);
        if (state.mode !== "L1") throw new Error("system opportunity belongs to an L1 chain");
        const eligible = Number.isFinite(riskScore) && riskScore < 0.3 && localOnly === true &&
            reversible === true && persistent === false && artifactCount === 1;
        state.state = eligible ? "executing" : "declined";
        state.terminal = !eligible;
        state.updatedAt = this.now();
        return { ...this.snapshot(state), action: eligible ? "execute_system_opportunity" : "decline_opportunity" };
    }

    markL1Applied(sessionId) {
        const state = this.requireActive(sessionId);
        if (state.mode !== "L1" || state.state !== "executing") throw new Error("L1 is not executing");
        state.state = "applied";
        state.terminal = true;
        state.updatedAt = this.now();
        return this.snapshot(state);
    }

    reset(sessionId) { return this.bySession.delete(sessionId); }

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
        };
    }
}

module.exports = { InteractionSessionStore, SUPPORTED_MODES };
