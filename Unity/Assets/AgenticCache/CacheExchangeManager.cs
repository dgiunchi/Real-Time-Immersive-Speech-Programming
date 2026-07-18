using RoslynCSharp;
using System;
using System.Collections.Generic;
using System.Text;
using Ubiq.Messaging;
using UnityEngine;

namespace AgenticCache
{
    public sealed class CacheExchangeManager : MonoBehaviour
    {
        [Serializable]
        private sealed class ArtifactProposalPayload
        {
            public string code;
            public string intent;
            public string mode;
        }

        [Serializable] private sealed class BackfillRequestPayload { public bool requestSnapshot; public long lastSeenSeq; }
        [Serializable] private sealed class AgentStatusPayload { public string state; public string detail; }
        [Serializable] private sealed class AgentUtterancePayload { public string text; }
        [Serializable] private sealed class CacheInvalidationPayload { public string reason; }
        [Serializable] private sealed class RollbackPayload { public string artifactId; }

        private sealed class PendingArtifact
        {
            public CacheEnvelope envelope;
            public ArtifactProposalPayload payload;
            public GameObject target;
            public long targetRevision;
        }

        private sealed class AppliedArtifact
        {
            public string artifactId;
            public string targetObjectId;
            public ScriptProxy proxy;
            public AppliedArtifact previous;
        }

        public LocalXRCache localCache = new LocalXRCache();
        public CachePublisher cachePublisher;
        public AgenticSceneRegistry sceneRegistry;
        public AgenticXRConsentPanel consentPanel;
        public TestRoslyn compiler;
        public string sessionId = "unity-xr-session";

        public float maxSnapshotAgeMsAutomatic = 2000f;
        public float maxSnapshotAgeMsSemiAutoConfirm = 120000f;
        public float maxSnapshotAgeMsSemiAutoSteer = 120000f;

        private readonly Dictionary<string, PendingArtifact> pending = new Dictionary<string, PendingArtifact>();
        private readonly Dictionary<string, AppliedArtifact> appliedByArtifactId = new Dictionary<string, AppliedArtifact>();
        private readonly Dictionary<string, AppliedArtifact> activeByObjectId = new Dictionary<string, AppliedArtifact>();
        private NetworkContext decisionContext;
        private CacheChannelRelay presenceRelay;
        private float nextHeartbeat;
        private string latestArtifactId;

        private void Start()
        {
            decisionContext = NetworkScene.Register(this, new NetworkId(100));
            AddRelay(new NetworkId(96), HandleChannel96);
            AddRelay(new NetworkId(97), HandleChannel97);
            AddRelay(new NetworkId(99), HandleChannel99);
            presenceRelay = AddRelay(new NetworkId(101), HandleChannel101);

            if (sceneRegistry != null)
            {
                localCache.SetSceneEpoch(sceneRegistry.SceneEpoch);
                localCache.SetLatestSnapshotId(sceneRegistry.SnapshotId);
            }
            ShowStatus("ready", "Connected to the XR runtime; waiting for Claude.");
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextHeartbeat && presenceRelay != null)
            {
                nextHeartbeat = Time.unscaledTime + 2f;
                var selectedId = sceneRegistry != null ? sceneRegistry.GetSelectedObjectId() : null;
                if (!string.IsNullOrEmpty(selectedId)) localCache.SetSelectedObject(selectedId);
                presenceRelay.Send(NewEnvelope(CacheMessageTypes.AgentPresenceHeartbeat, Guid.NewGuid().ToString(), selectedId,
                    "{\"state\":\"ready\",\"selectedObjectId\":\"" + AgenticSceneRegistry.Escape(selectedId) + "\"}"));
            }
        }

        private CacheChannelRelay AddRelay(NetworkId id, Action<CacheEnvelope, ReferenceCountedSceneGraphMessage> handler)
        {
            var relay = gameObject.AddComponent<CacheChannelRelay>();
            relay.Init(id, handler);
            return relay;
        }

        private void HandleChannel96(CacheEnvelope envelope, ReferenceCountedSceneGraphMessage raw)
        {
            if (envelope.type == CacheMessageTypes.BackfillRequest) HandleBackfillRequest(envelope);
            else if (envelope.type == CacheMessageTypes.DetailRequest || envelope.type == CacheMessageTypes.SceneQuery) HandleSceneQuery(envelope);
        }

