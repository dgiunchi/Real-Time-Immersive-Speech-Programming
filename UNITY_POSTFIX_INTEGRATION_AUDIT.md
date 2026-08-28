# DreamCodeVR2 postfix integration audit

This audit reflects current source and serialized assets after the integration fixes. Unity batch mode, Editor compilation and device execution were not run.

## UBIQ ENDPOINT

The active owner is `VerticalSliceRuntimeBootstrap.EnsureUbiqTcpConnection`. At `RuntimeInitializeLoadType.AfterSceneLoad`, before normal component `Start` methods, it loads the serialized `StudyConfiguration`, obtains the scene `NetworkScene`, and—when it has no connection—creates a supported Ubiq `ConnectionDefinition` with `ConnectionType.TcpClient`, then calls `NetworkScene.AddConnection(Connections.Resolve(definition))`.

The source uses the installed Ubiq public runtime API, without reflection:

```text
StudyConfiguration.ubiqServerHost = 130.136.2.161
StudyConfiguration.ubiqServerPort = 50000
ConnectionType = TcpClient
```

The owner deliberately does not create a WSS connection on port 50001. That port is the separate Researcher API target in this deployment. If a NetworkScene already has a connection, the bootstrap leaves it unchanged to avoid duplicate Ubiq connections; this behavior requires Play Mode verification of the resulting endpoint.

## ROOM GUID

The scene uses the package `Ubiq.Rooms.RoomJoiner` with `joinOnStart` and `joinOnConnectionChange` enabled. Its previously unresolved serialized GUID `7e7f5f8a0f874c9fb5c3e8dfb7e2a604` is now backed by:

`Assets/DreamCodeVR2/ExperimentalAuthoring/Resources/DreamCodeVR2RoomGuid.asset`

The asset is an installed-package `Ubiq.Rooms.RoomGuid` and contains:

```text
6765c52b-3ad6-4fb0-9030-2c9a05dc4731
```

This matches the current server room GUID. No scene RoomGuid reference was otherwise changed.

## RESEARCHER API

The active configuration and fallback both resolve to exactly:

```text
http://130.136.2.161:50001
```

`StudyConfiguration.asset` is loaded by the bootstrap and assigned to the condition manager. `DreamCodeVR2ResearcherControlClient.BaseUrl` prefers this serialized value; its fallback is the same address. No active Unity `3004`, `localhost`, or `127.0.0.1` fallback remains.

Routes and payloads are unchanged:

| Request | Route | Body |
| --- | --- | --- |
| GET | `/api/authoring/dev/health` | none |
| GET | `/api/authoring/dev/status/{peerUUID}` | none |
| POST | `/api/authoring/dev/session/start` | `{ condition, peerUUID }` |
| POST | `/api/authoring/dev/session/restart` | `{ condition, peerUUID }` |
| POST | `/api/authoring/dev/session/end` | `{ peerUUID }` |
| POST | `/api/authoring/dev/session/reset` | `{ peerUUID }` |

`peerUUID` casing is preserved. Response parsing remains `session_id`, `peer_uuid`, `condition`, `error`, `ended`, `reset`, `ready`, `healthy`.

## STT CLIENT CONTRACT

Unity does not call STT HTTP. `MicrophoneCapture` uses NetworkId 98 and sends `[UTF-8 RoomClient.Me.uuid][payload]` to the DreamCodeVR2 server. It sends exact controls `__STT_CONTROL__:start` and `__STT_CONTROL__:stop`; audio is pending microphone samples downmixed to mono, signed little-endian PCM16, current serialized sample rate 16000 Hz.

The current server receiver strips the first 36 bytes as peer UUID and expects the same control strings. Its defaults package PCM as 16000 Hz / 1 channel / 16 bit WAV and POST it to `http://130.136.2.161:50101/stt/transcribe`, subject to server-side `STT_*` environment overrides. No direct Unity requirement for port 50101 was added.

## PTT/UI ISOLATION

`MicrophoneCapture.UpdateRecordingFromLeftTrigger` now checks `ResearcherUiInteractionState` before reading/continuing left-controller trigger PTT. When the researcher panel is open:

- no trigger press starts a recording;
- if a recording was active, `SetRecording(false)` finalizes pending audio and emits exactly one normal stop control packet;
- later UI trigger clicks cause no start/stop traffic because PTT input processing returns early.

