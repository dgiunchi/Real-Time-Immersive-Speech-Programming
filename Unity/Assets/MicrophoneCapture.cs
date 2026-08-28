using System;
using System.Collections.Generic;
using System.Text;
using DreamCodeVR2.UI;
using DreamCodeVR2.ExperimentalAuthoring;
using Ubiq.Messaging;
using Ubiq.Networking;
using Ubiq.Rooms;
using UnityEngine;
using UnityEngine.XR;

public class MicrophoneCapture : MonoBehaviour, IPlaybackStatsSource
{
    private const string RecordingStartMessage = "__STT_CONTROL__:start";
    private const string RecordingStopMessage = "__STT_CONTROL__:stop";

    public event Action<bool> RecordingStateChanged;
    public event Action<SpeechCaptureDiagnostics> DiagnosticsUpdated;

    public bool sendToServer = true;
    public float gain = 1.0f;
    public int sampleRate = 16000;
    public int microphoneBufferSeconds = 1;
    public float triggerThreshold = 0.75f;
    public float releaseDebounceSeconds = 0.15f;
    public bool logRecordingState = true;
    public bool debugSpeechDiagnostics = true;
    public float minimumRecordingMs = 350f;
    public float nearSilentRmsThreshold = 0.005f;
    public float nearSilentPeakThreshold = 0.02f;
    public bool restartMicrophoneOnZeroSignal = true;
    public bool restartMicrophoneOnNearSilentCapture = true;
    public int maxAutoMicRecoveryAttempts = 2;
    public float microphoneRecoveryCooldownSeconds = 1.0f;
    public float nearSilentRecoveryMinimumRecordingMs = 800f;
    public float nearSilentRecoveryRmsThreshold = 0.0005f;
    public float nearSilentRecoveryPeakThreshold = 0.003f;
    public PlaybackStats lastFrameStats { get; private set; }
    public NetworkId networkId = new NetworkId(98);

    public bool IsRecording => isRecording;
    public bool IsMicrophoneReady => microphoneClip != null;
    public string ActiveMicrophoneDevice => Microphone.devices.Length > 0 ? Microphone.devices[0] : string.Empty;
    public SpeechCaptureDiagnostics LastDiagnostics { get; private set; }

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
    private bool loggedMicrophonePermissionGranted;
    private bool loggedMicrophonePermissionDenied;
    private bool microphonePermissionRequestPending;
    private bool loggedNoMicrophoneDevices;
    private bool loggedMicrophoneStarted;
    private bool researcherUiBlocked;
    private bool pttBlockedUntilRelease;
    private float lastTriggerPressedTime;
    private float recordingStartTime;
    private float lastMicrophoneRecoveryTime = float.NegativeInfinity;
    private int recordingSampleCount;
    private int recordingPcmBytes;
    private float recordingSquareSum;
    private float recordingPeak;
    private int autoMicRecoveryAttempts;
    private readonly List<InputDevice> leftControllers = new List<InputDevice>();
    private ExperimentConditionManager conditionManager;

    private void Start()
    {
        context = NetworkScene.Register(this, networkId);
        EnsureMicrophoneStarted();
    }

    private void OnDestroy()
    {
        StopMicrophoneCapture();
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
            if (!microphonePermissionRequestPending)
            {
                if (logRecordingState) Debug.Log("[MicrophoneCapture] requesting microphone permission");
                loggedMicrophonePermissionRequest = true;
                DreamCodeVR2ClientLogger.Event("stt", "MIC_PERMISSION_REQUEST", null, new { platform = "android" });
                microphonePermissionRequestPending = true;
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ =>
                {
                    microphonePermissionRequestPending = false;
                    if (!loggedMicrophonePermissionGranted)
                    {
                        loggedMicrophonePermissionGranted = true;
                        DreamCodeVR2ClientLogger.Event("stt", "MIC_PERMISSION_GRANTED", null, new { platform = "android" });
                    }
                };
                callbacks.PermissionDenied += _ =>
                {
                    microphonePermissionRequestPending = false;
                    if (!loggedMicrophonePermissionDenied)
                    {
                        loggedMicrophonePermissionDenied = true;
                        DreamCodeVR2ClientLogger.Warn("stt", "MIC_PERMISSION_DENIED", "Android microphone permission denied.");
                    }
                };
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
            }
            return;
        }

