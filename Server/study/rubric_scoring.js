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

function scoreL4({ doorFullyOpen, participantInsideApproachRegion, consentGatePresented, consentGateOutcome, qualityScore }) {
    if (!["approved", "rejected", "revised", "none"].includes(consentGateOutcome)) throw new Error("invalid L4 consentGateOutcome");
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

module.exports = { deriveImplicitBinaries, scoreL3, scoreL4, scoreL5 };
