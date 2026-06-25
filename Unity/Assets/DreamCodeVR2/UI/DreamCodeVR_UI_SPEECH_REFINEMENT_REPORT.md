# DreamCodeVR UI Speech Refinement Report

## Files Added
- `Assets/DreamCodeVR2/UI/DreamCodeVRSpeechTypes.cs`
- `Assets/DreamCodeVR2/UI/DreamCodeVRSpeechStatusBridge.cs`
- `Assets/DreamCodeVR2/UI/DreamCodeVR_UI_SPEECH_REFINEMENT_REPORT.md`

## Files Modified
- `Assets/DreamCodeVR2/UI/DreamCodeVRAuthoringUIController.cs`
- `Assets/DreamCodeVR2/UI/DreamCodeVRAuthoringUIBootstrap.cs`
- `Assets/MicrophoneCapture.cs`

## Speech State Model
- Added `SpeechUiState` with:
- `Initializing`
- `Ready`
- `Listening`
- `Processing`
- `Heard`
- `NoSpeechDetected`
- `EmptyAudioBuffer`
- `EmptyTranscript`
- `Error`
- Compact UI now reflects speech state instead of always showing `waiting`.

## Microphone Events / Wiring
- Kept existing `MicrophoneCapture.RecordingStateChanged(bool)` wiring.
- Added `MicrophoneCapture.DiagnosticsUpdated`.
- Added `DreamCodeVRSpeechStatusBridge` to subscribe to:
- microphone recording start/stop
- microphone diagnostics updates
- `TranscriptionCollector.TranscriptReceived`
- UI controller now reads speech state from the bridge instead of inferring it from transcript text alone.

## Empty Transcription Diagnostics
- Added concise `SpeechDebug` diagnostics with:
- microphone readiness
- device name
- recording duration
- sample count
- RMS
- peak
- PCM byte count
- near-silent detection
- too-short detection
- empty-audio-buffer detection
- Empty or whitespace transcript now logs:
- `[SpeechDebug] empty transcript received`

## Inspect Card Changes
- Removed labels from the Inspect card.
- Removed possible actions from the Inspect card.
- Inspect card now shows only:
- display name
- object id
- short description

## UI Behavior Changes
- Compact card still stays visible.
- Compact speech line now transitions across:
- `Initializing`
- `Listening`
- `Processing`
- `Heard`
- empty/error-like outcomes
- Speech detail card now appears only while speech state is relevant.
- Heard and error-like states return to `Ready` after a timeout.

## Manual Test Checklist
- start scene
- verify UI shows `Speech: Ready`
- press trigger / start recording
- verify UI shows `Speech: Listening...`
- release trigger / stop recording
- verify UI shows `Speech: Processing...`
- speak `What is this?`
- verify UI shows `Heard: "What is this?"`
- try very short trigger press
- verify UI shows `No speech detected` or `Empty audio buffer`
- reproduce Build And Run issue if possible
- check `SpeechDebug` logs for micReady, recordingMs, samples, rms, peak, pcmBytes, wavBytes
- verify ContextBridge still sends pointed_object
- verify SceneContext still sends objects
- verify STT still reaches server when audio is valid
- verify Inspect card no longer shows labels
- verify Inspect card no longer shows possible actions

## Known Risks / Limitations
- The transport still sends raw PCM, so `wavBytes` is reported as `0`.
- Near-silent and too-short detection are heuristic and may need tuning on-device.
- The first post-build microphone readiness issue may still depend on Android/runtime initialization timing outside Unity script control.
- This work adds diagnostics and UI clarity, but does not change server-side STT behavior.

## Next Recommended Steps
- Tune silence thresholds on the actual target headset/device.
- Capture a failing Build And Run session and compare `SpeechDebug` lines against a successful one.
- If the first-recording issue persists, consider a lightweight explicit mic warm-up UX step before the first experiment utterance.
