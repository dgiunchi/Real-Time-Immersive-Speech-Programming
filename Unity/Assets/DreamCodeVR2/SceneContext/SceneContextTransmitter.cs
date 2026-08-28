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

        private void Start()
        {
            started = true;
            context = NetworkScene.Register(this, networkId);
            EnsureCompiler();
            EnsureRoomClient();

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

            if (roomClient == null || roomClient.Me == null)
            {
                Debug.LogWarning("[SceneContext] cannot send context: RoomClient.Me not ready", this);
                return;
            }

            if (context.Scene == null || context.Scene.connectionCount == 0)
            {
                Debug.LogWarning("[SceneContext] sending scene context while NetworkScene has 0 connections", this);
            }

            var peerUuid = roomClient.Me.uuid ?? string.Empty;
            var peerBytes = Encoding.UTF8.GetBytes(peerUuid);
            if (peerBytes.Length != 36)
            {
                Debug.LogWarning(
                    $"[SceneContext] peer UUID must be exactly 36 bytes; got {peerBytes.Length} for '{peerUuid}'",
                    this);
                return;
            }

            var packet = compiler.CaptureSnapshot(peerUuid);
            var json = JsonConvert.SerializeObject(packet, Formatting.None, SerializerSettings);
            var payloadBytes = Encoding.UTF8.GetBytes(json);
            var totalBytes = peerBytes.Length + payloadBytes.Length;
            var message = ReferenceCountedSceneGraphMessage.Rent(totalBytes);

            peerBytes.CopyTo(new Span<byte>(message.bytes, message.start, peerBytes.Length));
            payloadBytes.CopyTo(new Span<byte>(message.bytes, message.start + peerBytes.Length, payloadBytes.Length));

            context.Send(message);
            DreamCodeVR2ClientLogger.Event("network", "SCENE_CONTEXT_SENT", null, new
            {
                scene_version = packet.scene_version,
                object_count = packet.objects?.Length ?? 0,
                current_task = FindFirstObjectByType<DreamCodeVR2.Quest.QuestRuntimeState>()?.GetCurrentTask()?.step,
                payload_bytes = totalBytes,
                reason
            });

            if (logContextSends)
            {
                Debug.Log(
                    $"[SceneContext] sent objects={packet.objects?.Length ?? 0} bytes={totalBytes} scene_version={packet.scene_version} reason={reason}",
                    this);
            }
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
    }
}
