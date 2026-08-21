using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Ubiq.Messaging;
using Ubiq.Networking;
using Ubiq.Rooms;
using UnityEngine;
using UnityEngine.XR;
using AgenticCache;

public class MicrophoneCapture : MonoBehaviour, IPlaybackStatsSource
{
    private const string RecordingStartMessage = "__STT_CONTROL__:start";
    private const string RecordingStopMessage = "__STT_CONTROL__:stop";

    public bool sendToServer = true;
    public float gain = 1.0f;
    public int sampleRate = 16000;
    public int microphoneBufferSeconds = 1;
    [Tooltip("Optional microphone device name. Leave empty to prefer the Oculus headset microphone while XR is active, then fall back to the system default.")]
    public string preferredMicrophoneDevice = "";
    public float triggerThreshold = 0.75f;
    public float releaseDebounceSeconds = 0.15f;
    [Tooltip("Recordings below this peak level are treated as a silent/stale microphone stream.")]
    public float silentRecordingPeakThreshold = 0.001f;
    [Tooltip("Delay before reopening the microphone after a silent recording.")]
    public float microphoneRestartDelaySeconds = 0.35f;
    public bool logRecordingState = true;
    public PlaybackStats lastFrameStats { get; private set; }
    public NetworkId networkId = new NetworkId(98);

    private NetworkContext context;
    private RoomClient roomClient;
    private AudioClip microphoneClip;
    private AudioClip lastRecordingClip;
    private AudioSource recordingPlaybackSource;
    private string activeMicrophoneDevice;
    private int lastMicPosition;
    private bool isRecording;
    private bool leftTriggerState;
    private bool loggedNoRoomClient;
    private bool loggedNoPeer;
    private bool loggedNoConnections;
    private bool loggedFirstAudioChunk;
    private bool loggedMicrophonePermissionRequest;
    private bool loggedNoMicrophoneDevices;
    private bool loggedMicrophoneStarted;
    private bool microphoneRestarting;
    private float lastTriggerPressedTime;
    private readonly List<InputDevice> leftControllers = new List<InputDevice>();
    private readonly List<float> currentRecordingSamples = new List<float>();

    private void Start()
    {
        context = NetworkScene.Register(this, networkId);
        recordingPlaybackSource = gameObject.AddComponent<AudioSource>();
        recordingPlaybackSource.playOnAwake = false;
        recordingPlaybackSource.loop = false;
        recordingPlaybackSource.spatialBlend = 0f;
        EnsureMicrophoneStarted();
    }

    private void OnDestroy()
    {
        if (microphoneClip)
        {
            Microphone.End(activeMicrophoneDevice);
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
            if (logRecordingState && !loggedMicrophonePermissionRequest)
            {
                Debug.Log("[MicrophoneCapture] requesting microphone permission");
                loggedMicrophonePermissionRequest = true;
            }
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            return;
        }
#endif
        if (microphoneClip || microphoneRestarting)
        {
            return;
        }

        if (Microphone.devices.Length == 0)
        {
            if (logRecordingState && !loggedNoMicrophoneDevices)
            {
                Debug.LogWarning("[MicrophoneCapture] no microphone devices found");
                loggedNoMicrophoneDevices = true;
            }
            return;
        }

        activeMicrophoneDevice = ResolveMicrophoneDevice();
        microphoneClip = Microphone.Start(activeMicrophoneDevice, true, Mathf.Max(1, microphoneBufferSeconds), sampleRate);
        lastMicPosition = Microphone.GetPosition(activeMicrophoneDevice);
        loggedNoMicrophoneDevices = false;

        if (logRecordingState && !loggedMicrophoneStarted)
        {
            Debug.Log(
                $"[MicrophoneCapture] microphone started selected={activeMicrophoneDevice ?? "<system default>"} " +
                $"devices={string.Join(",", Microphone.devices)} " +
                $"frequency={sampleRate} channels={microphoneClip.channels} samples={microphoneClip.samples} " +
                $"position={lastMicPosition}");
            loggedMicrophoneStarted = true;
        }
    }

