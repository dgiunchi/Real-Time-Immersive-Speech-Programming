"use strict";

function deriveImplicitBinaries(evidence, contextuallyAdmissible) {
    const grounded = evidence.targetInCurrentTaskRegion === true && evidence.stableObjectIdValid === true;
    const inEnvelope = evidence.local === true && evidence.reversible === true && evidence.persistent === false &&
        Number(evidence.artifactCount) <= 1 && Number(evidence.riskScore) < 0.3;
    const deadline = Math.min(
        Number.isFinite(Number(evidence.regionExitAt)) ? Number(evidence.regionExitAt) : Infinity,
        Number.isFinite(Number(evidence.subtaskCompletedAt)) ? Number(evidence.subtaskCompletedAt) : Infinity);
    const timely = Number.isFinite(Number(evidence.visibleChangeAt)) && Number(evidence.visibleChangeAt) <= deadline;
    const coded = contextuallyAdmissible === true;
    return {
        grounded, inEnvelope, timely, contextuallyAdmissible: coded,
        taskSuccess: grounded && inEnvelope && timely && coded,
        sources: { grounded: "log-derived", inEnvelope: "log-derived", timely: "log-derived", contextuallyAdmissible: "blind-rater" },
    };
}

function scoreL3({ padReached, goalPad, donePressed, repairInitiator, qualityScore }) {
    if (!["system", "participant", "none"].includes(repairInitiator)) throw new Error("invalid L3 repairInitiator");
    if (![0, 1, 2].includes(qualityScore)) throw new Error("L3 qualityScore must be 0, 1, or 2");
    return { taskSuccess: padReached === goalPad && donePressed === true, padReached, repairInitiator, qualityScore };
}

// The L4 interaction contract can terminate a chain in approved, rejected,
// timed_out, undone or cancelled, but only the first two had a scoring
// counterpart. A trial that timed out, which the protocol explicitly
// anticipates, could not be scored at all.
//
// timed_out maps to rejected because the protocol states that silence or a
// timeout at the consent gate defaults to rejection. undone and cancelled keep
// their own outcomes rather than collapsing into rejected: an approval the
// participant then undid, and a chain abandoned before any decision, are
// different events, and flattening them would lose that distinction before the
// analysis can decide how to treat them.
const CONSENT_GATE_OUTCOME_BY_TERMINAL_STATE = Object.freeze({
    approved: "approved",
    rejected: "rejected",
    timed_out: "rejected",
    undone: "undone",
    cancelled: "cancelled",
});

const VALID_CONSENT_GATE_OUTCOMES = Object.freeze([
    "approved", "rejected", "revised", "none", "undone", "cancelled",
]);

// Maps a terminal state from the L4 interaction chain to its scoring outcome.
// Throws on an unmapped state so a state added to the contract cannot be
// silently absorbed without deciding how it should be scored.
function consentGateOutcomeFor(terminalState) {
    const outcome = CONSENT_GATE_OUTCOME_BY_TERMINAL_STATE[terminalState];
    if (!outcome) {
        throw new Error(
            `no consent gate outcome is defined for L4 terminal state '${terminalState}'; ` +
            "add it to CONSENT_GATE_OUTCOME_BY_TERMINAL_STATE with an explicit scoring decision"
        );
    }
    return outcome;
}

function scoreL4({ doorFullyOpen, participantInsideApproachRegion, consentGatePresented, consentGateOutcome, qualityScore }) {
    if (!VALID_CONSENT_GATE_OUTCOMES.includes(consentGateOutcome)) throw new Error("invalid L4 consentGateOutcome");
    if (![0, 1, 2].includes(qualityScore)) throw new Error("L4 qualityScore must be 0, 1, or 2");
    return { taskSuccess: doorFullyOpen === true && participantInsideApproachRegion === true,
        consentGatePresented: Boolean(consentGatePresented), consentGateOutcome, qualityScore };
}

function scoreL5({ slowerStepsRevision, resetAfterFinishRevision, sequenceRuns, priorRequirementRestatementCount }) {
    for (const score of [slowerStepsRevision, resetAfterFinishRevision])
        if (![0, 1, 2].includes(score)) throw new Error("each L5 revision score must be 0, 1, or 2");
    if (!Number.isInteger(priorRequirementRestatementCount) || priorRequirementRestatementCount < 0)
        throw new Error("priorRequirementRestatementCount must be a non-negative integer");
    return {
        taskSuccess: slowerStepsRevision >= 1 && resetAfterFinishRevision >= 1 && sequenceRuns === true,
        slowerStepsRevision, resetAfterFinishRevision, sequenceRuns: Boolean(sequenceRuns), priorRequirementRestatementCount,
    };
}

module.exports = { deriveImplicitBinaries, scoreL3, scoreL4, scoreL5,
    consentGateOutcomeFor, CONSENT_GATE_OUTCOME_BY_TERMINAL_STATE, VALID_CONSENT_GATE_OUTCOMES };
