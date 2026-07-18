using System;
using System.Collections.Generic;
using System.Text;
using Ubiq.Messaging;
using UnityEngine;

namespace AgenticCache
{
    // Publishes compact SceneDelta/CacheSnapshot messages on NetworkId 95 (Unity ->
    // Server; main.tex tab:channels: "Scene deltas and cache snapshots"). Coalesces
    // high-frequency transform updates (sent at most once per coalesceIntervalMs per
    // object) and only sends semantic/script/component deltas when the object's
    // revision actually changes, per the task's CachePublisher spec.
    //
    // NOT compiled/verified in this environment - see docs/cache-exchange-layer.md.
    // Mirrors the NetworkId-registration pattern in CodeGenerationManager.cs /
    // MicrophoneCapture.cs exactly (NetworkScene.Register(this, networkId), then
    // context.Send(ReferenceCountedSceneGraphMessage)).
    public class CachePublisher : MonoBehaviour
    {
        public NetworkId networkId = new NetworkId(95);
        private NetworkContext context;

        public LocalXRCache localCache;
        public string sessionId = "default-session";

        [Tooltip("Minimum time between transform-only delta sends for the same object, to coalesce high-frequency movement.")]
        public float coalesceIntervalMs = 200f;

        private long deltaSeqCounter = 0;
        private readonly Dictionary<string, float> lastSentAtByObject = new Dictionary<string, float>();
        private readonly Dictionary<string, long> lastSentRevisionByObject = new Dictionary<string, long>();
        private readonly Queue<(string stableObjectId, long objectRevision, string tag, string region, string stateJson)> pendingHighPriority = new Queue<(string, long, string, string, string)>();

        private void Start()
        {
            context = NetworkScene.Register(this, networkId);
        }

        // Call for a transform-only update (position/rotation/scale). Coalesced -
        // may be dropped if called again for the same object within
        // coalesceIntervalMs; the LATEST call's values win when the coalesce window
        // flushes, via QueueDelta being called directly rather than buffered
        // per-field (kept simple for this scaffold: each call either sends
        // immediately or is skipped, not merged field-by-field).
        public void PublishTransformDelta(string stableObjectId, long objectRevision, string tag, string region, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            float now = Time.time;
            if (lastSentAtByObject.TryGetValue(stableObjectId, out var lastAt) && (now - lastAt) * 1000f < coalesceIntervalMs)
            {
                return; // coalesced away - a more recent call will supersede this one
            }

            string stateJson = $"{{\"transform\":{{\"pos\":[{position.x},{position.y},{position.z}],\"rot\":[{rotation.x},{rotation.y},{rotation.z},{rotation.w}],\"scale\":[{scale.x},{scale.y},{scale.z}]}}}}";
            SendDelta(stableObjectId, objectRevision, tag, region, stateJson);
        }

        // Call for a semantic/script/component change. Unlike transform updates,
        // these are NOT coalesced by time - only suppressed if the revision hasn't
        // actually advanced since the last send (per the spec: "send semantic/
        // script deltas only when revisions change").
        public void PublishStateDelta(string stableObjectId, long objectRevision, string tag, string region, string stateJson)
        {
            if (lastSentRevisionByObject.TryGetValue(stableObjectId, out var lastRev) && objectRevision <= lastRev)
            {
                return; // no revision advance - nothing new to say
            }
            SendDelta(stableObjectId, objectRevision, tag, region, stateJson);
        }

