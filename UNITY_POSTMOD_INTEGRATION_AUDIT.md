# DreamCodeVR2 post-modification integration audit

Authority: current Unity source, serialized scene/project settings, package cache and current `Server local` source. This is a static audit only: Unity, a build and a Quest device were not run.

## CORRECT CURRENT CONFIGURATION

### Deployment values serialized by Unity

`Assets/DreamCodeVR2/ExperimentalAuthoring/Resources/StudyConfiguration.asset` is loaded by `VerticalSliceRuntimeBootstrap` and assigned to `ExperimentConditionManager.studyConfiguration` and `AuthoringActionExecutor.studyConfiguration`.

| Setting | Serialized value | Static use |
| --- | --- | --- |
| `ubiqServerHost` | `130.136.2.161` | Inspector/configuration value only; no Unity connection code reads it. |
| `ubiqServerPort` | `50000` | Inspector/configuration value only; no Unity connection code reads it. |
| `researcherControlBaseUrl` | `http://130.136.2.161:50001` | Used by `DreamCodeVR2ResearcherControlClient.BaseUrl`. |
| Research-control fallback | `http://130.136.2.161:50001` | Used only if no `StudyConfiguration` is assigned. |

The only enabled build scene is `Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity`. `DreamCodeVR2_RuntimeServices` is present in the scene; `ExperimentalAuthoringRuntime` is created/fetched at runtime by `VerticalSliceRuntimeBootstrap`. The only condition enum members are C1 `VoiceCommandBaseline`, C2 `PlayerAuthoring`, C3 `DynamicStorytelling`.

The current server configuration has TCP Room Server port `50000`, WSS port `50001`, status port `50002`, and room GUID `6765c52b-3ad6-4fb0-9030-2c9a05dc4731`. Unity’s `50001` value is therefore the HTTP Research Control target specified for this deployment, not an Ubiq WSS setting.

## CONFIGURATION MISMATCHES

**Count: 3.**

1. Unity serializes `130.136.2.161:50000` but no current Unity `ConnectionDefinition`, `SimpleConnection`, `ConnectionManager`, scene endpoint, or runtime connection code consumes these fields. It does not statically prove an Ubiq TCP connection to that target.
2. The scene contains the package `Ubiq.Rooms.RoomJoiner`, configured to join on start and connection change. Its serialized `RoomGuid` asset reference is GUID `7e7f5f8a0f874c9fb5c3e8dfb7e2a604`; no matching asset was found in the current workspace/package cache, so its actual room GUID cannot be statically resolved. The local `Assets/RoomJoiner.cs` is a different, non-scene script and is not the package component serialized in the scene.
3. Android has `ForceInternetPermission: 0`, no project-owned `AndroidManifest.xml`/network-security configuration was found, and no cleartext setting was found. HTTP access to `http://130.136.2.161:50001` is therefore not statically guaranteed.

`localhost` remains in server Room Server configuration (`roomserver.uri`) because the server process connects to the Room Server it starts locally. It is not a current Unity endpoint. No active Unity occurrence of `127.0.0.1`, `3004`, `session_configuration`, NetworkId 94, Roslyn, or `mixed_initiative` was found. The current server code/config still contains legacy `outputNetworkId: 94`; that is outside Unity, but conflicts with a 94-free deployment if this legacy app is launched.

## STT CONTRACT

Unity `MicrophoneCapture` registers NetworkId 98. It requests an Android microphone permission at runtime, starts `Microphone.Start(null, true, microphoneBufferSeconds, sampleRate)`, and is currently serialized in the scene with `sampleRate: 16000`, `microphoneBufferSeconds: 1`, `sendToServer: true`, `gain: 1`, `triggerThreshold: 0.75`, and `releaseDebounceSeconds: 0.15`.

Input is the left XR controller selected by `InputDeviceCharacteristics.Left | Controller`. A press is `CommonUsages.triggerButton == true` or analog `CommonUsages.trigger >= triggerThreshold`; release is delayed by `releaseDebounceSeconds`.

For each poll, Unity reads all pending microphone samples, downmixes any microphone channel count to mono, applies gain, and encodes signed little-endian PCM16. Chunk size is variable: `pendingSampleCount * 2` bytes, with no fixed application-level chunk size. It prefixes every 98 packet with UTF-8 `RoomClient.Me.uuid` (expected UUID length is 36 bytes, but 98 does not validate the length).

Control payloads are exact UTF-8 strings `__STT_CONTROL__:start` and `__STT_CONTROL__:stop`. Start is sent after `SetRecording(true)` changes state; on stop Unity first forces pending samples to send, then sends the stop control payload.

The current server’s NetworkId-98 receiver strips the first 36 bytes as peer UUID, identifies a payload of 64 bytes or less starting with `__STT_CONTROL__:`, and uses `start`/`stop` to control recording. It otherwise buffers PCM. Server defaults are `16000 Hz`, `1` channel, `16` bit, explicit recording required, minimum `300 ms`, maximum `20000 ms`; it packages WAV and posts multipart data to `http://130.136.2.161:50101/stt/transcribe` unless overridden by `STT_HTTP_URL` or other `STT_*` environment variables.

