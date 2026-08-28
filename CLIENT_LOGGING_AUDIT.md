# Client logging audit

## LOGGER LOCATION

`Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/DreamCodeVR2ClientLogger.cs`; installed by `VerticalSliceRuntimeBootstrap`.

## LOG FILE FORMAT

Thread-safe JSON Lines at `Application.persistentDataPath/DreamCodeVR2/logs/client_<UTC>_run.jsonl`, auto-flushed and explicitly flushed for errors, pause, focus loss, quit, and logger disposal.

## EVENTS COVERED

Boot, configuration, Ubiq/room/peer, researcher HTTP, session lifecycle, PTT/microphone, authoring, C1/C2/C3, Unity errors, and researcher test marks are instrumented.

## STT LOGGING

NID 98 records only control kind and byte counts. Audio events record samples, byte count, estimated duration, and cumulative bytes; no PCM is persisted.

## NID98-102 LOGGING

NID 98: `NID98_SEND`; NID 99: `INTERACTION_CONTEXT_SENT`; NID 100: `SCENE_CONTEXT_SENT`; NID 101: `NID101_RECEIVED`; NID 102: `NID102_SENT`.

## SESSION CORRELATION

Logger correlation is updated from Ubiq peer availability and researcher session start, and carries peer UUID, server session ID, and canonical condition in subsequent entries.

## ERROR CAPTURE

Unity threaded log callbacks mirror log/warning/error/assert/exception messages. Warning/error counters are shown in the researcher panel.

## QUEST RETRIEVAL

Current package identifier: `com.VARLab.DreamCodeVR2`. Use `adb shell run-as` / `adb exec-out run-as` as documented in `CLIENT_LOGGING_GUIDE.md`.

## PRIVACY

No raw audio, tokens, keys, or complete network payloads are logged. `logTranscripts` is configured false by default.

## STATIC MUST FIX

0 known static blockers. Existing `RoomJoiner.cs` confirms the installed Ubiq event signatures used by the logger. No Unity batch build was run by design.

## MANUAL QUEST VERIFICATION

Build/install manually, complete one C1/C2/C3 run, open/close researcher panel while holding PTT, trigger one intentional warning, suspend/resume Quest, retrieve the JSONL file, and verify session/peer correlation plus the expected NID 98–102 sequence.
