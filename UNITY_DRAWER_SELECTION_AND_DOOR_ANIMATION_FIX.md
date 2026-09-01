# Drawer selection and door-motion fix

## DRAWER CURRENT ROOT CAUSE

When open, the drawer root collider is intentionally skipped so it cannot hide its contents. It then survives only as a fallback, which is unreliable for deliberate drawer selection because its large volume is behind or intersected by the cavity and contents.

## DRAWER SELECTION HANDLE

Bootstrap now creates a thin `DrawerSelectionHandle` child on the physical outer front face of each configured drawer. It follows the drawer transform and resolves through its parent `AIEditableObject`, so its canonical semantic ID remains the drawer ID.

## RAYCAST PRIORITY

`SemanticPointerRaycast` is the sole resolver for both systems: an explicit front handle wins when hit; selectable internal objects win when directly hit; and an open drawer body remains the final fallback. The large open-drawer collider is still transparent to content selection.

## POINTED / SELECTED CONSISTENCY

`SelectObjectRay` and `InteractionContextProvider` now call the same resolver. Proxy hits are converted to the parent drawer `AIEditableObject`, never exposed as a separate puzzle object.

## DOOR PREFAB AUDIT

The exit-door prefab has no Animator, Animation component, controller, or clips. Its `Door` child has the legacy `DoorScript.Door` MonoBehaviour.

## DOOR RECLOSE ROOT CAUSE

`DoorScript.Door.Update()` writes the child local rotation every frame and moves it toward closed whenever its own `open` flag is false. It overwrote `QuestDoorController`'s one-shot procedural rotation on the next frame.

## DOOR MOTION MODE

The valid existing architecture remains procedural hinge motion on the child `Door`; there is no usable prefab animation to adopt. Bootstrap disables only the conflicting legacy transform writer and logs the resolution.

## ANIMATOR INTEGRATION

Not applicable: no Animator or clips exist on this prefab. The motion diagnostics retain the requested `DOOR_MOTION_MODE`, `DOOR_ANIMATION_REQUESTED`, and `DOOR_ANIMATION_STATE` event names, with `mode=procedural`.

## DOOR SEMANTIC STATE

The semantic OPEN state is published only after the procedural pose is applied. Unlocking remains separate and does not open the door.

## RESET

Existing reset/close paths continue to use `QuestDoorController.TryClose`, which applies the closed anchor and publishes CLOSED. The disabled legacy script no longer changes state asynchronously.

## NON-REGRESSION

No server code, quest mappings, conditions, task predicates, key/lock resolver, manual drawer colliders, or the open-drawer interior penetration rule were changed.

## TESTS

Added an EditMode test that verifies a generated selection handle is a non-trigger child proxy of its canonical drawer. Existing door test continues to verify that only the `Door` child rotates and close returns to its anchor.

## NEXT DEVICE TEST

Open each table and cabinet drawer; aim at a contained object, then its front handle, then empty interior. Confirm VR selection and `POINTED` agree. Unlock the exit, verify it stays closed, OPEN it, verify it remains open and completes the final task, then CLOSE it if available.
