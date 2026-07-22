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
const { ExperienceContextStore } = require("./experience_context");
const { CheckpointStore } = require("./checkpoint_store");
const { appendEvaluationEvent } = require("../evaluation/event_logger");

class SharedMemory {
    constructor({ artifactLogPath, personProfilePath, experienceContextPath, checkpointPath } = {}) {
        this.visual = new VisualStore();
        this.sceneGraph = new SceneGraphStore(this.visual);
        this.artifactLog = new ArtifactLog({ filePath: artifactLogPath });
        this.personPolicy = new PersonPolicyStore({ filePath: personProfilePath });
        this.timeline = new TimelineRegistry();
        this.region = new RegionStore();
        this.intent = new IntentStore();
        this.experienceContext = new ExperienceContextStore({ filePath: experienceContextPath });
        this.checkpoints = new CheckpointStore({ filePath: checkpointPath });
        this.proposalSentAt = new Map();
        this.sensors = new SensorRegistry({ visualStore: this.visual, sceneGraphStore: this.sceneGraph, regionStore: this.region });
    }

    // Subscribes to every envelope (inbound and outbound) a SceneBridgeClient sees.
    attach(bridge) {
        bridge.on("envelope", (envelope) => {
            this.timeline.observeEnvelope(envelope);
            try {
                appendEvaluationEvent({
                    eventType: "envelope",
                    envelopeType: envelope.type,
                    sessionId: envelope.sessionId || null,
                    correlationId: envelope.correlationId || null,
                    targetObjectId: envelope.targetObjectId || null,
                    timestamp: envelope.timestamp || Date.now(),
                    status: envelope.payload && envelope.payload.status,
                    reason: envelope.payload && (envelope.payload.reason || envelope.payload.error),
                    operation: envelope.operation || null,
                    candidateId: envelope.candidateId || null,
                    candidateSetId: envelope.candidateSetId || null,
                    staleness: envelope.staleness || null,
                    verificationDurationMs: envelope.verificationDurationMs || null,
                    commitAttachDurationMs: envelope.commitAttachDurationMs || null,
                });
            } catch (error) {
                console.error(`[evaluation] failed to record envelope: ${error.message}`);
            }
            if (envelope.type === "SceneDelta") {
                this.visual.ingestSceneDelta(envelope);
                this.sceneGraph.ingestSceneDelta(envelope);
                this.sensors.ingestSceneDelta(envelope);
                for (const sensor of (envelope.payload && envelope.payload.sensorEvents) || []) {
                    if (["gaze", "locomotion"].includes(sensor.sensorType)) this.personPolicy.recordEvent({
                        sessionId: envelope.sessionId, eventType: `sensor:${sensor.sensorType}`,
                        targetObjectId: sensor.targetObjectId, region: sensor.value && sensor.value.regionId,
                        at: sensor.timestamp || envelope.timestamp,
                    });
                }
            }
            if (envelope.type === "ArtifactProposal" && envelope.correlationId) {
                this.proposalSentAt.set(envelope.correlationId, envelope.timestamp || Date.now());
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
                    operation: envelope.operation || (envelope.payload && envelope.payload.operation) || null,
                    artifactVersion: envelope.artifactVersion || null,
                    rollbackPointer: envelope.rollbackPointer || null,
                    candidateId: envelope.candidateId || null,
                    candidateSetId: envelope.candidateSetId || null,
                    interactionMode: envelope.interactionMode || null,
                    authoringMode: envelope.authoringMode || null,
                    riskScore: envelope.riskScore,
                    at: envelope.timestamp || Date.now(),
                });
                this.personPolicy.recordEvent({
                    sessionId: envelope.sessionId,
                    eventType,
                    targetObjectId: envelope.targetObjectId,
                    interactionMode: envelope.interactionMode,
                    authoringMode: envelope.authoringMode,
                    riskScore: envelope.riskScore,
                    responseLatencyMs: envelope.type === "UserDecision" && this.proposalSentAt.has(envelope.correlationId)
                        ? (envelope.timestamp || Date.now()) - this.proposalSentAt.get(envelope.correlationId) : null,
                    at: envelope.timestamp || Date.now(),
                });
                if (["ArtifactResult", "CommitAccepted", "CommitRejected", "RollbackResult"].includes(envelope.type)) this.proposalSentAt.delete(envelope.correlationId);
            }
        });
    }
}

module.exports = { SharedMemory };
