# DreamCodeVR2 client logging guide

DreamCodeVR2 writes one JSON Lines file for every client run. The logger is created by the existing runtime bootstrap and is enabled by default in `StudyConfiguration`.

## Location and filename

The directory is `Application.persistentDataPath/DreamCodeVR2/logs/`. Files are named `client_<UTC timestamp>_run.jsonl`.

For the current Android package (`com.VARLab.DreamCodeVR2`), retrieve logs with:

```powershell
adb shell run-as com.VARLab.DreamCodeVR2 ls files/DreamCodeVR2/logs
adb exec-out run-as com.VARLab.DreamCodeVR2 cat files/DreamCodeVR2/logs/<file>.jsonl > client.jsonl
```

If `run-as` is unavailable for the installed build, use Android Studio Device Explorer or enable the project-approved external-storage export workflow; do not change logging to use public storage merely for retrieval.

## Entry schema

Each line is a standalone JSON object with `timestamp`, `level`, `source`, `peer_uuid`, `session_id`, `condition`, `subsystem`, `event`, `message`, and compact `details`. Early boot entries can have correlation fields set to `null`; they are filled once Ubiq and the researcher session are available.

No microphone PCM bytes are written. `STT_AUDIO_CHUNK_SENT` and `NID98_SEND` include only byte counts, sample counts, estimated duration, running totals, and peer metadata. `logTranscripts` defaults to `false` and this logger does not add transcript content.

## Main events

- Boot/Ubiq: `APP_START`, `CONFIG_LOADED`, `UBIQ_CONNECT_START`, `PEER_UUID_AVAILABLE`, `ROOM_JOINED`.
- Researcher lifecycle: `CONDITION_SELECTED`, `SESSION_START_REQUEST`, `SESSION_READY`, `SESSION_STATUS_MISMATCH`, and reset/restart/end events.
- Speech: `PTT_*`, including `PTT_BLOCKED_NO_SESSION`, `MIC_*`, `STT_CONTROL_*`, `STT_AUDIO_CHUNK_SENT`, `NID98_SEND`, and `PROCESSING_STARTED` / `PROCESSING_RESOLVED` / `PROCESSING_TIMEOUT`.
- Context/protocol: `INTERACTION_CONTEXT_SENT` (NID 99), `SCENE_CONTEXT_SENT` (NID 100), `NID101_RECEIVED`, and `NID102_SENT`.
- Authoring/C3: `AUTHORING_EXECUTION_*`, `C1_*`, `C2_*`, and `C3_*` events.
- Researcher XR UI: `XR_UI_HOVER_ENTER`, `XR_UI_HOVER_EXIT`, `XR_UI_POINTER_DOWN`, `XR_UI_POINTER_UP`, `XR_UI_CLICK`, `RESEARCHER_BUTTON_CLICK`, and `RESEARCHER_UI_INPUT_ERROR`.

Unity logs, warnings, asserts, errors, and exceptions are mirrored as `UNITY_*`; errors include the stack trace. The researcher console exposes logging state, filename, last event, warning/error counts, and a `MARK TEST` button that emits `RESEARCHER_TEST_MARK`.

## Diagnosing a failed condition

For C1, verify a proposal/command event, `PREDEFINED_COMMAND_EXECUTION_REQUEST`, a matching acknowledgement, then scene-context send. For C2, follow `AUTHORING_PROPOSAL` → `AUTHORING_EXECUTION_START` → applied/failed event → `NID102_SENT`. For C3, follow task completion, NID 100 scene context, `C3_NEXT_TASK_GENERATED`, conversion, activation, and `NextTaskAck`.

For a connection issue, start at `CONFIG_LOADED`, then `UBIQ_CONNECT_START`, `UBIQ_CONNECTION_CREATED`, `PEER_UUID_AVAILABLE`, and room events. For speech, confirm `SESSION_READY` before PTT start/control, then audio metadata and stop/too-short diagnostics. `PTT_BLOCKED_NO_SESSION` is the expected event when no valid researcher session exists. A `PROCESSING_TIMEOUT` means no relevant NID101 response arrived within the configured timeout. Panel input is logged as `RESEARCHER_PANEL_TOGGLE_HOLD_START`, `RESEARCHER_PANEL_OPENED`, and `RESEARCHER_PANEL_CLOSED`.

The deployed endpoints are Ubiq `130.136.2.161:50000`, Researcher API `http://130.136.2.161:50001`, and server-side STT `130.136.2.161:50101`. The STT endpoint is server-side only and is not configured as a Unity client endpoint.
