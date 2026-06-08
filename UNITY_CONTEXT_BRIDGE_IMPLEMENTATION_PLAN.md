# Unity Context Bridge Implementation Plan

## Constraints

- Do not modify working push-to-talk behavior.
- Do not modify audio format.
- Do not modify STT server behavior for this milestone.
- Do not implement scene editing actions.
- Do not implement SceneAPI, BehaviorAPI, command decomposition, validation, undo, or generated behavior execution.
- Prefer additive scripts under `Unity/Assets/DreamCodeVR2/ContextBridge/`.

## Files To Add

Create:

- `Unity/Assets/DreamCodeVR2/ContextBridge/AIEditableObject.cs`
- `Unity/Assets/DreamCodeVR2/ContextBridge/SceneRegistry.cs`
- `Unity/Assets/DreamCodeVR2/ContextBridge/ObjectSummary.cs`
- `Unity/Assets/DreamCodeVR2/ContextBridge/InteractionContextSnapshot.cs`
- `Unity/Assets/DreamCodeVR2/ContextBridge/InteractionContextProvider.cs`
- `Unity/Assets/DreamCodeVR2/ContextBridge/InteractionContextTransmitter.cs`

Optional test helper:

- `Unity/Assets/DreamCodeVR2/ContextBridge/ContextBridgeDebugPanel.cs`

The debug panel is optional and should only log/display current context; it should not execute actions.

## Files To Modify

Preferred minimal modifications:

- `Unity/Assets/MicrophoneCapture.cs`
  - Add an optional serialized reference to `InteractionContextTransmitter`, or add a small event such as `RecordingStateChanged`.
  - On recording start, request a context snapshot send.
  - On recording stop, request a context snapshot send.
  - Do not change `SetRecording` semantics, mic start/stop behavior, PCM conversion, UUID prefixing, or `__STT_CONTROL__` strings.

Optional scene-only wiring:

- `Unity/Assets/Demos/DynamicCompiler/DynamicCompiler.unity`
  - Add `SceneRegistry`, `InteractionContextProvider`, and `InteractionContextTransmitter` GameObjects/components.
  - Add `AIEditableObject` to one test cube/object.
  - Wire pointer origins to one or both hand selector/controller transforms.
  - Wire `CodeGenerationManager` as the active selection source.

Possible non-invasive alternative:

- Do not edit `MicrophoneCapture.cs` initially.
- Add `InteractionContextTransmitter` with a manual send key or inspector button for testing.
- Later add the `MicrophoneCapture` hook once server-side context receiving is ready.

## Implementation Steps

1. Add data DTOs.
   - `ObjectSummary`
   - `InteractionContextSnapshot`
   - small serializable vector DTO, if avoiding custom JSON converters

2. Add `AIEditableObject`.
   - Stable `objectId`.
   - `displayName`, `description`, `labels`, `editable`.
   - `ToSummary()`.

3. Add `SceneRegistry`.
   - Registers/unregisters editable objects.
   - Can discover objects at startup.
   - Exposes `CurrentSceneVersion`.
   - Resolves summaries from `GameObject` and `Collider`.

4. Add `InteractionContextProvider`.
   - References `SceneRegistry`.
   - References pointer transforms.
   - Optionally references `SelectObjectRay[]` and `CodeGenerationManager`.
   - Captures `active_selection`, `pointed_object`, `pointed_world_position`, `last_action = null`, `pending_confirmation = null`, and `scene_version`.

5. Add `InteractionContextTransmitter`.
   - Registers `NetworkId(99)`.
   - Finds `RoomClient`.
   - Captures snapshot from provider.
   - Serializes JSON.
   - Sends `[peerUUID][json]` via Ubiq.

6. Wire recording transitions.
   - Add the smallest possible optional hook around `MicrophoneCapture.SetRecording`.
   - Send context on start and stop.
   - Keep audio path unchanged.

7. Add scene setup.
   - In the DynamicCompiler demo or a small test scene, add registry/provider/transmitter.
   - Add one test cube with `AIEditableObject`.

## Minimal Test Scene Setup

