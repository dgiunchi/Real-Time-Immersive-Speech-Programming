"use strict";

// Agent Working Cache (main.tex, Cache Exchange Layer §): mirrors recently ACCEPTED
// scene deltas so agents can plan without round-tripping to Unity for every read.
// Deliberately dumb - it does not decide what counts as "accepted" (that's
// CacheReconciler's job) or gate live commits (that's ProposalGate + Unity's own
// authoritative check). This class only stores and indexes what it's told to.
//
// NOT authoritative for live commits - every ProposalGate check re-verifies against
// this cache's CURRENT state at commit time, not a stale read taken during planning.

class AgentWorkingCache {
    constructor() {
        this.byObjectId = new Map(); // stableObjectId -> { objectRevision, sceneEpoch, snapshotId, deltaSeq, label, region, payload, lastAcceptedAt }
        this.byCorrelationId = new Map(); // correlationId -> { stableObjectId, envelopeType, at }
        this.byLabel = new Map(); // label -> Set<stableObjectId>
        this.byRegion = new Map(); // regionId -> Set<stableObjectId>
        this.byArtifactId = new Map(); // artifactId -> record
        this.currentSceneEpoch = null;
        this.currentSnapshotId = null;
    }

    _indexSet(map, key, objectId) {
        if (!key) return;
        if (!map.has(key)) map.set(key, new Set());
        map.get(key).add(objectId);
    }

    // Records an accepted delta or snapshot. `entry` shape: { stableObjectId,
    // objectRevision, sceneEpoch, snapshotId, deltaSeq, label, region, payload }.
    // isSnapshot=true replaces prior indexing for that object (a snapshot is a full
    // restatement, not an incremental patch).
    acceptState(entry, { isSnapshot = false } = {}) {
        const { stableObjectId } = entry;
        if (!stableObjectId) throw new Error("acceptState requires stableObjectId");

        const existing = this.byObjectId.get(stableObjectId);

        // Monotonic safety net (spec: "object revisions are monotonic per
        // stableObjectId"): a backfilled delta that arrives after a newer one was
        // already applied should not regress current state. It's still journaled by
        // the caller either way - this only guards the "current" pointer.
        if (existing && !isSnapshot && entry.objectRevision != null && existing.objectRevision != null && entry.objectRevision < existing.objectRevision) {
            return { ...existing, supersededByNewer: true };
        }

        if (existing && !isSnapshot) {
            // Unindex stale label/region membership before re-indexing.
            if (existing.label && this.byLabel.has(existing.label)) this.byLabel.get(existing.label).delete(stableObjectId);
            if (existing.region && this.byRegion.has(existing.region)) this.byRegion.get(existing.region).delete(stableObjectId);
        }

        const record = {
            stableObjectId,
            objectRevision: entry.objectRevision != null ? entry.objectRevision : (existing ? existing.objectRevision : 0),
            sceneEpoch: entry.sceneEpoch != null ? entry.sceneEpoch : (existing ? existing.sceneEpoch : null),
            snapshotId: entry.snapshotId != null ? entry.snapshotId : (existing ? existing.snapshotId : null),
            deltaSeq: entry.deltaSeq != null ? entry.deltaSeq : (existing ? existing.deltaSeq : null),
            label: entry.label != null ? entry.label : (existing ? existing.label : null),
            region: entry.region != null ? entry.region : (existing ? existing.region : null),
            payload: entry.payload !== undefined ? entry.payload : (existing ? existing.payload : null),
            lastAcceptedAt: Date.now(),
        };
        this.byObjectId.set(stableObjectId, record);
        this._indexSet(this.byLabel, record.label, stableObjectId);
        this._indexSet(this.byRegion, record.region, stableObjectId);

        if (record.sceneEpoch) this.currentSceneEpoch = record.sceneEpoch;
        if (record.snapshotId) this.currentSnapshotId = record.snapshotId;

        return record;
    }

    indexCorrelation(correlationId, { stableObjectId, envelopeType } = {}) {
        if (!correlationId) return;
        this.byCorrelationId.set(correlationId, { stableObjectId: stableObjectId || null, envelopeType: envelopeType || null, at: Date.now() });
    }

    recordArtifactOutcome(artifactId, record) {
        if (!artifactId) return;
        this.byArtifactId.set(artifactId, { ...record, at: Date.now() });
    }

    getByObjectId(stableObjectId) {
        return this.byObjectId.get(stableObjectId) || null;
    }

    getByCorrelationId(correlationId) {
        return this.byCorrelationId.get(correlationId) || null;
    }

    getByArtifactId(artifactId) {
        return this.byArtifactId.get(artifactId) || null;
    }

    queryByLabel(label) {
        return Array.from(this.byLabel.get(label) || []).map((id) => this.byObjectId.get(id));
    }

    queryByRegion(region) {
        return Array.from(this.byRegion.get(region) || []).map((id) => this.byObjectId.get(id));
    }

    getSceneEpoch() {
        return this.currentSceneEpoch;
    }
}

module.exports = { AgentWorkingCache };
