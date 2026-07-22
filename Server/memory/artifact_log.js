"use strict";

// Temporal memory layer / Version-Memory store (docs/agentic-xr-architecture.md
// §4.1/§8 phase 5; docs/shared-memory-and-experimental-space.md §1). Append-only
// JSON-lines log so history survives process restarts without a new dependency.
// Migrating to SQLite is the documented open decision in agentic-xr-architecture.md
// §9 - flat file is deliberately the starting point, not the final answer.

const fs = require("fs");
const path = require("path");

class ArtifactLog {
    constructor({ filePath } = {}) {
        this.filePath = filePath || path.join(__dirname, "data", "artifact_log.jsonl");
        fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
        this.byObjectId = new Map(); // objectId -> [entries]
        this.byArtifactId = new Map();
        this._loadExisting();
    }

    _loadExisting() {
        if (!fs.existsSync(this.filePath)) return;
        const lines = fs.readFileSync(this.filePath, "utf8").split("\n").filter(Boolean);
        for (const line of lines) {
            try {
                this._index(JSON.parse(line));
            } catch (err) {
                console.error(`[artifact_log] skipped malformed line: ${err.message}`);
            }
        }
    }

    _index(entry) {
        if (entry.artifactId) this.byArtifactId.set(entry.artifactId, entry);
        if (!entry.targetObjectId) return;
        if (!this.byObjectId.has(entry.targetObjectId)) this.byObjectId.set(entry.targetObjectId, []);
        this.byObjectId.get(entry.targetObjectId).push(entry);
    }

    append(entry) {
        const record = { ...entry, loggedAt: Date.now() };
        fs.appendFileSync(this.filePath, JSON.stringify(record) + "\n");
        this._index(record);
        return record;
    }

    history({ objectId, limit = 20 } = {}) {
        const entries = objectId
            ? (this.byObjectId.get(objectId) || [])
            : Array.from(this.byObjectId.values()).flat().sort((a, b) => a.loggedAt - b.loggedAt);
        return entries.slice(-limit).reverse();
    }

    evolution({ objectId, limit = 100 } = {}) {
        return this.history({ objectId, limit }).reverse().map((entry) => ({
            at: entry.loggedAt || entry.at,
            eventType: entry.eventType,
            operation: entry.operation || (entry.eventType && entry.eventType.includes("rollback") ? "rollback" : null),
            targetObjectId: entry.targetObjectId || null,
            artifactId: entry.artifactId || null,
            artifactVersion: entry.artifactVersion || null,
            supersedesArtifactId: entry.supersedesArtifactId || entry.rollbackPointer || null,
            candidateId: entry.candidateId || null,
            candidateSetId: entry.candidateSetId || null,
            selectionReason: entry.selectionReason || null,
            intent: entry.intent || null,
            status: entry.status || null,
            reason: entry.reason || null,
            correlationId: entry.correlationId || null,
        }));
    }

    activeArtifacts() {
        const active = new Map();
        for (const entry of this.history({ limit: Number.MAX_SAFE_INTEGER }).reverse()) {
            if (!entry.targetObjectId) continue;
            const operation = entry.operation || "create";
            const committed = entry.status === "committed" || entry.status === "removed" || entry.eventType === "commitaccepted";
            if (committed && operation !== "remove") active.set(entry.targetObjectId, entry);
            if ((committed && operation === "remove") || /rollback|removed/.test(entry.eventType || "")) active.delete(entry.targetObjectId);
        }
        return Array.from(active.values());
    }
}

module.exports = { ArtifactLog };
