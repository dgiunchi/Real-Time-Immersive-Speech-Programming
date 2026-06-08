# Unity Client Audit

## Scope

This audit covers the Unity client side of the DreamCodeVR / Ubiq project for the Context Bridge / GateContext enrichment milestone. It identifies where to capture and transmit:

- `active_selection`
- `pointed_object`
- `pointed_world_position`
- `last_action`
- `pending_confirmation`
- `scene_version`

`last_action` and `pending_confirmation` can remain `null` in the first implementation. This audit does not recommend implementing SceneAPI, BehaviorAPI, command decomposition, validation, undo, or generated behavior execution.

## Project Structure Overview

The Unity project lives under `Unity/`.

- `Unity/Assets/` contains the project scripts, demos, models, materials, Ubiq shader fixes, Roslyn runtime assets, XR assets, and legacy Ubiq dependencies.
- `Unity/Assets/Demos/DynamicCompiler/DynamicCompiler.unity` is the only enabled build scene in `Unity/ProjectSettings/EditorBuildSettings.asset`.
- `Unity/Assets/Demos/ConversationalAgent/Conversational Agent.unity`, `Unity/Assets/Demos/TextureGeneration/`, `Unity/Assets/Demos/Transcription/`, and `Unity/Assets/Demos/StoryTelling3D/` are demo scenes/assets.
- `Unity/Assets/LegacyUbiqDependencies/` contains runtime XR interaction, teleport, UI raycaster, and desktop/XR controller code.
- `Unity/Assets/RoslynCSharp/` contains the runtime compiler package used by the code generation demo.
- `Unity/Packages/manifest.json` declares `com.ucl.ubiq` from `https://github.com/UCL-VR/ubiq.git#upm-unity-v1.0.0-pre.16`, the Ubiq WebRTC fork, Oculus XR, and Newtonsoft JSON.

Core DreamCodeVR scripts currently sit directly in `Unity/Assets/` rather than under a domain namespace/folder. For the next milestone, an additive folder such as `Unity/Assets/DreamCodeVR2/ContextBridge/` is the cleanest place to add context scripts.

## Push-To-Talk Implementation

Push-to-talk is split across two paths:

- `Unity/Assets/MicrophoneCapture.cs`
  - XR left-trigger recording is handled in `UpdateRecordingFromLeftTrigger()`.
  - It checks left controller trigger button/value with `UnityEngine.XR.InputDevices`.
  - Press/release state changes call `SetRecording(bool recording)`.
  - Relevant lines: `MicrophoneCapture.cs:55-64`, `MicrophoneCapture.cs:110-150`, `MicrophoneCapture.cs:153-178`.

- `Unity/Assets/DesktopServerMicAudioController.cs`
  - Desktop/editor push-to-talk uses the spacebar.
  - `Input.GetKeyDown(KeyCode.Space)` calls `listenForCommand(true)`.
  - `Input.GetKeyUp(KeyCode.Space)` calls `listenForCommand(false)`.
  - `listenForCommand` sets microphone gain and calls `microphoneCapture.SetRecording(listen)`.
  - Relevant lines: `DesktopServerMicAudioController.cs:24-57`.

Selection scripts also alter mic gain while controller buttons are active:

- `Unity/Assets/SelectRay.cs:52-67`
- `Unity/Assets/SelectObjectRay.cs:54-64`, `SelectObjectRay.cs:113-118`

Those gain changes are separate from the explicit recording state in `MicrophoneCapture`.

## Where Audio Is Sent To The Server

Audio capture and transmission are owned by `Unity/Assets/MicrophoneCapture.cs`.

- The Ubiq service id is `new NetworkId(98)` at `MicrophoneCapture.cs:23`.
- The component registers with Ubiq via `NetworkScene.Register(this, networkId)` at `MicrophoneCapture.cs:41-44`.
- `SendPendingMicrophoneSamples()` streams samples only while recording, unless force-flushing on stop.
- `SendSamples(...)` converts Unity float samples to mono 16-bit little-endian PCM at the configured sample rate.
- `SendPayloadToServer(byte[] payload)` prefixes each payload with `RoomClient.Me.uuid`, rents a `ReferenceCountedSceneGraphMessage`, copies UUID bytes first, copies payload bytes after, then calls `context.Send(message)`.
- Relevant lines: `MicrophoneCapture.cs:180-227`, `MicrophoneCapture.cs:230-267`, `MicrophoneCapture.cs:284-322`.

