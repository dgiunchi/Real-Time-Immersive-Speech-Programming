# Lock, drawer, and key-reveal flow audit

## Root cause

The logical server drawer lock (`lock_drawer_001`) is already normalized to the
scene lock `lock_002` and unlocks successfully. The key use path did not apply
any physical insertion state, however, and the two drawer-open executors each
performed an independent, opaque lookup. The A1 payload also places `key_002`
on the desk although the intended A1 progression requires it to be discovered
after opening the drawer.

## Implemented flow

- `QuestLockController.TryUseKey` accepts only the currently configured exact
  key ID. A wrong key leaves the key, lock, and drawer unchanged.
- On success it creates/reuses a local `key_insert_anchor` under the resolved
  lock, parents the accepted key to it, makes its rigidbody kinematic and turns
  off grasping. It logs `KEY_SNAPPED_TO_LOCK` and sends a scene snapshot.
- `QuestInsertedKeyState` records the pre-insertion parent, pose, rigidbody and
  grasp state. The next quest application restores all inserted keys before
  applying the new placement/binding state.
- Both operational and predefined voice `OPEN` routes now call the same
  `QuestLockController.CanOpenTarget` check. It logs `DRAWER_OPEN_GATE` with
  the drawer ID, resolved lock ID, lock state, and required key, so failures can
  distinguish a locked drawer from a missing/misconfigured motion controller.
- A1 explicitly configures `key_002` and `clue_note_002` as contents of
  `table_drawer_001`. They are positioned at that drawer's
  `drawer_inside_anchor`, disabled while the drawer is closed, then enabled only
  after `ExperimentalDrawerController.MotionCompleted(true)`. The reveal emits
  `QUEST_OBJECT_REVEALED` and a fresh scene snapshot.

## Reset guarantees

Changing/restarting a quest restores inserted keys, clears the previous drawer
content reveal subscription/state, restores the normal scene visibility policy,
and then applies the selected quest's placements and lock bindings.

## Expected A1 trace

1. `QUEST_LOCK_BINDING_APPLIED`: `lock_002`, `key_001`,
   `table_drawer_001`.
2. Correct `USE_WITH`: `LOCK_USE_SUCCESS`, `KEY_SNAPPED_TO_LOCK`,
   `LOCK_UNLOCKED`.
3. `OPEN table_drawer_001`: `DRAWER_OPEN_GATE` has `is_locked:false`, followed
   by `DRAWER_MOTION_COMPLETE`.
4. `QUEST_OBJECT_REVEALED` exposes `key_002` and `clue_note_002`.

## Tests added

- Correct key snaps into its lock and restores its physical/grab state.
- A1 drawer contents remain hidden until a successful drawer opening completes.
