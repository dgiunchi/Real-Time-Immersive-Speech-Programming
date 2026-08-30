# Latest C1 A1 drawer-open log analysis

## RUN IDENTIFICATION

- Device log: `/sdcard/Android/data/com.VARLab.DreamCodeVR2/files/DreamCodeVR2/logs/client_20260829T173136Z_run.jsonl`
- UTC run start: `2026-08-29T17:31:36Z` (local device test around 19:31).
- Peer/session: `db0693cd-4ace-4a7f-ac1e-54d3f248a931` /
  `81ef1ed1-e1ff-4ad0-a3dc-50f3053b4f6d`.
- Condition: `voice_command_baseline` (C1).
- Quest instance: `set_a_instance_1`; the wire setup identifies the set as
  `set_a_ball_and_drawer`.
- The run contains the post-instrumentation events `LOCK_STATE_AFTER_UNLOCK_EVENT`,
  `LOCK_STATE_AFTER_SCENE_CONTEXT_REFRESH`, and `DRAWER_OPEN_GATE`.

## EVENT TIMELINE

All timestamps are UTC.

1. `17:32:22` T3 activates with `LOCK_UNLOCKED:lock_002`; the controller is
   `-848` and is initially locked.
2. `17:32:30–17:32:35` interaction context selects/points `key_001`.
3. `17:32:35` server proposes `USE_WITH key_001 -> lock_002` from the utterance
   “Use this key with the table drawer lock.”
4. `17:32:37` confirmation and local execution arrive. `LOCK_USE_ATTEMPT` says
   required key `key_001`, `is_locked_before:true`, controller `-848`.
5. `17:32:37` `KEY_SNAPPED_TO_LOCK`: `key_001 -> lock_002`, anchor
   `key_insert_anchor`.
6. `17:32:37` `LOCK_USE_SUCCESS` and `LOCK_UNLOCKED`: controller `-848`,
   `is_locked_after:false`, `is_unlocked_after:true`.
7. `17:32:37` T3 evaluates `LOCK_UNLOCKED` as true from controller `-848` and
   completes. Both post-event and post-SceneContext traces remain unlocked.
8. `17:32:37` T4 activates.
9. `17:32:50–17:32:53` the later open request ends with both selected and
   pointed object `table_drawer_001`. The server proposes `OPEN table_drawer_001`.
10. `17:32:55` local OPEN executes. `DRAWER_OPEN_GATE` resolves `lock_002`,
    controller `-848`, sees it unlocked, and allows opening.
11. `17:32:56` execution reports no error and `QUEST_OBJECT_REVEALED` exposes
    `key_002` and `clue_note_002` from `table_drawer_001`.

No `DRAWER_MOTION_START` or `DRAWER_MOTION_COMPLETE` exists in this run. The
controller returns successfully without a motion log when it is already at its
requested open pose; the subsequent `ObjectStateChanged` and final scene
snapshot report `table_drawer_001.is_open:true`.

## T3 KEY LOCK RESOLUTION

The actual pair is exactly `key_001 + lock_002`. The active task is
`set_a_instance_1:T3`, the key/lock command has target `key_001`, secondary
target `lock_002`, and the configured required key is `key_001`. The selected
and pointed item at recording boundaries is `key_001`; lock controller instance
is `-848`.

## LOCK STATE PERSISTENCE

`lock_002` changes `true -> false` at successful use and remains false through
the synchronous `LockOpened` event/T3 completion and SceneContext refresh:

| Event | Controller | Locked | Unlocked |
| --- | ---: | --- | --- |
| `LOCK_USE_ATTEMPT` | -848 | true | — |
| `LOCK_USE_SUCCESS` | -848 | false | true |
| `LOCK_UNLOCKED` | -848 | false | true |
| `LOCK_STATE_AFTER_UNLOCK_EVENT` | -848 | false | true |
| `LOCK_STATE_AFTER_SCENE_CONTEXT_REFRESH` | -848 | false | true |
| `DRAWER_OPEN_GATE` | -848 | false | true |

There is no `unlocked -> locked` transition after T3 in this run.

## T3 COMPLETION

