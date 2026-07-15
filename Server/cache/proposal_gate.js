"use strict";

// Proposal Gate (main.tex, Cache Exchange Layer §) - a compare-and-swap-style
// freshness/consistency check run BEFORE the backend even sends a CommitRequest to
// Unity. This is advisory/defense-in-depth, NOT the authoritative gate: "Unity
// decides whether a proposal is fresh, valid, consent-compatible, and safe to
// preview/commit" (the task's own architectural principle). Unity's own handler
// re-runs an equivalent check against its live LocalXRCache at commit time and has
// final say - this class exists so obviously-stale proposals are rejected fast,
// with a structured reason, instead of wasting a round trip.

const DEFAULT_MAX_SNAPSHOT_AGE_MS_BY_MODE = Object.freeze({
    automatic: 2000,
    semi_auto_confirm: 15000,
    semi_auto_steer: 30000,
});

class ProposalGate {
    constructor({ workingCache, reconciler, maxSnapshotAgeMsByMode } = {}) {
        if (!workingCache || !reconciler) throw new Error("ProposalGate requires workingCache and reconciler");
        this.workingCache = workingCache;
        this.reconciler = reconciler;
        this.maxSnapshotAgeMsByMode = maxSnapshotAgeMsByMode || DEFAULT_MAX_SNAPSHOT_AGE_MS_BY_MODE;
    }

    // Every field here mirrors the spec's checklist. Returns { accepted, reasons,
    // checkedAt } - reasons is a list (not just the first failure) so rejection
    // logs/study data show every violated invariant, not just whichever was
    // checked first.
    checkProposal({
        correlationId,
        targetObjectId,
        sceneEpoch,
        objectRevision,
        snapshotId,
        snapshotTakenAt,
        authoringMode,
        consentRoute,
        validationState,
    } = {}) {
        const reasons = [];

        if (!correlationId) {
            reasons.push("missing correlationId");
        } else if (this.reconciler.isInvalidated(correlationId)) {
            reasons.push(`correlationId invalidated: ${this.reconciler.getInvalidationReason(correlationId)}`);
        }

        const cached = targetObjectId ? this.workingCache.getByObjectId(targetObjectId) : null;
        if (!targetObjectId || !cached) {
            reasons.push("target object not found in Agent Working Cache");
        }

        const currentEpoch = this.workingCache.getSceneEpoch();
        if (currentEpoch && sceneEpoch && sceneEpoch !== currentEpoch) {
            reasons.push(`sceneEpoch mismatch: proposal=${sceneEpoch} current=${currentEpoch}`);
        }

        if (cached && objectRevision != null && cached.objectRevision != null && objectRevision !== cached.objectRevision) {
            reasons.push(`objectRevision mismatch: proposal=${objectRevision} current=${cached.objectRevision}`);
        }

        const maxAgeMs = this.maxSnapshotAgeMsByMode[authoringMode] ?? this.maxSnapshotAgeMsByMode.semi_auto_confirm;
        if (snapshotTakenAt != null) {
            const ageMs = Date.now() - snapshotTakenAt;
            if (ageMs > maxAgeMs) {
                reasons.push(`snapshot too old for '${authoringMode || "default"}' mode: ageMs=${ageMs} maxAgeMs=${maxAgeMs}`);
            }
        } else {
            reasons.push("missing snapshotTakenAt - cannot assess freshness");
        }

        if (!snapshotId) {
            reasons.push("missing snapshotId");
        }

        if (!consentRoute) {
            reasons.push("missing consent route");
        }

        if (validationState !== "accepted") {
            reasons.push(`validation state is '${validationState || "unknown"}', not 'accepted'`);
        }

        return { accepted: reasons.length === 0, reasons, checkedAt: Date.now() };
    }
}

module.exports = { ProposalGate, DEFAULT_MAX_SNAPSHOT_AGE_MS_BY_MODE };