Audio format is currently:

- sample rate: `16000`
- channels sent: mono mixdown
- sample format: signed 16-bit PCM, little-endian
- packet body: 36-byte peer UUID string followed by PCM bytes or STT control text

The server-side local app reads this through `new MessageReader(this.scene, 98)`, then splits `data.message.subarray(0, 36)` as peer UUID and `data.message.subarray(36)` as control/audio payload.

## Where STT Control Messages Are Sent

`Unity/Assets/MicrophoneCapture.cs` defines:

- `RecordingStartMessage = "__STT_CONTROL__:start"` at `MicrophoneCapture.cs:12`
- `RecordingStopMessage = "__STT_CONTROL__:stop"` at `MicrophoneCapture.cs:13`

`SetRecording(bool recording)` sends:

- start on transition to recording
- stop on transition out of recording, after force-flushing pending microphone samples

Relevant lines:

- `MicrophoneCapture.cs:153-178`
- `MicrophoneCapture.cs:269-281`
- `MicrophoneCapture.cs:324-327`

The important extension point is the recording transition in `SetRecording`. Context should be captured near the start/stop control messages, but the audio packet format and control strings should remain unchanged.

## Ubiq NetworkId Usage

The project uses fixed Ubiq `NetworkId` values as service channels:

- `93`: selection/material target messages in `SelectRay`; also declared in `SelectObjectRay`.
- `94`: code generation / conversational agent response channel in `CodeGenerationManager` and `ConversationalAgentManager`.
- `95`: story teller manager.
- `96`: text generation collector.
- `97`: texture generation collector.
- `98`: STT/audio channel in `MicrophoneCapture`; transcription collector also registers to `98`.

Custom scripts register with `NetworkScene.Register(this, networkId)` and implement `ProcessMessage(ReferenceCountedSceneGraphMessage data)` for inbound messages.

Important details:

- `MicrophoneCapture` sends raw bytes with `context.Send(message)`.
- `SelectRay` sends raw bytes with UUID prefix plus a string payload.
- Collectors usually parse JSON from server using `data.FromJson<Message>()`.
- In current custom Unity scripts, `NetworkId` is used for service routing, not as a stable per-object AI identity.

## Existing Object Selection

There are two selection-related scripts:

### `SelectRay`

`Unity/Assets/SelectRay.cs` is aimed at texture/material targeting.

- Requires a `LineRenderer`.
- Registers on `NetworkId(93)`.
- Listens to parent `IPrimaryButtonProvider.PrimaryButtonPress`.
- Runs a straight `Physics.Linecast` from controller transform forward while `isSelecting` is true.
- Attempts to resolve a mesh submesh/material.
- Sends `"<objectName>:<materialName>"` to the server with a UUID prefix.

Relevant lines:

- `SelectRay.cs:15-30`
- `SelectRay.cs:48-63`
- `SelectRay.cs:75-90`
- `SelectRay.cs:93-139`
- `SelectRay.cs:142-171`

This is useful precedent for context transmission, but it only provides material-level selection strings and does not maintain a structured context snapshot.

### `SelectObjectRay`

`Unity/Assets/SelectObjectRay.cs` is aimed at code-generation object targeting.

- Requires a `LineRenderer`.
- Declares `NetworkId(93)` but does not register a `NetworkContext` or send messages.
- Clones selector objects onto other `HandController`s.
- Runs `ComputeStraightRay()` every frame.
- Uses `Physics.Linecast` from controller transform forward.
- Treats only objects tagged `game` as selected.
- Writes the hit object into `codeGenerationManager.targetObject`.

Relevant lines:

- `SelectObjectRay.cs:15-29`
- `SelectObjectRay.cs:47-72`
- `SelectObjectRay.cs:74-95`
- `SelectObjectRay.cs:126-141`
- `SelectObjectRay.cs:144-200`
- `SelectObjectRay.cs:203-214`

This is the strongest existing source for `active_selection` and `pointed_object`, but its current state is not structured enough for the server. It also clears `CodeGenerationManager.targetObject` on miss, while `selectedObject` is only the current local hit.

## Existing Controller Raycast / Pointing

Controller pointing exists in:

