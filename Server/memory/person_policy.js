"use strict";

// Person/multi-user memory layer (docs/shared-memory-and-experimental-space.md §1).
// Static single-owner stub per docs/shared-memory-and-experimental-space.md §4.2 -
// intentionally not a real multi-user policy engine yet. Only needs to grow once the
// paper's L4/collaborative study tasks are actually being built and tested.

class PersonPolicyStore {
    constructor() {
        this.sessions = new Map(); // sessionId -> policy record
    }

    getPolicy({ sessionId } = {}) {
        const key = sessionId || "default";
        if (!this.sessions.has(key)) {
            this.sessions.set(key, {
                sessionId: key,
                role: "owner",
                permissions: ["select", "confirm", "reject", "undo", "persist"],
                consent: { sharedObjectMutation: true },
                note: "single-owner stub - not a real multi-user policy engine yet",
            });
        }
        return this.sessions.get(key);
    }
}

module.exports = { PersonPolicyStore };
