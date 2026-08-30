# Lock-state persistence audit

## USE_WITH STATE TRACE

`QuestLockController.TryUseKey` is the only successful C1 `USE_WITH` path. It
sets `IsLocked = false` before publishing `LockOpened`. Diagnostic events now
record the same controller instance before use, after the state write, after
the event publication, and after the outbound SceneContext snapshot:

- `LOCK_USE_ATTEMPT`
- `LOCK_USE_SUCCESS`
- `LOCK_UNLOCKED`
- `LOCK_STATE_AFTER_UNLOCK_EVENT`
- `LOCK_STATE_AFTER_SCENE_CONTEXT_REFRESH`

Each contains the canonical lock/key IDs, `is_locked_before`,
`is_locked_after`, `is_unlocked_after`, and `controller_instance_id`.

## T3 COMPLETION SOURCE

T3 is handled by `QuestEventDrivenValidator.OnEvent`. `LockOpened` only
triggers an evaluation. For a `LOCK_UNLOCKED:lock_002` condition,
`RuntimeTaskValidator.IsSatisfied` reads
`lock_002.GetComponent<QuestLockController>().IsUnlocked`. Therefore T3 cannot
complete while that exact controller is still locked. `TASK_SUCCESS_EVALUATION`
and `TASK_COMPLETED` now identify this validation source and controller ID.

## AUTHORITATIVE LOCK STATE

The authoritative mutable state is `QuestLockController.IsLocked`. The
`AuthoringSemanticState.state` value and SceneContext payload are outbound
representations only; SceneContext refresh has no client-side lock-state import
path.

## DUPLICATE LOCK FLAGS

`ExperimentalDrawerController` has `IsOpen` and `IsMoving`, but no lock flag.
No drawer-local serialized lock boolean is read by either C1 or operational
open handling. `QuestDoorController` references a `QuestLockController` for
the exit door only.

## STATE REVERSION

The only source paths that explicitly write a lock back to locked are:

- `QuestInstanceController.ResetControlledState` / `ClearQuestBinding`;
- `QuestInstanceController.ApplyInitialStates` for an incoming `locked` state;
- `ExperimentalPlaythroughReset.Reset`; and
- a later bootstrap configuration for the exit lock only.

Task completion and SceneContext publication do not set a lock to locked. New
state-transition diagnostics make any later reversion explicit.

## OPEN READ PATH

Both C1 predefined `OPEN` and operational `OPEN` call
`QuestLockController.CanOpenTarget(drawerId)`. It resolves the controller by
`associatedTargetObjectId`, then reads `IsLocked`. `DRAWER_OPEN_GATE` now logs
the drawer, resolved lock, resolved controller instance ID, the successful-use
controller instance ID, whether they match, lock state, and `open_allowed`.

## CONTROLLER IDENTITY

The last controller that successfully accepted a key is recorded per associated
target for diagnostics only. The next `OPEN` reports whether it is the same
runtime component. This does not alter the open decision.

## ROOT CAUSE

No device trace containing the new identity/state events exists yet, so a
runtime state-reversion or controller-identity mismatch cannot honestly be
named as the proven cause. The source shows that a correct T3 completion proves
`lock_002.IsUnlocked` at the instant of validation; the remaining failure must
be either a subsequent state reset or an `OPEN` lookup/controller mismatch.

## FIX APPLIED

Diagnostic-only instrumentation was added. No drawer mapping, forced-open
behaviour, server code, or clue-note placement was changed by this audit.

## NEXT DEVICE TEST

Run A1 once: use `key_001` on `lock_002`, wait for T3, then issue `OPEN` for
the selected drawer. Export the contiguous events from `LOCK_USE_ATTEMPT`
through `DRAWER_OPEN_GATE`. A valid trace has the same controller instance and
`lock_is_locked:false`, `open_allowed:true`.
