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
const { STUDY_ID_PATTERN } = require("./artifact_log");
const { GoalVerifier } = require("../orchestrator/goal_verifier");
const { GoalLoopController } = require("../orchestrator/goal_loop_controller");
const { FutureGoalPredictor } = require("../orchestrator/future_goal_predictor");
const { ActivityMonitor } = require("./activity_monitor");

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
        this.goalLoops = new GoalLoopController({
            artifactLog: this.artifactLog,
            verifier: new GoalVerifier({
                llmJudge: async ({ evidence }) => evidence.validatorJudgment || null,
                humanCheckpoint: async ({ evidence }) => ({ approved: evidence.humanApproved === true }),
            }),
        });
        this.futureGoals = new FutureGoalPredictor({ artifactLog: this.artifactLog });
        this.activity = new ActivityMonitor({
            threshold: process.env.AGENTICXR_ACTIVITY_THRESHOLD,
            windowMs: process.env.AGENTICXR_ACTIVITY_WINDOW_MS,
            cooldownMs: process.env.AGENTICXR_ACTIVITY_COOLDOWN_MS,
            anticipationThreshold: process.env.AGENTICXR_ANTICIPATION_THRESHOLD,
            anticipationCooldownMs: process.env.AGENTICXR_ANTICIPATION_COOLDOWN_MS,
        });
        this.activity.on("assist_worthy", (opportunity) => {
            if (String(process.env.AGENTICXR_ACTIVITY_MONITOR_PRIMARY || "false").toLowerCase() !== "true") return;
            this.artifactLog.append({
                eventType: "activity_assist_triggered",
                ...opportunity,
                correlationId: opportunity.triggerId,
                interactionMode: "L2",
                authoringMode: "semi_auto_confirm",
            });
        });
        this.activity.on("predicted_engagement", (prediction) => {
            if (String(process.env.AGENTICXR_ACTIVITY_MONITOR_PRIMARY || "false").toLowerCase() !== "true") return;
            this.artifactLog.append({
                eventType: "predicted_engagement",
                ...prediction,
                correlationId: prediction.predictionId,
                interactionMode: "L2",
            });
        });
        this.proposalSentAt = new Map();
        this.sensors = new SensorRegistry({ visualStore: this.visual, sceneGraphStore: this.sceneGraph, regionStore: this.region });
    }

    // Compact one-line environmental context for implicit/proactive turns
    // (docs/code-implicit-proactive-showcase-2026-08-13.md §2). Supplies the RAW
    // ingredients - region, anchor role/components, affordances, neighbours,
    // experience mode - and deliberately never names a function to apply: the
    // agents must derive WHAT fits from this context, which is the study's
    // variability requirement. Missing stores degrade gracefully to "unknown".
    describeContext({ sessionId, targetObjectId } = {}) {
        const parts = [];
        const region = sessionId ? this.region.getCurrentRegion(sessionId) : null;
        parts.push(`region: ${region && region.regionId || "unknown"}`);
        const visualEntry = targetObjectId ? this.visual.byObjectId.get(targetObjectId) : null;
        const focus = visualEntry && visualEntry.focus || null;
        if (focus) {
            const componentSummaries = (focus.components || []).map((component) => {
                const fields = component.fields || {};
                const role = fields.anchorRole || fields.description;
                return role ? `${component.type}(${role})` : component.type;
            }).filter((name) => name && name !== "Transform");
            parts.push(`target: ${focus.name || targetObjectId} tag=${focus.tag || "untagged"}` +
                (componentSummaries.length ? ` components=[${componentSummaries.join(", ")}]` : ""));
        } else if (targetObjectId) {
            parts.push(`target: ${targetObjectId} (no cached visual state)`);
        }
        if (targetObjectId) {
            const affordances = this.sceneGraph.queryAffordances({ objectId: targetObjectId });
            const names = Array.isArray(affordances && affordances.affordances)
                ? affordances.affordances : Array.isArray(affordances) ? affordances : [];
            if (names.length) parts.push(`affordances: ${names.join(", ")}`);
            const relations = this.sceneGraph.queryGraph({ objectId: targetObjectId }).slice(-4)
                .map((relation) => `${relation.relation}:${relation.from === targetObjectId ? relation.to : relation.from}`);
            if (relations.length) parts.push(`nearby: ${[...new Set(relations)].join(", ")}`);
        }
        const experience = sessionId ? this.experienceContext.get(sessionId) : null;
        parts.push(`experience mode: ${experience && experience.mode || "unspecified"}`);
        return parts.join("; ");
    }

    // Subscribes to every envelope (inbound and outbound) a SceneBridgeClient sees.
    attach(bridge) {
        bridge.on("envelope", (envelope) => {
            this.timeline.observeEnvelope(envelope);
            const hasStudyContext = Boolean(this.artifactLog.getStudyContext({
                sessionId: envelope.sessionId,
                correlationId: envelope.correlationId,
            }));
            const recordStudyEnvelope = (event) => {
                if (!hasStudyContext) return null;
                return this.artifactLog.appendStudyEvent({
                    sessionId: envelope.sessionId,
                    correlationId: envelope.correlationId,
                    targetObjectId: envelope.targetObjectId || null,
                    at: envelope.timestamp || Date.now(),
                    correlationIdValid: typeof envelope.correlationId === "string" &&
                        STUDY_ID_PATTERN.test(envelope.correlationId),
                    targetObjectValid: envelope.targetObjectId == null ||
                        (typeof envelope.targetObjectId === "string" && envelope.targetObjectId.length > 0),
                    timestampAgeMs: Number.isFinite(envelope.timestamp)
                        ? Math.max(0, Date.now() - envelope.timestamp) : null,
                    ...event,
                });
            };
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
                this.activity.observeSceneDelta(envelope);
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
                recordStudyEnvelope({
                    eventType: "proposal_preview_surfaced",
                    artifactVersion: envelope.artifactVersion || null,
                    candidateId: envelope.candidateId || null,
                    candidateSetId: envelope.candidateSetId || null,
                    validationState: envelope.validationState || null,
                    riskScore: envelope.riskScore,
                });
            }
            if (envelope.type === "AgentStatus") {
                recordStudyEnvelope({
                    eventType: "agent_status_sent",
                    status: envelope.payload && envelope.payload.state,
                });
            }
            if (envelope.type === "AgentStatusVisible") {
                recordStudyEnvelope({
                    eventType: "agent_status_surfaced",
                    status: envelope.payload && envelope.payload.status,
                });
            }
            if (envelope.type === "AgentUtterance") {
                recordStudyEnvelope({ eventType: "agent_acknowledgement_surfaced" });
            }
            if (["UserDecision", "ArtifactResult", "CommitAccepted", "CommitRejected", "RollbackResult"].includes(envelope.type)) {
                if (envelope.type === "UserDecision") this.activity.observeDecision(envelope);
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
                    consentRoute: envelope.consentRoute || null,
                    riskScore: envelope.riskScore,
                    validationState: envelope.validationState || null,
                    verificationDurationMs: envelope.verificationDurationMs ?? null,
                    commitAttachDurationMs: envelope.commitAttachDurationMs ?? null,
                    timestampAgeMs: Number.isFinite(envelope.timestamp)
                        ? Math.max(0, Date.now() - envelope.timestamp) : null,
                    correlationIdValid: typeof envelope.correlationId === "string" &&
                        STUDY_ID_PATTERN.test(envelope.correlationId),
                    targetObjectValid: typeof envelope.targetObjectId === "string" &&
                        envelope.targetObjectId.length > 0,
                    blockedUnsafeArtifact: Boolean(envelope.payload &&
                        /capability|namespace|unsafe/i.test(envelope.payload.reason || envelope.payload.error || "")),
                    staleness: envelope.staleness || null,
                    staleApplication: Boolean(envelope.type === "ArtifactResult" &&
                        envelope.staleness && envelope.staleness.isStale &&
                        envelope.payload && ["committed", "removed"].includes(envelope.payload.status)),
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
