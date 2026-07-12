"use strict";

// Visual memory layer (docs/shared-memory-and-experimental-space.md §1): coarse
// object-aligned data derived from SceneDelta pushes and sensor events. Deliberately
// not a full world reconstruction - just enough to ground and preview.

class VisualStore {
    constructor() {
        this.byObjectId = new Map(); // objectId -> { focus, lastSeenAt, sensorEvents }
        this.halo = new Map(); // objectId -> { summary, lastSeenAt }
    }

    ingestSceneDelta(envelope) {
        const payload = envelope.payload || {};
        const at = envelope.timestamp || Date.now();
        if (payload.focus && payload.focus.id) {
            const existing = this.byObjectId.get(payload.focus.id);
            this.byObjectId.set(payload.focus.id, {
                focus: payload.focus,
                lastSeenAt: at,
                sensorEvents: (existing && existing.sensorEvents) || [],
            });
        }
        for (const h of payload.halo || []) {
            this.halo.set(h.id, { summary: h, lastSeenAt: at });
        }
    }

    ingestSensorEvent(event) {
        const entry = this.byObjectId.get(event.targetObjectId);
        if (!entry) return;
        entry.sensorEvents.push(event);
        if (entry.sensorEvents.length > 20) entry.sensorEvents.shift();
    }

    query({ objectId, filter } = {}) {
        if (objectId) {
            const focusEntry = this.byObjectId.get(objectId);
            if (focusEntry) return { objectId, kind: "focus", ...focusEntry };
            const haloEntry = this.halo.get(objectId);
            if (haloEntry) return { objectId, kind: "halo", ...haloEntry };
            return { objectId, found: false };
        }
        const results = [];
        for (const [id, entry] of this.byObjectId) results.push({ objectId: id, kind: "focus", ...entry });
        for (const [id, entry] of this.halo) results.push({ objectId: id, kind: "halo", ...entry });
        if (!filter) return results;
        const needle = filter.toLowerCase();
        return results.filter((r) => JSON.stringify(r).toLowerCase().includes(needle));
    }
}

module.exports = { VisualStore };
