"use strict";

// "Sensors" = Unity-side scene components (proximity/collision triggers, gaze/ray
// hits, hand-tracking events) that continuously publish observations into Shared XR
// Memory - this is the concrete mechanism by which SceneController.cs is meant to
// populate the Visual and Semantic layers, per the clarified scope for this pass.
// No new NetworkId: sensor events ride inside the existing SceneDelta (95) envelope's
// payload.sensorEvents array, kept optional so existing plain SceneDelta payloads
// (e.g. from earlier versions of mock_unity_peer.js) remain valid without it.
//
// Unity-side sensor *components* (the C# MonoBehaviours that actually detect
// proximity/gaze/collision) are NOT built in this pass - this module defines the
// server-side contract and normalizer they will eventually feed.

const KNOWN_SENSOR_TYPES = Object.freeze(["proximity", "collision", "gaze", "handTracking", "gesture"]);

const RELATION_BY_SENSOR_TYPE = Object.freeze({
    proximity: "near",
    collision: "touching",
    gaze: "observed-by",
    handTracking: "reachable-from",
    gesture: "reachable-from",
});

class SensorRegistry {
    constructor({ visualStore, sceneGraphStore, maxRecent = 200 } = {}) {
        this.visualStore = visualStore;
        this.sceneGraphStore = sceneGraphStore;
        this.recent = [];
        this.maxRecent = maxRecent;
    }

    ingestSceneDelta(envelope) {
        const events = (envelope.payload && envelope.payload.sensorEvents) || [];
        for (const raw of events) this._ingestOne(raw, envelope.timestamp);
    }

    _ingestOne(raw, fallbackTimestamp) {
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
