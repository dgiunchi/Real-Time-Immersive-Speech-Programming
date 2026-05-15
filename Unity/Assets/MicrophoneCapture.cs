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
using Ubiq.Samples;
using Ubiq.Voip;
using Ubiq.Voip.Implementations;
using Ubiq.Voip.Implementations.Dotnet;
using Ubiq.XR;

public class MicrophoneCapture : MonoBehaviour, IPlaybackStatsSource
{
    private const string RecordingStartMessage = "__STT_CONTROL__:start";
    private const string RecordingStopMessage = "__STT_CONTROL__:stop";

    public bool sendToServer = true;
    public float gain = 1.0f;
    public PlaybackStats lastFrameStats { get; private set; }
    public NetworkId networkId = new NetworkId(98);
    private NetworkContext context;

    private RoomClient roomClient;
    private IDotnetVoipSource microphoneInput;
    private G722AudioDecoder decoder = new G722AudioDecoder();
    private HandController recordingHand;
    private bool recordingEventsBound;
    private bool isRecording;

    void Start()
    {
        context = NetworkScene.Register(this,networkId);
    }

    void OnDestroy()
    {
        if (microphoneInput != null)
        {
            microphoneInput.OnAudioSourceEncodedSample -= SendAudioToServer;
        }

        if (recordingHand != null)
        {
            recordingHand.TriggerPress.RemoveListener(SetRecording);
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!roomClient)
        {
            roomClient = NetworkScene.Find(this)?.GetComponentInChildren<RoomClient>();
            if (!roomClient)
            {
                return;
            }
        }

        if (microphoneInput == null)
        {
            microphoneInput = roomClient.GetComponentInChildren<IDotnetVoipSource>(includeInactive:true);
            if (microphoneInput != null)
            {
                microphoneInput.OnAudioSourceEncodedSample += SendAudioToServer;
            }
        }

        if (!recordingEventsBound)
        {
            BindLeftTrigger();
        }
    }

    private void BindLeftTrigger()
    {
        foreach (var hand in FindObjectsOfType<HandController>())
        {
            if (hand.Left)
            {
                recordingHand = hand;
                recordingHand.TriggerPress.AddListener(SetRecording);
                recordingEventsBound = true;
                return;
            }
        }
    }

    private void SetRecording(bool recording)
    {
        if (recording == isRecording)
        {
            return;
        }

        isRecording = recording;
        SendControlMessage(recording ? RecordingStartMessage : RecordingStopMessage);
    }

    private void SendAudioToServer(uint durationRtpUnits, byte[] sample)
    {
        // lastFrameStats

        if (sendToServer && isRecording) {
            // Debug.Log("Sending audio to server");
            // Decode the sample from G722 to PCM
            short[] decodedSampleShort = decoder.Decode(sample);

            var stats = new PlaybackStats();
            stats.sampleCount = decodedSampleShort.Length;
            foreach(var pcm in decodedSampleShort)
            {
                stats.volumeSum += Mathf.Abs(((float)pcm) / short.MaxValue);
            }
            lastFrameStats = stats;

            byte[] decodedSampleByte = new byte[decodedSampleShort.Length * sizeof(short)];
            Buffer.BlockCopy(decodedSampleShort, 0, decodedSampleByte, 0, decodedSampleByte.Length);

            SendPayloadToServer(decodedSampleByte);
        }
    }

    private void SendControlMessage(string controlMessage)
    {
        if (!sendToServer)
        {
            return;
        }

        SendPayloadToServer(System.Text.Encoding.UTF8.GetBytes(controlMessage));
    }

    private void SendPayloadToServer(byte[] payload)
    {
        if (!roomClient || roomClient.Me == null)
        {
            return;
        }

        // Get the client UUID
        byte[] clientUUID = System.Text.Encoding.UTF8.GetBytes(roomClient.Me.uuid);

        // Create a message that fits the client UUID and payload
        var message = ReferenceCountedSceneGraphMessage.Rent(payload.Length + clientUUID.Length);

        // Copy the client UUID and payload into the message
        clientUUID.CopyTo(new Span<byte>(message.bytes, message.start, clientUUID.Length));
        payload.CopyTo(new Span<byte>(message.bytes, message.start + clientUUID.Length, payload.Length));

        // Send the message to the server with a fixed network ID
        context.Send(message);
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage msg)
    {
    }
}
