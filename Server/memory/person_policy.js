"use strict";

// Person/multi-user memory layer (docs/shared-memory-and-experimental-space.md §1).
// Role/permissions are still a static single-owner stub per
// docs/shared-memory-and-experimental-space.md §4.2 - intentionally not a real
// multi-user policy engine yet, only needs to grow once the paper's L4/collaborative
// study tasks are actually being built. What DOES change per session now is
// priorDecisions: the paper says "confirmations, rejections, undo events, and agent
// results update temporal and person memory" (main.tex, Shared XR Memory) - every
// commit/simulate/explicit event recorded to the Temporal layer (artifact_log.js)
// also calls recordEvent() here, per docs/next-build-prompt.md §2.4.

const MAX_PRIOR_DECISIONS = 50;

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
                priorDecisions: [],
                note: "role/permissions are a single-owner stub - not a real multi-user policy engine yet",
            });
        }
        return this.sessions.get(key);
    }

    recordEvent({ sessionId, eventType, targetObjectId, at = Date.now() } = {}) {
        const policy = this.getPolicy({ sessionId });
        policy.priorDecisions.push({ eventType, targetObjectId: targetObjectId || null, at });
        if (policy.priorDecisions.length > MAX_PRIOR_DECISIONS) policy.priorDecisions.shift();
        return policy;
    }
}

module.exports = { PersonPolicyStore };
