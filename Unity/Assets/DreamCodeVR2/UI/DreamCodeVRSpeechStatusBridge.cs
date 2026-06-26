using System;
using UnityEngine;

namespace DreamCodeVR2.UI
{
    public class DreamCodeVRSpeechStatusBridge : MonoBehaviour
    {
        public MicrophoneCapture microphoneCapture;
        public bool debugSpeechDiagnostics = true;
        public float heardDisplaySeconds = 4f;
        public float errorDisplaySeconds = 5f;

        public SpeechUiState CurrentState { get; private set; } = SpeechUiState.Initializing;
        public string LatestTranscript { get; private set; }
        public string CompactSpeechText { get; private set; } = "Speech: Initializing...";
        public string DetailedSpeechText { get; private set; } = "Speech: Initializing...";
        public string DiagnosticsSummaryText { get; private set; } = "Waiting for microphone initialization.";
        public SpeechCaptureDiagnostics LatestDiagnostics { get; private set; }

        public event Action StateChanged;

        private bool subscribedToMicrophone;
        private float readyAtTime = float.NegativeInfinity;

        private void OnEnable()
        {
            TranscriptionCollector.TranscriptReceived += OnTranscriptReceived;
            EnsureMicrophoneSubscription();
            RefreshInitializationState();
        }

        private void OnDisable()
        {
            TranscriptionCollector.TranscriptReceived -= OnTranscriptReceived;
            RemoveMicrophoneSubscription();
        }

        private void Update()
        {
            EnsureMicrophoneSubscription();
            RefreshInitializationState();

            if (CurrentState == SpeechUiState.Heard && Time.unscaledTime - readyAtTime >= heardDisplaySeconds)
            {
                SetState(SpeechUiState.Ready, "Speech: Ready", "Speech: Ready", BuildDiagnosticsSummary(LatestDiagnostics));
            }
            else if (IsErrorLike(CurrentState) && Time.unscaledTime - readyAtTime >= errorDisplaySeconds)
            {
                SetState(SpeechUiState.Ready, "Speech: Ready", "Speech: Ready", BuildDiagnosticsSummary(LatestDiagnostics));
            }
        }

        private void EnsureMicrophoneSubscription()
        {
            if (!microphoneCapture)
            {
                microphoneCapture = FindFirstObjectByType<MicrophoneCapture>();
            }

            if (!microphoneCapture || subscribedToMicrophone)
            {
                return;
            }

            microphoneCapture.RecordingStateChanged += OnRecordingStateChanged;
            microphoneCapture.DiagnosticsUpdated += OnDiagnosticsUpdated;
            subscribedToMicrophone = true;
        }

        private void RemoveMicrophoneSubscription()
        {
            if (microphoneCapture && subscribedToMicrophone)
            {
                microphoneCapture.RecordingStateChanged -= OnRecordingStateChanged;
                microphoneCapture.DiagnosticsUpdated -= OnDiagnosticsUpdated;
            }

            subscribedToMicrophone = false;
        }

        private void RefreshInitializationState()
        {
            if (!microphoneCapture || !microphoneCapture.IsMicrophoneReady)
            {
                if (CurrentState == SpeechUiState.Initializing)
                {
                    SetState(
                        SpeechUiState.Initializing,
                        "Speech: Initializing...",
                        "Speech: Initializing...",
                        "Waiting for microphone initialization.");
                }
                return;
            }

            if (CurrentState == SpeechUiState.Initializing)
            {
                SetState(SpeechUiState.Ready, "Speech: Ready", "Speech: Ready", BuildDiagnosticsSummary(LatestDiagnostics));
            }
        }

        private void OnRecordingStateChanged(bool isRecording)
        {
            if (isRecording)
            {
                SetState(
                    SpeechUiState.Listening,
                    "Speech: Listening...",
                    "Speech: Listening...",
                    BuildDiagnosticsSummary(LatestDiagnostics));
                return;
            }

            if (LatestDiagnostics.isEmptyAudioBuffer)
            {
                SetState(
                    SpeechUiState.EmptyAudioBuffer,
                    "Speech: No speech detected",
                    "Speech: Empty audio buffer",
                    BuildDiagnosticsSummary(LatestDiagnostics));
                return;
            }

            if (LatestDiagnostics.isZeroSignal)
            {
                SetState(
                    SpeechUiState.Error,
                    "Speech: Error",
                    "Speech: Mic retrying...",
                    BuildDiagnosticsSummary(LatestDiagnostics));
                return;
            }

            if (LatestDiagnostics.isNearSilent || LatestDiagnostics.isTooShort)
            {
                SetState(
                    SpeechUiState.NoSpeechDetected,
                    "Speech: No speech detected",
                    "Speech: No speech detected",
                    BuildDiagnosticsSummary(LatestDiagnostics));
                return;
            }

            SetState(
                SpeechUiState.Processing,
                "Speech: Processing...",
                "Speech: Processing...",
                BuildDiagnosticsSummary(LatestDiagnostics));
        }

