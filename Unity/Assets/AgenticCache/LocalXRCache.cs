using System;
using System.Collections.Generic;
using UnityEngine;

namespace AgenticCache
{
    // Unity's fast, frame-adjacent cache (main.tex, Cache Exchange Layer §; the
    // task's "Unity Local XR Cache"). Pure data + monotonicity/TTL bookkeeping - no
    // networking here, CachePublisher/CacheExchangeManager own the wire.
    //
    // NOT compiled/verified in this environment (no Unity Editor available) - see
    // docs/cache-exchange-layer.md for what that means and how to actually verify
    // this once Unity is opened.
    public class LocalXRCache
    {
        [Serializable]
        public class ObjectRecord
        {
            public string stableObjectId;
            public long objectRevision = -1;
            public float lastUpdatedAt; // Time.time when last touched
            public float ttlMs = -1;
            public string tag;
            public string region;

            public bool IsFresh(float nowSeconds)
            {
                if (ttlMs < 0) return true; // no TTL configured - never expires by this check
                return (nowSeconds - lastUpdatedAt) * 1000f <= ttlMs;
            }
        }

        [Serializable]
        public class PendingProposal
        {
            public string correlationId;
            public string targetObjectId;
            public string snapshotId;
            public long objectRevision;
            public string authoringMode;
            public string interactionMode;
            public float receivedAt;
            public bool invalidated;
            public string invalidationReason;
        }

        public string CurrentSceneEpoch { get; private set; }
        public string LatestSnapshotId { get; private set; }
        public string SelectedObjectId { get; private set; }
        public string FocusObjectId { get; private set; }
        public string AgentStatusState { get; private set; }
        public string AgentStatusDetail { get; private set; }

        private readonly Dictionary<string, ObjectRecord> objectsByStableId = new Dictionary<string, ObjectRecord>();
        private readonly List<string> haloObjectIds = new List<string>();
        private readonly Dictionary<string, PendingProposal> proposalsByCorrelationId = new Dictionary<string, PendingProposal>();
        private readonly Dictionary<string, string> previewByCorrelationId = new Dictionary<string, string>(); // correlationId -> preview description/handle
        private readonly Dictionary<string, string> rollbackPointerByArtifactId = new Dictionary<string, string>(); // artifactId -> previous artifactId (or null-ish sentinel)

        public void SetSceneEpoch(string epoch) => CurrentSceneEpoch = epoch;
        public void SetLatestSnapshotId(string snapshotId) => LatestSnapshotId = snapshotId;
        public void SetSelectedObject(string stableObjectId) => SelectedObjectId = stableObjectId;
        public void SetFocusObject(string stableObjectId) => FocusObjectId = stableObjectId;

        public void SetAgentStatus(string state, string detail)
        {
            AgentStatusState = state;
            AgentStatusDetail = detail;
        }

        public void SetHalo(IEnumerable<string> objectIds)
        {
            haloObjectIds.Clear();
            haloObjectIds.AddRange(objectIds);
        }

        public IReadOnlyList<string> GetHalo() => haloObjectIds;

        // Monotonicity guard (spec: "object revisions are monotonic per
        // stableObjectId"): returns false (and does not update) if the incoming
        // revision would regress a known object's state - mirrors
        // Server/cache/agent_working_cache.js's acceptState safety net.
        public bool TryAcceptObjectState(string stableObjectId, long objectRevision, string tag, string region, float ttlMs)
        {
            if (string.IsNullOrEmpty(stableObjectId)) return false;

            if (objectsByStableId.TryGetValue(stableObjectId, out var existing) && objectRevision >= 0 && existing.objectRevision >= 0 && objectRevision < existing.objectRevision)
            {
                return false; // supersededByNewer - caller already has a fresher revision
            }

            var record = existing ?? new ObjectRecord { stableObjectId = stableObjectId };
            record.objectRevision = objectRevision;
            record.tag = tag;
            record.region = region;
            record.ttlMs = ttlMs;
            record.lastUpdatedAt = Time.time;
            objectsByStableId[stableObjectId] = record;
            return true;
        }

        public ObjectRecord GetByStableObjectId(string stableObjectId)
        {
            return objectsByStableId.TryGetValue(stableObjectId, out var record) ? record : null;
        }

        public void MarkProposalPending(string correlationId, string targetObjectId, string snapshotId, long objectRevision, string authoringMode, string interactionMode)
        {
            if (string.IsNullOrEmpty(correlationId)) return;
            proposalsByCorrelationId[correlationId] = new PendingProposal
            {
                correlationId = correlationId,
                targetObjectId = targetObjectId,
                snapshotId = snapshotId,
                objectRevision = objectRevision,
                authoringMode = authoringMode,
                interactionMode = interactionMode,
                receivedAt = Time.time,
            };
        }

        public PendingProposal GetProposalByCorrelationId(string correlationId)
        {
            return proposalsByCorrelationId.TryGetValue(correlationId, out var p) ? p : null;
        }

        public void InvalidateProposal(string correlationId, string reason)
        {
            if (proposalsByCorrelationId.TryGetValue(correlationId, out var p))
            {
                p.invalidated = true;
                p.invalidationReason = reason;
            }
        }

        public void ClearProposal(string correlationId)
        {
            proposalsByCorrelationId.Remove(correlationId);
        }

        // Selection change, rejection, undo, region change, or ownership change can
        // invalidate proposals for an object (spec's consistency rules) - call this
        // when any of those happen locally, independent of a server-driven
        // CacheInvalidation message.
        public void InvalidateProposalsForObject(string stableObjectId, string reason)
        {
            foreach (var kvp in proposalsByCorrelationId)
            {
                if (kvp.Value.targetObjectId == stableObjectId && !kvp.Value.invalidated)
                {
                    kvp.Value.invalidated = true;
                    kvp.Value.invalidationReason = reason;
                }
            }
        }

        public void SetPreview(string correlationId, string previewHandle) => previewByCorrelationId[correlationId] = previewHandle;
        public string GetPreview(string correlationId) => previewByCorrelationId.TryGetValue(correlationId, out var v) ? v : null;

        public void SetRollbackPointer(string artifactId, string previousArtifactId) => rollbackPointerByArtifactId[artifactId] = previousArtifactId;
        public string GetRollbackPointer(string artifactId) => rollbackPointerByArtifactId.TryGetValue(artifactId, out var v) ? v : null;
    }
}
