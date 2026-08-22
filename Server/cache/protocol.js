"use strict";

// Cache Exchange Layer protocol (main.tex, "Cache Exchange Layer" subsection;
// rag/prompts/cache_exchange_agenticxr_prompt.md; docs/cache-exchange-layer.md).
//
// Channel mapping: the paper's updated channel table (tab:channels) fits every new
// message type onto the SIX channels the bridge already owns (95-97, 99-101) - no
// new NetworkIds are allocated. 94/98 (legacy) are untouched.
//
//   95  Unity -> Server : SceneDelta, CacheSnapshot, BackfillResponse
//   96  Server -> Unity : SceneQuery/DetailRequest, BackfillRequest
//   97  Server -> Unity : AgentUtterance, AgentStatus
//   99  Server -> Unity : ArtifactProposal, CommitRequest, RollbackRequest
//   100 Unity -> Server : ArtifactResult/UserDecision, CommitAccepted, CommitRejected, RollbackResult
//   101 Bidirectional   : AgentPresenceHeartbeat, CacheInvalidation, DeltaAck, DeltaNack
//
// One documented deviation from the paper's compressed table: the table lists
// "delta acks" under the Unity-to-Server row (100). This scaffold sends
// DeltaAck/DeltaNack Server->Unity instead, because the RECEIVER of a SceneDelta is
// the backend (deltas flow Unity->Server on 95), and acknowledging what you received
// is the only direction that makes distributed-systems sense. Channel 101
// (bidirectional, "heartbeat, invalidation, recovery") accommodates this without
// needing a new NetworkId. See docs/cache-exchange-layer.md for the full rationale.

const { CHANNELS, makeEnvelope } = require("../mcp/unity_scene_bridge/protocol");

const CACHE_MESSAGE_TYPES = Object.freeze({
    CACHE_SNAPSHOT: "CacheSnapshot",
    SCENE_DELTA: "SceneDelta",
    DELTA_ACK: "DeltaAck",
    DELTA_NACK: "DeltaNack",
    BACKFILL_REQUEST: "BackfillRequest",
    BACKFILL_RESPONSE: "BackfillResponse",
    CACHE_INVALIDATION: "CacheInvalidation",
    AGENT_STATUS: "AgentStatus",
    AGENT_STATUS_VISIBLE: "AgentStatusVisible",
    PROPOSAL_VISIBLE: "ProposalVisible",
    DETAIL_REQUEST: "DetailRequest",
    DETAIL_RESPONSE: "DetailResponse",
    ARTIFACT_PROPOSAL: "ArtifactProposal",
    COMMIT_REQUEST: "CommitRequest",
    COMMIT_ACCEPTED: "CommitAccepted",
    COMMIT_REJECTED: "CommitRejected",
    ROLLBACK_REQUEST: "RollbackRequest",
    ROLLBACK_RESULT: "RollbackResult",
    CANCEL_REQUEST: "CancelRequest",
    TRIAL_RESET_REQUEST: "TrialResetRequest",
    TRIAL_RESET: "TrialReset",
    STUDY_TRIAL_START_REQUEST: "StudyTrialStartRequest",
    STUDY_TRIAL_CONFIGURED: "StudyTrialConfigured",
});