    private string ResolveMicrophoneDevice()
    {
        var devices = Microphone.devices;
        if (!string.IsNullOrWhiteSpace(preferredMicrophoneDevice))
        {
            foreach (var device in devices)
            {
                if (device.IndexOf(preferredMicrophoneDevice, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return device;
                }
            }

            Debug.LogWarning($"[MicrophoneCapture] preferred device not found: {preferredMicrophoneDevice}");
        }

        foreach (var device in devices)
        {
            if (device.IndexOf("Oculus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                device.IndexOf("Headset Microphone", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return device;
            }
        }

        return null;
    }

    private void UpdateRecordingFromLeftTrigger()
    {
        var triggerPressed = GetLeftTriggerPressed();
        if (triggerPressed)
        {
            lastTriggerPressedTime = Time.unscaledTime;
        }

        var effectivePressed = triggerPressed ||
            (leftTriggerState && Time.unscaledTime - lastTriggerPressedTime < releaseDebounceSeconds);

        if (effectivePressed == leftTriggerState)
        {
            return;
        }

        leftTriggerState = effectivePressed;
        SetRecording(effectivePressed);
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

            if (controller.TryGetFeatureValue(CommonUsages.trigger, out float value) && value >= triggerThreshold)
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

        if (!recording && isRecording)
        {
            SendPendingMicrophoneSamples(true);
            if (FinalizeRecordingForPlayback())
            {
                RestartMicrophoneCapture("silent recording");
            }
        }

        isRecording = recording;
        if (isRecording)
        {
            lastMicPosition = microphoneClip ? Microphone.GetPosition(activeMicrophoneDevice) : 0;
            currentRecordingSamples.Clear();
            loggedFirstAudioChunk = false;
        }

        if (logRecordingState)
        {
            Debug.Log($"[MicrophoneCapture] recording {(recording ? "start" : "stop")}");
        }

        var control = recording ? RecordingStartMessage : RecordingStopMessage;
        if (recording)
        {
            var registry = FindFirstObjectByType<AgenticSceneRegistry>();
            var selectedObjectId = registry != null ? registry.GetSelectedObjectId() : null;
            if (!string.IsNullOrEmpty(selectedObjectId)) control += ":" + selectedObjectId;
        }
        SendControlMessage(control);
    }

    private void SendPendingMicrophoneSamples(bool force = false)
    {
        if (!sendToServer || (!isRecording && !force) || roomClient == null || roomClient.Me == null)
        {
            return;
        }

        if (!microphoneClip)
        {
            if (logRecordingState)
            {
                Debug.LogWarning("[MicrophoneCapture] no microphone clip while recording");
            }
            return;
        }

        var currentPosition = Microphone.GetPosition(activeMicrophoneDevice);
        if (lastMicPosition < 0)
        {
            lastMicPosition = currentPosition;
            return;
        }

        if (currentPosition < 0 || currentPosition == lastMicPosition)
        {
            if (force && logRecordingState)
            {
                Debug.LogWarning(
                    $"[MicrophoneCapture] no pending samples on stop currentPosition={currentPosition} " +
                    $"lastMicPosition={lastMicPosition}");
            }
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
            currentRecordingSamples.Add(int16 / 32768f);

            stats.sampleCount++;
            stats.volumeSum += Mathf.Abs(mixed);
        }

        lastFrameStats = stats;
        SendPayloadToServer(pcm);
    }

    private bool FinalizeRecordingForPlayback()
    {
        if (currentRecordingSamples.Count == 0)
        {
            if (lastRecordingClip) Destroy(lastRecordingClip);
            lastRecordingClip = null;
            Debug.LogWarning("[MicrophoneCapture] recording contained no samples; restarting microphone");
            return true;
        }

        if (lastRecordingClip) Destroy(lastRecordingClip);
        lastRecordingClip = AudioClip.Create(
            "Last STT Recording",
            currentRecordingSamples.Count,
            1,
            sampleRate,
            false);
        lastRecordingClip.SetData(currentRecordingSamples.ToArray(), 0);

        var sumSquares = 0d;
        var peak = 0f;
        foreach (var sample in currentRecordingSamples)
        {
            sumSquares += sample * sample;
            peak = Mathf.Max(peak, Mathf.Abs(sample));
        }
        var rms = Mathf.Sqrt((float)(sumSquares / currentRecordingSamples.Count));
        Debug.Log(
            $"[MicrophoneCapture] saved last recording duration={lastRecordingClip.length:0.00}s " +
            $"rms={rms:0.0000} peak={peak:0.0000}");

        return peak < silentRecordingPeakThreshold;
    }

    public void RestartMicrophoneCapture(string reason = "manual request")
    {
        if (microphoneRestarting)
        {
            return;
        }

        StartCoroutine(RestartMicrophoneCoroutine(reason));
    }

    private IEnumerator RestartMicrophoneCoroutine(string reason)
    {
        microphoneRestarting = true;
        var deviceToClose = activeMicrophoneDevice;
        if (logRecordingState)
        {
            Debug.LogWarning($"[MicrophoneCapture] restarting microphone reason={reason} device={deviceToClose ?? "<system default>"}");
        }

        var panel = FindFirstObjectByType<AgenticXRConsentPanel>();
        if (panel != null)
        {
            panel.ShowStatus("microphone reconnecting", "The recording was silent. Reopening the headset microphone...");
        }

        Microphone.End(deviceToClose);
        if (microphoneClip)
        {
            Destroy(microphoneClip);
        }
        microphoneClip = null;
        activeMicrophoneDevice = null;
        lastMicPosition = 0;
        loggedMicrophoneStarted = false;

        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, microphoneRestartDelaySeconds));

        microphoneRestarting = false;
        EnsureMicrophoneStarted();
        if (microphoneClip && panel != null)
        {
            panel.ShowStatus("microphone reconnected", "Please hold the left trigger and try speaking again.");
        }
    }