Use `Assets/Demos/DynamicCompiler/DynamicCompiler.unity` for integration testing because it is the enabled build scene and already contains the Ubiq room/client/audio/code generation setup.

Minimal scene objects:

- `Context Bridge`
  - `SceneRegistry`
  - `InteractionContextProvider`
  - `InteractionContextTransmitter`

- `Cube`
  - `Collider`
  - `Renderer`
  - tag: `game` if testing current `SelectObjectRay`
  - `AIEditableObject`
    - `objectId = "cube_001"`
    - `displayName = "Demo Cube"`
    - `labels = ["cube", "demo_object"]`
    - `editable = true`

Provider references:

- `sceneRegistry`: the scene registry component
- `pointerOrigins`: right hand selector/controller transform, left hand selector/controller transform, or camera transform for desktop tests
- `codeGenerationManager`: existing scene `CodeGenerationManager`, if present
- `existingSelectionSources`: existing `SelectObjectRay` components, if present
- `raycastLayers`: include the cube layer
- `maxRayDistance`: `8`

Transmitter references:

- `provider`: the context provider
- `networkId`: `99`
- `sendOnStart`: optional for smoke testing
- `sendPeriodicallyWhileRecording`: optional, disabled by default until server receiver exists

## How To Test With One Cube

1. Open `Assets/Demos/DynamicCompiler/DynamicCompiler.unity`.
2. Add or identify a cube with a collider.
3. Set the cube tag to `game` if using the existing `SelectObjectRay` selection path.
4. Add `AIEditableObject` to the cube:
   - `objectId = "cube_001"`
   - `displayName = "Demo Cube"`
   - `editable = true`
5. Add `SceneRegistry` and confirm the cube registers.
6. Add `InteractionContextProvider`.
7. Assign a pointer origin:
   - VR: controller/hand transform used by selector ray.
   - Desktop: camera transform for a simple raycast smoke test.
8. Add `InteractionContextTransmitter` with `NetworkId(99)`.
9. Start the scene and join the room as usual.
10. Point at the cube.
11. Trigger a context send:
   - manually from a debug button/key, or
   - through the future recording start/stop hook.
12. Confirm the local debug log shows:
   - `active_selection.id = "cube_001"` when selected
   - `pointed_object.id = "cube_001"` when pointed at
   - non-null `pointed_world_position`
   - `last_action = null`
   - `pending_confirmation = null`
   - `scene_version = 0` or `1`

## How To Verify The Server Receives Context

When server context receiving is added later, verify with a separate reader on `NetworkId(99)`.

Expected server packet parsing:

```ts
const peerUUID = data.message.subarray(0, 36).toString();
const json = data.message.subarray(36).toString('utf8');
const snapshot = JSON.parse(json);
```

Expected assertions:

- `peerUUID` matches the Unity client's `RoomClient.Me.uuid`.
- `snapshot.type === "InteractionContextUpdate"`.
- `snapshot.active_selection.id === "cube_001"` when the cube is selected.
- `snapshot.pointed_object.id === "cube_001"` when the controller/camera ray points at the cube.
- `snapshot.pointed_world_position` contains numeric `x`, `y`, and `z`.
- `snapshot.last_action === null`.
- `snapshot.pending_confirmation === null`.

Log example:

```text
[ContextBridge] peer=... active_selection=cube_001 pointed_object=cube_001 scene_version=0
```

The STT server path should continue to receive only `NetworkId(98)` packets with the existing audio/control format. Context verification should happen on `NetworkId(99)`.

## Risks And Notes

- `SelectObjectRay` currently computes pointing every frame and writes `CodeGenerationManager.targetObject` only for objects tagged `game`. A provider-owned raycast can report `pointed_object` even when no object is actively selected.
- Existing object names are not stable enough for AI use. `AIEditableObject.objectId` should become the authoritative id.
- `scene_version` is static until future scene mutation APIs exist.
- If both hands have pointer origins, define a deterministic selection rule, such as nearest hit or most recently active hand.
- Avoid sending context through `NetworkId(98)`, because the server STT reader treats unknown payloads as audio.

