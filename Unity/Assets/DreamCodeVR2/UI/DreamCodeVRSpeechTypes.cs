using System;

namespace DreamCodeVR2.UI
{
    public enum SpeechUiState
    {
        Initializing,
        Ready,
        Listening,
        Processing,
        Heard,
        NoSpeechDetected,
        EmptyAudioBuffer,
        EmptyTranscript,
        Error
    }

    [Serializable]
    public struct SpeechCaptureDiagnostics
    {
        public string stage;
        public bool micReady;
        public string deviceName;
        public float recordingMs;
        public int samples;
        public float rms;
        public float peak;
        public int pcmBytes;
        public int wavBytes;
        public bool isNearSilent;
        public bool isTooShort;
        public bool isEmptyAudioBuffer;
        public bool isZeroSignal;
        public string note;

        public string ToLogString()
        {
            return
                $"[SpeechDebug] stage={stage} micReady={micReady} device=\"{deviceName}\" " +
                $"recordingMs={recordingMs:0} samples={samples} rms={rms:0.0000} peak={peak:0.0000} " +
                $"pcmBytes={pcmBytes} wavBytes={wavBytes} isNearSilent={isNearSilent} " +
                $"isTooShort={isTooShort} isEmptyAudioBuffer={isEmptyAudioBuffer} isZeroSignal={isZeroSignal} note=\"{note}\"";
        }
    }
}
