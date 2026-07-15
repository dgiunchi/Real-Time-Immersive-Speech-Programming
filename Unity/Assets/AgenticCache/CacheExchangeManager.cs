using System;
using System.Collections.Generic;
using System.Text;
using Ubiq.Messaging;
using UnityEngine;

namespace AgenticCache
{
    // The authoritative side of the Cache Exchange Layer (main.tex, Cache Exchange
    // Layer §; docs/cache-exchange-layer.md). "Unity remains authoritative for live
    // mutation. Backend agents may propose artifacts [but] Unity decides whether a
    // proposal is fresh, valid, consent-compatible, and safe to preview/commit" -
    // this class is where that decision actually happens (HandleCommitRequest).
    //
    // Registers four small CacheChannelRelay components (96, 97, 99, 101 - see that
    // class for why one component can't cover multiple NetworkIds) plus reuses
    // CachePublisher's channel-95 connection for DetailResponse/BackfillResponse,
    // and opens one more send-only registration on 100 for
    // CommitAccepted/CommitRejected/RollbackResult.
    //
    // NOT compiled/verified in this environment - no Unity Editor available. Written
    // against the exact patterns proven in CodeGenerationManager.cs/
    // MicrophoneCapture.cs/SelectRay.cs (NetworkScene.Register, context.Send with a
    // ReferenceCountedSceneGraphMessage). See docs/cache-exchange-layer.md for the
    // full list of what's scaffolded vs. verified vs. TODO.
    public class CacheExchangeManager : MonoBehaviour
    {
        public LocalXRCache localCache = new LocalXRCache();
        public CachePublisher cachePublisher;

        [Tooltip("Per-authoringMode max acceptable snapshot age, mirroring Server/cache/proposal_gate.js's DEFAULT_MAX_SNAPSHOT_AGE_MS_BY_MODE.")]
        public float maxSnapshotAgeMsAutomatic = 2000f;
        public float maxSnapshotAgeMsSemiAutoConfirm = 15000f;
        public float maxSnapshotAgeMsSemiAutoSteer = 30000f;

        private NetworkId decisionChannelId = new NetworkId(100);
        private NetworkContext decisionContext;

        // Compact per-object delta history kept locally so BackfillResponse can
        // serve missing ranges without re-deriving them - mirrors
        // mock_unity_peer.js's deltaHistoryBySession on the Node side, which this
        // Unity implementation is meant to eventually replace.
        private readonly Dictionary<string, List<(long deltaSeq, string stableObjectId, long objectRevision, string tag, string region, string stateJson, long timestamp)>> deltaHistoryBySession =
            new Dictionary<string, List<(long, string, long, string, string, string, long)>>();

        private void Start()
        {
            decisionContext = NetworkScene.Register(this, decisionChannelId);

            AddRelay(new NetworkId(96), HandleChannel96); // SceneQuery(legacy)/DetailRequest, BackfillRequest
            AddRelay(new NetworkId(97), HandleChannel97); // AgentUtterance(legacy), AgentStatus
            AddRelay(new NetworkId(99), HandleChannel99); // ArtifactProposal(legacy), CommitRequest, RollbackRequest
            AddRelay(new NetworkId(101), HandleChannel101); // AgentPresenceHeartbeat(legacy), CacheInvalidation, DeltaAck, DeltaNack
        }

        private void AddRelay(NetworkId id, Action<CacheEnvelope, ReferenceCountedSceneGraphMessage> handler)
        {
            var relay = gameObject.AddComponent<CacheChannelRelay>();
            relay.Init(id, handler);
        }

        // --- Channel 96: SceneQuery(legacy) / DetailRequest, BackfillRequest ---

        private void HandleChannel96(CacheEnvelope envelope, ReferenceCountedSceneGraphMessage raw)
        {
            switch (envelope.type)
            {
                case CacheMessageTypes.BackfillRequest:
                    HandleBackfillRequest(envelope);
                    break;
                case CacheMessageTypes.DetailRequest:
                case CacheMessageTypes.SceneQuery:
                    HandleDetailRequest(envelope);
                    break;
                default:
                    Debug.LogWarning($"[CacheExchangeManager] unexpected type '{envelope.type}' on channel 96");
                    break;
            }
        }