// Which channel each new type rides on, for reference/assertions - the actual
// scene.send() calls live in scene_bridge_client.js, this map exists so tests and
// the reconciler can validate a message arrived on the channel it's supposed to.
const CHANNEL_BY_TYPE = Object.freeze({
    [CACHE_MESSAGE_TYPES.CACHE_SNAPSHOT]: CHANNELS.SCENE_DELTA,
    [CACHE_MESSAGE_TYPES.SCENE_DELTA]: CHANNELS.SCENE_DELTA,
    [CACHE_MESSAGE_TYPES.BACKFILL_RESPONSE]: CHANNELS.SCENE_DELTA,
    [CACHE_MESSAGE_TYPES.DETAIL_REQUEST]: CHANNELS.SCENE_QUERY,
    [CACHE_MESSAGE_TYPES.BACKFILL_REQUEST]: CHANNELS.SCENE_QUERY,
    [CACHE_MESSAGE_TYPES.AGENT_STATUS]: CHANNELS.AGENT_UTTERANCE,
    [CACHE_MESSAGE_TYPES.AGENT_STATUS_VISIBLE]: CHANNELS.USER_DECISION,
    [CACHE_MESSAGE_TYPES.PROPOSAL_VISIBLE]: CHANNELS.USER_DECISION,
    [CACHE_MESSAGE_TYPES.ARTIFACT_PROPOSAL]: CHANNELS.ARTIFACT_CHANNEL,
    [CACHE_MESSAGE_TYPES.COMMIT_REQUEST]: CHANNELS.ARTIFACT_CHANNEL,
    [CACHE_MESSAGE_TYPES.ROLLBACK_REQUEST]: CHANNELS.ARTIFACT_CHANNEL,
    [CACHE_MESSAGE_TYPES.COMMIT_ACCEPTED]: CHANNELS.USER_DECISION,
    [CACHE_MESSAGE_TYPES.COMMIT_REJECTED]: CHANNELS.USER_DECISION,
    [CACHE_MESSAGE_TYPES.ROLLBACK_RESULT]: CHANNELS.USER_DECISION,
    [CACHE_MESSAGE_TYPES.TRIAL_RESET_REQUEST]: CHANNELS.ARTIFACT_CHANNEL,
    [CACHE_MESSAGE_TYPES.TRIAL_RESET]: CHANNELS.USER_DECISION,
    [CACHE_MESSAGE_TYPES.STUDY_TRIAL_START_REQUEST]: CHANNELS.USER_DECISION,
    [CACHE_MESSAGE_TYPES.STUDY_TRIAL_CONFIGURED]: CHANNELS.AGENT_UTTERANCE,
    [CACHE_MESSAGE_TYPES.CANCEL_REQUEST]: CHANNELS.USER_DECISION,
    [CACHE_MESSAGE_TYPES.DELTA_ACK]: CHANNELS.AGENT_PRESENCE,
    [CACHE_MESSAGE_TYPES.DELTA_NACK]: CHANNELS.AGENT_PRESENCE,
    [CACHE_MESSAGE_TYPES.CACHE_INVALIDATION]: CHANNELS.AGENT_PRESENCE,
});

// Message types whose Unity-bound payload must be pre-stringified rather than sent
// as a nested JSON object. Unity's JsonUtility (used by the existing, PROVEN
// CodeGenerationManager.cs pattern) cannot deserialize an arbitrary nested object
// into a field - the existing bridge types (SceneQuery/SceneDelta/ArtifactProposal/
// ArtifactResult/AgentUtterance/AgentPresenceHeartbeat) were never actually
// exercised against a real Unity JsonUtility parser (Unity side was design-only
// until now), so this was a latent, undiscovered issue for them too. For the NEW
// Cache Exchange types - being given real Unity handlers in this pass - this
// scaffold sends `payload` as a JSON string, and Unity's CacheEnvelope.payload field
// is typed `string` accordingly. See docs/cache-exchange-layer.md.
const STRINGIFY_PAYLOAD_FOR_UNITY = new Set([
    // Original AgenticXR bridge messages. These now use the same payload-string
    // convention as the cache messages so Unity has one unambiguous wire shape.
    "SceneQuery",
    "SceneDelta",
    "ArtifactProposal",
    "ArtifactResult",
    "AgentUtterance",
    "AgentPresenceHeartbeat",
    "UserDecision",
    CACHE_MESSAGE_TYPES.CACHE_SNAPSHOT,
    CACHE_MESSAGE_TYPES.DELTA_ACK,
    CACHE_MESSAGE_TYPES.DELTA_NACK,
    CACHE_MESSAGE_TYPES.BACKFILL_REQUEST,
    CACHE_MESSAGE_TYPES.BACKFILL_RESPONSE,
    CACHE_MESSAGE_TYPES.CACHE_INVALIDATION,
    CACHE_MESSAGE_TYPES.AGENT_STATUS,
    CACHE_MESSAGE_TYPES.AGENT_STATUS_VISIBLE,
    CACHE_MESSAGE_TYPES.PROPOSAL_VISIBLE,
    CACHE_MESSAGE_TYPES.DETAIL_REQUEST,
    CACHE_MESSAGE_TYPES.DETAIL_RESPONSE,
    CACHE_MESSAGE_TYPES.COMMIT_REQUEST,
    CACHE_MESSAGE_TYPES.COMMIT_ACCEPTED,
    CACHE_MESSAGE_TYPES.COMMIT_REJECTED,
    CACHE_MESSAGE_TYPES.ROLLBACK_REQUEST,
    CACHE_MESSAGE_TYPES.ROLLBACK_RESULT,
    CACHE_MESSAGE_TYPES.CANCEL_REQUEST,
    CACHE_MESSAGE_TYPES.TRIAL_RESET_REQUEST,
    CACHE_MESSAGE_TYPES.TRIAL_RESET,
    CACHE_MESSAGE_TYPES.STUDY_TRIAL_START_REQUEST,
    CACHE_MESSAGE_TYPES.STUDY_TRIAL_CONFIGURED,
]);