`TASK_SUCCESS_EVALUATION` for T3 has
`condition:LOCK_UNLOCKED`, `current_value:true`, `lock_is_unlocked:true`, and
`lock_controller_instance_id:-848`. `TASK_COMPLETED` explicitly reports
`QuestEventDrivenValidator.RuntimeTaskValidator.IsSatisfied`; it is not merely
an event-cached completion.

## POINTED DRAWER AFTER COLLIDER FIX

The latest run proves raycast selection can resolve a drawer semantic ID:
`[Selection] hit=table_drawer_001 resolved=table_drawer_001 display=Desk Drawer 1`.
The OPEN command consequently targets that same canonical object.

However, the active-device SceneContext snapshot in this run lists a
`BoxCollider` on `table_drawer_001`, but **not** on the physically locked
`table_drawer_002`. Thus this log does not prove that the later manual collider
edit on the locked drawer was saved and included in the installed APK.

## OPEN TARGET RESOLUTION

OPEN targets `table_drawer_001` / `Desk Drawer 1`, not `table_drawer_002` /
`Locked Desk Drawer`. This is not a raycast child/parent-ID failure: the hit,
selected, pointed, interaction-context, proposal, and execution IDs all agree
on `table_drawer_001`.

## DRAWER LOCK GATE

`DRAWER_OPEN_GATE` reports:

- drawer ID: `table_drawer_001`;
- resolved lock ID: `lock_002`;
- resolved controller / successful-use controller: `-848` / `-848`;
- same controller: `true`;
- locked/unlocked: `false` / `true`;
- open allowed: `true`.

The association source is `QuestLockController.associatedTargetObjectId`, set
from the A1 server `task_targets.drawer` during `QuestInstance` conversion.

## DRAWER MOTION

OPEN reaches the local `ExperimentalDrawerController` and returns successfully;
there is no rejection or configuration failure. The absence of a motion-start
event, together with `is_open:true` in the later scene snapshot, means the
selected `table_drawer_001` was already at (or was treated as at) its open pose.
The log supplies no evidence that the new BoxCollider or rigidbody blocked it.

## GOLDEN KEY VISUAL STATE

Logical key use succeeds. `KEY_SNAPPED_TO_LOCK` confirms parentage to a runtime
`key_insert_anchor` under `lock_002`; no event deactivates `key_001` afterwards.
The later SceneContext material hierarchy shows the Golden Key under the
physical `table_drawer_002` hierarchy, because that is where `lock_002` lives.
The current insertion offset is a fixed local `Vector3.back * .035f`; the log
does not provide renderer bounds/visibility, so it cannot prove whether that
pose is visually inside lock geometry. It is not hidden by quest visibility.

## T4 CONTENT DRAWER

`QuestDrawerContentsReveal` is subscribed to and reveals from
`table_drawer_001`. The event proves that `key_002` and `clue_note_002` are
owned/revealed by `table_drawer_001` in this run.

## ROOT CAUSE

**Proven:** the A1 runtime binds logical lock `lock_002` to
`table_drawer_001`, while the actual scene hierarchy/snapshot places physical
`lock_002` inside `table_drawer_002` (the locked desk drawer). The test then
opens `table_drawer_001`, an already openable drawer, and reveals its contents.
The lock does not re-lock; OPEN reads the same unlocked controller and passes.

Classification supported by evidence: **B** (drawer IDs can now be raycast
resolved), **C** (the logical target drawer is wrong for the physical lock), and
**I** (the OPEN command succeeds on the wrong/open drawer rather than failing
at the lock gate).

## RECOMMENDED MINIMAL FIX

After confirming the intended A1 physical puzzle is the second desk drawer,
bind the A1 logical drawer-lock role for `lock_002` to `table_drawer_002` and
attach A1 drawer contents/reveal to that same canonical drawer. This is a
client mapping correction only. Do not change `IsLocked`, force an OPEN, or
remove the manually added collider.

## REMAINING UNCERTAINTIES

- The latest installed-run snapshot has no BoxCollider on `table_drawer_002`;
  rebuild/deploy after saving the manual collider edit before relying on it.
- A focused retest must point at `table_drawer_002`, then verify its raycast
  semantic ID and drawer motion events.
- The Golden Key insertion orientation/offset needs a visual retest after the
  target drawer mapping is corrected; logs prove parentage, not visual exposure.