        private void HandleSceneQuery(CacheEnvelope envelope)
        {
            if (sceneRegistry == null || cachePublisher == null) return;
            var targetId = !string.IsNullOrEmpty(envelope.targetObjectId) ? envelope.targetObjectId : sceneRegistry.GetSelectedObjectId();
            var target = sceneRegistry.Find(targetId);
            if (target == null)
            {
                SendSceneResponse(envelope, "{\"focus\":null,\"halo\":[],\"error\":\"target_not_found\"}", -1);
                return;
            }
            targetId = target.GetComponent<StableObjectId>().Value;
            var revision = sceneRegistry.GetRevision(target);
            localCache.SetFocusObject(targetId);
            localCache.SetSelectedObject(targetId);
            localCache.TryAcceptObjectState(targetId, revision, target.tag, string.Empty, 5000);
            SendSceneResponse(envelope, sceneRegistry.BuildFocusAndHaloJson(targetId), revision);
            ShowStatus("scene_grounded", "Claude inspected " + target.name + ".");
        }

        private void SendSceneResponse(CacheEnvelope request, string payload, long revision)
        {
            var response = NewEnvelope(CacheMessageTypes.SceneDelta, request.correlationId, request.targetObjectId, payload);
            response.sessionId = request.sessionId;
            response.sceneEpoch = sceneRegistry.SceneEpoch;
            response.snapshotId = sceneRegistry.SnapshotId;
            response.objectRevision = revision;
            cachePublisher.SendRaw(response);
        }

        private void HandleBackfillRequest(CacheEnvelope envelope)
        {
            if (sceneRegistry == null || cachePublisher == null) return;
            var payload = Parse<BackfillRequestPayload>(envelope.payload);
            if (payload != null && payload.requestSnapshot)
            {
                var response = NewEnvelope(CacheMessageTypes.CacheSnapshot, envelope.correlationId, null, sceneRegistry.BuildSnapshotJson());
                response.sessionId = envelope.sessionId;
                response.sceneEpoch = sceneRegistry.SceneEpoch;
                response.snapshotId = sceneRegistry.SnapshotId;
                cachePublisher.SendRaw(response);
                return;
            }
            var empty = NewEnvelope(CacheMessageTypes.BackfillResponse, envelope.correlationId, null, "{\"deltas\":[]}");
            empty.sessionId = envelope.sessionId;
            empty.sceneEpoch = sceneRegistry.SceneEpoch;
            empty.snapshotId = sceneRegistry.SnapshotId;
            cachePublisher.SendRaw(empty);
        }

        private void HandleChannel97(CacheEnvelope envelope, ReferenceCountedSceneGraphMessage raw)
        {
            if (envelope.type == CacheMessageTypes.AgentStatus)
            {
                var status = Parse<AgentStatusPayload>(envelope.payload);
                localCache.SetAgentStatus(status != null ? status.state : null, status != null ? status.detail : null);
                ShowStatus(status != null ? status.state : "working", status != null ? status.detail : null);
            }
            else if (envelope.type == CacheMessageTypes.AgentUtterance)
            {
                var utterance = Parse<AgentUtterancePayload>(envelope.payload);
                ShowStatus("claude", utterance != null ? utterance.text : "Claude is responding.");
            }
        }

        private void HandleChannel99(CacheEnvelope envelope, ReferenceCountedSceneGraphMessage raw)
        {
            if (envelope.type == CacheMessageTypes.ArtifactProposal) HandleArtifactProposal(envelope);
            else if (envelope.type == CacheMessageTypes.CommitRequest) ApprovePending(envelope.correlationId);
            else if (envelope.type == CacheMessageTypes.RollbackRequest) HandleRollbackRequest(envelope);
        }

