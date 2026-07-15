"use strict";

// Append-only Event Journal (main.tex, Cache Exchange Layer §; docs/next steps).
// Records every accepted scene delta, user decision, agent status, proposal,
// validation result, preview, commit, rollback, and invalidation, so either side can
// backfill a missing range after packet loss, reconnect, agent restart, or overload.
//
// Storage is behind a small adapter interface (append/range/latestSeq) so swapping
// in-memory for file-backed or a real database later doesn't change callers. Starts
// in-memory per docs/next-build-prompt.md's precedent (artifact_log.js started as
// flat JSON-lines for the same reason) - kept even simpler here since the journal is
// explicitly NOT meant to be the long-term store (that's Shared XR Memory's
// artifact_log.js); this is short-term recovery state.

class InMemoryJournalStore {
    constructor() {
        this.bySession = new Map(); // sessionId -> array of records, seq-ordered
    }

    append(sessionId, record) {
        if (!this.bySession.has(sessionId)) this.bySession.set(sessionId, []);
        this.bySession.get(sessionId).push(record);
        return record;
    }

    range(sessionId, afterSeq, limit) {
        const records = this.bySession.get(sessionId) || [];
        const filtered = records.filter((r) => r.seq > afterSeq);
        return typeof limit === "number" ? filtered.slice(0, limit) : filtered;
    }

    latestSeq(sessionId) {
        const records = this.bySession.get(sessionId) || [];
        return records.length ? records[records.length - 1].seq : 0;
    }
}

class EventJournal {
    constructor({ store, maxPerSession = 2000 } = {}) {
        this.store = store || new InMemoryJournalStore();
        this.maxPerSession = maxPerSession;
        this.seqBySession = new Map(); // sessionId -> last assigned journal seq
    }

    _nextSeq(sessionId) {
        const next = (this.seqBySession.get(sessionId) || 0) + 1;
        this.seqBySession.set(sessionId, next);
        return next;
    }

    // eventType: "delta" | "snapshot" | "decision" | "agent_status" | "proposal" |
    // "validation" | "preview" | "commit" | "rollback" | "invalidation"
    append(sessionId, eventType, data) {
        const key = sessionId || "default";
        const record = {
            seq: this._nextSeq(key),
            sessionId: key,
            eventType,
            data,
            at: Date.now(),
        };
        this.store.append(key, record);
        return record;
    }

    // Backfill by sessionId + lastSeenSeq, per the spec's exact naming.
    backfill(sessionId, lastSeenSeq = 0, limit) {
        const key = sessionId || "default";
        return this.store.range(key, lastSeenSeq, limit);
    }

    latestSeq(sessionId) {
        return this.store.latestSeq(sessionId || "default");
    }
}

module.exports = { EventJournal, InMemoryJournalStore };
