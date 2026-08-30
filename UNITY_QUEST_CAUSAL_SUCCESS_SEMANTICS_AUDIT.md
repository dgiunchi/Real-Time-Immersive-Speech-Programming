# Unity Quest causal-success semantics audit

## GLOBAL RULE

Action tasks complete from their direct causal world-state predicate. Reveals and accessibility changes remain world side effects and are not silently required for an action task.

`NextTaskWireConverter` now removes `OBJECT_REVEALED` only when the same server task already has a causal action predicate (`PAINTING_ALIGNED`, `OBJECT_OPEN`, `LOCK_UNLOCKED`, `OBJECT_ACTIVE`, `OBJECT_AT_ANCHOR`, or `DOOR_OPEN`) and its text does not explicitly request discovery. The server contract, IDs, task order, and condition designs are unchanged.

## PAINTING

`QuestPaintingController.TryAlign()` sets the aligned pose and `IsAligned`, then publishes the existing `ObjectStateChanged/aligned` event. The validator uses `PAINTING_ALIGNED`; clue visibility is no longer a required sibling predicate for the action task. The controller does not complete the task directly.

## DRAWERS

`ExperimentalDrawerController` publishes `ObjectStateChanged/open` after its motion completes. `OBJECT_OPEN` is evaluated directly, independently from an inactive/hidden note or key.

## LOCKS

`QuestLockController.TryUseKey()` changes `IsLocked` and publishes `LockOpened`. `LOCK_UNLOCKED` is evaluated directly; opening an associated drawer or door remains a separate action.

## LAMPS

`QuestLampController.SetLampState(true)` publishes its existing state-change event. `OBJECT_ACTIVE` completes the task without requiring any visual reveal.

## BALL

`QuestPlacementMonitor.NotifyPlaced()` parents the sphere to the configured `AuthoringAnchor` and publishes `ObjectPlacedInZone`. `OBJECT_AT_ANCHOR` verifies that exact anchor ID and completes the task.

## EXPLICIT DISCOVERY TASKS

`OBJECT_REVEALED`, `OBJECT_HELD`, and `OBJECT_GRABBED` remain supported direct predicates. A reveal predicate is retained when there is no causal-action predicate, or when the task text explicitly asks to find, retrieve, inspect, read, pick up, or grab content.

## SUCCESS CONDITION MAPPING

The existing direct mappings remain:

- `painting_aligned` -> `QuestPaintingController.IsAligned`;
- `object_open` -> drawer/door `IsOpen`;
- `lock_unlocked` -> `QuestLockController.IsUnlocked`;
- `object_active` -> `QuestLampController.IsActive`;
- `object_at_anchor` -> exact parent `AuthoringAnchor.anchorId`;
- `door_open` -> `QuestDoorController.IsOpen`.

## EVENT REEVALUATION

`QuestEventDrivenValidator` already re-evaluates every active task on existing bus events from painting, drawers, locks, lamps, placement, and doors. It now logs `TASK_SUCCESS_EVALUATION` for each condition with `task_id`, `condition`, `current_value`, and `result`. A successful active-task transition logs `TASK_COMPLETED` with `task_id` and `triggering_condition`.

`QuestRuntimeState` only completes entries whose status is `Active`, preventing a `TaskCompleted` bus notification from completing the same task recursively or more than once.

## TESTS

Added EditMode coverage for:

- aligned painting while an unrelated clue remains hidden;
- opened drawer while its note remains hidden;
- unlocked lock while its drawer remains closed;
- active lamp with no reveal;
- sphere at its configured basket anchor;
- explicit reveal task that does not complete from painting alignment alone;
- server-condition normalization: action task drops the reveal consequence, explicit discovery task retains it.

The Unity Test Runner must run these tests; no Unity compiler/test runner is available in the current shell.
