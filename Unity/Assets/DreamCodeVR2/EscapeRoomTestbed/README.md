# DreamCodeVR2 Escape Room Testbed

## Scene purpose

`DreamCodeVR2_EscapeRoom_Testbed.unity` is a minimal semantic escape-room-style test scene for validating DreamCodeVR 2.0 scene metadata, Ubiq networking, ContextBridge selection context, and SceneContext snapshot delivery without implementing actual puzzle logic.

## Required runtime objects

The scene reuses the duplicated DynamicCompiler runtime setup and renames the existing context bridge object to `DreamCodeVR2_RuntimeServices`.

That object contains:

- `SceneRegistry`
- `InteractionContextProvider`
- `InteractionContextTransmitter`
- `SceneContextCompiler`
- `SceneContextTransmitter`

The existing Ubiq/RoomClient/NetworkScene setup from the working DynamicCompiler scene is preserved.

## Object list and stable IDs

- `room_floor`
- `wall_back`
- `wall_left`
- `wall_right`
- `door_001`
- `lock_001`
- `drawer_001`
- `table_001`
- `key_001`
- `button_001`
- `painting_001`
- `clue_note_001`
- `socket_001`
- `sphere_001`
- `lamp_001`

## How to test SceneContext

1. Open `Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity`.
2. Start the server with:

```text
SCENE_CONTEXT_ENABLED=true
SCENE_CONTEXT_NETWORK_ID=100
SCENE_CONTEXT_TTL_MS=30000
```

3. Enter Play Mode.
4. Confirm the Unity Console shows:

```text
[SceneContext] sent objects=<N> bytes=<B> scene_version=<V>
```

5. Confirm the server log receives the corresponding scene context update.

## How to test ContextBridge

1. In Play Mode, point at one of the tagged room objects such as `key_001` or `door_001`.
2. Use push-to-talk or a manual context send path already available in the duplicated scene runtime.
3. Confirm the Unity Console shows ContextBridge logs similar to:

```text
[ContextBridge] peer=<uuid> active_selection=... pointed_object=...
```

4. Say a simple semantic edit request such as `make the key red`.

## Known limitations

- No actual door, lock, drawer, clue, or puzzle behavior is implemented.
- `AIEditableObject` currently exposes labels and editability only; per-object operation lists are not authored in this scene because the current component API does not support them.
- Final verification of compile state, scene opening, and runtime logs must still be done inside the Unity Editor.

## Non-goals

- No SceneAPI or BehaviorAPI implementation
- No Reference Resolver
- No planner, undo, memory, or plan preview
- No generated-code runtime changes
- No STT/audio path changes
