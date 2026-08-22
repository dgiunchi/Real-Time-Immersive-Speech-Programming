"use strict";

const fs = require("fs");
const path = require("path");

const CONTRACT_PATH = path.join(__dirname, "interaction_contract.v1.json");

function loadInteractionContract(contractPath = CONTRACT_PATH) {
    return JSON.parse(fs.readFileSync(contractPath, "utf8"));
}

function expandTransitions(mode) {
    const terminals = new Set(mode.terminalStates || []);
    return (mode.transitions || []).flatMap((transition) => {
        const fromStates = transition.from === "*"
            ? mode.states.filter((state) => !terminals.has(state) && state !== transition.to)
            : [transition.from];
        return fromStates.map((from) => ({ ...transition, from }));
    });
}

function pathExists({ initial, target, adjacency, forbidden = new Set() }) {
    if (forbidden.has(initial)) return false;
    const pending = [initial];
    const visited = new Set();
    while (pending.length) {
        const state = pending.shift();
        if (state === target) return true;
        if (visited.has(state)) continue;
        visited.add(state);
        for (const next of adjacency.get(state) || []) if (!forbidden.has(next)) pending.push(next);
    }
    return false;
}

function analyzeInteractionContract(contract = loadInteractionContract()) {
    const modes = {};
    let ok = true;
    for (const [modeName, mode] of Object.entries(contract.modes || {})) {
        const transitions = expandTransitions(mode);
        const terminals = new Set(mode.terminalStates || []);
        const adjacency = new Map(mode.states.map((state) => [state, []]));
        for (const transition of transitions) {
            if (adjacency.has(transition.from)) adjacency.get(transition.from).push(transition.to);
        }
        const reachable = new Set();
        const pending = [mode.initialState];
        while (pending.length) {
            const state = pending.shift();
            if (reachable.has(state)) continue;
            reachable.add(state);
            for (const next of adjacency.get(state) || []) pending.push(next);
        }
        const canReachTerminal = new Set(terminals);
        let changed = true;
        while (changed) {
            changed = false;
            for (const transition of transitions) {
                if (canReachTerminal.has(transition.to) && !canReachTerminal.has(transition.from)) {
                    canReachTerminal.add(transition.from);
                    changed = true;
                }
            }
        }
        const unreachableStates = mode.states.filter((state) => !reachable.has(state));
        const deadEnds = mode.states.filter((state) => !terminals.has(state) && (adjacency.get(state) || []).length === 0);
        const noTerminalPath = mode.states.filter((state) => !terminals.has(state) && !canReachTerminal.has(state));
        const requiredGateBypasses = [];
        if (mode.approvalRequiredBeforeCommit && mode.states.includes("approved") &&
            pathExists({ initial: mode.initialState, target: "approved", adjacency, forbidden: new Set(["awaiting_decision"]) })) {
            requiredGateBypasses.push("approved reachable without awaiting_decision");
        }
        const consentBypasses = transitions.filter((transition) =>
            transition.to === "approved" && transition.from !== "awaiting_decision");
        const previewBypasses = transitions.filter((transition) =>
            transition.to === "approved" && transition.from !== "awaiting_decision");
        const clarificationBypasses = mode.proposalRequiresState ? transitions.filter((transition) =>
            transition.to === "proposing" && transition.from !== mode.proposalRequiresState) : [];
        const report = {
            unreachableStates, deadEnds, noTerminalPath, requiredGateBypasses,
            bypassTransitions: {
                consent: consentBypasses,
                preview: previewBypasses,
                clarification: clarificationBypasses,
            },
            stateCount: mode.states.length,
            transitionCount: transitions.length,
        };
        report.ok = unreachableStates.length === 0 && deadEnds.length === 0 && noTerminalPath.length === 0 &&
            requiredGateBypasses.length === 0 && consentBypasses.length === 0 && previewBypasses.length === 0 &&
            clarificationBypasses.length === 0;
        ok = ok && report.ok;
        modes[modeName] = report;
    }
    return { ok, protocolId: contract.protocolId, methodVersion: contract.methodVersion, modes };
}

