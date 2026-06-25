# DreamCodeVR Unity Quest Readiness Audit

## Scene
- Target scene: `DreamCodeVR2_EscapeRoom_Testbed`
- Authoring UI root: `DreamCodeVR_AuthoringUI` is runtime-created and extendable.
- Existing pointed/selected object flow remains available through `InteractionContextProvider` and `DreamCodeVRAuthoringUIController`.

## AIEditableObject Inventory
- `basket_001`
- `cabinet_001`
- `cabinet_drawer_001`
- `cabinet_drawer_002`
- `cabinet_drawer_003`
- `clue_note_001`
- `clue_note_002`
- `door_001`
- `floor_001`
- `key_001`
- `key_002`
- `lamp_001`
- `lamp_002`
- `lamp_003`
- `lamp_004`
- `lock_001`
- `lock_002`
- `lock_003`
- `painting_001`
- `table_001`
- `table_drawer_001`
- `table_drawer_002`
- `table_drawer_003`
- wall objects and other room shell entries listed in the scene snapshot

Source: [SCENE_SNAPSHOT_ESCAPE_ROOM_AFTER_FIXES.md](/C:/Users/Scianso/Documents/GitHub/Real-Time-Immersive-Speech-Programming/Unity/Assets/DreamCodeVR2/EscapeRoomTestbed/SCENE_SNAPSHOT_ESCAPE_ROOM_AFTER_FIXES.md)

## Anchors Found
- `drawer_inside_anchor`: multiple instances present
- `desk_surface_anchor`: present
- `basket_inside_anchor`: present
- `cabinet_top_anchor`: present

These anchors are sufficient for constrained placement without arbitrary coordinates. Placement logic should prefer anchors under the intended parent object whenever the anchor name is duplicated.

## Clue Note TMP Text
- `clue_note_001` has a TextMeshPro child text component in the scene file.
- `clue_note_002` has a TextMeshPro child text component in the scene file.
- Runtime clue text updates are feasible through `GetComponentInChildren<TMP_Text>(true)`.

## Materials
- Exact `soccer_ball_material` asset name was not found in the repository.
- Existing relevant scene materials include:
  - `Sphere.mat`
  - `GoldKey.mat`
  - `SilverKey.mat`
  - `Paper.mat`
  - `BlueButton.mat`
  - `Red.mat`
- Simple colored materials exist partially, but not with the exact normalized names requested (`red_material`, `blue_material`, `green_material`, `yellow_material`).
- A runtime fallback material path is therefore advisable for constrained created objects.

## Runtime Repositioning Safety
- `key_001` can be repositioned and reparented at runtime.
- `key_002` can be repositioned and reparented at runtime.
- `clue_note_002` can be repositioned and reparented at runtime.
- Existing `AIEditableObject` metadata can be preserved as long as the original GameObjects are reused.

## Selection And Context
- Selection still resolves through `SelectObjectRay` using `AIEditableObject` plus `tag == game`.
- Pointed object context still resolves through `InteractionContextProvider`.
- Runtime-created constrained objects can participate if they get:
  - `AIEditableObject`
  - collider
  - `game` tag

## DreamCodeVR Authoring UI
- The refined authoring UI exists as runtime bootstrap code.
- It already supports compact status, inspect, speech, plan preview, and feedback cards.
- It can be extended minimally for scenario mode and quest preview without reintroducing the old Ubiq menu.

## Readiness Summary
- Scene readiness for constrained quest setup: good
- Anchor coverage for deterministic placement: good
- Clue note runtime text updates: supported
- Runtime-created constrained puzzle objects: supported with fallback materials
- UI extension path: supported
- Main risk: exact material asset naming is incomplete, so constrained runtime fallback materials are needed
