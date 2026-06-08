# Unity Context Bridge Design

## Goals

The Unity Context Bridge should provide the server Intent Gate with a structured, current snapshot of the user's interaction context:

- `active_selection`
- `pointed_object`
- `pointed_world_position`
- `last_action`
- `pending_confirmation`
- `scene_version`

The first implementation should leave `last_action` and `pending_confirmation` as `null`. It should not implement scene editing, generated behavior execution, validation, undo, command decomposition, SceneAPI, or BehaviorAPI.

## Namespace And Folder

Additive Unity scripts should live under:

`Unity/Assets/DreamCodeVR2/ContextBridge/`

Recommended namespace:

`DreamCodeVR2.ContextBridge`

This avoids adding more global classes directly under `Assets/` and keeps the bridge easy to remove or move.

## AIEditableObject

`AIEditableObject` is a MonoBehaviour attached to scene objects that may be referenced by the AI.

Purpose:

- provide a stable AI-facing object id
- expose a human-readable display name
- expose optional semantic labels
- supply object summaries to `SceneRegistry`
- make selection/raycast hits resolvable without depending on fragile Unity object names

Recommended fields:

- `string objectId`
  - Stable id used in context snapshots.
  - Manually set in inspector or generated once in editor.
  - Example: `cube_001`, `training_sphere`, `kitchen_table_01`.

- `string displayName`
  - AI/user-facing name.
  - Defaults to `gameObject.name` if blank.

- `string description`
  - Optional short description for the AI.

- `string[] labels`
  - Optional semantic labels such as `cube`, `furniture`, `target`, `demo_object`.

- `bool editable`
  - Whether the object may be targeted by future editing actions.
  - Future APIs can use this, but v0.1 only reports it.

- `bool includeRendererBounds`
  - Whether summaries should include approximate world bounds.

Recommended behavior:

- On `OnEnable`, register with `SceneRegistry`.
- On `OnDisable`, unregister.
- `ObjectSummary ToSummary()` returns an object summary with id, names, transform, bounds, labels, and editable flag.

Do not add behavior execution methods to this component in this milestone.

## SceneRegistry

`SceneRegistry` is a MonoBehaviour that maintains the mapping from Unity objects to AI object summaries.

Purpose:

- central lookup from `GameObject`, `Collider`, or `AIEditableObject` to `ObjectSummary`
- stable object id validation
- scene version tracking
- optional discovery of existing `AIEditableObject` components at startup

Recommended fields:

- `int sceneVersion`
  - Starts at `0` or `1`.
  - Increment only when future scene-editing operations mutate the scene.
  - For the first implementation, it can remain constant.

- `bool autoDiscoverOnStart`
  - Finds all `AIEditableObject` components in the active scene.

- `bool allowFallbackSummaries`
  - If true, non-registered raycast hits can be summarized from `GameObject.name`.
  - Recommended default: true for early testing, false for production once objects are tagged.

Recommended methods:

- `Register(AIEditableObject editableObject)`
- `Unregister(AIEditableObject editableObject)`
- `bool TryGetSummary(GameObject obj, out ObjectSummary summary)`
- `bool TryGetSummary(Collider collider, out ObjectSummary summary)`
- `IReadOnlyList<ObjectSummary> GetAllSummaries()`
- `int CurrentSceneVersion { get; }`

Fallback summary behavior:

- Use `gameObject.name` as both id and display name only when no `AIEditableObject` exists.
- Mark fallback summaries as `editable = false` unless explicitly configured otherwise.
- Include a `source = "fallback"` marker if included in `ObjectSummary`.

## InteractionContextProvider

`InteractionContextProvider` captures local interaction state and returns an `InteractionContextSnapshot`.

Purpose:

- resolve current active selection
- resolve currently pointed object
- resolve current pointed world position
- provide null `last_action` and `pending_confirmation` for v0.1
- include scene version

Recommended fields:

- `SceneRegistry sceneRegistry`
- `Transform[] pointerOrigins`
  - Controller/hand transforms used for raycasts.
  - For desktop testing, can use camera transform.

- `SelectObjectRay[] existingSelectionSources`
  - Optional references to current selection scripts.
  - Lets provider read `selectedObject` or `CodeGenerationManager.targetObject` without changing selection behavior.

- `CodeGenerationManager codeGenerationManager`
  - Optional source of `targetObject`.

- `LayerMask raycastLayers`
- `float maxRayDistance = 8f`
- `bool useExistingSelection = true`
- `bool raycastEverySnapshot = true`

Recommended capture order:

1. Determine `active_selection`.
   - Prefer `CodeGenerationManager.targetObject` if set.
   - Else prefer latest valid `SelectObjectRay.selectedObject`.
   - Resolve through `SceneRegistry`.

2. Determine pointing.
   - Raycast from pointer origins.
   - Use nearest valid hit.
   - Set `pointed_world_position` to `RaycastHit.point`.
   - Resolve `pointed_object` through `SceneRegistry`.

