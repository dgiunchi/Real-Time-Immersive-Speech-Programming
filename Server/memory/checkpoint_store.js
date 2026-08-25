"use strict";

const fs = require("fs");
const path = require("path");

class CheckpointStore {
    constructor({ filePath } = {}) {
        this.filePath = filePath || path.join(__dirname, "data", "system_checkpoint.json");
    }

    // artifactSourceStore is optional. Without it a checkpoint still records what
    // was active, but nothing can be reattached, because the journal never held
    // the code. Passing it is what turns "this target still exists" into "this
    // artifact can actually be restored".
    save({ artifactLog, personPolicy, experienceContext, artifactSourceStore = null, sceneEpoch = null } = {}) {
        const checkpoint = {
            schemaVersion: "1.1", checkpointedAt: Date.now(), sceneEpoch,
            activeArtifacts: artifactLog ? artifactLog.activeArtifacts().map((entry) => {
                const artifactId = entry.artifactId || null;
                const stored = artifactId && artifactSourceStore ? artifactSourceStore.get(artifactId) : null;
                return {
                    targetObjectId: entry.targetObjectId, artifactId,
                    artifactVersion: entry.artifactVersion || null, rollbackPointer: entry.rollbackPointer || null,
                    correlationId: entry.correlationId || null,
                    // Recorded on the checkpoint so a restore does not depend on
                    // the source store still holding the entry later.
                    source: stored ? stored.source : null,
                    sourceAvailable: Boolean(stored && stored.source),
                };
            }) : [],
            consentedProfiles: personPolicy ? personPolicy.snapshotConsentedProfiles() : [],
            experienceContexts: experienceContext ? experienceContext.snapshot() : [],
        };
        fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
        const temporary = `${this.filePath}.tmp`;
        fs.writeFileSync(temporary, JSON.stringify(checkpoint, null, 2) + "\n");
        fs.renameSync(temporary, this.filePath);
        return checkpoint;
    }

    // Three outcomes, not two. An artifact whose target survived is only
    // reattachable if its source was captured; one whose target survived but
    // whose source was not captured is reported separately rather than being
    // called resumable, because calling it resumable and then failing to restore
    // it is the failure this split exists to prevent.
    load({ currentObjectIds = [] } = {}) {
        if (!fs.existsSync(this.filePath)) {
            return { status: "missing", resumable: [], reattachable: [], unreattachable: [], orphaned: [] };
        }
        const checkpoint = JSON.parse(fs.readFileSync(this.filePath, "utf8"));
        const existing = new Set(currentObjectIds);
        const classify = existing.size > 0;
        const resumable = classify ? (checkpoint.activeArtifacts || []).filter((entry) => existing.has(entry.targetObjectId)) : [];
        const reattachable = resumable.filter((entry) => Boolean(entry.source));
        const unreattachable = resumable
            .filter((entry) => !entry.source)
            .map((entry) => ({ ...entry, reason: "no source was captured for this artifact" }));
        const orphaned = classify ? (checkpoint.activeArtifacts || []).filter((entry) => !existing.has(entry.targetObjectId)).map((entry) => ({ ...entry, status: "orphaned", reason: "stable object is absent from current scene" })) : [];
        return {
            status: classify ? "classified" : "awaiting_scene_inventory",
            checkpoint, resumable, reattachable, unreattachable, orphaned,
        };
    }
}

module.exports = { CheckpointStore };
