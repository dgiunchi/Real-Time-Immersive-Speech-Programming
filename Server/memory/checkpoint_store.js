"use strict";

const fs = require("fs");
const path = require("path");

class CheckpointStore {
    constructor({ filePath } = {}) {
        this.filePath = filePath || path.join(__dirname, "data", "system_checkpoint.json");
    }

    save({ artifactLog, personPolicy, experienceContext, sceneEpoch = null } = {}) {
        const checkpoint = {
            schemaVersion: "1.0", checkpointedAt: Date.now(), sceneEpoch,
            activeArtifacts: artifactLog ? artifactLog.activeArtifacts().map((entry) => ({
                targetObjectId: entry.targetObjectId, artifactId: entry.artifactId || null,
                artifactVersion: entry.artifactVersion || null, rollbackPointer: entry.rollbackPointer || null,
                correlationId: entry.correlationId || null,
            })) : [],
            consentedProfiles: personPolicy ? personPolicy.snapshotConsentedProfiles() : [],
            experienceContexts: experienceContext ? experienceContext.snapshot() : [],
        };
        fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
        const temporary = `${this.filePath}.tmp`;
        fs.writeFileSync(temporary, JSON.stringify(checkpoint, null, 2) + "\n");
        fs.renameSync(temporary, this.filePath);
        return checkpoint;
    }

    load({ currentObjectIds = [] } = {}) {
        if (!fs.existsSync(this.filePath)) return { status: "missing", resumable: [], orphaned: [] };
        const checkpoint = JSON.parse(fs.readFileSync(this.filePath, "utf8"));
        const existing = new Set(currentObjectIds);
        const classify = existing.size > 0;
        const resumable = classify ? (checkpoint.activeArtifacts || []).filter((entry) => existing.has(entry.targetObjectId)) : [];
        const orphaned = classify ? (checkpoint.activeArtifacts || []).filter((entry) => !existing.has(entry.targetObjectId)).map((entry) => ({ ...entry, status: "orphaned", reason: "stable object is absent from current scene" })) : [];
        return { status: classify ? "classified" : "awaiting_scene_inventory", checkpoint, resumable, orphaned };
    }
}

module.exports = { CheckpointStore };
