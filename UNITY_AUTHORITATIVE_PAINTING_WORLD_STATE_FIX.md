# Authoritative Painting World State Fix

## ROOT CAUSE

`QuestPaintingController` already detected physical painting alignment locally and published a quest event used by `QuestEventDrivenValidator` to complete T1. However, it did not emit an authoritative `QuestWorldStateEvent` carrying `semantic_state.painting_001 = "aligned"` before `ExperimentStateEvent / task_completed`.

Because the quest-event publish was synchronous, local task completion could be reported before the server received painting alignment evidence on the NID102 world-state channel.

## PAINTING STATE OWNER

The physical and semantic owner is `QuestPaintingController` in:

- `Unity/Assets/DreamCodeVR2/Quest/QuestWorldControllers.cs`

It owns:

- `TryAlign(...)`
- continuous aligned-pose detection in `Update()`
- `IsAligned`
- `AuthoringSemanticState` mutation for the painting

## PHYSICAL ALIGNMENT DETECTION

Physical alignment is confirmed in two places:

1. `QuestPaintingController.TryAlign(...)`
   This directly moves `painting_001` to `alignedAnchor` and now verifies the pose with the same tolerance check before publishing semantic state.

2. `QuestPaintingController.Update()`
   In non-C1 manipulation conditions, it observes the live transform and confirms alignment when:
   - position is within `alignmentPositionTolerance`
   - rotation is within `alignmentRotationTolerance`

The shared physical predicate is now centralized in:

- `QuestPaintingController.IsPhysicallyAligned()`

## SEMANTIC STATE

Painting semantic state is now explicitly tracked as:

- `aligned`
- `crooked`

`QuestPaintingController` updates both:

- `IsAligned`
- `AuthoringSemanticState.state`

and suppresses duplicate transition publication when the semantic state has not changed.

## QUEST WORLD EVENT

The existing NID102 world-state transport is reused through:

- `AuthoringProtocolClient.SendQuestWorldStateEvent(...)`

No new transport or wrapper was introduced.

The event type used is:

- `PAINTING_STATE_CHANGED`

## SERVER-COMPATIBLE PAYLOAD

The event payload is root-flat and server-compatible. The semantic payload shape is:

```json
{
  "type": "QuestWorldStateEvent",
  "protocol_version": 1,
  "event_type": "PAINTING_STATE_CHANGED",
  "object_id": "painting_001",
  "semantic_state": {
    "painting_001": "aligned"
  }
}
```

It does not use `world_state` wrapping and does not use an incompatible nested semantic object.

## SCENECONTEXT

On semantic transition, `QuestPaintingController` now performs:

1. semantic state update
2. `SceneContextTransmitter.SendSceneContextSnapshot(...)`
3. `QuestWorldStateReporter.PaintingStateChanged(...)`
4. local quest event publish

This preserves SceneContext publication while ensuring the explicit world-state event is also sent.

## ORDER BEFORE TASK_COMPLETED

For the painting alignment path, ordering is now:

1. physical pose reaches aligned anchor
2. semantic state becomes `aligned`
3. SceneContext snapshot is sent
4. root-flat `QuestWorldStateEvent` is emitted with `semantic_state.painting_001 = "aligned"`
5. `QuestEventType.ObjectStateChanged` is published
6. `QuestEventDrivenValidator` completes T1
7. `ExperimentStateEvent / task_completed` is sent

This removes the previous race where task completion could arrive without authoritative painting evidence.

## RESET

`ResetCrooked()` now clears painting semantic state back to `crooked` without emitting a duplicate per-transition event during canonical reset.

Canonical reset behavior remains preserved through the existing full reset world-state snapshot:

- `QuestWorldStateReporter.ResetCompleted(...)`

That snapshot already reports `painting_001: "crooked"` when the painting is reset.

## SET A/B/C

The fix is shared for all sets because it is attached to the canonical `painting_001` controller and uses normalized canonical set IDs from:

- `QuestCanonicalSetIds.Normalize(...)`

This supports:

- `set_a`
- `set_b`
- `set_c`

without set-specific branches.

## DIAGNOSTICS

Added/retained diagnostics include:

- `PAINTING_SEMANTIC_STATE_CHANGED`
- `QUEST_WORLD_STATE_WIRE_PAYLOAD`

`PAINTING_SEMANTIC_STATE_CHANGED` includes:

- `object_id`
- `old_state`
- `new_state`
- `physical_alignment_confirmed`
- `session_id`
- `canonical_set_id`

## TESTS

Added coverage verifies:

- painting alignment prepares a root-flat `QuestWorldStateEvent`
- payload contains `session_id`, `canonical_set_id`, `object_id`
- `semantic_state.painting_001 = "aligned"`
- no `world_state` wrapper is used
- world-state event is prepared before task completion for the same transition
- repeated aligned transitions do not spam duplicate events
- reset clears aligned semantic state back to `crooked`
- set normalization works for A/B/C

## NEXT DEVICE TEST

Run a new Set A C1 session and confirm the timeline:

1. physical painting alignment
2. `PAINTING_SEMANTIC_STATE_CHANGED`
3. `QUEST_WORLD_STATE_WIRE_PAYLOAD`
4. NID102 `QuestWorldStateEvent` with `semantic_state.painting_001 = "aligned"`
5. `TASK_COMPLETED`
6. NID102 `ExperimentStateEvent` with `event=task_completed`
7. server acceptance
8. `NextTaskGenerated(set_a:T2)`
9. `NextTaskActivationRequest(set_a:T2)`