The panel calls `ResearcherUiInteractionState.Open` as it opens. That saves microphone gain, stops an active recording once, and mutes it. Closing restores the saved gain and reenables normal PTT processing. This is explicit state gating, not gain-only suppression. PTT source remains the left XR controller `CommonUsages.triggerButton` or analog `CommonUsages.trigger >= triggerThreshold`; panel toggle is a one-second hold of left Y via Unity XR `CommonUsages.secondaryButton`.

## C3 WIRE/RUNTIME CONVERSION

101 `NextTaskGenerated` now deserializes its `task` as `ServerNextTaskDto`, whose wire field is `string[] success_conditions`. It is not deserialized directly as runtime conditions.

`NextTaskWireConverter.TryConvert` is the explicit boundary. It accepts only canonical non-empty `interact:<object-id>` values and converts each to the existing allow-listed runtime condition:

```text
interact:key_001
-> RuntimeSuccessCondition { type = "OBJECT_GRABBED", object_id = "key_001" }
```

`OBJECT_GRABBED` is the current `RuntimeTaskValidator` interaction condition. Malformed/unsupported conditions reject the generated task locally with a clear `[C3] rejected generated task` warning; no task is stored or activated.

Activation validation failures now only log a local `[C3] activation failed locally` warning and leave the task unactivated. No failure `NextTaskAck` is invented. Successful activation alone sends flat `NextTaskAck { status: "activated", task_id }`.

## 101/102 REGRESSION CHECK

101 retains canonical receive types: `PredefinedCommandRejected`, `PredefinedCommandProposal`, `PredefinedCommandExecutionRequest`, `AuthoringRejected`, `AuthoringProposal`, `AuthoringExecutionRequest`, `AuthoringStatus`, `AuthoringUndoRequest`, `NextTaskGenerated`, `NextTaskActivationRequest`.

102 remains flat JSON after the UTF-8 peer UUID prefix: `AuthoringAck`, `PredefinedCommandAck`, `ExperimentStateEvent`, `NextTaskAck`. No `{ type, body }` wrapper, old 101 type strings, or `session_configuration` send is active. `AuthoringProposal` only displays a proposal; only execution requests mutate Unity. C1 execution remains server-request driven and C2 remains proposal/execution separated. C3 remains `task_completed` → generated task → activation request → validator → successful `NextTaskAck`.

## ANDROID NETWORKING

Added project-owned `Assets/Plugins/Android/AndroidManifest.xml` with:

```xml
<uses-permission android:name="android.permission.INTERNET" />
<application android:usesCleartextTraffic="true" />
```

This is the minimum Android manifest configuration for the HTTP Researcher API at port 50001. It also permits normal TCP Ubiq use at port 50000. It does not add an STT 50101 requirement to Unity.

## STATIC MUST FIX

**Count: 0 Unity-client integration blockers.**

The current server’s legacy code-generation source still contains NetworkId 94, but no NetworkId 94/Roslyn/runtime-C# path is active in the DreamCodeVR2 Unity client. Removing that server-side legacy application is outside this Unity-client fix set.

## MANUAL UNITY VERIFICATION

1. Confirm the bootstrap finds the intended NetworkScene before `RoomJoiner.Start`, opens one TCP connection, and logs `130.136.2.161:50000`.
2. Confirm the resolved RoomGuid asset appears in the package RoomJoiner Inspector and joins `6765c52b-3ad6-4fb0-9030-2c9a05dc4731`.
3. Confirm Researcher API health/start/restart/status/end/reset against `http://130.136.2.161:50001`, including `peerUUID` and `session_id` values.
4. Send a canonical C3 `interact:<object-id>` task and verify `OBJECT_GRABBED` is the intended completion semantic for that server instruction.
5. Verify malformed C3 conditions and failed activations do not emit `NextTaskAck`.
6. Verify all canonical 101/102 messages, the 36-byte peer prefix, C1/C2 proposal/execution separation, SceneContext refreshes, and NetworkId-98 start/audio/stop ordering.

## QUEST VERIFICATION

1. Verify the generated Android manifest is merged, INTERNET is present, and cleartext HTTP reaches port 50001.
2. Verify TCP reachability/firewall access to `130.136.2.161:50000`, peer UUID assignment and Room join.
3. Verify left-trigger PTT works with the panel closed; with panel open, it sends no spurious STT controls; opening the panel mid-recording yields one clean finalized stop.
4. Verify microphone permission, 16 kHz mono PCM capture, all C1/C2/C3 flows, researcher UI ray interaction and Quest controller panel gesture.