        // "Ask Unity for a fresh snapshot" (payload.requestSnapshot=true) or recover
        // a missing deltaSeq range (payload.lastSeenSeq). NOTE: because
        // BackfillRequest is not one of the three legacy types, its payload IS
        // pre-stringified by the Node side (STRINGIFY_PAYLOAD_FOR_UNITY) - this
        // handler can reliably parse it, unlike the ArtifactProposal/SceneQuery/
        // AgentUtterance handlers below (see CacheEnvelope.cs's class comment).
        private void HandleBackfillRequest(CacheEnvelope envelope)
        {
            var payload = JsonUtility.FromJson<BackfillRequestPayload>(envelope.payload);

            if (payload != null && payload.requestSnapshot)
            {
                Debug.Log($"[CacheExchangeManager] BackfillRequest(requestSnapshot) correlationId={envelope.correlationId}");
                // TODO: build the real snapshot from the live scene (stable IDs,
                // per SceneController.cs, once it publishes them - see
                // docs/agentic-xr-architecture.md phase 1). For now this scaffold
                // does not synthesize scene content on Unity's behalf, matching
                // this pass's server-side-only verification scope.
                return;
            }

            long lastSeenSeq = payload != null ? payload.lastSeenSeq : 0;
            if (!deltaHistoryBySession.TryGetValue(envelope.sessionId, out var history))
            {
                history = new List<(long, string, long, string, string, string, long)>();
            }

            var sb = new StringBuilder();
            sb.Append("{\"deltas\":[");
            bool first = true;
            int count = 0;
            foreach (var entry in history)
            {
                if (entry.deltaSeq <= lastSeenSeq) continue;
                if (!first) sb.Append(',');
                first = false;
                count++;
                sb.Append($"{{\"deltaSeq\":{entry.deltaSeq},\"stableObjectId\":\"{entry.stableObjectId}\",\"objectRevision\":{entry.objectRevision}," +
                          $"\"tag\":\"{entry.tag}\",\"region\":\"{entry.region}\",\"state\":{entry.stateJson},\"timestamp\":{entry.timestamp}}}");
            }
            sb.Append("]}");

            var response = new CacheEnvelope
            {
                schemaVersion = "1.0",
                type = CacheMessageTypes.BackfillResponse,
                sessionId = envelope.sessionId,
                correlationId = envelope.correlationId,
                originAgent = "unity_cache_exchange",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sceneEpoch = localCache.CurrentSceneEpoch,
                snapshotId = localCache.LatestSnapshotId,
                payload = sb.ToString(),
            };
            cachePublisher.SendRaw(response);
            Debug.Log($"[CacheExchangeManager] BackfillRequest lastSeenSeq={lastSeenSeq} -> sent {count} missing delta(s)");
        }

        // Legacy SceneQuery / new DetailRequest - answer with the focus+halo detail
        // for an object. TODO: wire to SceneController.cs's per-object component/
        // field reflection once it has stable IDs; this scaffold has no scene data
        // source of its own yet.
        private void HandleDetailRequest(CacheEnvelope envelope)
        {
            Debug.Log($"[CacheExchangeManager] DetailRequest/SceneQuery for target={envelope.targetObjectId} correlationId={envelope.correlationId} - TODO: real scene lookup, see docs/cache-exchange-layer.md");
        }

        // --- Channel 97: AgentUtterance(legacy), AgentStatus ---

        private void HandleChannel97(CacheEnvelope envelope, ReferenceCountedSceneGraphMessage raw)
        {
            if (envelope.type == CacheMessageTypes.AgentStatus)
            {
                var payload = JsonUtility.FromJson<AgentStatusPayload>(envelope.payload);
                localCache.SetAgentStatus(payload?.state, payload?.detail);
                Debug.Log($"[CacheExchangeManager] AgentStatus: {payload?.state} ({payload?.detail}) - perceived synchronicity: show this immediately to the user, before any validation completes.");
                return;
            }
            // AgentUtterance(legacy): plain speech/text filler - TODO surface to the
            // Coordinator's UI/TTS once that exists; payload parsing is unreliable
            // here (see CacheEnvelope.cs class comment).
            Debug.Log($"[CacheExchangeManager] AgentUtterance received correlationId={envelope.correlationId}");
        }

