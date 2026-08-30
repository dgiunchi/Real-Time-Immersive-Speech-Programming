using System;
using System.Collections;
using System.Text;
using DreamCodeVR2.ExperimentalAuthoring;
using Newtonsoft.Json;
using Ubiq.Messaging;
using Ubiq.Networking;
using Ubiq.Rooms;
using UnityEngine;

namespace DreamCodeVR2.SceneContext
{
    public class SceneContextTransmitter : MonoBehaviour
    {
        private const int MaxDiagnosticJsonCharacters = 20000;
        private static readonly string[] C1DiagnosticObjectIds = { "painting_001", "table_drawer_001", "table_drawer_002", "table_drawer_003", "cabinet_drawer_001", "cabinet_drawer_002", "cabinet_drawer_003", "door_001", "key_001", "key_002", "lamp_001", "lamp_002", "lamp_003", "lamp_004", "sphere_001", "basket_001" };
        public NetworkId networkId = new NetworkId(100);
        public SceneContextCompiler compiler;
        public float initialSendDelaySeconds = 1.5f;
        public float snapshotIntervalSeconds = 15f;
        public bool logContextSends = true;

        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private NetworkContext context;
        private RoomClient roomClient;
        private Coroutine sendLoop;
        private bool started;
        private bool deferredPublication;
        private bool roomReadySubscribed;
        private string deferredReason;

        // Exposed for diagnostics and EditMode coverage. This is intentionally read-only:
        // callers still request publication only through SendSceneContextSnapshot.
        public bool PublicationIsDeferred => deferredPublication;

        private void Start()
        {
            started = true;
            context = NetworkScene.Register(this, networkId);
            EnsureCompiler();
            EnsureRoomClient();
            SubscribeRoomReady();

            if (sendLoop == null)
            {
                sendLoop = StartCoroutine(SendLoop());
            }
        }

        private void OnDisable()
        {
            if (sendLoop != null)
            {
                StopCoroutine(sendLoop);
                sendLoop = null;
            }
        }

        private void OnEnable()
        {
            if (started && sendLoop == null)
            {
                sendLoop = StartCoroutine(SendLoop());
            }
        }

        [ContextMenu("Send Scene Context Snapshot")]
        public void SendSceneContextSnapshotFromContextMenu()
        {
            SendSceneContextSnapshot("context menu");
        }

        public void SendSceneContextSnapshot(string reason = "manual")
        {
            EnsureCompiler();
            EnsureRoomClient();

            if (!compiler)
            {
                Debug.LogWarning("[SceneContext] cannot send context: compiler missing", this);
                return;
            }

            SubscribeRoomReady();
            if (!IsNetworkReady(out var readinessReason))
            {
                DeferPublication(reason, readinessReason);
                return;
            }

            var peerUuid = roomClient.Me.uuid ?? string.Empty;
            var peerBytes = Encoding.UTF8.GetBytes(peerUuid);
            if (peerBytes.Length != 36)
            {
                DeferPublication(reason, "invalid_peer_uuid");
                return;
            }

            var packet = compiler.CaptureSnapshot(peerUuid);
            var json = JsonConvert.SerializeObject(packet, Formatting.None, SerializerSettings);
            var payloadBytes = Encoding.UTF8.GetBytes(json);
            var totalBytes = peerBytes.Length + payloadBytes.Length;
            var message = ReferenceCountedSceneGraphMessage.Rent(totalBytes);

            peerBytes.CopyTo(new Span<byte>(message.bytes, message.start, peerBytes.Length));
            payloadBytes.CopyTo(new Span<byte>(message.bytes, message.start + peerBytes.Length, payloadBytes.Length));

            try
            {
                context.Send(message);
            }
            catch (Exception exception)
            {
                // A connection can disappear between the readiness check and Send. Keep this
                // narrow guard at the transport boundary; bootstrap/world setup is unaffected.
                DeferPublication(reason, "network_send_unavailable");
                Debug.LogWarning("[SceneContext] send deferred because the network became unavailable: " + exception.Message, this);
                return;
            }
            LogNid100Diagnostics(packet, json, reason);
            DreamCodeVR2ClientLogger.Event("network", "SCENE_CONTEXT_SENT", null, new
            {
                scene_version = packet.scene_version,
                object_count = packet.objects?.Length ?? 0,
                current_task = FindFirstObjectByType<DreamCodeVR2.Quest.QuestRuntimeState>()?.GetCurrentTask()?.step,
                payload_bytes = totalBytes,
                reason
            });

            if (deferredPublication)
            {
                DreamCodeVR2ClientLogger.Event("network", "SCENE_CONTEXT_DEFERRED_SEND_COMPLETED", null, new { deferred_reason = deferredReason, send_reason = reason });
                deferredPublication = false;
                deferredReason = null;
            }

            if (logContextSends)
            {
                Debug.Log(
                    $"[SceneContext] sent objects={packet.objects?.Length ?? 0} bytes={totalBytes} scene_version={packet.scene_version} reason={reason}",
                    this);
            }
        }

