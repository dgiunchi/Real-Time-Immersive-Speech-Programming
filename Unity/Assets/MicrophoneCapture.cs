using System;
using System.Collections.Generic;
using System.Text;
using Ubiq.Messaging;
using Ubiq.Networking;
using Ubiq.Rooms;
using UnityEngine;
using UnityEngine.XR;

public class MicrophoneCapture : MonoBehaviour, IPlaybackStatsSource
{
    private const string RecordingStartMessage = "__STT_CONTROL__:start";
    private const string RecordingStopMessage = "__STT_CONTROL__:stop";

    public bool sendToServer = true;
    public float gain = 1.0f;
    public int sampleRate = 16000;
    public int microphoneBufferSeconds = 1;
    public PlaybackStats lastFrameStats { get; private set; }
    public NetworkId networkId = new NetworkId(98);

    private NetworkContext context;
    private RoomClient roomClient;
    private AudioClip microphoneClip;
    private int lastMicPosition;
    private bool isRecording;
    private bool leftTriggerState;
    private readonly List<InputDevice> leftControllers = new List<InputDevice>();

    private void Start()
    {
        context = NetworkScene.Register(this, networkId);
        EnsureMicrophoneStarted();
    }

    private void OnDestroy()
    {
        if (microphoneClip)
        {
            Microphone.End(null);
        }
    }

    private void Update()
    {
        if (!roomClient)
        {
            roomClient = NetworkScene.Find(this)?.GetComponentInChildren<RoomClient>();
        }

        EnsureMicrophoneStarted();
        UpdateRecordingFromLeftTrigger();
        SendPendingMicrophoneSamples();
    }

    private void EnsureMicrophoneStarted()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            return;
        }
#endif
        if (microphoneClip || Microphone.devices.Length == 0)
        {
            return;
        }

        microphoneClip = Microphone.Start(null, true, Mathf.Max(1, microphoneBufferSeconds), sampleRate);
        lastMicPosition = Microphone.GetPosition(null);
    }

    private void UpdateRecordingFromLeftTrigger()
    {
        var triggerPressed = GetLeftTriggerPressed();
        if (triggerPressed == leftTriggerState)
        {
            return;
        }

        leftTriggerState = triggerPressed;
        SetRecording(triggerPressed);
    }

    private bool GetLeftTriggerPressed()
    {
        leftControllers.Clear();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller,
            leftControllers);

        foreach (var controller in leftControllers)
        {
            if (controller.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed)
            {
                return true;
            }
        }

        return false;
    }

    public void SetRecording(bool recording)
    {
        if (recording == isRecording)
        {
            return;
        }

        isRecording = recording;
        if (isRecording)
        {
            lastMicPosition = microphoneClip ? Microphone.GetPosition(null) : 0;
        }
        SendControlMessage(recording ? RecordingStartMessage : RecordingStopMessage);
    }

    private void SendPendingMicrophoneSamples()
    {
        if (!sendToServer || !isRecording || !microphoneClip || roomClient == null || roomClient.Me == null)
        {
            return;
        }

        var currentPosition = Microphone.GetPosition(null);
        if (lastMicPosition < 0)
        {
            lastMicPosition = currentPosition;
            return;
        }

        if (currentPosition < 0 || currentPosition == lastMicPosition)
        {
            return;
        }

        if (currentPosition > lastMicPosition)
        {
            SendSamples(lastMicPosition, currentPosition - lastMicPosition);
        }
        else
        {
            SendSamples(lastMicPosition, microphoneClip.samples - lastMicPosition);
            if (currentPosition > 0)
            {
                SendSamples(0, currentPosition);
            }
        }

        lastMicPosition = currentPosition;
    }

    private void SendSamples(int startSample, int sampleCount)
    {
        if (sampleCount <= 0)
        {
            return;
        }

        var samples = new float[sampleCount * microphoneClip.channels];
        if (!microphoneClip.GetData(samples, startSample))
        {
            return;
        }

        var pcm = new byte[sampleCount * sizeof(short)];
        var stats = new PlaybackStats();
        var outputOffset = 0;
        var channels = Mathf.Max(1, microphoneClip.channels);

        for (var i = 0; i < sampleCount; i++)
        {
            var mixed = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                mixed += samples[(i * channels) + channel];
            }
            mixed = Mathf.Clamp(mixed / channels * gain, -1f, 1f);

            var int16 = (short)Mathf.RoundToInt(mixed * short.MaxValue);
            pcm[outputOffset++] = (byte)(int16 & 0xff);
            pcm[outputOffset++] = (byte)((int16 >> 8) & 0xff);

            stats.sampleCount++;
            stats.volumeSum += Mathf.Abs(mixed);
        }

        lastFrameStats = stats;
        SendPayloadToServer(pcm);
    }

    private void SendControlMessage(string controlMessage)
    {
        if (!sendToServer)
        {
            return;
        }

        SendPayloadToServer(Encoding.UTF8.GetBytes(controlMessage));
    }

    private void SendPayloadToServer(byte[] payload)
    {
        if (roomClient == null || roomClient.Me == null)
        {
            return;
        }

        var clientUUID = Encoding.UTF8.GetBytes(roomClient.Me.uuid);
        var message = ReferenceCountedSceneGraphMessage.Rent(payload.Length + clientUUID.Length);

        clientUUID.CopyTo(new Span<byte>(message.bytes, message.start, clientUUID.Length));
        payload.CopyTo(new Span<byte>(message.bytes, message.start + clientUUID.Length, payload.Length));

        context.Send(message);
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage msg)
    {
    }
}
