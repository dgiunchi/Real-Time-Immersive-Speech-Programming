"use strict";

// "Speech updates intent" (main.tex, Shared XR Memory sensor sentence). Scoped down
// per docs/next-build-prompt.md §1.3 / §2.5: real speech-to-text is not wired into
// the new orchestrator pipeline yet (STT still only feeds the legacy
// code_runtime_generator path) - this store exists so the memory shape and plumbing
// are ready, fed today by the orchestrator's CLI-provided intent string as an
// explicit stand-in, NOT real speech. Say this plainly in any status update.

const MAX_PER_SESSION = 20;

class IntentStore {
    constructor() {
        this.bySession = new Map(); // sessionId -> [{ text, correlationId, at }]
    }

    record({ sessionId, text, correlationId, at = Date.now() } = {}) {
        if (!text) return null;
        const key = sessionId || "default";
        if (!this.bySession.has(key)) this.bySession.set(key, []);
        const entries = this.bySession.get(key);
        const entry = { text, correlationId: correlationId || null, at };
        entries.push(entry);
        if (entries.length > MAX_PER_SESSION) entries.shift();
        return entry;
    }

    recent({ sessionId, limit = 10 } = {}) {
        const key = sessionId || "default";
        const entries = this.bySession.get(key) || [];
        return entries.slice(-limit).reverse();
    }
}

module.exports = { IntentStore };
