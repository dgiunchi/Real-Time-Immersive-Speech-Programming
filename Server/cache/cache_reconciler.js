"use strict";

// Cache Reconciler (main.tex, Cache Exchange Layer §): the judgment layer between
// raw incoming SceneDelta/CacheSnapshot envelopes and the Agent Working Cache /
// Event Journal. Detects gaps, duplicates, staleness, epoch changes, and object
// revision conflicts, and recommends (but does not itself send) recovery actions -
// requesting backfill, asking Unity for a fresh snapshot, or invalidating pending
// proposals. Sending the actual recovery messages is the caller's job (see
// Server/mcp/unity_scene_bridge/server.js, which owns the bridge connection).

class CacheReconciler {
    constructor({ workingCache, journal }) {
        if (!workingCache || !journal) throw new Error("CacheReconciler requires workingCache and journal");
        this.workingCache = workingCache;
        this.journal = journal;
        this.seenSeqsBySession = new Map(); // sessionId -> Set<deltaSeq> actually accepted (not just "highest seen")
        this.highWaterMarkBySession = new Map(); // sessionId -> highest deltaSeq ever seen (for gap detection)
        this.pendingCorrelations = new Map(); // correlationId -> sessionId
        this.invalidatedCorrelations = new Map(); // correlationId -> reason
    }

    // Processes one inbound SceneDelta or CacheSnapshot envelope. Returns
    // { outcome, detail, recommendedAction }. outcome is one of: "accepted",
    // "duplicate", "stale". A gap, epoch change, or revision supersession can
    // accompany "accepted" (the delta is still journaled either way; these flags
    // just tell the caller whether to also trigger recovery). isBackfill=true marks
    // this envelope as a recovered (not live) delta - it can still be older than the
    // current working-cache revision without that being treated as a problem;
    // AgentWorkingCache's own monotonic safety net (see acceptState) handles not
    // regressing "current" state when a backfilled delta arrives late.
    reconcileDelta(envelope, { isBackfill = false } = {}) {
        const sessionId = envelope.sessionId || "default";
        const isSnapshot = envelope.type === "CacheSnapshot";

        if (!this.seenSeqsBySession.has(sessionId)) this.seenSeqsBySession.set(sessionId, new Set());
        const seenSet = this.seenSeqsBySession.get(sessionId);
        const highWaterMark = this.highWaterMarkBySession.get(sessionId) || 0;

        if (!isSnapshot && envelope.deltaSeq != null && seenSet.has(envelope.deltaSeq)) {
            return { outcome: "duplicate", detail: { deltaSeq: envelope.deltaSeq, highWaterMark }, recommendedAction: null };
        }

        if (!isBackfill && envelope.ttlMs != null && envelope.timestamp != null) {
            const ageMs = Date.now() - envelope.timestamp;
            if (ageMs > envelope.ttlMs) {
                return { outcome: "stale", detail: { ageMs, ttlMs: envelope.ttlMs }, recommendedAction: "snapshot" };
            }
        }

        let gap = null;
        if (!isSnapshot && !isBackfill && envelope.deltaSeq != null && highWaterMark > 0 && envelope.deltaSeq > highWaterMark + 1) {
            gap = { sessionId, fromSeq: highWaterMark + 1, toSeq: envelope.deltaSeq - 1 };
        }

        const knownEpoch = this.workingCache.getSceneEpoch();
        const epochChanged = !!(knownEpoch && envelope.sceneEpoch && envelope.sceneEpoch !== knownEpoch);

        if (envelope.deltaSeq != null) {
            seenSet.add(envelope.deltaSeq);
            this.highWaterMarkBySession.set(sessionId, Math.max(highWaterMark, envelope.deltaSeq));
        }

        const acceptResult = this.workingCache.acceptState(
            {
                stableObjectId: envelope.stableObjectId,
                objectRevision: envelope.objectRevision,
                sceneEpoch: envelope.sceneEpoch,
                snapshotId: envelope.snapshotId,
                deltaSeq: envelope.deltaSeq,
                label: envelope.payload && envelope.payload.tag,
                region: envelope.payload && envelope.payload.region,
                payload: envelope.payload,
            },
            { isSnapshot }
        );
        this.journal.append(sessionId, isSnapshot ? "snapshot" : "delta", envelope);

        let invalidatedCount = 0;
        if (epochChanged) {
            invalidatedCount = this.invalidatePendingForSession(sessionId, `sceneEpoch changed to ${envelope.sceneEpoch}`);
        }

        return {
            outcome: "accepted",
            detail: { gap, epochChanged, invalidatedCount, supersededByNewer: !!acceptResult.supersededByNewer },
            recommendedAction: gap ? "backfill" : null,
        };
    }

    // Missing-detail check: does the working cache know enough about this object to
    // ground a proposal, or does the caller need a DetailRequest first?
    hasSufficientDetail(stableObjectId) {
        const entry = this.workingCache.getByObjectId(stableObjectId);
        return !!(entry && entry.payload);
    }

    markProposalPending(correlationId, sessionId) {
        if (!correlationId) return;
        this.pendingCorrelations.set(correlationId, sessionId || "default");
        this.invalidatedCorrelations.delete(correlationId);
    }

    invalidateProposal(correlationId, reason) {
        if (!correlationId) return;
        const sessionId = this.pendingCorrelations.get(correlationId) || "default";
        this.invalidatedCorrelations.set(correlationId, reason);
        this.pendingCorrelations.delete(correlationId);
        this.journal.append(sessionId, "invalidation", { correlationId, reason });
    }

    invalidatePendingForSession(sessionId, reason) {
        let count = 0;
        for (const [correlationId, cSessionId] of this.pendingCorrelations) {
            if (cSessionId === sessionId) {
                this.invalidatedCorrelations.set(correlationId, reason);
                this.pendingCorrelations.delete(correlationId);
                count += 1;
            }
        }
        return count;
    }

    isInvalidated(correlationId) {
        return this.invalidatedCorrelations.has(correlationId);
    }

    getInvalidationReason(correlationId) {
        return this.invalidatedCorrelations.get(correlationId) || null;
    }

    isPending(correlationId) {
        return this.pendingCorrelations.has(correlationId);
    }

    // Clears a correlationId from "pending" on normal completion (accepted or
    // cleanly rejected by Unity) - distinct from invalidateProposal, which records
    // a reason because something ELSE (an epoch change, a user action) made the
    // proposal stale before it could even be decided.
    resolvePending(correlationId) {
        this.pendingCorrelations.delete(correlationId);
    }
}

module.exports = { CacheReconciler };
