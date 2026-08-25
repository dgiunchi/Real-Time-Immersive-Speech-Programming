"use strict";

// Deterministic ordering guard for the six step authoring pipeline.
//
// The pipeline is described in the router's system prompt and nowhere else, so
// its ordering is enforced only by the model choosing to follow instructions.
// Three of its rules are stated as mandatory yet nothing checks them:
//
//   - a dry run must precede a proposal, and the prompt says the model may not
//     skip one on its own;
//   - candidates must be ranked before one is proposed;
//   - a fresh query_scene must happen immediately before propose_artifact,
//     because candidate generation and validation can outlive the proposal
//     freshness window.
//
// A prompt cannot enforce any of these. This tracker records what actually
// happened for a correlation id and refuses a proposal that skipped a step, in
// the same spirit as mode_policy and conflict_policy: the model may decide what
// to propose, not whether the checks ran.

const { STUDY_CONDITIONS, isVerificationBypassed, SIMULATION_SKIPPED_STATUS } = require("./mode_policy");

const STEPS = Object.freeze({
    QUERY_SCENE: "query_scene",
    SIMULATE: "simulate_artifact",
    RANK: "rank_artifact_candidates",
    CONFLICT: "conflict_resolver",
    PROPOSE: "propose_artifact",
});

class PipelineTracker {
    /**
     * @param {object} options
     * @param {string} options.correlationId
     * @param {string} [options.condition] study condition; the H2 arm legitimately
     *        bypasses dry runs, but must mark them skipped rather than omit them.
     */
    constructor({ correlationId, condition = STUDY_CONDITIONS.VERIFICATION } = {}) {
        if (!correlationId) throw new Error("PipelineTracker requires a correlationId");
        this.correlationId = correlationId;
        this.condition = condition;
        this.events = [];
    }

    record(step, detail = {}) {
        if (!Object.values(STEPS).includes(step)) {
            throw new Error(`unknown pipeline step '${step}'`);
        }
        this.events.push({ step, at: this.events.length, ...detail });
        return this;
    }

    _lastIndexOf(step) {
        for (let index = this.events.length - 1; index >= 0; index -= 1) {
            if (this.events[index].step === step) return index;
        }
        return -1;
    }

    _has(step) { return this._lastIndexOf(step) !== -1; }

    /**
     * @param {object} proposal
     * @param {string} proposal.targetObjectId
     * @param {number} [proposal.candidateCount]
     * @param {string} [proposal.snapshotId] the snapshot the proposal will carry
     * @returns {{allowed: boolean, reasons: string[]}}
     */
    checkProposeAllowed({ targetObjectId, candidateCount = 1, snapshotId = null } = {}) {
        const reasons = [];

        // A dry run must have happened, or been explicitly skipped by the arm
        // that is designed to skip it. Absent and skipped are different: absent
        // means nobody looked.
        const simulateIndex = this._lastIndexOf(STEPS.SIMULATE);
        if (simulateIndex === -1) {
            if (isVerificationBypassed(this.condition)) {
                reasons.push(`no dry run recorded; the ${this.condition} arm must still record one with status ${SIMULATION_SKIPPED_STATUS}`);
            } else {
                reasons.push("no dry run was recorded before the proposal");
            }
        }

        // Multi candidate sets must be ranked, otherwise the selection is
        // unexplained and H4's best of N claim has no basis.
        if (candidateCount > 1 && !this._has(STEPS.RANK)) {
            reasons.push(`${candidateCount} candidates were generated but none were ranked`);
        }

        // The conflict check must have run, and its verdict is enforced by
        // conflict_policy rather than re-decided here.
        if (!this._has(STEPS.CONFLICT)) {
            reasons.push("the conflict resolver did not run before the proposal");
        }

        // The mandatory freshness refresh: a query_scene must be the most recent
        // grounding step, after any simulate or rank, for this target.
        const refreshIndex = this._lastIndexOf(STEPS.QUERY_SCENE);
        if (refreshIndex === -1) {
            reasons.push("no query_scene refresh was recorded before the proposal");
        } else {
            const rankIndex = this._lastIndexOf(STEPS.RANK);
            if (refreshIndex < Math.max(simulateIndex, rankIndex)) {
                reasons.push("the last query_scene predates the dry run or ranking, so the proposal is not grounded in a fresh snapshot");
            }
            const refresh = this.events[refreshIndex];
            if (targetObjectId && refresh.targetObjectId && refresh.targetObjectId !== targetObjectId) {
                reasons.push(`the refresh grounded '${refresh.targetObjectId}' but the proposal targets '${targetObjectId}'`);
            }
            if (snapshotId && refresh.snapshotId && refresh.snapshotId !== snapshotId) {
                reasons.push("the proposal carries a different snapshotId than the refresh that grounded it");
            }
        }

        return { allowed: reasons.length === 0, reasons, correlationId: this.correlationId };
    }

    assertProposeAllowed(proposal = {}) {
        const checked = this.checkProposeAllowed(proposal);
        if (!checked.allowed) {
            throw new Error(`pipeline order violated for '${this.correlationId}': ${checked.reasons.join("; ")}`);
        }
        return checked;
    }

    summary() {
        return { correlationId: this.correlationId, condition: this.condition, steps: this.events.map((event) => event.step) };
    }
}

module.exports = { PipelineTracker, STEPS };