- `SelectRay.ComputeStraightRay()`: linecast from controller transform to `transform.position + transform.forward * range`.
- `SelectObjectRay.ComputeStraightRay()`: same linecast pattern, always enabled in current code.
- `LegacyUbiqDependencies/RuntimeXR/UIInteraction/XRUIRaycaster.cs`: UI raycasting.
- `LegacyUbiqDependencies/RuntimeXR/UIInteraction/DesktopUIRaycaster.cs`: desktop UI raycasting.
- `LegacyUbiqDependencies/RuntimeXR/Teleporting/TeleportRay.cs`: teleport raycasting.
- `LegacyUbiqDependencies/RuntimeXR/XRPlayerController.cs` and `DesktopPlayerController.cs`: locomotion/interaction raycasts.

For Context Bridge v0.1, the cleanest path is not to reuse UI or teleport raycasters. Add an `InteractionContextProvider` that can either:

- reference existing `SelectObjectRay` instances and read their latest selected object, or
- perform its own non-invasive raycast from configured controller/hand transforms.

The second option avoids changing current selection behavior and lets the provider store `pointed_world_position`.

## Object IDs And Names Useful For AI

Current useful object metadata:

- `GameObject.name` is used by `SelectRay` and `SelectObjectRay`.
- `Renderer.materials[i].name` is used by `SelectRay` for material targeting.
- Unity tag `game` exists in `ProjectSettings/TagManager.asset` and is used by `SelectObjectRay`.
- `CodeGenerationManager.targetObject` stores the currently selected object for generated code execution.

Missing for AI context:

- no stable AI object id
- no authored display name separate from `GameObject.name`
- no semantic labels/categories
- no consistent object registry
- no scene version counter
- no structured object summary serialization

Therefore `AIEditableObject` and `SceneRegistry` should be introduced before sending context to the Intent Gate.

## Where Context Snapshot Should Be Captured

Recommended capture points:

1. Capture immediately before sending `__STT_CONTROL__:start`.
   - Best place to align with speech intent start.
   - Existing line: `MicrophoneCapture.SetRecording(true)` before `SendControlMessage(...)`.

2. Capture immediately before or after sending `__STT_CONTROL__:stop`.
   - Best place to capture final pointing/selection state at utterance end.
   - Existing line: `MicrophoneCapture.SetRecording(false)` after forced audio flush and before/around `SendControlMessage(...)`.

3. Optionally capture periodically while recording, at a low rate such as 5-10 Hz.
   - Useful if user points at one object, speaks, then points at another during the utterance.
   - This can be done by a separate context transmitter observing its own `recording` state, without changing audio format.

4. Capture on selection change.
   - Existing candidate: `SelectObjectRay.SetCurrentSelection(GameObject obj)`.
   - Prefer a future event or provider-owned polling path over direct coupling for the first bridge.

Recommended first implementation:

- Add `InteractionContextProvider` under `Assets/DreamCodeVR2/ContextBridge/`.
- Let it independently raycast from configured hand/controller transforms and consult `SceneRegistry`.
- Add a lightweight `InteractionContextTransmitter` that sends snapshots on explicit calls and optionally at a low periodic rate while recording.
- Add only a minimal optional hook from `MicrophoneCapture.SetRecording(...)` to request context send on start/stop, or wire the hook externally from a companion component if avoiding any push-to-talk script edit is preferred.

## Where Context Message Should Be Sent To The Server

Do not reuse `NetworkId(98)` because it is the audio/STT channel. Reusing it would risk confusing the current STT parser, which treats every non-control payload as PCM audio.

Recommended new channel:

- `NetworkId(99)`
- sender component: `InteractionContextTransmitter`
- packet body: 36-byte `RoomClient.Me.uuid` prefix followed by UTF-8 JSON
- message type: `InteractionContextUpdate`

This mirrors existing UUID-prefix packets while separating context from audio. On the server, a future non-STT `MessageReader(scene, 99)` can parse the JSON and enrich GateContext before intent classification.

The sender should use the same Ubiq primitives already used by `MicrophoneCapture`:

- find `RoomClient` through `NetworkScene.Find(this)?.GetComponentInChildren<RoomClient>()`
- register with `NetworkScene.Register(this, new NetworkId(99))`
- rent a `ReferenceCountedSceneGraphMessage`
- copy UUID bytes first, JSON bytes second
- call `context.Send(message)`