        private void HandleArtifactProposal(CacheEnvelope envelope)
        {
            var payload = Parse<ArtifactProposalPayload>(envelope.payload);
            var target = sceneRegistry != null ? sceneRegistry.Find(envelope.targetObjectId) : null;
            if (payload == null || string.IsNullOrWhiteSpace(payload.code))
            {
                SendArtifactResult(envelope, "error", null, "ArtifactProposal contained no C# code.");
                return;
            }
            if (target == null)
            {
                SendArtifactResult(envelope, "rejected", null, "The target object no longer exists.");
                return;
            }
            if (envelope.HasObjectRevision && sceneRegistry.GetRevision(target) != envelope.objectRevision)
            {
                SendArtifactResult(envelope, "rejected", null, "The target object changed while Claude was reasoning.");
                return;
            }

            ShowStatus("validating", "Testing Claude's code on a staging clone.");
            var clone = Instantiate(target);
            clone.name = target.name + " [AgenticXR Verification]";
            clone.SetActive(false);
            ScriptProxy stageProxy = null;
            var stageError = compiler == null ? "The Roslyn runtime compiler is unavailable in this scene." : null;
            var staged = compiler != null && compiler.TryCompileAndAttach(clone, payload.code, out stageProxy, out stageError);
            if (stageProxy != null) stageProxy.Dispose();
            Destroy(clone);
            if (!staged)
            {
                SendArtifactResult(envelope, "error", null, stageError ?? "Staging compilation failed.");
                return;
            }
            if (string.Equals(payload.mode, "simulate", StringComparison.OrdinalIgnoreCase))
            {
                SendArtifactResult(envelope, "simulated", null, null);
                ShowStatus("validated", "The proposal passed the Verification Space dry-run.");
                return;
            }

            localCache.MarkProposalPending(envelope.correlationId, envelope.targetObjectId, envelope.snapshotId,
                envelope.objectRevision, envelope.authoringMode, envelope.interactionMode);
            pending[envelope.correlationId] = new PendingArtifact
            {
                envelope = envelope,
                payload = payload,
                target = target,
                targetRevision = sceneRegistry.GetRevision(target),
            };
            if (string.Equals(envelope.authoringMode, "automatic", StringComparison.OrdinalIgnoreCase))
                ApprovePending(envelope.correlationId);
            else
            {
                ShowStatus("waiting_for_user", "A validated proposal needs your approval.");
                if (consentPanel != null) consentPanel.ShowProposal(envelope.correlationId, target.name, payload.intent);
            }
        }

        public void ApprovePending(string correlationId)
        {
            if (!pending.TryGetValue(correlationId, out var proposal)) return;
            var target = sceneRegistry.Find(proposal.envelope.targetObjectId);
            if (target == null)
            {
                RejectPending(correlationId, "target_no_longer_exists");
                return;
            }
            if (sceneRegistry.GetRevision(target) != proposal.targetRevision)
            {
                RejectPending(correlationId, "target_changed_before_approval");
                return;
            }
            if (!string.IsNullOrEmpty(localCache.SelectedObjectId) && localCache.SelectedObjectId != proposal.envelope.targetObjectId)
            {
                RejectPending(correlationId, "selection_changed_before_approval");
                return;
            }
            if (!compiler.TryCompileAndAttach(target, proposal.payload.code, out var proxy, out var error))
            {
                pending.Remove(correlationId);
                localCache.ClearProposal(correlationId);
                SendArtifactResult(proposal.envelope, "error", null, error);
                return;
            }

            activeByObjectId.TryGetValue(proposal.envelope.targetObjectId, out var previous);
            if (previous != null && previous.proxy != null && previous.proxy.MonoBehaviourInstance != null)
                previous.proxy.MonoBehaviourInstance.enabled = false;

            var artifactId = "unity-" + correlationId;
            var applied = new AppliedArtifact
            {
                artifactId = artifactId,
                targetObjectId = proposal.envelope.targetObjectId,
                proxy = proxy,
                previous = previous,
            };
            appliedByArtifactId[artifactId] = applied;
            activeByObjectId[applied.targetObjectId] = applied;
            latestArtifactId = artifactId;
            pending.Remove(correlationId);
            localCache.ClearProposal(correlationId);
            localCache.SetRollbackPointer(artifactId, previous != null ? previous.artifactId : null);
            if (consentPanel != null) consentPanel.HideProposal();
            SendArtifactResult(proposal.envelope, "committed", artifactId, null);
            ShowStatus("committed", "The validated behaviour is now live. Use Undo to revert it.");
        }

        public void RejectPending(string correlationId, string reason)
        {
            if (!pending.TryGetValue(correlationId, out var proposal)) return;
            pending.Remove(correlationId);
            localCache.ClearProposal(correlationId);
            if (consentPanel != null) consentPanel.HideProposal();
            SendArtifactResult(proposal.envelope, "rejected", null, reason);
            ShowStatus("rejected", "The proposal was not applied.");
        }

        public void UndoLatest()
        {
            if (string.IsNullOrEmpty(latestArtifactId))
            {
                ShowStatus("undo", "There is no generated behaviour to undo.");
                return;
            }
            Rollback(latestArtifactId);
        }

