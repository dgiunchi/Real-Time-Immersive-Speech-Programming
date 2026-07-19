"use strict";

// Shared XR Memory aggregator (docs/shared-memory-and-experimental-space.md). Ties
// the five memory layers together and wires them to a SceneBridgeClient's envelope
// stream so they populate automatically as messages flow, rather than requiring every
// call site to remember to update every store.

const { VisualStore } = require("./visual_store");
const { SceneGraphStore } = require("./scene_graph_store");
const { ArtifactLog } = require("./artifact_log");
const { PersonPolicyStore } = require("./person_policy");
const { TimelineRegistry } = require("./timeline_registry");
const { SensorRegistry } = require("./sensor_registry");
const { RegionStore } = require("./region_store");
const { IntentStore } = require("./intent_store");

class SharedMemory {
    constructor({ artifactLogPath } = {}) {
        this.visual = new VisualStore();
        this.sceneGraph = new SceneGraphStore(this.visual);
        this.artifactLog = new ArtifactLog({ filePath: artifactLogPath });
        this.personPolicy = new PersonPolicyStore();
        this.timeline = new TimelineRegistry();
        this.region = new RegionStore();
        this.intent = new IntentStore();
        this.sensors = new SensorRegistry({ visualStore: this.visual, sceneGraphStore: this.sceneGraph, regionStore: this.region });
    }

    // Subscribes to every envelope (inbound and outbound) a SceneBridgeClient sees.
    attach(bridge) {
        bridge.on("envelope", (envelope) => {
            this.timeline.observeEnvelope(envelope);
            if (envelope.type === "SceneDelta") {
                this.visual.ingestSceneDelta(envelope);
                this.sceneGraph.ingestSceneDelta(envelope);
                this.sensors.ingestSceneDelta(envelope);
            }
            if (["UserDecision", "ArtifactResult", "CommitAccepted", "CommitRejected", "RollbackResult"].includes(envelope.type)) {
                const eventType = envelope.type === "UserDecision"
                    ? `user_decision:${(envelope.payload && envelope.payload.decision) || "unknown"}`
                    : envelope.type.toLowerCase();
                this.artifactLog.append({
                    eventType,
                    sessionId: envelope.sessionId || null,
                    correlationId: envelope.correlationId || null,
                    targetObjectId: envelope.targetObjectId || null,
                    artifactId: (envelope.payload && envelope.payload.artifactId) || envelope.artifactId || null,
                    status: envelope.payload && (envelope.payload.status || envelope.payload.decision),
                    reason: envelope.payload && (envelope.payload.reason || envelope.payload.error),
                    at: envelope.timestamp || Date.now(),
                });
                this.personPolicy.recordEvent({
                    sessionId: envelope.sessionId,
                    eventType,
                    targetObjectId: envelope.targetObjectId,
                    at: envelope.timestamp || Date.now(),
                });
            }
        });
    }
}

module.exports = { SharedMemory };
