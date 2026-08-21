using System;

namespace AgenticCache
{
    // Mirrors Server/mcp/unity_scene_bridge/protocol.js's makeEnvelope() +
    // Server/cache/protocol.js's makeCacheEnvelope() field-for-field.
    //
    // IMPORTANT - JsonUtility limitation (see docs/cache-exchange-layer.md
    // "Wire format and the JsonUtility payload problem"): Unity's JsonUtility
    // cannot deserialize an arbitrary nested JSON object into a field, so `payload`
    // is declared as a raw JSON STRING here. This works correctly for every NEW
    // Cache Exchange message type (CacheSnapshot, DeltaAck, DeltaNack,
    // BackfillRequest, BackfillResponse, CacheInvalidation, AgentStatus,
    // DetailRequest, DetailResponse, CommitRequest, CommitAccepted, CommitRejected,
    // RollbackRequest, RollbackResult) because the Node side pre-stringifies their
    // payload before sending (Server/cache/protocol.js STRINGIFY_PAYLOAD_FOR_UNITY).
    //
    // The original SceneQuery, AgentUtterance, and ArtifactProposal messages now use
    // the same pre-stringified payload convention in Server/cache/protocol.js, so
    // all Unity-bound envelope types share one unambiguous wire representation.
    //
    // Long fields that are conceptually nullable on the JS side (deltaSeq,
    // objectRevision, ttlMs) use -1 as the "not present" sentinel, because
    // JsonUtility historically handles nullable value types poorly. Check for -1,
    // not null.
    [Serializable]
    public class CacheEnvelope
    {
        public string schemaVersion;
        public string type;
        public string sessionId;
        public string correlationId;
        public string originAgent;
        public string targetObjectId;
        public string stableObjectId;
        public string authoringMode;
        public string interactionMode;
        public string priority;
        public long timestamp;
        public string validationState;
        public string validationSummary;
        public float riskScore = -1f;
        public string consentRoute;
        public string requiredPermissions;
        public string expectedSideEffects;
        public string artifactVersion;
        public string artifactId;
        public string rollbackPointer;
        public string operation;
        public string existingArtifactId;
        public string candidateId;
        public string candidateSetId;
        public double verificationDurationMs;
        public double commitAttachDurationMs;

        // Cache Exchange Layer fields (tab:envelope: "sceneEpoch, snapshotId ->
        // cache freshness boundary"; "deltaSeq, objectRevision -> idempotence and
        // conflict checks").
        public string sceneEpoch;
        public string snapshotId;
        public long deltaSeq = -1;
        public long objectRevision = -1;
        public string source;
        public float confidence = 1f;
        public long ttlMs = -1;

        // Raw JSON string - see class-level comment. Callers parse this themselves
        // per message type (e.g. JsonUtility.FromJson<ArtifactProposalPayload>(payload)
        // once the payload IS reliably JSON, not the current empty/malformed state
        // for the three legacy types).
        public string payload;

        public bool HasDeltaSeq => deltaSeq >= 0;
        public bool HasObjectRevision => objectRevision >= 0;
        public bool HasTtlMs => ttlMs >= 0;
    }

    // Message type string constants - mirrors Server/cache/protocol.js's
    // CACHE_MESSAGE_TYPES exactly, so C# code never hand-types a literal.
    public static class CacheMessageTypes
    {
        public const string CacheSnapshot = "CacheSnapshot";
        public const string SceneDelta = "SceneDelta";
        public const string DeltaAck = "DeltaAck";
        public const string DeltaNack = "DeltaNack";
        public const string BackfillRequest = "BackfillRequest";
        public const string BackfillResponse = "BackfillResponse";
        public const string CacheInvalidation = "CacheInvalidation";
        public const string AgentStatus = "AgentStatus";
        public const string AgentStatusVisible = "AgentStatusVisible";
        public const string DetailRequest = "DetailRequest";
        public const string DetailResponse = "DetailResponse";
        public const string ArtifactProposal = "ArtifactProposal";
        public const string CommitRequest = "CommitRequest";
        public const string CommitAccepted = "CommitAccepted";
        public const string CommitRejected = "CommitRejected";
        public const string RollbackRequest = "RollbackRequest";
        public const string RollbackResult = "RollbackResult";

        // Legacy (docs/agentic-xr-architecture.md), unchanged, still received on
        // the same channels.
        public const string SceneQuery = "SceneQuery";
        public const string ArtifactResult = "ArtifactResult";
        public const string AgentUtterance = "AgentUtterance";
        public const string AgentPresenceHeartbeat = "AgentPresenceHeartbeat";
        public const string UserDecision = "UserDecision";
        public const string CancelRequest = "CancelRequest";
    }
}