**STT mismatch count: 0 static mismatches.** Unity downmixes to mono PCM16 at 16 kHz and control strings match. The actual microphone frequency and server environment overrides still require runtime verification.

## 98-102 CONTRACT

### 98 — microphone/STT

Framing is `[UTF-8 peer UUID][control or PCM16 mono audio]`. See STT contract above.

### 99 — InteractionContext

`InteractionContextTransmitter` sends `[UTF-8 peer UUID][JSON]`; it does not validate UUID length. JSON is an `InteractionContextSnapshot` with `peer`, `timestamp_unix_ms`, `scene_version`, `active_selection`, `pointed_object`, `pointed_world_position`, `last_action` (currently null), `pending_confirmation`, `current_task_id`, `recently_interacted_object_ids`, `object_currently_held` (currently false), `last_incorrect_attempt`, and `hint_count`.

It sends on microphone recording start/stop by default; periodic send is disabled in the scene configuration. Object summaries contain `id`, display/unity name, semantic types, labels, description, position/rotation/scale, active/editable state and parent ID.

### 100 — SceneContext

`SceneContextTransmitter` validates a **36-byte** peer UUID and sends `[36-byte UTF-8 peer UUID][JSON]`. The JSON `SceneContextPacket` is type `SceneContextUpdate` and contains `schema_version` (current default `0`), `peer`, `timestamp_unix_ms`, `scene_version`, `scene_name`, and sorted editable-object summaries. Each emitted object can include ID, names, labels/types, description, transform, active/editable/parent, materials, components, available operations, editable properties, allowed behaviors, quest-critical/semantic state, runtime/behavior state, anchor/held/affordance state, action/task origin, predefined commands, editable affordances and task protection.

The active vertical slice references `table_drawer_001`, `key_001`, `lock_001`, `door_001` and (from the researcher panel) `lamp_001`.

### 101 — server to Unity

Accepted canonical `type` values are `PredefinedCommandRejected`, `PredefinedCommandProposal`, `PredefinedCommandExecutionRequest`, `AuthoringRejected`, `AuthoringProposal`, `AuthoringExecutionRequest`, `AuthoringStatus`, `AuthoringUndoRequest`, `NextTaskGenerated`, and `NextTaskActivationRequest`.

`AuthoringProposal`/`AuthoringExecutionRequest` use `action`; `AuthoringUndoRequest` uses `action_id`; predefined proposal/execution uses `command` and `command_id`; C3 generated/activation uses `task`/`task_id`. `AuthoringAction` includes canonical `action_id`, `target_object_id`, `secondary_object_id`, `parameters` and `api_call`; `PredefinedVoiceCommand` includes canonical `command_id`, `target_object_id`, `intent`, `preset_id`, `secondary_object_id`, `peer_uuid`, `schema_version`. No legacy 101 type strings are active in the Unity protocol client.

### 102 — Unity to server

Every send is flat JSON after the UTF-8 peer UUID prefix. Current sent types are:

- `AuthoringAck`: `action_id`, `status` (`applied`, `failed`, or `undone`), `detail`.
- `PredefinedCommandAck`: `command_id`, `status` (`applied` or `failed`), `detail`.
- `ExperimentStateEvent`: `event`, `task_id`; permitted events are `task_started`, `task_completed`, `incorrect_attempt`, `hint_requested`, `session_completed`.
- `NextTaskAck`: only on success, `status: "activated"`, `task_id`.

No remaining Unity `{ type, body }` wrapper or legacy outbound type string was found.

**Protocol mismatch count: 3.**

1. C3 `success_conditions` deserializes as `RuntimeSuccessCondition[]` objects (`type`, `object_id`, `anchor_id`, `value`, `children`); a server contract that sends string conditions is incompatible.
2. Failure paths call `SendNextTaskAck(..., false, ...)`, but that method intentionally emits nothing for failures. There is no failure `NextTaskAck` on the wire.
3. The current `Server local` app still creates/uses output NetworkId 94 for legacy code generation. Unity is 94-free, but the server app is not.

## SESSION FLOW

Condition buttons call `PrepareResearcherCondition`, which changes only `selectedCondition`. START obtains the current peer UUID, calls health, then calls start or restart depending on `conditionManager.sessionStarted`. The request body is `{ "condition": "voice_command_baseline|player_authoring|dynamic_storytelling", "peerUUID": "..." }`.

On a non-error response with `session_id`, Unity resets local playthrough, assigns `conditionManager.sessionId`, calls `StartSession(false)`, sends a SceneContext snapshot, then GETs status. READY requires returned `session_id` to equal the local value and returned `condition` to equal `ServerCondition(conditionManager.condition)`. The HTTP API is therefore the session authority in code. END and RESET call peer-scoped POST endpoints and reset locally only on `ended: true` / `reset: true`.

