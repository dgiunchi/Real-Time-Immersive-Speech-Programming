"use strict";

const { randomUUID } = require("crypto");

// NetworkId scheme shared with Unity. See docs/agentic-xr-architecture.md §2.1.
// 94/98 are the pre-existing DreamCodeVR channels (code_runtime_generator) and
// are left untouched here - this bridge only owns the new range below.
const CHANNELS = Object.freeze({
    AUDIO_IN: 98, // existing: Unity -> Server, push-to-talk audio (not used here)
    CODE_GENERATED_LEGACY: 94, // existing: Server -> Unity, single-shot compiled code
    SCENE_DELTA: 95, // Unity -> Server: scene state pushes (focus + halo, diffs)
    SCENE_QUERY: 96, // Server -> Unity: on-demand detail request
    AGENT_UTTERANCE: 97, // Server -> Unity: coordinator speech/text/emote (streamed)
    ARTIFACT_CHANNEL: 99, // Server -> Unity: ArtifactProposal (see note below)
    USER_DECISION: 100, // Unity -> Server: UserDecision *and* the final ArtifactResult ack
    AGENT_PRESENCE: 101, // both directions: heartbeat
});

// Clarification vs. the architecture doc's channel table: 99 always carries an
// "ArtifactProposal" envelope (authoringMode tells Unity whether to show a
// confirm UI or apply immediately). Unity always answers on 100 with a final
// "ArtifactResult" envelope (status: committed|rejected|error) sharing the same
// correlationId, whether or not a human was in the loop. Explicit user
// approve/reject clicks may also appear on 100 as "UserDecision" envelopes for
// telemetry, but callers of proposeArtifact() only ever wait on "ArtifactResult".

const SCHEMA_VERSION = "1.0";
const SESSION_ID_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;

function validateSessionId(sessionId) {
    if (typeof sessionId !== "string" || !SESSION_ID_PATTERN.test(sessionId)) {
        throw new Error("sessionId is required and must be 1-128 safe identifier characters");
    }
    return sessionId;
}

// The paper's five interaction modes (main.tex, tab:modes) - who *initiates* an
// authoring episode. Distinct from authoringMode, which answers who *controls
// execution* (docs/next-build-prompt.md §1.1). Optional, additive: existing
// authoringMode routing is unchanged by this field's presence or absence.
const INTERACTION_MODES = Object.freeze({
    L1: "L1", // Proactive automatic authoring
    L2: "L2", // Context-triggered automatic authoring
    L3: "L3", // Clarification-driven authoring
    L4: "L4", // Confirmation-gated authoring
    L5: "L5", // Conversational co-authoring
});

function makeEnvelope({ type, sessionId, correlationId, originAgent, targetObjectId, authoringMode, interactionMode, priority, payload }) {
    if (!type) {
        throw new Error("makeEnvelope requires a 'type'");
    }
    return {
        schemaVersion: SCHEMA_VERSION,
        type,
        sessionId: validateSessionId(sessionId),
        correlationId: correlationId || randomUUID(),
        originAgent: originAgent || "unity_scene_bridge",
        targetObjectId: targetObjectId || null,
        authoringMode: authoringMode || null,
        interactionMode: interactionMode || null,
        priority: priority || "normal",
        timestamp: Date.now(),
        payload: payload || {},
    };
}

function isEnvelope(obj) {
    return !!obj && typeof obj === "object" && typeof obj.type === "string" && typeof obj.correlationId === "string";
}

module.exports = { CHANNELS, SCHEMA_VERSION, INTERACTION_MODES, SESSION_ID_PATTERN, validateSessionId, makeEnvelope, isEnvelope };
