"use strict";

// "Sensors" = Unity-side scene components (proximity/collision triggers, gaze/ray
// hits, hand-tracking events, locomotion/region crossings) that continuously publish
// observations into Shared XR Memory - this is the concrete mechanism by which
// SceneController.cs is meant to populate the Visual, Semantic, and region-context
// state, per the paper's sensor-as-memory-update-source sentence (main.tex, Shared
// XR Memory and Experimental Space; docs/paper-sync-timelines-and-modes.md §1.3).
// No new NetworkId: sensor events ride inside the existing SceneDelta (95) envelope's
// payload.sensorEvents array, kept optional so existing plain SceneDelta payloads
// (e.g. from earlier versions of mock_unity_peer.js) remain valid without it.
//
// Unity-side emitters (2026-08-11): Unity/Assets/AgenticCache/ImplicitTriggerSensors.cs
// now emits discrete `locomotion` (region volume entry/exit), `proximity`
// (enter/exit within a radius), and `gaze` (head-ray dwell) events onto this path,
// alongside CachePublisher's scene-entry locomotion and selection-gaze events.
// collision/handTracking/gesture remain server-contract-only, with no Unity emitter.

const KNOWN_SENSOR_TYPES = Object.freeze(["proximity", "collision", "gaze", "handTracking", "gesture", "locomotion"]);

const RELATION_BY_SENSOR_TYPE = Object.freeze({
    proximity: "near",
    collision: "touching",
    gaze: "observed-by",
    handTracking: "reachable-from",
    gesture: "reachable-from",
    // locomotion is intentionally absent here - it updates region context
    // (regionStore), not an object-to-object relation. See _ingestOne below.
});

class SensorRegistry {
    constructor({ visualStore, sceneGraphStore, regionStore, maxRecent = 200 } = {}) {
        this.visualStore = visualStore;
        this.sceneGraphStore = sceneGraphStore;
        this.regionStore = regionStore;
        this.recent = [];
        this.maxRecent = maxRecent;
    }

    ingestSceneDelta(envelope) {
        const events = (envelope.payload && envelope.payload.sensorEvents) || [];
        for (const raw of events) this._ingestOne(raw, envelope.timestamp);
    }

    _ingestOne(raw, fallbackTimestamp) {
        // Study telemetry is routed to ArtifactLog by SharedMemory. It is not a
        // spatial sensor and should not produce noisy "unrecognized" errors.
        if (raw && String(raw.sensorType || "").startsWith("study_")) return;
        if (!raw || !KNOWN_SENSOR_TYPES.includes(raw.sensorType)) {
            console.error(`[sensor_registry] dropped unrecognized sensor event: ${JSON.stringify(raw)}`);
            return;
        }
        const event = {
            sensorType: raw.sensorType,
            sourceObjectId: raw.sourceObjectId || null,
            targetObjectId: raw.targetObjectId || null,
            value: raw.value !== undefined ? raw.value : null,
            confidence: typeof raw.confidence === "number" ? raw.confidence : 1,
            timestamp: raw.timestamp || fallbackTimestamp || Date.now(),
        };

        this.recent.push(event);
        if (this.recent.length > this.maxRecent) this.recent.shift();

        if (event.sensorType === "locomotion") {
            // Discrete region entry/exit, not an object relation - route separately
            // and skip the relation-derivation logic below.
            if (this.regionStore) this.regionStore.ingestLocomotionEvent(event);
            return;
        }

        if (this.visualStore && event.targetObjectId) {
            this.visualStore.ingestSensorEvent(event);
        }
        if (this.sceneGraphStore && event.sourceObjectId && event.targetObjectId) {
            this.sceneGraphStore.relations.push({
                from: event.sourceObjectId,
                to: event.targetObjectId,
                relation: RELATION_BY_SENSOR_TYPE[event.sensorType] || "related-to",
                source: `sensor:${event.sensorType}`,
                timestamp: event.timestamp,
            });
            this.sceneGraphStore._prune();
        }
    }

    query({ objectId, sensorType } = {}) {
        return this.recent.filter(
            (e) =>
                (!objectId || e.sourceObjectId === objectId || e.targetObjectId === objectId) &&
                (!sensorType || e.sensorType === sensorType)
        );
    }
}

module.exports = { SensorRegistry, KNOWN_SENSOR_TYPES };