// deltaSeq is per-session (backfill is requested as "sessionId + lastSeenSeq"), not
// global - callers that publish deltas (in real life, Unity; here, mock_unity_peer.js)
// should keep one counter per sessionId. This factory hands out such a counter.
function createDeltaSeqCounter() {
    let seq = 0;
    return function nextDeltaSeq() {
        seq += 1;
        return seq;
    };
}

// Extends the existing envelope (schemaVersion, type, sessionId, correlationId,
// originAgent, targetObjectId, authoringMode, interactionMode, priority, timestamp,
// payload - protocol.js) with the Cache Exchange Layer's freshness/idempotence
// fields (tab:envelope): sceneEpoch, snapshotId, deltaSeq, objectRevision, source,
// confidence, ttlMs. stableObjectId is an alias for targetObjectId at the wire
// level (same concept, paper's preferred name for this layer) - both are set to the
// same value so either name can be read.
function makeCacheEnvelope({
    type,
    sessionId,
    correlationId,
    originAgent,
    targetObjectId,
    stableObjectId,
    authoringMode,
    interactionMode,
    priority,
    payload,
    sceneEpoch,
    snapshotId,
    deltaSeq,
    objectRevision,
    source,
    confidence,
    ttlMs,
}) {
    const objectId = targetObjectId || stableObjectId || null;
    const envelope = makeEnvelope({
        type,
        sessionId,
        correlationId,
        originAgent,
        targetObjectId: objectId,
        authoringMode,
        interactionMode,
        priority,
        payload,
    });
    envelope.stableObjectId = objectId;
    envelope.sceneEpoch = sceneEpoch || null;
    envelope.snapshotId = snapshotId || null;
    envelope.deltaSeq = deltaSeq != null ? deltaSeq : null;
    envelope.objectRevision = objectRevision != null ? objectRevision : null;
    envelope.source = source || "server";
    envelope.confidence = typeof confidence === "number" ? confidence : 1;
    envelope.ttlMs = ttlMs != null ? ttlMs : null;
    return envelope;
}

// Returns a copy suitable for scene.send() when the receiver is (eventually) a real
// Unity JsonUtility parser - see STRINGIFY_PAYLOAD_FOR_UNITY above.
function toWireFormat(envelope) {
    if (!STRINGIFY_PAYLOAD_FOR_UNITY.has(envelope.type)) return envelope;
    return { ...envelope, payload: JSON.stringify(envelope.payload || {}) };
}

// Inverse of toWireFormat - used by anything on the Node side (mock peer, tests)
// that needs to read a stringified payload back as an object.
function fromWireFormat(envelope) {
    if (typeof envelope.payload !== "string") return envelope;
    try {
        return { ...envelope, payload: JSON.parse(envelope.payload) };
    } catch (err) {
        console.error(`[cache/protocol] failed to parse stringified payload for ${envelope.type}: ${err.message}`);
        return envelope;
    }
}

module.exports = {
    CACHE_MESSAGE_TYPES,
    CHANNEL_BY_TYPE,
    STRINGIFY_PAYLOAD_FOR_UNITY,
    createDeltaSeqCounter,
    makeCacheEnvelope,
    toWireFormat,
    fromWireFormat,
};
