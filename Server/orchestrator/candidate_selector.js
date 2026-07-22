"use strict";

const LIFECYCLE_OPERATIONS = Object.freeze(["create", "edit", "remove"]);

function validateLifecycle(candidate) {
    const operation = candidate.operation || "create";
    const reasons = [];
    if (!LIFECYCLE_OPERATIONS.includes(operation)) reasons.push(`unsupported operation '${operation}'`);
    if (["create", "edit"].includes(operation) && !candidate.code) reasons.push(`${operation} requires code`);
    if (["edit", "remove"].includes(operation) && !candidate.existingArtifactId) reasons.push(`${operation} requires existingArtifactId`);
    if (operation === "remove" && candidate.code) reasons.push("remove must not carry replacement code");
    return { accepted: reasons.length === 0, reasons, operation };
}

function preferenceFit(candidate, profile) {
    if (!profile || !profile.learned) return 0;
    const preferred = profile.learned.preferredAuthoringMode;
    let fit = preferred && preferred === candidate.authoringMode ? 1 : 0;
    if (typeof profile.learned.meanAcceptedRisk === "number" && typeof candidate.riskScore === "number") {
        fit += Math.max(0, 1 - Math.abs(profile.learned.meanAcceptedRisk - candidate.riskScore));
    }
    return fit / 2;
}

function contextFit(candidate, experienceContext) {
    if (!experienceContext || !experienceContext.mode || !candidate.experienceMode) return 0;
    return candidate.experienceMode === experienceContext.mode ? 1 : -1;
}

function scoreCandidate(candidate, { profile, experienceContext } = {}) {
    const lifecycle = validateLifecycle(candidate);
    const simulationPassed = candidate.simulationStatus === "simulated";
    const validationPassed = candidate.validationState === "accepted";
    if (!lifecycle.accepted || !simulationPassed || !validationPassed) {
        return { eligible: false, score: Number.NEGATIVE_INFINITY, lifecycle, preferenceFit: 0, contextFit: 0 };
    }
    const risk = typeof candidate.riskScore === "number" ? candidate.riskScore : 1;
    const userFit = preferenceFit(candidate, profile);
    const activityFit = contextFit(candidate, experienceContext);
    const score = 100 - (risk * 40) + (userFit * 15) + (activityFit * 10);
    return { eligible: true, score: Math.round(score * 100) / 100, lifecycle, preferenceFit: userFit, contextFit: activityFit };
}

function rankCandidates(candidates, context = {}) {
    if (!Array.isArray(candidates) || candidates.length < 2) throw new Error("rankCandidates requires at least two candidates");
    const ranked = candidates.map((candidate, index) => ({
        ...candidate,
        candidateId: candidate.candidateId || `candidate-${index + 1}`,
        ranking: scoreCandidate(candidate, context),
    })).sort((a, b) => b.ranking.score - a.ranking.score || a.candidateId.localeCompare(b.candidateId));
    const selected = ranked.find((candidate) => candidate.ranking.eligible) || null;
    return { selected, ranked, rejected: ranked.filter((candidate) => !selected || candidate.candidateId !== selected.candidateId) };
}

module.exports = { LIFECYCLE_OPERATIONS, validateLifecycle, scoreCandidate, rankCandidates };
