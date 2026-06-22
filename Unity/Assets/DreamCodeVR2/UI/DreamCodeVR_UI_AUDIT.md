# DreamCodeVR UI / Ubiq Audit

## Current UI Objects
- `Menu` in the Escape Room scene is active and contains the legacy join / identity flow.
- `Menu Panel` is active and appears to host the current menu body.
- `Join Room Panel` exists and is currently inactive.
- Multiple `Keyboard` objects exist in the scene and are wired to legacy text-entry / joincode flows.
- A `Canvas` under the player notifications setup exists but is inactive by default and appears to be used for connection notifications rather than the main menu.
- `Wrist Menu Invoker` exists as a prefab instance under the player setup and appears to belong to the XR/Ubiq interaction stack rather than DreamCodeVR object selection.

## Ubiq Runtime Objects
- `RoomClient` is present and still required for networking and room presence.
- `NetworkScene` is present and still required for all Ubiq message flows.
- `InteractionContextTransmitter` uses `NetworkId 99` and is preserved.
- `MicrophoneCapture` uses `NetworkId 98` and is preserved.
- `TranscriptionCollector` uses `NetworkId 98` and is preserved as the current local transcript source.
- `SceneContextTransmitter` is preserved and continues to depend on `RoomClient`.
- `SceneRegistry` is preserved and remains the current local semantic object inventory.
- Player avatar / hand / XR interaction prefabs from `LegacyUbiqDependencies/Player.prefab` are preserved.
- `Wrist Menu Invoker` is preserved because it belongs to the runtime XR interaction layer.

## Menu Dependencies
- The legacy menu UI is not the only room-join path.
- `RoomJoiner` is present in the Escape Room scene and automatically calls `RoomClient.Join(...)` once the `NetworkScene` has a live connection.
- Because `RoomJoiner` performs the actual room join automatically, the current menu is not required for room entry in the experimental Escape Room flow.
- Some legacy scripts still keep references to `mainMenu`, so a conservative approach is preferred: hide or disable the obsolete UI visuals without removing `RoomClient`, `NetworkScene`, or XR/Ubiq runtime objects.

## Safe Cleanup Plan
- Preserve all Ubiq runtime/networking objects.
- Preserve `RoomClient`, `NetworkScene`, `SceneContext`, `ContextBridge`, `MicrophoneCapture`, `TranscriptionCollector`, avatars, and wrist-menu infrastructure.
- Hide the obsolete menu UI at runtime by grouping `Menu`, `Keyboard`, `Join Room Panel`, `Menu Panel`, and `Join Room` under a disabled `Legacy_Menu_Disabled` root.
- Keep the cleanup reversible and additive by doing it from DreamCodeVR UI bootstrap code rather than deleting scene content.

## Proposed DreamCodeVR Authoring UI
- Add a runtime-created world-space canvas named `DreamCodeVR_AuthoringUI`.
- Anchor it in front of the player camera so it is readable in VR but out of the main interaction path.
- Sections:
- `HeaderPanel`: title and experiment framing.
- `PointingPanel`: pointed object, selected object, labels.
- `InspectPanel`: display name, object id, description, lightweight possible actions.
- `SpeechPanel`: transcript plus placeholder intent/policy/debug text.
- `PlanPanel`: placeholder plan preview.
- `FeedbackPanel`: current status and undo/repair hint placeholder.
- Use TextMeshPro and dark translucent panels for readability.

## Risks
- Hiding legacy menu roots may hide join-related visuals that some workflows still expect, even though `RoomJoiner` auto-joins.
- `Wrist Menu Invoker` should not be removed without verifying whether it is needed for runtime menu access or controller UX.
- The new authoring UI should not intercept object selection, so it should remain read-only and not alter existing world-selection raycasts.
- Manual tests:
- scene starts normally
- room still auto-joins
- server still logs peer joined
- pointed object continues to reach ContextBridge
- SceneContext still sends scene objects
- push-to-talk / STT still works