        // --- Channel 99: ArtifactProposal(legacy), CommitRequest, RollbackRequest ---

        private void HandleChannel99(CacheEnvelope envelope, ReferenceCountedSceneGraphMessage raw)
        {
            switch (envelope.type)
            {
                case CacheMessageTypes.CommitRequest:
                    HandleCommitRequest(envelope);
                    break;
                case CacheMessageTypes.RollbackRequest:
                    HandleRollbackRequest(envelope);
                    break;
                case CacheMessageTypes.ArtifactProposal:
                    HandleArtifactProposal(envelope);
                    break;
                default:
                    Debug.LogWarning($"[CacheExchangeManager] unexpected type '{envelope.type}' on channel 99");
                    break;
            }
        }

        // Records the proposal as pending and (TODO) shows the confirm/ghost-preview
        // UI for non-automatic authoringMode. Payload (code/intent) is not reliably
        // parseable yet - see CacheEnvelope.cs's class comment - so this only
        // bookkeeps the envelope-level fields for now.
        private void HandleArtifactProposal(CacheEnvelope envelope)
        {
            localCache.MarkProposalPending(envelope.correlationId, envelope.targetObjectId, envelope.snapshotId, envelope.objectRevision, envelope.authoringMode, envelope.interactionMode);
            Debug.Log($"[CacheExchangeManager] ArtifactProposal pending correlationId={envelope.correlationId} target={envelope.targetObjectId} mode={envelope.authoringMode} - TODO: confirm/ghost-preview UI");
        }

        // THE authoritative compare-and-swap gate (main.tex, Cache Exchange Layer §
        // final paragraph). Every check mirrors Server/cache/proposal_gate.js's
        // advisory pre-flight check, but THIS is the one that actually decides -
        // the backend's version can never override a rejection here.
        private void HandleCommitRequest(CacheEnvelope envelope)
        {
            var reasons = new List<string>();

            var proposal = localCache.GetProposalByCorrelationId(envelope.correlationId);
            if (proposal == null)
            {
                reasons.Add("correlationId not found or already resolved");
            }
            else if (proposal.invalidated)
            {
                reasons.Add($"correlationId invalidated: {proposal.invalidationReason}");
            }

            var record = localCache.GetByStableObjectId(envelope.targetObjectId);
            if (record == null)
            {
                reasons.Add("target object not found in Local XR Cache");
            }

            if (!string.IsNullOrEmpty(localCache.CurrentSceneEpoch) && !string.IsNullOrEmpty(envelope.sceneEpoch) && envelope.sceneEpoch != localCache.CurrentSceneEpoch)
            {
                reasons.Add($"sceneEpoch mismatch: proposal={envelope.sceneEpoch} current={localCache.CurrentSceneEpoch}");
            }

            if (record != null && envelope.HasObjectRevision && record.objectRevision >= 0 && envelope.objectRevision != record.objectRevision)
            {
                reasons.Add($"objectRevision mismatch: proposal={envelope.objectRevision} current={record.objectRevision}");
            }

            float maxAgeMs = envelope.authoringMode == "automatic" ? maxSnapshotAgeMsAutomatic
                : envelope.authoringMode == "semi_auto_steer" ? maxSnapshotAgeMsSemiAutoSteer
                : maxSnapshotAgeMsSemiAutoConfirm;
            long ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - envelope.timestamp;
            if (ageMs > (long)maxAgeMs)
            {
                reasons.Add($"snapshot too old for '{envelope.authoringMode}' mode: ageMs={ageMs} maxAgeMs={maxAgeMs}");
            }

            bool accepted = reasons.Count == 0;
            var result = new CacheEnvelope
            {
                schemaVersion = "1.0",
                type = accepted ? CacheMessageTypes.CommitAccepted : CacheMessageTypes.CommitRejected,
                sessionId = envelope.sessionId,
                correlationId = envelope.correlationId,
                originAgent = "unity_cache_exchange",
                targetObjectId = envelope.targetObjectId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                payload = accepted ? "{\"artifactId\":\"unity-" + envelope.correlationId + "\"}" : SerializeReasons(reasons),
            };

            if (accepted)
            {
                // TODO: this is where the real Roslyn compile+attach happens (see
                // Unity/Assets/Scenes/Scripts/TestRoslyn.cs's RunCode pattern) -
                // deliberately not wired here since ArtifactProposal's `code` field
                // is not reliably parseable yet (see class-level comment).
                localCache.SetRollbackPointer(result.payload, proposal?.targetObjectId);
            }
            localCache.ClearProposal(envelope.correlationId);

            SendOnDecisionChannel(result);
            Debug.Log($"[CacheExchangeManager] CommitRequest {envelope.correlationId} -> {(accepted ? "CommitAccepted" : "CommitRejected: " + string.Join("; ", reasons))}");
        }

