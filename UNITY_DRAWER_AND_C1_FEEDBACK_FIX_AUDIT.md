# Drawer and C1 feedback fix audit

## CURRENT DRAWER BUG ROOT CAUSE

`ExperimentalDrawerController` stored the closed local position but used `openLocalPosition = (0, 0, 0.22)` as an absolute local-space destination. For `table_drawer_001`, the closed local pose is `(-0.39916354, 0.78225887, -0.5909955)`. The OPEN command therefore moved the drawer to an unrelated point in the table's local space, causing the observed uncontrolled displacement/disappearance.

## DRAWER MOTION COMPONENT

`ExperimentalDrawerController` now uses serialized `closedAnchor` and `openAnchor` Transform references. It validates them, animates exactly between their authored poses, supports interruption by replacing the active coroutine, and does not use renderer bounds, mesh bounds, inferred axes, or offsets.

## CLOSED ANCHOR

`Study Table/DrawerClosedAnchor`, a stationary child of the Study Table root, is initialized to the exact existing local pose of `S.T Drawer 1`.

## OPEN ANCHOR

`Study Table/DrawerOpenAnchor`, also a stationary child of the Study Table root, is assigned to the controller. It starts at the closed pose intentionally: asset geometry and visual orientation are not sufficient evidence to infer its physical slide direction safely.

## COORDINATE SPACE

Runtime movement reads both anchors in world space and applies world-space poses consistently. It never mixes `transform.localPosition` with an anchor world position.

## OPEN EXTENSION

The intended open extension is approximately 85% of usable drawer travel, but no mesh-derived estimate was written. The final extension is defined exclusively by moving `DrawerOpenAnchor` in Unity Scene View.

## ANIMATION

Duration is serialized on `ExperimentalDrawerController` and defaults to 0.5 seconds. The animation uses `Mathf.SmoothStep`; duration `<= 0` snaps safely to the requested anchor.

## PHYSICS INTERACTION

The runtime bootstrap keeps this scripted drawer Rigidbody kinematic, as before. The motion controller does not permanently alter Rigidbody state. If the object is made non-kinematic later, it uses `Rigidbody.MovePosition`/`MoveRotation` instead of direct transform writes.

## C1 SUCCESS FEEDBACK

After a successful `PredefinedCommandExecutionRequest`, C1 replaces the proposal with `Command confirmed`. For drawer motion it waits for motion completion before showing that success state.

## C1 FAILURE FEEDBACK

Invalid anchors or any failed predefined execution show `Command could not be applied` with the returned concise error detail. The normal failed `PredefinedCommandAck` is preserved.

## PROPOSAL UI CLEANUP

The pending C1 proposal state is cleared on terminal execution. Success feedback remains for 1.2 seconds by default, then the proposal card hides. C2/C3 proposal lifecycles are not changed.

## LOGGING

Drawer: `DRAWER_MOTION_START`, `DRAWER_MOTION_COMPLETE`, `DRAWER_MOTION_INTERRUPTED`, and `DRAWER_MOTION_CONFIGURATION_ERROR` include object id, source/target position, duration, and requested state. C1 UI: `C1_COMMAND_SUCCESS_FEEDBACK_SHOWN`, `C1_COMMAND_SUCCESS_FEEDBACK_HIDDEN`, and `C1_COMMAND_FAILURE_FEEDBACK_SHOWN`.

## STATIC BLOCKERS

1. `DrawerOpenAnchor` requires final visual placement in Unity Scene View before OPEN can execute. Until it differs from `DrawerClosedAnchor`, the command fails safely and no movement is applied.

## MANUAL UNITY SETUP

Open `Assets/Scrivanie e cassettiere/Prefabs/Study Table.prefab`, select `S.T Drawer 1`, and inspect `ExperimentalDrawerController`. Move `DrawerOpenAnchor` straight along the real drawer slide direction until the drawer is about 85% extended and still visibly inside the cabinet. Do not parent either anchor under the drawer.

## QUEST TEST PLAN

### TEST A — OPEN

1. START C1.
2. Wait SESSION READY.
3. Close researcher panel.
4. Point at drawer.
5. Say `open this drawer`.
6. Confirm with `yes`.

Expected: smooth ~0.5 s outward slide, stops at Open Anchor, remains in cabinet, no teleport/disappearance.

### TEST B — SUCCESS FEEDBACK

After drawer reaches Open Anchor, expect `Command confirmed`; after ~1.2 s the proposal card disappears.

### TEST C — CLOSE

Issue and confirm CLOSE. Expect exact smooth return to Closed Anchor.

### TEST D — REPEAT

OPEN while already open. Expect safe no-op and no additional translation.

### TEST E — INTERRUPT

Issue CLOSE while OPEN is animating. Expect one controlled motion toward the latest target, without competing coroutines.

### TEST F — FAILURE

Temporarily make either anchor missing/overlapping in the Editor. Expect no movement, a failed ACK, concise failure UI, and a configuration-error log.