class StudySessionMachine {
    constructor({ contract = loadInteractionContract(), now = () => Date.now() } = {}) {
        this.contract = contract;
        this.now = now;
        this.sessions = new Map();
        this.graphs = new Map(Object.entries(contract.modes).map(([name, mode]) => [name, {
            mode,
            transitions: expandTransitions(mode),
        }]));
    }

    create({ sessionId, mode, correlationId, targetObjectId = null, artifactId = null }) {
        if (!this.graphs.has(mode)) throw new Error(`unsupported interaction mode '${mode}'`);
        const definition = this.graphs.get(mode).mode;
        const state = {
            sessionId, mode, correlationId, targetObjectId, artifactId,
            previousArtifactId: null, state: definition.initialState,
            utterances: [], revisionCount: 0, createdAt: this.now(), updatedAt: this.now(), terminal: false,
            transitionHistory: [],
        };
        this.sessions.set(sessionId, state);
        return state;
    }

    transitionState(state, to, event, context = {}) {
        const graph = this.graphs.get(state.mode);
        if (!graph) throw new Error(`unsupported interaction mode '${state.mode}'`);
        if (state.terminal) throw new Error(`cannot transition terminal state '${state.state}'`);
        if (context.correlationId && (graph.mode.sameCorrelationRequired || graph.mode.revisionPreservesCorrelation) &&
            context.correlationId !== state.correlationId) {
            throw new Error(`${state.mode} transition must preserve correlationId`);
        }
        const transition = graph.transitions.find((candidate) =>
            candidate.from === state.state && candidate.to === to && candidate.event === event);
        if (!transition) throw new Error(`undeclared ${state.mode} transition '${state.state}' -> '${to}' via '${event}'`);
        if (event === "cancel" && this.contract.global.cancelTerminal === true &&
            !(graph.mode.terminalStates || []).includes(to)) throw new Error("cancel must be terminal");
        if (event === "timeout" && this.contract.global.timeoutTerminal === true &&
            !(graph.mode.terminalStates || []).includes(to)) throw new Error("timeout must be terminal");
        if (to === "approved") {
            if (graph.mode.approvalRequiredBeforeCommit && state.state !== "awaiting_decision") {
                throw new Error(`${state.mode} approval requires awaiting_decision`);
            }
            if (Number.isInteger(graph.mode.requiredRevisionCount) && state.revisionCount < graph.mode.requiredRevisionCount) {
                throw new Error(`${state.mode} approval requires ${graph.mode.requiredRevisionCount} revisions`);
            }
        }
        if (event === "revision_received") state.revisionCount += 1;
        const at = this.now();
        state.transitionHistory.push({ from: state.state, to, event, at });
        state.state = to;
        state.updatedAt = at;
        state.terminal = (graph.mode.terminalStates || []).includes(to);
        return state;
    }

    assertUtteranceAllowed(state, nextCount) {
        const maximum = this.graphs.get(state.mode).mode.maximumParticipantUtterances;
        if (Number.isInteger(maximum) && nextCount > maximum) {
            throw new Error(`${state.mode} utterance budget exhausted`);
        }
        return true;
    }

    bindArtifact(state, artifactId, previousArtifactId = null) {
        const definition = this.graphs.get(state.mode).mode;
        if (definition.sameArtifactRequired && state.artifactId && artifactId !== state.artifactId &&
            previousArtifactId !== state.artifactId) {
            throw new Error(`${state.mode} artifact revision must identify the previous artifact`);
        }
        state.previousArtifactId = state.artifactId;
        state.artifactId = artifactId;
        state.updatedAt = this.now();
        return state;
    }

    assertCommitAllowed(state) {
        const definition = this.graphs.get(state.mode).mode;
        if (definition.approvalRequiredBeforeCommit && state.state !== "approved") {
            throw new Error(`${state.mode} commit requires approved state`);
        }
        return true;
    }

    reset(sessionId) {
        if (this.contract.global.resetClearsPendingState !== true) throw new Error("contract does not permit reset");
        return this.sessions.delete(sessionId);
    }
}

module.exports = {
    CONTRACT_PATH,
    loadInteractionContract,
    expandTransitions,
    analyzeInteractionContract,
    StudySessionMachine,
};