        private void SendDelta(string stableObjectId, long objectRevision, string tag, string region, string stateJson)
        {
            deltaSeqCounter += 1;
            lastSentAtByObject[stableObjectId] = Time.time;
            lastSentRevisionByObject[stableObjectId] = objectRevision;

            var envelope = new CacheEnvelope
            {
                schemaVersion = "1.0",
                type = CacheMessageTypes.SceneDelta,
                sessionId = sessionId,
                correlationId = Guid.NewGuid().ToString(),
                originAgent = "unity_cache_publisher",
                targetObjectId = stableObjectId,
                stableObjectId = stableObjectId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sceneEpoch = localCache != null ? localCache.CurrentSceneEpoch : null,
                snapshotId = localCache != null ? localCache.LatestSnapshotId : null,
                deltaSeq = deltaSeqCounter,
                objectRevision = objectRevision,
                source = "unity",
                confidence = 1f,
                // SceneDelta is NOT in the Node side's STRINGIFY_PAYLOAD_FOR_UNITY
                // set (Server/cache/protocol.js) - it is the one type Unity SENDS,
                // never parses, so there is no JsonUtility deserialization concern
                // here. `payload` carries a hand-built JSON object literal (tag,
                // region, plus the caller's stateJson), matching the nested shape
                // Node's mock peer / reconciler already expect.
                payload = $"{{\"tag\":\"{Escape(tag)}\",\"region\":\"{Escape(region)}\",\"state\":{stateJson}}}",
            };

            SendEnvelope(envelope);

            if (localCache != null)
            {
                localCache.TryAcceptObjectState(stableObjectId, objectRevision, tag, region, -1);
            }
        }

        public void PublishSnapshot(IEnumerable<(string stableObjectId, long objectRevision, string tag, string region, string stateJson)> objects, string sceneEpoch, string snapshotId)
        {
            var sb = new StringBuilder();
            sb.Append("{\"objects\":[");
            bool first = true;
            foreach (var o in objects)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"{{\"stableObjectId\":\"{Escape(o.stableObjectId)}\",\"objectRevision\":{o.objectRevision},\"tag\":\"{Escape(o.tag)}\",\"region\":\"{Escape(o.region)}\",\"state\":{o.stateJson}}}");
            }
            sb.Append("]}");

            var envelope = new CacheEnvelope
            {
                schemaVersion = "1.0",
                type = CacheMessageTypes.CacheSnapshot,
                sessionId = sessionId,
                correlationId = Guid.NewGuid().ToString(),
                originAgent = "unity_cache_publisher",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sceneEpoch = sceneEpoch,
                snapshotId = snapshotId,
                source = "unity",
                confidence = 1f,
                // CacheSnapshot IS in STRINGIFY_PAYLOAD_FOR_UNITY on the Node side,
                // but that only matters for messages UNITY RECEIVES. Unity always
                // SENDS CacheSnapshot, so the payload here is a plain JSON object
                // literal - Node's fromWireFormat() only applies (skips) stringify
                // logic based on message type, and correctly parses this as JSON
                // regardless, since JSON.parse(outerEnvelope) already turns this
                // string field into... note: because the OUTER envelope is sent as
                // JSON text over the wire (see Message.Create in messaging.js),
                // this payload string will arrive at Node as a STRING (matching
                // fromWireFormat's expectation), not as a pre-parsed object -
                // consistent with the wire contract.
                payload = sb.ToString(),
            };

            SendEnvelope(envelope);

            if (localCache != null)
            {
                localCache.SetSceneEpoch(sceneEpoch);
                localCache.SetLatestSnapshotId(snapshotId);
            }
        }

        // Public so CacheExchangeManager can reuse this NetworkId-95 connection for
        // DetailResponse/BackfillResponse instead of opening a second registration
        // on the same channel (see CacheExchangeManager's constructor comment).
        public void SendRaw(CacheEnvelope envelope)
        {
            SendEnvelope(envelope);
        }

        private void SendEnvelope(CacheEnvelope envelope)
        {
            string json = JsonUtility.ToJson(envelope);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var message = ReferenceCountedSceneGraphMessage.Rent(bytes.Length);
            bytes.CopyTo(new Span<byte>(message.bytes, message.start, bytes.Length));
            context.Send(message);
        }

        private static string Escape(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // NetworkId 95 is outbound for Unity. The method is present because Ubiq's
        // registration contract requires a processor even for send-only contexts.
        public void ProcessMessage(ReferenceCountedSceneGraphMessage message) { }
    }
}
