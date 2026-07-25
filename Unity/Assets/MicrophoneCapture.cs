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
    public float triggerThreshold = 0.75f;
    public float releaseDebounceSeconds = 0.15f;
    public bool logRecordingState = true;
    [Tooltip("If true, only the LEFT controller arms push-to-talk. Off by default so either hand works — a mistracked left controller used to silently disable speech.")]
    public bool preferLeftControllerOnly = false;
    [Tooltip("Seconds between mic status reports to the researcher panel (0 = off).")]
    public float statusReportInterval = 1f;
    public PlaybackStats lastFrameStats { get; private set; }
    public NetworkId networkId = new NetworkId(98);

    private NetworkContext context;
    private RoomClient roomClient;
    private AudioClip microphoneClip;
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
    private float lastTriggerPressedTime;
    private readonly List<InputDevice> leftControllers = new List<InputDevice>();
    private bool remoteRecordOverride;
    private float lastStatusReportTime;
    private float lastLevel;

    private void Start()
    {
        context = NetworkScene.Register(this, networkId);

        // A 1-second loop buffer loses audio whenever a frame hitches longer than
        // that (common on device during compilation/GC), which shows up as an
        // empty transcript. Ten seconds costs ~320 KB and removes the failure mode.
        microphoneBufferSeconds = Mathf.Max(10, microphoneBufferSeconds);

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
        ReportMicStatus();
    }

    // Android can silently stop a capture (focus loss, another app grabbing the
    // mic, permission granted after start). Detect that and restart, otherwise the
    // app looks fine but records nothing for the rest of the session.
    private void RestartMicrophoneIfStopped()
    {
        if (!microphoneClip) return;
        if (Microphone.IsRecording(null)) return;

        if (logRecordingState)
        {
            Debug.LogWarning("[MicrophoneCapture] capture stopped unexpectedly — restarting");
        }
        Microphone.End(null);
        microphoneClip = null;
        loggedMicrophoneStarted = false;
        EnsureMicrophoneStarted();
    }

    // Publishes mic health to the researcher's control panel so a dead microphone
    // is visible before a participant is in the headset, instead of being
    // discovered as an empty transcript mid-session.
    private void ReportMicStatus()
    {
        if (statusReportInterval <= 0f || !sendToServer) return;
        if (Time.unscaledTime - lastStatusReportTime < statusReportInterval) return;
        lastStatusReportTime = Time.unscaledTime;

        RestartMicrophoneIfStopped();

        int devices = Microphone.devices.Length;
        bool live = microphoneClip && Microphone.IsRecording(null);
        float level = lastFrameStats.sampleCount > 0
            ? lastFrameStats.volumeSum / lastFrameStats.sampleCount
            : 0f;
        lastLevel = Mathf.Max(level, lastLevel * 0.7f); // brief decay so peaks stay readable

        // Kept well under the server's 64-byte control-message threshold.
        SendPayloadToServer(Encoding.UTF8.GetBytes(
            $"__MIC_STATUS__:d{devices}|l{(live ? 1 : 0)}|r{(isRecording ? 1 : 0)}|v{lastLevel:F3}"));
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
        if (microphoneClip)
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

        microphoneClip = Microphone.Start(null, true, Mathf.Max(1, microphoneBufferSeconds), sampleRate);
        lastMicPosition = Microphone.GetPosition(null);
        loggedNoMicrophoneDevices = false;

        if (logRecordingState && !loggedMicrophoneStarted)
        {
            Debug.Log(
                $"[MicrophoneCapture] microphone started devices={string.Join(",", Microphone.devices)} " +
                $"frequency={sampleRate} channels={microphoneClip.channels} samples={microphoneClip.samples} " +
                $"position={lastMicPosition}");
            loggedMicrophoneStarted = true;
        }
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
        // Push-to-talk must never be the reason a session fails. Historically this
        // only polled the LEFT controller's trigger, so a controller that wasn't
        // tracked (or a participant using the other hand) meant the mic never armed
        // and the transcript stayed empty with no visible reason.
        //
        // Now: either controller, trigger OR grip, plus a remote override the
        // researcher can drive from the control panel, plus Space in the Editor.
        if (remoteRecordOverride)
        {
            return true;
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        if (Input.GetKey(KeyCode.Space))
        {
            return true;
        }
#endif

        leftControllers.Clear();
        InputDevices.GetDevicesWithCharacteristics(
            preferLeftControllerOnly
                ? InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller
                : InputDeviceCharacteristics.Controller,
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

            if (controller.TryGetFeatureValue(CommonUsages.gripButton, out bool grip) && grip)
            {
                return true;
            }

            if (controller.TryGetFeatureValue(CommonUsages.grip, out float gripValue) && gripValue >= triggerThreshold)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Researcher fallback: hold recording open from the control panel when the
    /// controller trigger is unavailable. Driven via StudyOutcomes ("mic/start").
    /// </summary>
    public void SetRemoteRecordOverride(bool held)
    {
        remoteRecordOverride = held;
        if (logRecordingState)
        {
            Debug.Log($"[MicrophoneCapture] remote record override {(held ? "ON" : "OFF")}");
        }
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
        }

        isRecording = recording;
        if (isRecording)
        {
            lastMicPosition = microphoneClip ? Microphone.GetPosition(null) : 0;
            loggedFirstAudioChunk = false;
        }

        if (logRecordingState)
        {
            Debug.Log($"[MicrophoneCapture] recording {(recording ? "start" : "stop")}");
        }

        SendControlMessage(recording ? RecordingStartMessage : RecordingStopMessage);
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

        var currentPosition = Microphone.GetPosition(null);
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
