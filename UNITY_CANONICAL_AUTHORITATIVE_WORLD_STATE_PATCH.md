# Unity Canonical Authoritative World-State Patch

Date: 2026-09-02

## 1. Event types added

- `DRAWER_STATE_CHANGED`
- `DOOR_STATE_CHANGED`
- `OBJECT_ANCHOR_CHANGED`

## 2. Exact payload fields

- `DRAWER_STATE_CHANGED`
  - `event_type`
  - `object_id`
  - `state`

- `DOOR_STATE_CHANGED`
  - `event_type`
  - `object_id`
  - `state`

- `OBJECT_ANCHOR_CHANGED`
  - `event_type`
  - `object_id`
  - `anchor_id`

The existing root-flat `QuestWorldStateEvent` serializer is preserved.

## 3. Emitting controller for each

- `DRAWER_STATE_CHANGED`
  - emitted from `ExperimentalDrawerController.PublishState`

- `DOOR_STATE_CHANGED`
  - emitted from `QuestDoorController.Publish`

- `OBJECT_ANCHOR_CHANGED`
  - emitted from `QuestPlacementMonitor.NotifyPlaced`
  - cleared on exit from `QuestPlacementMonitor.OnTriggerExit`

## 4. Ordering changes

- drawers now follow:
  - physical drawer motion
  - semantic open/closed state
  - `SceneContext`
  - `DRAWER_STATE_CHANGED`
  - local `ObjectStateChanged`

- doors now follow:
  - physical door motion
  - semantic open/closed state
  - `SceneContext`
  - `DOOR_STATE_CHANGED`
  - local `ObjectStateChanged`

- lock unlock now follows:
  - physical unlock
  - semantic lock state
  - `SceneContext`
  - `LOCK_STATE_CHANGED`
  - local `LockOpened`

- basket placement now follows:
  - physical parent/anchor confirmation
  - `SceneContext`
  - `OBJECT_ANCHOR_CHANGED`
  - local `ObjectPlacedInZone`

## 5. Painting/clue lifecycle

- `PAINTING_STATE_CHANGED` is preserved unchanged.
- Canonical fixed `set_a` / `set_b` / `set_c` sessions no longer auto-reveal `clue_note_001` from `QuestPaintingController`.
- `clue_note_001` remains authored in place, hidden at reset, and is expected to be revealed by server consequence flow.

## 6. Set B reset and B-T3 behavior

- reset conversion no longer hardcodes the non-exit drawer target to `table_drawer_002`; it now respects the incoming canonical `task_targets.drawer`
- `lock_002` can remain physically locked while `table_drawer_002` is physically closed
- a locked drawer still cannot open through the existing physical gate
- successful drawer open now emits `DRAWER_STATE_CHANGED(state=open)`

## 7. Set C sphere reset/reveal behavior

- if `sphere_001` is omitted from `required_runtime_objects`, Unity does not create a runtime sphere at reset
- existing fixed-visibility filtering continues to hide irrelevant puzzle objects
- consequence-driven reveal still activates the sphere later
- initial runtime sphere profile application remains condition-sensitive through the existing `sphere_profile` path

## 8. Post-reveal generation behavior

- successful `REVEAL_OBJECT_IN_CONTAINER` still bumps `OBJECT_AVAILABILITY_GENERATION` first through `QuestWorldStateReporter.Revealed`
- it then emits `OBJECT_REVEALED`
- when a drawer reveal path is used, `DRAWER_OPEN_TRANSITION` now reuses that same `availability_generation`

## 9. Basket authoritative evidence

- basket placement now emits authoritative `OBJECT_ANCHOR_CHANGED`
- canonical success payload is:
  - `object_id = sphere_001`
  - `anchor_id = basket_001.basket_inside_anchor`
- leaving the anchor clears the authoritative anchor field by emitting a follow-up `OBJECT_ANCHOR_CHANGED` with cleared anchor data

## 10. Door authoritative evidence

- door open/close now emits authoritative `DOOR_STATE_CHANGED`
- exact canonical field used is `state`, not `door_state`

## 11. Consequence ACK ordering

- `QuestConsequenceAck` remains after consequence application in `QuestConsequenceDispatcher`
- for the patched authoritative paths:
  - `SET_LOCK_STATE` emits `LOCK_STATE_CHANGED` before ACK
  - `SET_SPHERE_PROFILE` emits `SPHERE_PROFILE_CHANGED` before ACK
  - `CLOSE_DRAWER` emits `DRAWER_STATE_CHANGED(state=closed)` before ACK
  - `REVEAL_OBJECT_IN_CONTAINER` emits availability generation plus reveal evidence before ACK
- `SET_OBJECT_VISIBILITY` now uses `QuestNoteController` visibility handling for clue notes without moving them

## 12. task_completed role

- `ExperimentStateEvent/task_completed` is preserved
- it remains a hint/telemetry path only
- fixed-task progression still waits for:
  - `NextTaskGenerated`
  - `NextTaskActivationRequest`

## 13. Tests

Focused edit-mode tests were added/updated for:

- exact `state` field on `DRAWER_STATE_CHANGED`
- exact `state` field on `DOOR_STATE_CHANGED`
- exact `anchor_id` field on `OBJECT_ANCHOR_CHANGED`
- root-flat world-state payload checks
- drawer and door world-state ordering before local completion
- canonical painting no longer auto-revealing assigned clue
- `SET_OBJECT_VISIBILITY` revealing a clue without moving it
- Set B locked drawer reset expectations
- reveal/discovery generation reuse between `OBJECT_REVEALED` and `DRAWER_OPEN_TRANSITION`

## 14. Remaining known mismatch

- `SET_OBJECT_VISIBILITY` and `SET_CLUE_TEXT` still do not produce their own dedicated authoritative world-event types; they rely on the existing consequence/result flow plus `SceneContext`, which is acceptable for this patch scope but still narrower than a fully event-complete contract.
