using System.Collections;
using System.Collections.Generic;
using Ubiq.Networking;
using UnityEngine;
using Ubiq.Dictionaries;
using Ubiq.Messaging;
using Ubiq.Logging.Utf8Json;
using Ubiq.Rooms;
using System;
using System.Text;
using UnityEngine.Events;

public class TranscriptionCollector : MonoBehaviour
{
    // Concrete subclass so the UnityEvent is serializable and always instantiated.
    [Serializable] public class TranscriptEvent : UnityEvent<string> { }

    public NetworkId networkId = new NetworkId(98);
    private NetworkContext context;

    [Header("VR Display")]
    public TranscriptDisplay transcriptDisplay;

    [Header("Events")]
    public TranscriptEvent onTranscriptReceived = new TranscriptEvent();

    private const string STT_CONTROL_PREFIX = "__STT_CONTROL__:";

    [Serializable]
    private struct Message
    {
        public string type;
        public string peer;
        public string data;
    }

    void Awake()
    {
        if (onTranscriptReceived == null) onTranscriptReceived = new TranscriptEvent();
    }

    void Start()
    {
        context = NetworkScene.Register(this, networkId);
    }

    void Update() { }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage data)
    {
        // Raw bytes: check for STT control messages first
        if (data.data.Length <= 64)
        {
            var raw = Encoding.UTF8.GetString(data.bytes, data.start, data.length);
            if (raw.StartsWith(STT_CONTROL_PREFIX))
            {
                var action = raw.Substring(STT_CONTROL_PREFIX.Length);
                if (action == "start" && transcriptDisplay) transcriptDisplay.OnRecordingStart();
                else if (action == "stop" && transcriptDisplay) transcriptDisplay.OnRecordingStop();
                return;
            }
        }

        Message message = data.FromJson<Message>();
        if (string.IsNullOrWhiteSpace(message.data)) return;

        Debug.Log($"[Transcript] {message.peer}: {message.data}");

        // Show transcript in VR immediately so participant sees what the system heard
        if (transcriptDisplay) transcriptDisplay.ShowTranscript(message.data);

        onTranscriptReceived?.Invoke(message.data);
    }
}