        if (!loggedMicrophonePermissionGranted)
        {
            loggedMicrophonePermissionGranted = true;
            DreamCodeVR2ClientLogger.Event("stt", "MIC_PERMISSION_GRANTED", null, new { platform = "android" });
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
                DreamCodeVR2ClientLogger.Error("stt", "MIC_ERROR", "No microphone devices found.");
            }

            PublishDiagnostics("init", "no microphone devices", 0, 0f, 0f, 0, false, false, true);
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
            DreamCodeVR2ClientLogger.Event("stt", "MIC_START", null, new
            {
                sample_rate = sampleRate,
                channels = microphoneClip.channels,
                samples = microphoneClip.samples
            });
        }

        PublishDiagnostics("init", "microphone ready", 0, 0f, 0f, 0, false, false, false);
    }

    private void UpdateRecordingFromLeftTrigger()
    {
        if (ResearcherUiInteractionState.IsResearcherUiInteractionActive)
        {
            if (!researcherUiBlocked)
            {
                researcherUiBlocked = true;
                DreamCodeVR2ClientLogger.Event("stt", "PTT_BLOCKED_BY_RESEARCHER_UI");
            }

            if (isRecording)
            {
                DreamCodeVR2ClientLogger.Event("stt", "PTT_FORCED_STOP_PANEL_OPEN");
                SetRecording(false);
            }
            return;
        }
        researcherUiBlocked = false;
        var triggerPressed = GetLeftTriggerPressed();
        if (!triggerPressed)
        {
            pttBlockedUntilRelease = false;
        }

        if (triggerPressed && (pttBlockedUntilRelease || !HasReadyResearcherSession()))
        {
            if (isRecording) SetRecording(false);
            leftTriggerState = false;
            if (!pttBlockedUntilRelease)
            {
                pttBlockedUntilRelease = true;
                var manager = conditionManager;
                DreamCodeVR2ClientLogger.Warn("stt", "PTT_BLOCKED_NO_SESSION", "Participant PTT requires a researcher session in READY state.", new
                {
                    peer_uuid = roomClient?.Me?.uuid,
                    condition = manager?.condition.ToString(),
                    session_id = manager?.sessionId,
                    session_started = manager?.sessionStarted,
                    session_ready = manager?.IsResearcherSessionReady
                });
                FindFirstObjectByType<DreamCodeVRSpeechStatusBridge>()?.ShowNoActiveSession();
            }
            return;
        }

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
        DreamCodeVR2ClientLogger.Event("stt", effectivePressed ? "PTT_PRESS" : "PTT_STOP");
        SetRecording(effectivePressed);
    }

    private bool HasReadyResearcherSession()
    {
        if (!conditionManager) conditionManager = FindFirstObjectByType<ExperimentConditionManager>();
        return conditionManager && conditionManager.IsResearcherSessionReady;
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

        if (recording)
        {
            ResetRecordingDiagnostics();
            recordingStartTime = Time.unscaledTime;
            lastMicPosition = microphoneClip ? Microphone.GetPosition(null) : 0;
            loggedFirstAudioChunk = false;
            DreamCodeVR2ClientLogger.Event("stt", "PTT_START", null, new { sample_rate = sampleRate, channels = microphoneClip ? microphoneClip.channels : 0 });
            PublishDiagnostics("start", "recording started", 0, 0f, 0f, 0, false, false, false);
        }
        else if (isRecording)
        {
            SendPendingMicrophoneSamples(true);
            PublishStopDiagnostics();
        }

        isRecording = recording;

        if (logRecordingState)
        {
            Debug.Log($"[MicrophoneCapture] recording {(recording ? "start" : "stop")}");
        }

        SendControlMessage(recording ? RecordingStartMessage : RecordingStopMessage);
        RecordingStateChanged?.Invoke(isRecording);
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

            PublishDiagnostics("stop", "microphone clip missing", 0, 0f, 0f, 0, false, false, true);
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

            if (force)
            {
                PublishDiagnostics("stop", "no pending samples on stop", 0, 0f, 0f, 0, false, false, true);
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
        var squareSum = 0f;
        var peak = 0f;

        for (var i = 0; i < sampleCount; i++)
        {
            var mixed = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                mixed += samples[(i * channels) + channel];
            }
            mixed = Mathf.Clamp(mixed / channels * gain, -1f, 1f);

            var abs = Mathf.Abs(mixed);
            squareSum += mixed * mixed;
            if (abs > peak)
            {
                peak = abs;
            }

            var int16 = (short)Mathf.RoundToInt(mixed * short.MaxValue);
            pcm[outputOffset++] = (byte)(int16 & 0xff);
            pcm[outputOffset++] = (byte)((int16 >> 8) & 0xff);

            stats.sampleCount++;
            stats.volumeSum += abs;
        }

        var rms = sampleCount > 0 ? Mathf.Sqrt(squareSum / sampleCount) : 0f;
        var nearSilent = rms <= nearSilentRmsThreshold && peak <= nearSilentPeakThreshold;

        recordingSampleCount += sampleCount;
        recordingPcmBytes += pcm.Length;
        recordingSquareSum += squareSum;
        if (peak > recordingPeak)
        {
            recordingPeak = peak;
        }

        lastFrameStats = stats;
        DreamCodeVR2ClientLogger.Event("stt", "STT_AUDIO_CHUNK_SENT", null, new
        {
            bytes = pcm.Length,
            samples = sampleCount,
            estimated_duration_ms = sampleRate > 0 ? sampleCount * 1000f / sampleRate : 0f,
            running_total_bytes = recordingPcmBytes
        });

        if (debugSpeechDiagnostics)
        {
            PublishDiagnostics("before-send", "sending pcm chunk", sampleCount, rms, peak, pcm.Length, nearSilent, false, false);
        }

        Debug.Log($"[MicrophoneCapture] audio chunk pcmBytes={pcm.Length} gain={gain} avgVolume={(stats.sampleCount > 0 ? stats.volumeSum / stats.sampleCount : 0f)}");
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

        var eventName = controlMessage == RecordingStartMessage ? "STT_CONTROL_START_SENT" : "STT_CONTROL_STOP_SENT";
        DreamCodeVR2ClientLogger.Event("stt", eventName);
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

        var payloadKind = control == RecordingStartMessage ? "control_start" : control == RecordingStopMessage ? "control_stop" : "audio";
        DreamCodeVR2ClientLogger.Event("network", "NID98_SEND", null, new
        {
            payload_kind = payloadKind,
            bytes = payload.Length,
            peer_uuid = roomClient.Me.uuid
        });

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

    private void ResetRecordingDiagnostics()
    {
        recordingSampleCount = 0;
        recordingPcmBytes = 0;
        recordingSquareSum = 0f;
        recordingPeak = 0f;
    }

    private void PublishStopDiagnostics()
    {
        var recordingMs = Mathf.Max(0f, (Time.unscaledTime - recordingStartTime) * 1000f);
        var rms = recordingSampleCount > 0 ? Mathf.Sqrt(recordingSquareSum / recordingSampleCount) : 0f;
        var nearSilent = rms <= nearSilentRmsThreshold && recordingPeak <= nearSilentPeakThreshold;
        var emptyAudioBuffer = recordingSampleCount <= 0 || recordingPcmBytes <= 0;
        var zeroSignal = recordingSampleCount > 0 && recordingPcmBytes > 0 && Mathf.Approximately(rms, 0f) && Mathf.Approximately(recordingPeak, 0f);
        var tooShort = recordingMs > 0f && recordingMs < minimumRecordingMs;
        var nearSilentRecoveryCandidate = !zeroSignal
            && !emptyAudioBuffer
            && recordingMs >= nearSilentRecoveryMinimumRecordingMs
            && rms <= nearSilentRecoveryRmsThreshold
            && recordingPeak <= nearSilentRecoveryPeakThreshold;
        var note = zeroSignal
            ? "all-zero microphone capture"
            : nearSilentRecoveryCandidate
            ? "near-silent microphone capture eligible for recovery"
            : emptyAudioBuffer
            ? "recording stopped with empty audio buffer"
            : tooShort
                ? "recording shorter than minimum threshold"
                : nearSilent
                    ? "recording near silent"
                    : "recording captured audio";

        PublishDiagnostics("stop", note, recordingSampleCount, rms, recordingPeak, recordingPcmBytes, nearSilent, tooShort, emptyAudioBuffer, zeroSignal);

        if (tooShort)
        {
            DreamCodeVR2ClientLogger.Warn("stt", "STT_RECORDING_TOO_SHORT", note, new { duration_ms = recordingMs, minimum_ms = minimumRecordingMs, bytes = recordingPcmBytes });
        }

        if (zeroSignal)
        {
            TryRecoverMicrophoneFromZeroSignal();
            return;
        }

        if (nearSilentRecoveryCandidate)
        {
            TryRecoverMicrophoneFromNearSilentCapture();
            return;
        }

        if (!emptyAudioBuffer && !nearSilent)
        {
            autoMicRecoveryAttempts = 0;
        }
    }

    private void PublishDiagnostics(
        string stage,
        string note,
        int samples,
        float rms,
        float peak,
        int pcmBytes,
        bool isNearSilent,
        bool isTooShort,
        bool isEmptyAudioBuffer,
        bool isZeroSignal = false)
    {
        var diagnostics = new SpeechCaptureDiagnostics
        {
            stage = stage,
            micReady = IsMicrophoneReady,
            deviceName = ActiveMicrophoneDevice,
            recordingMs = recordingStartTime <= 0f ? 0f : Mathf.Max(0f, (Time.unscaledTime - recordingStartTime) * 1000f),
            samples = samples,
            rms = rms,
            peak = peak,
            pcmBytes = pcmBytes,
            wavBytes = 0,
            isNearSilent = isNearSilent,
            isTooShort = isTooShort,
            isEmptyAudioBuffer = isEmptyAudioBuffer,
            isZeroSignal = isZeroSignal,
            note = note
        };

        LastDiagnostics = diagnostics;
        DiagnosticsUpdated?.Invoke(diagnostics);
    }

    private void TryRecoverMicrophoneFromZeroSignal()
    {
        if (!restartMicrophoneOnZeroSignal)
        {
            return;
        }

        if (autoMicRecoveryAttempts >= Mathf.Max(0, maxAutoMicRecoveryAttempts))
        {
            Debug.LogWarning("[SpeechDebug] microphone auto-recovery skipped: max attempts reached");
            return;
        }

        if (Time.unscaledTime - lastMicrophoneRecoveryTime < microphoneRecoveryCooldownSeconds)
        {
            Debug.LogWarning("[SpeechDebug] microphone auto-recovery skipped: cooldown active");
            return;
        }

        autoMicRecoveryAttempts++;
        lastMicrophoneRecoveryTime = Time.unscaledTime;
        Debug.LogWarning($"[SpeechDebug] microphone auto-recovery attempt={autoMicRecoveryAttempts}");

        StopMicrophoneCapture();
        EnsureMicrophoneStarted();
        PublishDiagnostics("reinit", "microphone restarted after zero-signal capture", 0, 0f, 0f, 0, false, false, false);
    }

    private void TryRecoverMicrophoneFromNearSilentCapture()
    {
        if (!restartMicrophoneOnNearSilentCapture)
        {
            return;
        }

        if (autoMicRecoveryAttempts >= Mathf.Max(0, maxAutoMicRecoveryAttempts))
        {
            Debug.LogWarning("[SpeechDebug] microphone near-silent recovery skipped: max attempts reached");
            return;
        }

        if (Time.unscaledTime - lastMicrophoneRecoveryTime < microphoneRecoveryCooldownSeconds)
        {
            Debug.LogWarning("[SpeechDebug] microphone near-silent recovery skipped: cooldown active");
            return;
        }

        autoMicRecoveryAttempts++;
        lastMicrophoneRecoveryTime = Time.unscaledTime;
        Debug.LogWarning($"[SpeechDebug] microphone near-silent recovery attempt={autoMicRecoveryAttempts}");

        StopMicrophoneCapture();
        EnsureMicrophoneStarted();
        PublishDiagnostics("reinit", "microphone restarted after near-silent capture", 0, 0f, 0f, 0, false, false, false);
    }

    private void StopMicrophoneCapture()
    {
        if (microphoneClip)
        {
            Microphone.End(null);
            microphoneClip = null;
        }

        lastMicPosition = -1;
        loggedMicrophoneStarted = false;
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage msg)
    {
    }
}