Routes are:

- GET `/api/authoring/dev/health`
- GET `/api/authoring/dev/status/{peerUUID}`
- POST `/api/authoring/dev/session/start`
- POST `/api/authoring/dev/session/restart`
- POST `/api/authoring/dev/session/end`
- POST `/api/authoring/dev/session/reset`

Response parsing is `JsonUtility` fields `session_id`, `peer_uuid`, `condition`, `error`, `ended`, `reset`, `ready`, `healthy`. Request casing is exactly `peerUUID`. There is no NetworkId-102 `session_configuration` send. SceneContext is refreshed after local start and after authoring execution/undo; C3 also refreshes before the task-completed event.

## C1, C2 AND C3 FLOWS

**C1:** left-trigger PTT produces 98 control/audio traffic. A predefined proposal is displayed; `Confirm` records telemetry only and does not locally execute. Execution occurs only after a `PredefinedCommandExecutionRequest`; it invokes `PredefinedVoiceCommandExecutor` and emits `PredefinedCommandAck`.

**C2:** an `AuthoringProposal` is shown but not locally applied. Only `AuthoringExecutionRequest` maps `operation` to an authoring action, executes `AuthoringActionExecutor`, sends `AuthoringAck`, then refreshes SceneContext. Undo calls the undo manager and sends `AuthoringAck` with `undone`/`failed`.

**C3:** completion sets `WaitingForNextTask`, updates UI, refreshes SceneContext and emits `ExperimentStateEvent/task_completed`. `NextTaskGenerated` is stored. `NextTaskActivationRequest` fetches it by ID, validates it through `RuntimeTaskValidator`, activates the task, updates UI and emits successful `NextTaskAck`. The structured/string success-condition incompatibility above remains the decisive static blocker.

## XR UI AND PTT INTERACTION

The bootstrap creates one `EventSystem` plus `StandaloneInputModule` only when none exists, and adds an `XRUIRaycaster` child only to hands that do not already have one. The researcher panel creates a world-space Canvas with `GraphicRaycaster` and `XRUICanvas`. Panel toggle is F5 or a one-second left-Y hold using Unity XR `CommonUsages.secondaryButton`.

`ResearcherUiInteractionState` is true while the panel is open. `MicrophoneCapture` explicitly checks this state before reading/continuing participant PTT, so trigger input is reserved for researcher UI while the panel is visible. With the panel closed, PTT also requires the authoritative researcher session READY state.

## QUEST NETWORKING

The project has Oculus XR package `4.5.4`, Android minimum SDK 29, arm64 architecture, IL2CPP backend, and runtime microphone permission request. The only build scene is the Escape Room testbed.

Static certainty: Unity source uses `UnityWebRequest` for Research Control and Ubiq TCP is intended for port 50000. Device verification is required for generated INTERNET permission, Android cleartext HTTP to port 50001, DNS/IP reachability, firewall access, actual TCP connection to port 50000, microphone permission, and Quest controller/UI behavior.

## MUST FIX BEFORE MANUAL BUILD

1. Bind the serialized `ubiqServerHost`/`ubiqServerPort` to the actual Ubiq client connection owner, or restore/configure the missing active `ConnectionDefinition`/connection asset.
2. Restore or assign a resolvable RoomGuid asset to the package RoomJoiner and confirm it is the intended room.
3. Provide Android INTERNET/cleartext configuration appropriate to the HTTP Research Control endpoint.
4. Resolve the C3 `success_conditions` representation before relying on server-driven C3.
5. Decide and implement the expected failure acknowledgement behavior for `NextTaskAck`.
6. Remove/disable the server’s legacy NetworkId-94 code-generation path for a fully 94-free deployment.
7. Make PTT explicitly consult researcher-UI state if simultaneous trigger consumption must be prevented.

## MUST VERIFY IN UNITY PLAYMODE

1. Exactly one NetworkScene/RoomClient connection is created and targets `130.136.2.161:50000`.
2. The RoomJoiner has the intended resolvable RoomGuid and joins after connection.
3. Research Control health/start/restart/status/end/reset succeeds at `http://130.136.2.161:50001` with the exact DTOs.
4. EventSystem count, per-hand XRUIRaycaster count, panel ray hover/click and no duplicate event processing.
5. C1/C2/C3 canonical 101/102 exchanges, including malformed/failure paths and C3 conditions.
6. 98 control ordering, PCM duration/chunking and 99/100 payload reception.

## MUST VERIFY ON QUEST

1. Android networking: INTERNET, cleartext Research Control HTTP and TCP Ubiq firewall reachability.
2. Microphone permission and actual capture frequency/channel behavior.
3. Left-trigger press/release, debounce, PTT suppression while panel is visible, and panel-close voice-test procedure.
4. LAN room join, peer UUID, researcher session lifecycle, and all C1/C2/C3 participant flows.
