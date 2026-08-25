"use strict";

// Durable store for the C# source of committed artifacts, keyed by artifactId.
//
// Why this exists: the event journal records that an artifact was committed, its
// id, version, operation and status, but never the code. So after a scene reset
// there is nothing to reattach, and resume_system_checkpoint can only report an
// entry as resumable in the sense that its target still exists, not in the sense
// that it can actually be restored.
//
// The source is kept out of the append-only journal deliberately. The journal is
// an evidence record that is exported and read; source blobs would bloat every
// export and every backfill range. This store is a separate, replaceable,
// bounded map that the checkpoint reads from when it needs to make an artifact
// reattachable.
//
// It holds generated program text, not participant data. It must still be
// treated as local-only: it lives under memory/data, which is not committed.

const fs = require("fs");
const path = require("path");

const DEFAULT_MAX_ENTRIES = 500;

class ArtifactSourceStore {
    constructor({ filePath, maxEntries = DEFAULT_MAX_ENTRIES } = {}) {
        this.filePath = filePath || path.join(__dirname, "data", "artifact_sources.json");
        this.maxEntries = maxEntries;
        this.entries = new Map();
        this._load();
    }

    _load() {
        try {
            if (!fs.existsSync(this.filePath)) return;
            const parsed = JSON.parse(fs.readFileSync(this.filePath, "utf8"));
            for (const entry of parsed.entries || []) {
                if (entry && entry.artifactId) this.entries.set(entry.artifactId, entry);
            }
        } catch {
            // A corrupt store must not stop the runtime. It means reattachment is
            // unavailable, which the checkpoint reports honestly rather than
            // failing the session.
            this.entries = new Map();
        }
    }

    _persist() {
        // Oldest entries are dropped first, so a long session cannot grow the
        // store without bound. A dropped artifact becomes unreattachable, and the
        // checkpoint says so rather than pretending otherwise.
        while (this.entries.size > this.maxEntries) {
            this.entries.delete(this.entries.keys().next().value);
        }
        fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
        const temporary = `${this.filePath}.tmp`;
        fs.writeFileSync(temporary, JSON.stringify({
            schemaVersion: "1.0",
            entries: Array.from(this.entries.values()),
        }, null, 2) + "\n");
        fs.renameSync(temporary, this.filePath);
    }

    /**
     * Records the source that produced a committed artifact.
     * @returns {{stored: boolean, reason?: string}}
     */
    record({ artifactId, targetObjectId, source, artifactVersion = null, correlationId = null, at = Date.now() } = {}) {
        if (!artifactId) return { stored: false, reason: "artifactId is required" };
        if (!targetObjectId) return { stored: false, reason: "targetObjectId is required" };
        if (typeof source !== "string" || source.trim() === "") {
            // A remove operation legitimately has no source. Saying so is better
            // than storing an empty string that later looks reattachable.
            return { stored: false, reason: "no source supplied" };
        }
        this.entries.delete(artifactId);
        this.entries.set(artifactId, { artifactId, targetObjectId, source, artifactVersion, correlationId, at });
        this._persist();
        return { stored: true };
    }

    get(artifactId) {
        const entry = this.entries.get(artifactId);
        return entry ? { ...entry } : null;
    }

    forget(artifactId) {
        const existed = this.entries.delete(artifactId);
        if (existed) this._persist();
        return existed;
    }

    size() { return this.entries.size; }
}

module.exports = { ArtifactSourceStore, DEFAULT_MAX_ENTRIES };
