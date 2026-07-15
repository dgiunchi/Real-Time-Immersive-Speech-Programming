"use strict";

// Static named-region lookup - "locomotion updates region context" per the paper
// (main.tex, Shared XR Memory sensor sentence; docs/next-build-prompt.md §2.3).
// Discrete: fires on region entry/exit, not continuous per-frame proximity (that's
// what the existing "proximity" sensor type in sensor_registry.js already covers).
// REGION_RULES is intentionally empty by default - real scenes define their own
// regions; extend this list (or load it from config) once Unity actually publishes
// locomotion sensor events.
const REGION_RULES = [
    // { regionId: "workshop-entrance", anchorObjectTag: "doorway" },
];

class RegionStore {
    constructor() {
        this.currentRegionBySession = new Map(); // sessionId -> { regionId, enteredAt }
        this.history = []; // bounded ring buffer of transitions, for inspection/debugging
    }

    ingestLocomotionEvent(event) {
        // event.value: { regionId, entering: boolean }
        if (!event || !event.value || !event.value.regionId) {
            console.error(`[region_store] dropped locomotion event with no regionId: ${JSON.stringify(event)}`);
            return;
        }
        const sessionId = event.sourceObjectId || "default";
        const current = this.currentRegionBySession.get(sessionId);

        if (event.value.entering) {
            this.currentRegionBySession.set(sessionId, { regionId: event.value.regionId, enteredAt: event.timestamp });
        } else if (current && current.regionId === event.value.regionId) {
            this.currentRegionBySession.delete(sessionId);
        }

        this.history.push({ ...event, sessionId });
        if (this.history.length > 200) this.history.shift();
    }

    getCurrentRegion(sessionId = "default") {
        return this.currentRegionBySession.get(sessionId) || null;
    }
}

module.exports = { RegionStore, REGION_RULES };