        private void OnDiagnosticsUpdated(SpeechCaptureDiagnostics diagnostics)
        {
            LatestDiagnostics = diagnostics;
            DiagnosticsSummaryText = BuildDiagnosticsSummary(diagnostics);

            if (debugSpeechDiagnostics)
            {
                Debug.Log(diagnostics.ToLogString());
            }

            if (diagnostics.stage == "stop")
            {
                if (diagnostics.isEmptyAudioBuffer)
                {
                    SetState(
                        SpeechUiState.EmptyAudioBuffer,
                        "Speech: No speech detected",
                        "Speech: Empty audio buffer",
                        DiagnosticsSummaryText);
                }
                else if (diagnostics.isZeroSignal)
                {
                    SetState(
                        SpeechUiState.Error,
                        "Speech: Error",
                        "Speech: Mic retrying...",
                        DiagnosticsSummaryText);
                }
                else if (diagnostics.isNearSilent || diagnostics.isTooShort)
                {
                    SetState(
                        SpeechUiState.NoSpeechDetected,
                        "Speech: No speech detected",
                        "Speech: No speech detected",
                        DiagnosticsSummaryText);
                }
            }
        }

        private void OnTranscriptReceived(string transcript)
        {
            LatestTranscript = transcript;

            if (string.IsNullOrWhiteSpace(transcript))
            {
                Debug.Log("[SpeechDebug] empty transcript received");

                if (LatestDiagnostics.isEmptyAudioBuffer)
                {
                    SetState(
                        SpeechUiState.EmptyAudioBuffer,
                        "Speech: No speech detected",
                        "Speech: Empty audio buffer",
                        DiagnosticsSummaryText);
                    return;
                }

                if (LatestDiagnostics.isZeroSignal)
                {
                    SetState(
                        SpeechUiState.Error,
                        "Speech: Error",
                        "Speech: Mic retrying...",
                        DiagnosticsSummaryText);
                    return;
                }

                if (LatestDiagnostics.isNearSilent || LatestDiagnostics.isTooShort)
                {
                    SetState(
                        SpeechUiState.NoSpeechDetected,
                        "Speech: No speech detected",
                        "Speech: No speech detected",
                        DiagnosticsSummaryText);
                    return;
                }

                SetState(
                    SpeechUiState.EmptyTranscript,
                    "Speech: No speech detected",
                    "Speech: Empty transcription",
                    DiagnosticsSummaryText);
                return;
            }

            var trimmed = transcript.Trim();
            SetState(
                SpeechUiState.Heard,
                $"Speech: Heard: \"{TruncateInline(trimmed, 22)}\"",
                trimmed,
                DiagnosticsSummaryText);
        }

        private void SetState(SpeechUiState newState, string compactText, string detailText, string diagnosticsText)
        {
            var changed = CurrentState != newState
                || CompactSpeechText != compactText
                || DetailedSpeechText != detailText
                || DiagnosticsSummaryText != diagnosticsText;

            CurrentState = newState;
            CompactSpeechText = compactText;
            DetailedSpeechText = detailText;
            DiagnosticsSummaryText = diagnosticsText;

            if (newState == SpeechUiState.Ready || newState == SpeechUiState.Heard || IsErrorLike(newState))
            {
                readyAtTime = Time.unscaledTime;
            }

            if (!changed)
            {
                return;
            }

            Debug.Log($"[SpeechDebug] state={newState}");
            StateChanged?.Invoke();
        }

        private static bool IsErrorLike(SpeechUiState state)
        {
            return state == SpeechUiState.NoSpeechDetected
                || state == SpeechUiState.EmptyAudioBuffer
                || state == SpeechUiState.EmptyTranscript
                || state == SpeechUiState.Error;
        }

        private static string BuildDiagnosticsSummary(SpeechCaptureDiagnostics diagnostics)
        {
            if (!diagnostics.micReady)
            {
                return "Mic not ready.";
            }

            return
                $"Mic: {(string.IsNullOrWhiteSpace(diagnostics.deviceName) ? "default" : diagnostics.deviceName)} | " +
                $"ms: {diagnostics.recordingMs:0} | samples: {diagnostics.samples} | " +
                $"rms: {diagnostics.rms:0.0000} | peak: {diagnostics.peak:0.0000}";
        }

        private static string TruncateInline(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }
    }
}