3. Fill remaining fields.
   - `scene_version = sceneRegistry.CurrentSceneVersion`
   - `last_action = null`
   - `pending_confirmation = null`

The provider should not send network messages itself. Keep capture and transmission separate.

## ObjectSummary

`ObjectSummary` is the AI-facing representation of a Unity object.

Recommended JSON fields:

```json
{
  "id": "cube_001",
  "display_name": "Demo Cube",
  "unity_name": "Cube",
  "labels": ["cube", "demo_object"],
  "editable": true,
  "active": true,
  "position": { "x": 0.0, "y": 1.0, "z": 2.0 },
  "rotation_euler": { "x": 0.0, "y": 45.0, "z": 0.0 },
  "bounds_center": { "x": 0.0, "y": 1.0, "z": 2.0 },
  "bounds_size": { "x": 1.0, "y": 1.0, "z": 1.0 }
}
```

Required fields for v0.1:

- `id`
- `display_name`
- `unity_name`
- `editable`
- `active`
- `position`

Optional fields:

- `description`
- `labels`
- `rotation_euler`
- `bounds_center`
- `bounds_size`
- `source`

## InteractionContextSnapshot

`InteractionContextSnapshot` is the root context object sent from Unity.

Recommended JSON fields:

```json
{
  "schema_version": 1,
  "type": "InteractionContextUpdate",
  "peer": "00000000-0000-0000-0000-000000000000",
  "timestamp_unix_ms": 1710000000000,
  "scene_version": 0,
  "active_selection": {
    "id": "cube_001",
    "display_name": "Demo Cube",
    "unity_name": "Cube",
    "labels": ["cube", "demo_object"],
    "editable": true,
    "active": true,
    "position": { "x": 0.0, "y": 1.0, "z": 2.0 }
  },
  "pointed_object": {
    "id": "cube_001",
    "display_name": "Demo Cube",
    "unity_name": "Cube",
    "labels": ["cube", "demo_object"],
    "editable": true,
    "active": true,
    "position": { "x": 0.0, "y": 1.0, "z": 2.0 }
  },
  "pointed_world_position": { "x": 0.0, "y": 1.0, "z": 1.5 },
  "last_action": null,
  "pending_confirmation": null
}
```

Notes:

- `active_selection` may be `null`.
- `pointed_object` may be `null`.
- `pointed_world_position` may be `null` if the ray misses.
- `last_action` is `null` for v0.1.
- `pending_confirmation` is `null` for v0.1.
- `scene_version` comes from `SceneRegistry`.

## Serialization Format

Recommended wire format:

`[36-byte peer UUID][UTF-8 JSON InteractionContextSnapshot]`

This mirrors the existing audio/STT packet shape while keeping context on a separate network id.

Recommended JSON serializer:

- Prefer Newtonsoft JSON because `Unity/Packages/manifest.json` already includes `com.unity.nuget.newtonsoft-json`.
- Use snake_case field names to match the target GateContext fields.
- Include explicit `null` values for `last_action`, `pending_confirmation`, and missing object/position fields.

If avoiding Newtonsoft in the first pass, Unity `JsonUtility` can work with DTO classes that use public fields named in snake_case, but Newtonsoft is safer for null handling and future nested payloads.

## Proposed Ubiq Message Type And NetworkId

Use a new fixed Ubiq service id:

`NetworkId(99)`

Message type:

`InteractionContextUpdate`

Sender component:

`InteractionContextTransmitter`

Ubiq behavior:

- `InteractionContextTransmitter` registers with `NetworkScene.Register(this, new NetworkId(99))`.
- It finds `RoomClient.Me.uuid`.
- It captures a snapshot through `InteractionContextProvider`.
- It serializes the snapshot to UTF-8 JSON.
- It prefixes the JSON with the 36-byte peer UUID.
- It sends using `context.Send(message)`.

Server-side future reader:

```ts
this.components.contextReceiver = new MessageReader(this.scene, 99);
this.components.contextReceiver.on('data', (data: { message: Buffer }) => {
  const peerUUID = data.message.subarray(0, 36).toString();
  const json = data.message.subarray(36).toString('utf8');
  const snapshot = JSON.parse(json);
  // Store latest context by peerUUID and merge into GateContext.
});
```

This is intentionally separate from `MessageReader(scene, 98)` so STT/audio behavior remains unchanged.

## Capture Timing

Recommended v0.1 timing:

- Send one snapshot on recording start.
- Send one snapshot on recording stop.
- Optionally send periodic snapshots while recording at 5-10 Hz.
- Optionally send a snapshot when active selection changes.

The start/stop snapshots align context with the user's utterance without requiring changes to STT payloads.

## Out Of Scope

Do not implement in this milestone:

- scene editing actions
- behavior generation or execution
- command decomposition
- validation
- undo
- generated behavior runtime
- SceneAPI or BehaviorAPI

