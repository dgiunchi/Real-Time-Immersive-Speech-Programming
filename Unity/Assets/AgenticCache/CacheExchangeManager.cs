using RoslynCSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            public string operation;
            public string existingArtifactId;
            public string candidateId;
            public string candidateSetId;
            public int candidateCount;
            public string selectionReason;
            public string experienceMode;
            // Study condition agenticxr_no_verification (H2 arm): true skips ONLY the
            // Verification Space staging-clone dry-run. Freshness checks, preview,
            // and consent routing are unchanged, and mode policy is enforced
            // server-side before the proposal is ever sent.
            public bool verificationBypassed;
        }

        [Serializable] private sealed class BackfillRequestPayload { public bool requestSnapshot; public long lastSeenSeq; }
        [Serializable] private sealed class DeltaAckPayload { public long deltaSeq; }
        [Serializable] private sealed class AgentStatusPayload { public string state; public string detail; }
        [Serializable] private sealed class AgentUtterancePayload { public string text; }
        [Serializable] private sealed class CacheInvalidationPayload { public string reason; }
        [Serializable] private sealed class RollbackPayload { public string artifactId; }
        [Serializable] private sealed class CheckpointArtifact
        {
            public string artifactId; public string correlationId; public string targetObjectId; public string code;
            public string artifactVersion; public string rollbackPointer;
        }
        [Serializable] private sealed class RuntimeCheckpoint
        {
            public string schemaVersion = "1.0"; public string sceneName; public long savedAt; public CheckpointArtifact[] activeArtifacts;
        }

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
            public CacheEnvelope proposalEnvelope;
            public string operation;
            public string code;
            public string artifactVersion;
            public string rollbackPointer;
            public GameObjectStateSnapshot stateBeforeApply;
            public long applySequence;
        }

        private sealed class GameObjectStateSnapshot
        {
            private sealed class RendererState
            {
                public Renderer renderer;
                public bool enabled;
                public Material[] sharedMaterials;
                public Color[] colors;
            }

            private GameObject target;
            private bool activeSelf;
            private Vector3 localPosition;
            private Quaternion localRotation;
            private Vector3 localScale;
            private readonly List<RendererState> renderers = new List<RendererState>();

            public static GameObjectStateSnapshot Capture(GameObject target)
            {
                if (target == null) return null;
                var snapshot = new GameObjectStateSnapshot
                {
                    target = target,
                    activeSelf = target.activeSelf,
                    localPosition = target.transform.localPosition,
                    localRotation = target.transform.localRotation,
                    localScale = target.transform.localScale,
                };
                foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    var colors = new Color[materials.Length];
                    for (var i = 0; i < materials.Length; i++)
                        colors[i] = materials[i] != null && materials[i].HasProperty("_Color")
                            ? materials[i].color : Color.clear;
                    snapshot.renderers.Add(new RendererState
                    {
                        renderer = renderer,
                        enabled = renderer.enabled,
                        sharedMaterials = (Material[])materials.Clone(),
                        colors = colors,
                    });
                }
                return snapshot;
            }

            public void Restore()
            {
                if (target == null) return;
                target.transform.localPosition = localPosition;
                target.transform.localRotation = localRotation;
                target.transform.localScale = localScale;
                foreach (var state in renderers)
                {
                    if (state.renderer == null) continue;
                    state.renderer.sharedMaterials = state.sharedMaterials;
                    state.renderer.enabled = state.enabled;
                    for (var i = 0; i < state.sharedMaterials.Length && i < state.colors.Length; i++)
                    {
                        var material = state.sharedMaterials[i];
                        if (material != null && material.HasProperty("_Color")) material.color = state.colors[i];
                    }
                }
                target.SetActive(activeSelf);
            }
        }

        public LocalXRCache localCache = new LocalXRCache();
        public CachePublisher cachePublisher;
        public AgenticSceneRegistry sceneRegistry;
        public AgenticXRConsentPanel consentPanel;
        public TestRoslyn compiler;
        public GeneratedBehaviourWatchdog executionWatchdog;
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
        private string activeAgentSessionId;
        private string activeAgentCorrelationId;
        private string activeAgentTargetObjectId;
        private long nextApplySequence;
        private string CheckpointPath => Path.Combine(Application.persistentDataPath, "agenticxr-runtime-checkpoint.json");

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
            Invoke(nameof(RestoreRuntimeCheckpoint), 0.5f);
        }

        private void OnApplicationQuit() => SaveRuntimeCheckpoint();

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
                TrackActiveAgentRequest(envelope, status != null ? status.state : null);
                localCache.SetAgentStatus(status != null ? status.state : null, status != null ? status.detail : null);
                if (status != null && status.state == "heard" && consentPanel != null)
                {
                    ShowStatus("heard", "Speech recognized.");
                    consentPanel.ShowTranscript(status.detail);
                }
                else
                {
                    ShowStatus(status != null ? status.state : "working", status != null ? status.detail : null);
                }
                var visible = NewEnvelope(CacheMessageTypes.AgentStatusVisible, envelope.correlationId,
                    envelope.targetObjectId, "{\"status\":\"" +
                    AgenticSceneRegistry.Escape(status != null ? status.state : "working") + "\"}");
                visible.sessionId = envelope.sessionId;
                SendDecision(visible);
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
            var operation = payload != null && !string.IsNullOrEmpty(payload.operation) ? payload.operation : "create";
            var removes = string.Equals(operation, "remove", StringComparison.OrdinalIgnoreCase);
            if (payload == null || (!removes && string.IsNullOrWhiteSpace(payload.code)))
            {
                SendArtifactResult(envelope, "error", null, "Create/edit ArtifactProposal contained no C# code.");
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

            if ((string.Equals(operation, "edit", StringComparison.OrdinalIgnoreCase) || removes) &&
                (string.IsNullOrEmpty(payload.existingArtifactId) ||
                 !activeByObjectId.TryGetValue(envelope.targetObjectId, out var currentArtifact) ||
                 currentArtifact.artifactId != payload.existingArtifactId))
            {
                SendArtifactResult(envelope, "rejected", null, operation + " references no active artifact.");
                return;
            }
            if ((string.Equals(operation, "edit", StringComparison.OrdinalIgnoreCase) || removes) &&
                string.Equals(envelope.authoringMode, "automatic", StringComparison.OrdinalIgnoreCase))
            {
                SendArtifactResult(envelope, "rejected", null, operation + " requires explicit confirmation.");
                return;
            }

            var staged = removes || payload.verificationBypassed;
            if (!staged)
            {
                ShowStatus("validating", "Testing Claude's code on a staging clone.");
                var verificationStartedAt = Time.realtimeSinceStartupAsDouble;
                var clone = Instantiate(target);
                clone.name = target.name + " [AgenticXR Verification]";
                clone.SetActive(false);
                ScriptProxy stageProxy = null;
                var stageError = compiler == null ? "The Roslyn runtime compiler is unavailable in this scene." : null;
                staged = compiler != null && compiler.TryCompileAndAttach(clone, payload.code, out stageProxy, out stageError);
                if (stageProxy != null) stageProxy.Dispose();
                Destroy(clone);
                envelope.verificationDurationMs = (Time.realtimeSinceStartupAsDouble - verificationStartedAt) * 1000.0;
                if (!staged)
                {
                    SendArtifactResult(envelope, "error", null, stageError ?? "Staging compilation failed.");
                    return;
                }
            }
            if (string.Equals(payload.mode, "simulate", StringComparison.OrdinalIgnoreCase))
            {
                if (payload.verificationBypassed)
                {
                    // The backend skips dry-runs server-side in this condition; if one
                    // still arrives, answer honestly rather than faking evidence.
                    SendArtifactResult(envelope, "skipped_no_verification", null, null);
                    return;
                }
                SendArtifactResult(envelope, "simulated", null, null);
                ShowStatus("validated", "The proposal passed the Verification Space dry-run.");
                return;
            }

            localCache.MarkProposalPending(envelope.correlationId, envelope.targetObjectId, envelope.snapshotId,
                envelope.objectRevision, envelope.authoringMode, envelope.interactionMode);
            localCache.SetPreview(envelope.correlationId, payload.verificationBypassed
                ? "UNVERIFIED: this proposal skipped the Verification Space dry-run (study condition)."
                : payload.validationSummary ?? "Compiled successfully on an inactive verification clone.");
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
                    payload.validationSummary, payload.riskScore, payload.requiredPermissions, payload.expectedSideEffects,
                    payload.candidateCount, payload.selectionReason);
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
            var operation = !string.IsNullOrEmpty(proposal.payload.operation) ? proposal.payload.operation : "create";
            if (string.Equals(operation, "remove", StringComparison.OrdinalIgnoreCase))
            {
                CommitRemoval(correlationId, proposal, commitRequest, userApproved);
                return;
            }
            ScriptProxy proxy = null;
            string error = null;
            var stateBeforeApply = GameObjectStateSnapshot.Capture(target);
            var commitAttachStartedAt = Time.realtimeSinceStartupAsDouble;
            if (compiler == null || !compiler.TryCompileAndAttach(target, proposal.payload.code, out proxy, out error))
            {
                pending.Remove(correlationId);
                localCache.ClearProposal(correlationId);
                var compileError = error ?? "The Roslyn runtime compiler is unavailable.";
                if (commitRequest != null) SendCommitResult(commitRequest, false, null, compileError);
                else SendArtifactResult(proposal.envelope, "error", null, compileError);
                return;
            }
            proposal.envelope.commitAttachDurationMs = (Time.realtimeSinceStartupAsDouble - commitAttachStartedAt) * 1000.0;

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
                proposalEnvelope = proposal.envelope,
                operation = operation,
                code = proposal.payload.code,
                artifactVersion = proposal.payload.artifactVersion,
                rollbackPointer = previous != null ? previous.artifactId : null,
                stateBeforeApply = stateBeforeApply,
                applySequence = ++nextApplySequence,
            };
            appliedByArtifactId[artifactId] = applied;
            activeByObjectId[applied.targetObjectId] = applied;
            latestArtifactId = artifactId;
            executionWatchdog?.Register(proxy.MonoBehaviourInstance, artifactId);
            pending.Remove(correlationId);
            localCache.ClearProposal(correlationId);
            localCache.SetRollbackPointer(artifactId, previous != null ? previous.artifactId : null);
            if (consentPanel != null) consentPanel.HideProposal();
            if (userApproved) SendUserDecision(proposal.envelope, "approved", null);
            if (commitRequest != null) SendCommitResult(commitRequest, true, artifactId, null);
            else SendArtifactResult(proposal.envelope, "committed", artifactId, null);
            ShowStatus("committed", "The validated behaviour is now live. Use Undo to revert it.");
            SaveRuntimeCheckpoint();
        }

        private void CommitRemoval(string correlationId, PendingArtifact proposal, CacheEnvelope commitRequest, bool userApproved)
        {
            if (!appliedByArtifactId.TryGetValue(proposal.payload.existingArtifactId, out var removed) || removed.targetObjectId != proposal.envelope.targetObjectId)
            {
                RejectForCommit(correlationId, proposal, commitRequest, "remove_target_not_active");
                return;
            }
            var target = sceneRegistry != null ? sceneRegistry.Find(removed.targetObjectId) : null;
            var stateBeforeRemoval = GameObjectStateSnapshot.Capture(target);
            if (removed.proxy != null && removed.proxy.MonoBehaviourInstance != null)
            {
                executionWatchdog?.Unregister(removed.proxy.MonoBehaviourInstance);
                removed.proxy.MonoBehaviourInstance.enabled = false;
            }
            removed.stateBeforeApply?.Restore();
            var artifactId = "unity-remove-" + correlationId;
            var tombstone = new AppliedArtifact
            {
                artifactId = artifactId, correlationId = correlationId, targetObjectId = removed.targetObjectId,
                previous = removed, proposalEnvelope = proposal.envelope, operation = "remove",
                artifactVersion = proposal.payload.artifactVersion, rollbackPointer = removed.artifactId,
                stateBeforeApply = stateBeforeRemoval, applySequence = ++nextApplySequence,
            };
            appliedByArtifactId[artifactId] = tombstone;
            activeByObjectId.Remove(removed.targetObjectId);
            latestArtifactId = artifactId;
            pending.Remove(correlationId);
            localCache.ClearProposal(correlationId);
            localCache.SetRollbackPointer(artifactId, removed.artifactId);
            if (consentPanel != null) consentPanel.HideProposal();
            if (userApproved) SendUserDecision(proposal.envelope, "approved", null);
            if (commitRequest != null) SendCommitResult(commitRequest, true, artifactId, null);
            else SendArtifactResult(proposal.envelope, "removed", artifactId, null);
            ShowStatus("removed", "The generated behaviour was removed. Use Undo to restore it.");
            SaveRuntimeCheckpoint();
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

        public void CancelActiveRequest()
        {
            if (string.IsNullOrEmpty(activeAgentCorrelationId) || string.IsNullOrEmpty(activeAgentSessionId))
            {
                ShowStatus("cancel", "There is no active request to cancel.");
                return;
            }

            var envelope = NewEnvelope(CacheMessageTypes.CancelRequest, activeAgentCorrelationId,
                activeAgentTargetObjectId, "{\"reason\":\"user_cancelled\"}");
            envelope.sessionId = activeAgentSessionId;
            SendDecision(envelope);
            ShowStatus("cancelling", "Cancelling the current Claude request.");
        }

        private void TrackActiveAgentRequest(CacheEnvelope envelope, string state)
        {
            if (envelope == null || string.IsNullOrEmpty(envelope.correlationId)) return;
            if (state == "cancelled" || state == "failed" || state == "rejected" || state == "committed" ||
                state == "rolled_back")
            {
                if (activeAgentCorrelationId == envelope.correlationId)
                {
                    activeAgentSessionId = null;
                    activeAgentCorrelationId = null;
                    activeAgentTargetObjectId = null;
                }
                return;
            }

            activeAgentSessionId = envelope.sessionId;
            activeAgentCorrelationId = envelope.correlationId;
            activeAgentTargetObjectId = envelope.targetObjectId;
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

        private bool Rollback(string artifactId, bool saveCheckpoint = true)
        {
            if (string.IsNullOrEmpty(artifactId) || !appliedByArtifactId.TryGetValue(artifactId, out var applied)) return false;
            if (applied.proxy != null) executionWatchdog?.Unregister(applied.proxy.MonoBehaviourInstance);
            if (applied.proxy != null) applied.proxy.Dispose();
            applied.stateBeforeApply?.Restore();
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
            if (saveCheckpoint) SaveRuntimeCheckpoint();
            return true;
        }

        public void ResetTrialState()
        {
            CancelActiveRequest();
            var resetCorrelationId = "trial-reset-" + Guid.NewGuid().ToString();
            var resetArtifacts = appliedByArtifactId.Values
                .OrderByDescending(item => item.applySequence)
                .Select(item => item.artifactId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToArray();
            foreach (var artifactId in appliedByArtifactId.Values
                .OrderByDescending(item => item.applySequence)
                .Select(item => item.artifactId)
                .ToArray())
            {
                Rollback(artifactId, false);
            }
            pending.Clear();
            localCache.ClearAllProposals();
            activeByObjectId.Clear();
            appliedByArtifactId.Clear();
            latestArtifactId = null;
            var resetPayload = new StringBuilder("{\"status\":\"trial_reset\",\"artifactIds\":[");
            for (var i = 0; i < resetArtifacts.Length; i++)
            {
                if (i > 0) resetPayload.Append(',');
                resetPayload.Append('"').Append(AgenticSceneRegistry.Escape(resetArtifacts[i])).Append('"');
            }
            resetPayload.Append("]}");
            SendDecision(NewEnvelope(CacheMessageTypes.TrialReset, resetCorrelationId, null, resetPayload.ToString()));
            try
            {
                if (File.Exists(CheckpointPath)) File.Delete(CheckpointPath);
                if (File.Exists(CheckpointPath + ".tmp")) File.Delete(CheckpointPath + ".tmp");
                ShowStatus("trial_reset", "All generated behaviours were removed and the trial checkpoint was cleared.");
            }
            catch (Exception error)
            {
                Debug.LogError("[AgenticXR] trial reset failed: " + error.Message);
                ShowStatus("trial_reset_failed", error.Message);
            }
        }

        private void SaveRuntimeCheckpoint()
        {
            try
            {
                var entries = new List<CheckpointArtifact>();
                foreach (var item in activeByObjectId.Values)
                {
                    if (item == null || string.IsNullOrEmpty(item.code)) continue;
                    entries.Add(new CheckpointArtifact
                    {
                        artifactId = item.artifactId, correlationId = item.correlationId, targetObjectId = item.targetObjectId,
                        code = item.code, artifactVersion = item.artifactVersion, rollbackPointer = item.rollbackPointer,
                    });
                }
                var checkpoint = new RuntimeCheckpoint
                {
                    sceneName = SceneManager.GetActiveScene().name,
                    savedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    activeArtifacts = entries.ToArray(),
                };
                var temporary = CheckpointPath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(checkpoint, true));
                if (File.Exists(CheckpointPath)) File.Delete(CheckpointPath);
                File.Move(temporary, CheckpointPath);
            }
            catch (Exception error) { Debug.LogError("[AgenticXR] checkpoint save failed: " + error.Message); }
        }

        private void RestoreRuntimeCheckpoint()
        {
            if (!File.Exists(CheckpointPath) || compiler == null || sceneRegistry == null) return;
            RuntimeCheckpoint checkpoint;
            try { checkpoint = JsonUtility.FromJson<RuntimeCheckpoint>(File.ReadAllText(CheckpointPath)); }
            catch (Exception error) { Debug.LogError("[AgenticXR] checkpoint load failed: " + error.Message); return; }
            if (checkpoint == null || checkpoint.activeArtifacts == null) return;
            foreach (var entry in checkpoint.activeArtifacts)
            {
                var target = checkpoint.sceneName == SceneManager.GetActiveScene().name ? sceneRegistry.Find(entry.targetObjectId) : null;
                if (target == null)
                {
                    Debug.LogError("[AgenticXR] checkpoint orphaned artifact " + entry.artifactId + ": stable object is absent from current scene.");
                    SendDecision(NewEnvelope(CacheMessageTypes.RollbackResult, entry.correlationId, entry.targetObjectId,
                        "{\"status\":\"checkpoint_orphaned\",\"artifactId\":\"" + AgenticSceneRegistry.Escape(entry.artifactId) + "\"}"));
                    continue;
                }
                var stateBeforeApply = GameObjectStateSnapshot.Capture(target);
                if (!compiler.TryCompileAndAttach(target, entry.code, out var proxy, out var error))
                {
                    Debug.LogError("[AgenticXR] checkpoint restore failed for " + entry.artifactId + ": " + error);
                    SendDecision(NewEnvelope(CacheMessageTypes.RollbackResult, entry.correlationId, entry.targetObjectId,
                        "{\"status\":\"checkpoint_restore_failed\",\"artifactId\":\"" + AgenticSceneRegistry.Escape(entry.artifactId) + "\"}"));
                    continue;
                }
                var restored = new AppliedArtifact
                {
                    artifactId = entry.artifactId, correlationId = entry.correlationId, targetObjectId = entry.targetObjectId,
                    code = entry.code, artifactVersion = entry.artifactVersion, rollbackPointer = entry.rollbackPointer,
                    proxy = proxy, operation = "resume",
                    stateBeforeApply = stateBeforeApply,
                    applySequence = ++nextApplySequence,
                };
                appliedByArtifactId[restored.artifactId] = restored;
                activeByObjectId[restored.targetObjectId] = restored;
                latestArtifactId = restored.artifactId;
                executionWatchdog?.Register(proxy.MonoBehaviourInstance, restored.artifactId);
                SendDecision(NewEnvelope(CacheMessageTypes.RollbackResult, entry.correlationId, entry.targetObjectId,
                    "{\"status\":\"checkpoint_resumed\",\"artifactId\":\"" + AgenticSceneRegistry.Escape(entry.artifactId) + "\"}"));
            }
        }

        public void ReportExecutionWatchdog(string artifactId, string reason, float frameMs, long allocationBytes)
        {
            if (string.IsNullOrEmpty(artifactId) || !appliedByArtifactId.TryGetValue(artifactId, out var applied)) return;
            var detail = reason + " (frameMs=" + frameMs.ToString("0.0") + ", allocationBytes=" + allocationBytes + ")";
            SendArtifactResult(applied.proposalEnvelope, "watchdog_disabled", artifactId, detail);
            ShowStatus("watchdog_disabled", "A generated behaviour was disabled because it exceeded the runtime budget. Use Undo to remove it.");
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
            response.operation = request.operation;
            response.existingArtifactId = request.existingArtifactId;
            response.candidateId = request.candidateId;
            response.candidateSetId = request.candidateSetId;
            response.verificationDurationMs = request.verificationDurationMs;
            response.commitAttachDurationMs = request.commitAttachDurationMs;
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
