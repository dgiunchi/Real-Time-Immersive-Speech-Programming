using System;
using System.Text;
using Newtonsoft.Json;
using Ubiq.Messaging;
using Ubiq.Networking;
using Ubiq.Rooms;
using UnityEngine;

namespace DreamCodeVR2.ContextBridge
{
    public class InteractionContextTransmitter : MonoBehaviour
    {
        public NetworkId networkId = new NetworkId(99);
        public InteractionContextProvider provider;
        public global::MicrophoneCapture microphoneCapture;
        public bool sendOnRecordingStart = true;
        public bool sendOnRecordingStop = true;
        public bool sendPeriodicallyWhileRecording;
        public float periodicHz = 5f;
        public KeyCode manualSendKey = KeyCode.None;
        public bool logContextSends = true;

        private NetworkContext context;
        private RoomClient roomClient;
        private bool recording;
        private float nextPeriodicSendTime;
        private bool subscribedToMicrophone;

        private void Start()
        {
            context = NetworkScene.Register(this, networkId);
            EnsureProvider();
            EnsureRoomClient();
            EnsureMicrophoneSubscription();
        }

        private void OnEnable()
        {
            EnsureMicrophoneSubscription();
        }

        private void OnDisable()
        {
            RemoveMicrophoneSubscription();
        }

        private void Update()
        {
            EnsureRoomClient();
            EnsureMicrophoneSubscription();

            if (manualSendKey != KeyCode.None && Input.GetKeyDown(manualSendKey))
            {
                SendContextSnapshot("manual key");
            }

            if (!sendPeriodicallyWhileRecording || !recording)
            {
                return;
            }

            if (Time.unscaledTime >= nextPeriodicSendTime)
            {
                SendContextSnapshot("periodic recording");
                nextPeriodicSendTime = Time.unscaledTime + PeriodicIntervalSeconds();
            }
        }

        [ContextMenu("Send Context Snapshot")]
        public void SendContextSnapshotFromContextMenu()
        {
            SendContextSnapshot("context menu");
        }

        public void SendContextSnapshot(string reason = "manual")
        {
            EnsureProvider();
            EnsureRoomClient();

            if (!provider)
            {
                Debug.LogWarning("[ContextBridge] cannot send context: provider missing", this);
                return;
            }

            if (roomClient == null || roomClient.Me == null)
            {
                Debug.LogWarning("[ContextBridge] cannot send context: RoomClient.Me not ready", this);
                return;
            }

            if (context.Scene == null || context.Scene.connectionCount == 0)
            {
                Debug.LogWarning("[ContextBridge] sending context while NetworkScene has 0 connections", this);
            }

            var peerUuid = roomClient.Me.uuid;
            var snapshot = provider.CaptureSnapshot(peerUuid);
            var json = JsonConvert.SerializeObject(snapshot, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include
            });

            SendJsonPayload(peerUuid, json);

            if (logContextSends)
            {
                Debug.Log(
                    $"[ContextBridge] sent peer={peerUuid} " +
                    $"active_selection={SummaryId(snapshot.active_selection)} " +
                    $"pointed_object={SummaryId(snapshot.pointed_object)} " +
                    $"scene_version={snapshot.scene_version} reason={reason}");
            }
        }

        public void ProcessMessage(ReferenceCountedSceneGraphMessage msg)
        {
        }

        private void SendJsonPayload(string peerUuid, string json)
        {
            var peerBytes = Encoding.UTF8.GetBytes(peerUuid);
            var payloadBytes = Encoding.UTF8.GetBytes(json);
            var message = ReferenceCountedSceneGraphMessage.Rent(peerBytes.Length + payloadBytes.Length);

            peerBytes.CopyTo(new Span<byte>(message.bytes, message.start, peerBytes.Length));
            payloadBytes.CopyTo(new Span<byte>(message.bytes, message.start + peerBytes.Length, payloadBytes.Length));

            context.Send(message);
        }

        private void EnsureProvider()
        {
            if (!provider)
            {
                provider = FindFirstObjectByType<InteractionContextProvider>();
            }
        }

        private void EnsureRoomClient()
        {
            if (!roomClient)
            {
                roomClient = NetworkScene.Find(this)?.GetComponentInChildren<RoomClient>();
            }
        }

        private void EnsureMicrophoneSubscription()
        {
            if (!microphoneCapture)
            {
                microphoneCapture = FindFirstObjectByType<global::MicrophoneCapture>();
            }

            if (!microphoneCapture || subscribedToMicrophone)
            {
                return;
            }

            microphoneCapture.RecordingStateChanged += OnRecordingStateChanged;
            subscribedToMicrophone = true;
        }

        private void RemoveMicrophoneSubscription()
        {
            if (microphoneCapture && subscribedToMicrophone)
            {
                microphoneCapture.RecordingStateChanged -= OnRecordingStateChanged;
            }

            subscribedToMicrophone = false;
        }

        private void OnRecordingStateChanged(bool isRecording)
        {
            recording = isRecording;
            if (isRecording)
            {
                nextPeriodicSendTime = Time.unscaledTime + PeriodicIntervalSeconds();
                if (sendOnRecordingStart)
                {
                    SendContextSnapshot("recording start");
                }
            }
            else if (sendOnRecordingStop)
            {
                SendContextSnapshot("recording stop");
            }
        }

        private static string SummaryId(ObjectSummary summary)
        {
            return summary == null || string.IsNullOrEmpty(summary.id) ? "null" : summary.id;
        }

        private float PeriodicIntervalSeconds()
        {
            return 1f / Mathf.Max(0.1f, periodicHz);
        }
    }
}
