using RoslynCSharp;
using System;
using System.Collections.Generic;
using System.Text;
using Ubiq.Messaging;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            public long snapshotTakenAt;
            public string validationState;
            public string validationSummary;
            public float riskScore = -1f;
            public string consentRoute;
            public string[] requiredPermissions;
            public string expectedSideEffects;
            public string artifactVersion;
        }

        [Serializable] private sealed class BackfillRequestPayload { public bool requestSnapshot; public long lastSeenSeq; }
        [Serializable] private sealed class DeltaAckPayload { public long deltaSeq; }
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
            public string correlationId;
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
        public float proposalTimeoutSeconds = 120f;

        private readonly Dictionary<string, PendingArtifact> pending = new Dictionary<string, PendingArtifact>();
        private readonly Dictionary<string, AppliedArtifact> appliedByArtifactId = new Dictionary<string, AppliedArtifact>();
        private readonly Dictionary<string, AppliedArtifact> activeByObjectId = new Dictionary<string, AppliedArtifact>();
        private NetworkContext decisionContext;
        private CacheChannelRelay presenceRelay;
        private float nextHeartbeat;
        private string latestArtifactId;
        private string observedSelectedObjectId;

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

        private void OnEnable() => SceneManager.activeSceneChanged += OnActiveSceneChanged;
        private void OnDisable() => SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            pending.Clear();
            localCache.ClearAllProposals();
            compiler = FindFirstObjectByType<TestRoslyn>();
            if (sceneRegistry != null)
            {
                localCache.SetSceneEpoch(sceneRegistry.SceneEpoch);
                localCache.SetLatestSnapshotId(sceneRegistry.SnapshotId);
            }
        }

        private void Update()
        {
            var selectedId = sceneRegistry != null ? sceneRegistry.GetSelectedObjectId() : null;
            if (selectedId != observedSelectedObjectId)
            {
                if (!string.IsNullOrEmpty(observedSelectedObjectId))
                    localCache.InvalidateProposalsForObject(observedSelectedObjectId, "selection_changed");
                observedSelectedObjectId = selectedId;
                localCache.SetSelectedObject(selectedId);
            }

            var timedOut = new List<string>();
            foreach (var item in pending)
            {
                var record = localCache.GetProposalByCorrelationId(item.Key);
                if (record != null && Time.unscaledTime - record.receivedAt > proposalTimeoutSeconds) timedOut.Add(item.Key);
            }
            foreach (var correlationId in timedOut) RejectPending(correlationId, "confirmation_timeout");

            if (Time.unscaledTime >= nextHeartbeat && presenceRelay != null)
            {
                nextHeartbeat = Time.unscaledTime + 2f;
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
            var needsSnapshot = payload == null || payload.requestSnapshot ||
                (!string.IsNullOrEmpty(envelope.sceneEpoch) && envelope.sceneEpoch != sceneRegistry.SceneEpoch) ||
                !cachePublisher.CanBackfillAfter(payload != null ? payload.lastSeenSeq : 0);
            if (needsSnapshot)
            {
                var response = NewEnvelope(CacheMessageTypes.CacheSnapshot, envelope.correlationId, null, sceneRegistry.BuildSnapshotJson());
                response.sessionId = envelope.sessionId;
                response.sceneEpoch = sceneRegistry.SceneEpoch;
                response.snapshotId = sceneRegistry.SnapshotId;
                cachePublisher.SendRaw(response);
                return;
            }
            var backfill = NewEnvelope(CacheMessageTypes.BackfillResponse, envelope.correlationId, null,
                cachePublisher.BuildBackfillPayload(payload.lastSeenSeq));
            backfill.sessionId = envelope.sessionId;
            backfill.sceneEpoch = sceneRegistry.SceneEpoch;
            backfill.snapshotId = sceneRegistry.SnapshotId;
            cachePublisher.SendRaw(backfill);
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
            else if (envelope.type == CacheMessageTypes.CommitRequest) CommitPending(envelope.correlationId, envelope, false);
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
            var freshnessError = ValidateProposalEnvelope(envelope, payload);
            if (freshnessError != null)
            {
                SendArtifactResult(envelope, "rejected", null, freshnessError);
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
            localCache.SetPreview(envelope.correlationId, payload.validationSummary ?? "Compiled successfully on an inactive verification clone.");
            pending[envelope.correlationId] = new PendingArtifact
            {
                envelope = envelope,
                payload = payload,
                target = target,
                targetRevision = sceneRegistry.GetRevision(target),
            };
            if (string.Equals(envelope.authoringMode, "automatic", StringComparison.OrdinalIgnoreCase))
                CommitPending(envelope.correlationId, null, false);
            else
            {
                ShowStatus("waiting_for_user", "A validated proposal needs your approval.");
                if (consentPanel != null) consentPanel.ShowProposal(envelope.correlationId, target.name, payload.intent,
                    payload.validationSummary, payload.riskScore, payload.requiredPermissions, payload.expectedSideEffects);
            }
        }

        private string ValidateProposalEnvelope(CacheEnvelope envelope, ArtifactProposalPayload payload)
        {
            var validationState = !string.IsNullOrEmpty(payload.validationState) ? payload.validationState : envelope.validationState;
            if (!string.Equals(validationState, "accepted", StringComparison.OrdinalIgnoreCase))
                return "The proposal has not passed validation.";
            if (!string.IsNullOrEmpty(envelope.sceneEpoch) && envelope.sceneEpoch != sceneRegistry.SceneEpoch)
                return "The proposal belongs to an earlier scene epoch.";
            if (!string.IsNullOrEmpty(envelope.snapshotId) && envelope.snapshotId != sceneRegistry.SnapshotId)
                return "The proposal references a stale scene snapshot.";

            var observedAt = payload.snapshotTakenAt > 0 ? payload.snapshotTakenAt : envelope.timestamp;
            var maxAge = string.Equals(envelope.authoringMode, "automatic", StringComparison.OrdinalIgnoreCase)
                ? maxSnapshotAgeMsAutomatic
                : string.Equals(envelope.authoringMode, "semi_auto_steer", StringComparison.OrdinalIgnoreCase)
                    ? maxSnapshotAgeMsSemiAutoSteer : maxSnapshotAgeMsSemiAutoConfirm;
            if (observedAt <= 0 || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - observedAt > maxAge)
                return "The proposal's scene snapshot is too old for this authoring mode.";

            var consentRoute = !string.IsNullOrEmpty(payload.consentRoute) ? payload.consentRoute : envelope.consentRoute;
            if (string.IsNullOrEmpty(consentRoute)) return "The proposal has no consent route.";
            if (string.Equals(envelope.authoringMode, "automatic", StringComparison.OrdinalIgnoreCase) &&
                (payload.riskScore < 0f || payload.riskScore >= 0.3f || consentRoute != "automatic_low_risk"))
                return "Automatic application is restricted to validated low-risk proposals.";
            return null;
        }

        public void ApprovePending(string correlationId) => CommitPending(correlationId, null, true);

        private void CommitPending(string correlationId, CacheEnvelope commitRequest, bool userApproved)
        {
            if (!pending.TryGetValue(correlationId, out var proposal))
            {
                if (commitRequest != null) SendCommitResult(commitRequest, false, null, "No active proposal matches this correlation ID.");
                return;
            }
            var cachedProposal = localCache.GetProposalByCorrelationId(correlationId);
            if (cachedProposal == null || cachedProposal.invalidated)
            {
                var reason = cachedProposal != null ? cachedProposal.invalidationReason : "proposal_not_active";
                pending.Remove(correlationId);
                localCache.ClearProposal(correlationId);
                if (commitRequest != null) SendCommitResult(commitRequest, false, null, reason);
                else SendArtifactResult(proposal.envelope, "rejected", null, reason);
                return;
            }
            var target = sceneRegistry.Find(proposal.envelope.targetObjectId);
            if (target == null)
            {
                RejectForCommit(correlationId, proposal, commitRequest, "target_no_longer_exists");
                return;
            }
            if (sceneRegistry.GetRevision(target) != proposal.targetRevision)
            {
                RejectForCommit(correlationId, proposal, commitRequest, "target_changed_before_approval");
                return;
            }
            if (!string.IsNullOrEmpty(localCache.SelectedObjectId) && localCache.SelectedObjectId != proposal.envelope.targetObjectId)
            {
                RejectForCommit(correlationId, proposal, commitRequest, "selection_changed_before_approval");
                return;
            }
            var freshnessError = ValidateProposalEnvelope(proposal.envelope, proposal.payload);
            if (freshnessError != null)
            {
                RejectForCommit(correlationId, proposal, commitRequest, freshnessError);
                return;
            }
            ScriptProxy proxy = null;
            string error = null;
            if (compiler == null || !compiler.TryCompileAndAttach(target, proposal.payload.code, out proxy, out error))
            {
                pending.Remove(correlationId);
                localCache.ClearProposal(correlationId);
                var compileError = error ?? "The Roslyn runtime compiler is unavailable.";
                if (commitRequest != null) SendCommitResult(commitRequest, false, null, compileError);
                else SendArtifactResult(proposal.envelope, "error", null, compileError);
                return;
            }

            activeByObjectId.TryGetValue(proposal.envelope.targetObjectId, out var previous);
            if (previous != null && previous.proxy != null && previous.proxy.MonoBehaviourInstance != null)
                previous.proxy.MonoBehaviourInstance.enabled = false;

            var artifactId = "unity-" + correlationId;
            var applied = new AppliedArtifact
            {
                artifactId = artifactId,
                correlationId = correlationId,
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
            if (userApproved) SendUserDecision(proposal.envelope, "approved", null);
            if (commitRequest != null) SendCommitResult(commitRequest, true, artifactId, null);
            else SendArtifactResult(proposal.envelope, "committed", artifactId, null);
            ShowStatus("committed", "The validated behaviour is now live. Use Undo to revert it.");
        }

        private void RejectForCommit(string correlationId, PendingArtifact proposal, CacheEnvelope commitRequest, string reason)
        {
            pending.Remove(correlationId);
            localCache.ClearProposal(correlationId);
            if (consentPanel != null) consentPanel.HideProposal();
            if (commitRequest != null) SendCommitResult(commitRequest, false, null, reason);
            else SendArtifactResult(proposal.envelope, "rejected", null, reason);
        }

        public void RejectPending(string correlationId, string reason)
        {
            if (!pending.TryGetValue(correlationId, out var proposal)) return;
            pending.Remove(correlationId);
            localCache.ClearProposal(correlationId);
            if (consentPanel != null) consentPanel.HideProposal();
            SendUserDecision(proposal.envelope, reason == "confirmation_timeout" ? "timeout" : "rejected", reason);
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
            if (!appliedByArtifactId.TryGetValue(latestArtifactId, out var applied)) return;
            var decision = NewEnvelope(CacheMessageTypes.UserDecision, applied.correlationId, applied.targetObjectId,
                "{\"decision\":\"undo\",\"artifactId\":\"" + AgenticSceneRegistry.Escape(applied.artifactId) + "\"}");
            SendDecision(decision);
            var rolledBack = Rollback(latestArtifactId);
            var result = NewEnvelope(CacheMessageTypes.RollbackResult, applied.correlationId, applied.targetObjectId,
                rolledBack ? "{\"status\":\"rolled_back\",\"artifactId\":\"" + AgenticSceneRegistry.Escape(applied.artifactId) + "\"}"
                    : "{\"status\":\"not_found\"}");
            SendDecision(result);
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
            if (envelope.type == CacheMessageTypes.DeltaAck)
            {
                var ack = Parse<DeltaAckPayload>(envelope.payload);
                var seq = envelope.HasDeltaSeq ? envelope.deltaSeq : ack != null ? ack.deltaSeq : -1;
                if (seq >= 0 && cachePublisher != null) cachePublisher.AcknowledgeThrough(seq);
                return;
            }
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
            response.validationState = request.validationState;
            response.validationSummary = request.validationSummary;
            response.riskScore = request.riskScore;
            response.consentRoute = request.consentRoute;
            response.requiredPermissions = request.requiredPermissions;
            response.expectedSideEffects = request.expectedSideEffects;
            response.artifactVersion = request.artifactVersion;
            response.artifactId = artifactId;
            SendDecision(response);
        }

        private void SendCommitResult(CacheEnvelope request, bool accepted, string artifactId, string reason)
        {
            var payload = new StringBuilder("{\"status\":\"").Append(accepted ? "committed" : "rejected").Append('"');
            if (!string.IsNullOrEmpty(artifactId)) payload.Append(",\"artifactId\":\"").Append(AgenticSceneRegistry.Escape(artifactId)).Append('"');
            if (!string.IsNullOrEmpty(reason)) payload.Append(",\"reason\":\"").Append(AgenticSceneRegistry.Escape(reason)).Append('"');
            payload.Append('}');
            var response = NewEnvelope(accepted ? CacheMessageTypes.CommitAccepted : CacheMessageTypes.CommitRejected,
                request.correlationId, request.targetObjectId, payload.ToString());
            response.sessionId = request.sessionId;
            response.artifactId = artifactId;
            SendDecision(response);
        }

        private void SendUserDecision(CacheEnvelope proposal, string decision, string reason)
        {
            var payload = new StringBuilder("{\"decision\":\"").Append(AgenticSceneRegistry.Escape(decision)).Append('"');
            if (!string.IsNullOrEmpty(reason)) payload.Append(",\"reason\":\"").Append(AgenticSceneRegistry.Escape(reason)).Append('"');
            payload.Append('}');
            var envelope = NewEnvelope(CacheMessageTypes.UserDecision, proposal.correlationId, proposal.targetObjectId, payload.ToString());
            envelope.sessionId = proposal.sessionId;
            envelope.authoringMode = proposal.authoringMode;
            envelope.interactionMode = proposal.interactionMode;
            SendDecision(envelope);
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
