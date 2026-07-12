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

class SharedMemory {
    constructor({ artifactLogPath } = {}) {
        this.visual = new VisualStore();
        this.sceneGraph = new SceneGraphStore(this.visual);
        this.artifactLog = new ArtifactLog({ filePath: artifactLogPath });
        this.personPolicy = new PersonPolicyStore();
        this.timeline = new TimelineRegistry();
        this.sensors = new SensorRegistry({ visualStore: this.visual, sceneGraphStore: this.sceneGraph });
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
        });
    }
}

module.exports = { SharedMemory };