        private void HandleRollbackRequest(CacheEnvelope envelope)
        {
            var payload = Parse<RollbackPayload>(envelope.payload);
            var artifactId = payload != null && !string.IsNullOrEmpty(payload.artifactId) ? payload.artifactId : latestArtifactId;
            var ok = Rollback(artifactId);
            SendDecision(NewEnvelope(CacheMessageTypes.RollbackResult, envelope.correlationId, envelope.targetObjectId,
                ok ? "{\"status\":\"rolled_back\"}" : "{\"status\":\"not_found\"}"));
        }

        private bool Rollback(string artifactId)
        {
            if (string.IsNullOrEmpty(artifactId) || !appliedByArtifactId.TryGetValue(artifactId, out var applied)) return false;
            if (applied.proxy != null) applied.proxy.Dispose();
            if (applied.previous != null && applied.previous.proxy != null && applied.previous.proxy.MonoBehaviourInstance != null)
            {
                applied.previous.proxy.MonoBehaviourInstance.enabled = true;
                activeByObjectId[applied.targetObjectId] = applied.previous;
                latestArtifactId = applied.previous.artifactId;
            }
            else
            {
                activeByObjectId.Remove(applied.targetObjectId);
                latestArtifactId = null;
            }
            appliedByArtifactId.Remove(artifactId);
            ShowStatus("rolled_back", "The last generated behaviour was removed.");
            return true;
        }

        private void HandleChannel101(CacheEnvelope envelope, ReferenceCountedSceneGraphMessage raw)
        {
            if (envelope.type != CacheMessageTypes.CacheInvalidation) return;
            var payload = Parse<CacheInvalidationPayload>(envelope.payload);
            if (!string.IsNullOrEmpty(envelope.correlationId)) localCache.InvalidateProposal(envelope.correlationId, payload != null ? payload.reason : null);
            if (!string.IsNullOrEmpty(envelope.targetObjectId)) localCache.InvalidateProposalsForObject(envelope.targetObjectId, payload != null ? payload.reason : null);
        }

        private void SendArtifactResult(CacheEnvelope request, string status, string artifactId, string error)
        {
            var payload = new StringBuilder("{\"status\":\"").Append(AgenticSceneRegistry.Escape(status)).Append('"');
            if (!string.IsNullOrEmpty(artifactId)) payload.Append(",\"artifactId\":\"").Append(AgenticSceneRegistry.Escape(artifactId)).Append('"');
            if (!string.IsNullOrEmpty(error)) payload.Append(",\"error\":\"").Append(AgenticSceneRegistry.Escape(error)).Append('"');
            payload.Append('}');
            var response = NewEnvelope(CacheMessageTypes.ArtifactResult, request.correlationId, request.targetObjectId, payload.ToString());
            response.sessionId = request.sessionId;
            response.authoringMode = request.authoringMode;
            response.interactionMode = request.interactionMode;
            SendDecision(response);
        }

        private void SendDecision(CacheEnvelope envelope)
        {
            var json = JsonUtility.ToJson(envelope);
            var bytes = Encoding.UTF8.GetBytes(json);
            var message = ReferenceCountedSceneGraphMessage.Rent(bytes.Length);
            bytes.CopyTo(new Span<byte>(message.bytes, message.start, bytes.Length));
            decisionContext.Send(message);
        }

        private CacheEnvelope NewEnvelope(string type, string correlationId, string targetObjectId, string payload)
        {
            return new CacheEnvelope
            {
                schemaVersion = "1.0",
                type = type,
                sessionId = sessionId,
                correlationId = correlationId,
                originAgent = "unity_agenticxr",
                targetObjectId = targetObjectId,
                stableObjectId = targetObjectId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sceneEpoch = sceneRegistry != null ? sceneRegistry.SceneEpoch : null,
                snapshotId = sceneRegistry != null ? sceneRegistry.SnapshotId : null,
                payload = payload,
            };
        }

        private static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception e) { Debug.LogWarning("[AgenticXR] invalid payload: " + e.Message); return null; }
        }

        private void ShowStatus(string state, string detail)
        {
            localCache.SetAgentStatus(state, detail);
            if (consentPanel != null) consentPanel.ShowStatus(state, detail);
            Debug.Log("[AgenticXR] " + state + ": " + detail);
        }

        // Required by NetworkScene.Register for this component's send-only channel
        // 100 context. Replies are handled by the dedicated channel relays.
        public void ProcessMessage(ReferenceCountedSceneGraphMessage message) { }
    }
}