    public bool PlayLastRecording()
    {
        if (!lastRecordingClip || recordingPlaybackSource == null)
        {
            Debug.LogWarning("[MicrophoneCapture] no completed recording is available for playback");
            return false;
        }

        recordingPlaybackSource.Stop();
        recordingPlaybackSource.clip = lastRecordingClip;
        recordingPlaybackSource.Play();
        Debug.Log($"[MicrophoneCapture] playing last recording duration={lastRecordingClip.length:0.00}s");
        return true;
    }

    private void SendControlMessage(string controlMessage)
    {
        if (!sendToServer)
        {
            return;
        }

        if (logRecordingState)
        {
            Debug.Log($"[MicrophoneCapture] control {controlMessage}");
        }

        SendPayloadToServer(Encoding.UTF8.GetBytes(controlMessage));
    }

    private void SendPayloadToServer(byte[] payload)
    {
        if (!roomClient)
        {
            roomClient = NetworkScene.Find(this)?.GetComponentInChildren<RoomClient>();
        }

        var control = payload.Length <= 64 ? Encoding.UTF8.GetString(payload) : null;
        if (roomClient == null || roomClient.Me == null)
        {
            if (logRecordingState && roomClient == null && !loggedNoRoomClient)
            {
                Debug.LogWarning("[MicrophoneCapture] drop packet: RoomClient not found");
                loggedNoRoomClient = true;
            }
            else if (logRecordingState && roomClient != null && roomClient.Me == null && !loggedNoPeer)
            {
                Debug.LogWarning("[MicrophoneCapture] drop packet: RoomClient.Me not ready");
                loggedNoPeer = true;
            }
            return;
        }

        if (context.Scene == null || context.Scene.connectionCount == 0)
        {
            if (logRecordingState && !loggedNoConnections)
            {
                Debug.LogWarning("[MicrophoneCapture] sending while NetworkScene has 0 connections");
                loggedNoConnections = true;
            }
        }

        var clientUUID = Encoding.UTF8.GetBytes(roomClient.Me.uuid);
        var message = ReferenceCountedSceneGraphMessage.Rent(payload.Length + clientUUID.Length);

        clientUUID.CopyTo(new Span<byte>(message.bytes, message.start, clientUUID.Length));
        payload.CopyTo(new Span<byte>(message.bytes, message.start + clientUUID.Length, payload.Length));

        context.Send(message);

        if (logRecordingState && control != null && control.StartsWith("__STT_CONTROL__:"))
        {
            Debug.Log($"[MicrophoneCapture] sent control {control} peer={roomClient.Me.uuid} bytes={message.length}");
        }
        else if (logRecordingState && !loggedFirstAudioChunk)
        {
            Debug.Log($"[MicrophoneCapture] sent first audio chunk peer={roomClient.Me.uuid} pcmBytes={payload.Length}");
            loggedFirstAudioChunk = true;
        }
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage msg)
    {
    }
}