        private void HandleRollbackRequest(CacheEnvelope envelope)
        {
            // TODO: actually destroy/replace the live component per
            // localCache.GetRollbackPointer(artifactId), mirroring the rollback
            // design in docs/agentic-xr-architecture.md §4.1.
            var result = new CacheEnvelope
            {
                schemaVersion = "1.0",
                type = CacheMessageTypes.RollbackResult,
                sessionId = envelope.sessionId,
                correlationId = envelope.correlationId,
                originAgent = "unity_cache_exchange",
                targetObjectId = envelope.targetObjectId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                payload = "{\"status\":\"rolled_back\"}",
            };
            SendOnDecisionChannel(result);
            Debug.Log($"[CacheExchangeManager] RollbackRequest {envelope.correlationId} -> RollbackResult rolled_back (TODO: real component rollback)");
        }

        // --- Channel 101: AgentPresenceHeartbeat(legacy), CacheInvalidation, DeltaAck, DeltaNack ---

        private void HandleChannel101(CacheEnvelope envelope, ReferenceCountedSceneGraphMessage raw)
        {
            switch (envelope.type)
            {
                case CacheMessageTypes.CacheInvalidation:
                    var payload = JsonUtility.FromJson<CacheInvalidationPayload>(envelope.payload);
                    if (!string.IsNullOrEmpty(envelope.correlationId)) localCache.InvalidateProposal(envelope.correlationId, payload?.reason);
                    if (!string.IsNullOrEmpty(envelope.targetObjectId)) localCache.InvalidateProposalsForObject(envelope.targetObjectId, payload?.reason);
                    Debug.Log($"[CacheExchangeManager] CacheInvalidation target={envelope.targetObjectId} reason={payload?.reason}");
                    break;
                case CacheMessageTypes.DeltaAck:
                    Debug.Log($"[CacheExchangeManager] DeltaAck deltaSeq={envelope.deltaSeq}");
                    break;
                case CacheMessageTypes.DeltaNack:
                    Debug.LogWarning($"[CacheExchangeManager] DeltaNack deltaSeq={envelope.deltaSeq} - TODO: consider resending from local delta history");
                    break;
                // AgentPresenceHeartbeat(legacy): no action needed beyond liveness.
            }
        }

        private void SendOnDecisionChannel(CacheEnvelope envelope)
        {
            string json = JsonUtility.ToJson(envelope);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var message = ReferenceCountedSceneGraphMessage.Rent(bytes.Length);
            bytes.CopyTo(new Span<byte>(message.bytes, message.start, bytes.Length));
            decisionContext.Send(message);
        }

        private static string SerializeReasons(List<string> reasons)
        {
            var sb = new StringBuilder();
            sb.Append("{\"reasons\":[");
            for (int i = 0; i < reasons.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(reasons[i].Replace("\"", "\\\"")).Append('"');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [Serializable] private class BackfillRequestPayload { public bool requestSnapshot; public long lastSeenSeq; }
        [Serializable] private class AgentStatusPayload { public string state; public string detail; }
        [Serializable] private class CacheInvalidationPayload { public string reason; }
    }
}