        private static void LogNid100Diagnostics(SceneContextPacket packet, string json, string reason)
        {
            var relevantObjects = new System.Collections.Generic.List<SceneObjectSummary>();
            foreach (var item in packet.objects ?? Array.Empty<SceneObjectSummary>())
            {
                if (item != null && IsC1DiagnosticObject(item.id)) relevantObjects.Add(item);
            }

            DreamCodeVR2ClientLogger.Event("network", "NID100_SCENE_CONTEXT_SENT", null, new
            {
                timestamp_unix_ms = packet.timestamp_unix_ms,
                reason,
                serialized_json = TruncateDiagnosticJson(json),
                json_truncated = json != null && json.Length > MaxDiagnosticJsonCharacters,
                relevant_quest_objects = relevantObjects.ToArray()
            });

            foreach (var item in relevantObjects)
            {
                // Keep the C1 snapshot deliberately small and stable for direct server/client comparison.
                DreamCodeVR2ClientLogger.Event("network", "C1_CAPABILITY_SNAPSHOT", null, new
                {
                    object_id = item.id,
                    labels = item.labels,
                    predefined_voice_commands = item.predefined_voice_commands,
                    predefined_presets = item.predefined_presets
                });
                LogMissingExpectedC1Capabilities(item);
            }
        }

        private static bool IsC1DiagnosticObject(string objectId)
        {
            foreach (var candidate in C1DiagnosticObjectIds) if (candidate == objectId) return true;
            return false;
        }

        private static void LogMissingExpectedC1Capabilities(SceneObjectSummary item)
        {
            string[] expectedCommands = null;
            string[] expectedPresets = null;
            switch (item.id)
            {
                case "painting_001": expectedCommands = new[] { "move_to_preset" }; expectedPresets = new[] { "aligned" }; break;
                case "table_drawer_001": case "table_drawer_002": case "table_drawer_003":
                case "cabinet_drawer_001": case "cabinet_drawer_002": case "cabinet_drawer_003":
                case "door_001": expectedCommands = new[] { "open", "close" }; break;
                case "key_001": case "key_002": expectedCommands = new[] { "use_with" }; break;
                case "lamp_001": case "lamp_002": case "lamp_003": case "lamp_004": expectedCommands = new[] { "activate", "deactivate", "toggle" }; break;
                case "sphere_001": expectedCommands = new[] { "move_to_preset", "place_in" }; expectedPresets = new[] { "soccer_ball" }; break;
            }
            var missingCommands = Missing(item.predefined_voice_commands, expectedCommands);
            var missingPresets = Missing(item.predefined_presets, expectedPresets);
            if (missingCommands.Length == 0 && missingPresets.Length == 0) return;
            DreamCodeVR2ClientLogger.Warn("network", "C1_CAPABILITY_EXPECTED_MISSING", "The live NID100 packet is missing an expected C1 capability.", new
            {
                object_id = item.id,
                missing_predefined_voice_commands = missingCommands,
                missing_predefined_presets = missingPresets
            });
        }

        private static string[] Missing(string[] actual, string[] expected)
        {
            if (expected == null || expected.Length == 0) return Array.Empty<string>();
            var values = new System.Collections.Generic.List<string>();
            foreach (var required in expected)
            {
                var found = false;
                foreach (var value in actual ?? Array.Empty<string>()) if (value == required) { found = true; break; }
                if (!found) values.Add(required);
            }
            return values.ToArray();
        }

        private static string TruncateDiagnosticJson(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length <= MaxDiagnosticJsonCharacters) return json;
            return json.Substring(0, MaxDiagnosticJsonCharacters) + "...[truncated]";
        }

        public void ProcessMessage(ReferenceCountedSceneGraphMessage msg)
        {
        }

        private IEnumerator SendLoop()
        {
            if (initialSendDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(initialSendDelaySeconds);
            }

            SendSceneContextSnapshot("startup");

            while (true)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, snapshotIntervalSeconds));
                SendSceneContextSnapshot("periodic");
            }
        }

        private void EnsureCompiler()
        {
            if (!compiler)
            {
                compiler = FindFirstObjectByType<SceneContextCompiler>();
            }
        }

        private void EnsureRoomClient()
        {
            if (!roomClient)
            {
                roomClient = NetworkScene.Find(this)?.GetComponentInChildren<RoomClient>();
            }
        }

        private void SubscribeRoomReady()
        {
            if (!roomClient || roomReadySubscribed) return;
            roomReadySubscribed = true;
            roomClient.OnJoinedRoom.AddListener(_ => FlushDeferredPublication());
        }

        private void FlushDeferredPublication()
        {
            if (deferredPublication) SendSceneContextSnapshot("deferred network ready");
        }

        private bool IsNetworkReady(out string reason)
        {
            // NetworkContext is a value type; an unregistered/default context exposes no Scene.
            if (context.Scene == null) { reason = "network_context_unavailable"; return false; }
            if (context.Scene.connectionCount <= 0) { reason = "no_network_connection"; return false; }
            if (roomClient == null || roomClient.Me == null) { reason = "room_peer_unavailable"; return false; }
            reason = null;
            return true;
        }

        private void DeferPublication(string requestReason, string reason)
        {
            if (!deferredPublication || deferredReason != reason)
                DreamCodeVR2ClientLogger.Event("network", "SCENE_CONTEXT_SEND_DEFERRED", null, new { reason, request_reason = requestReason });
            deferredPublication = true;
            deferredReason = reason;
        }
    }
}
