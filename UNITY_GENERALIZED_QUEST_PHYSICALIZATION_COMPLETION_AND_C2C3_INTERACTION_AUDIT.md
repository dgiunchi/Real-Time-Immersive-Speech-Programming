# Generalized quest physicalization and interaction audit

## RESOLVED QUESTINSTANCE

`ResolvedQuestInstance` and `QuestInstanceResolver` now provide the Unity-side resolved lock bindings, physical drawer target, placements, initial states and relevant IDs. `QuestInstanceController` resolves the instance once before applying lock bindings and placements.

## LEGACY NORMALIZATION

`QuestCanonicalIds` is the single compatibility surface for legacy lock IDs and task object IDs. The protocol converter delegates lock, drawer and generated-task normalization to it.

## A1/A2/B1/C1

The runtime validates resolved object IDs through the existing task validator and controller lookup. A1 legacy drawer wiring is resolved through the same canonical mapping; A2/B1/C1 retain server-provided physical bindings.

## TASK COMPLETION SEMANTICS

`RuntimeTaskValidator` remains authoritative. World controllers only publish canonical state events: `PAINTING_ALIGNED`, `OBJECT_AT_ANCHOR`, `LOCK_UNLOCKED`, `OBJECT_OPEN`, `OBJECT_ACTIVE`, and `DOOR_OPEN`.

## RUNTIME OBJECT CREATION

Existing sphere creation protects against duplicate canonical IDs and clears runtime spheres during reset. The present wire contract exposes `quest_setup`/`c1_setup`; future `required_runtime_objects` should be mapped into the same resolved runtime-object list when delivered by the server.

## NOTE READABILITY

Drawer note placement derives readable face and text-top vectors from the actual TMP hierarchy. It aligns the readable face to the support normal, resolves roll against the anchor forward direction, rests the renderer bounds on the support plane, and logs `QUEST_NOTE_READABILITY_ALIGNED`.

## DOOR HINGE

Bootstrap creates/reuses `DoorHingeAnchor`, derives a hinge edge from door bounds when none is authored, and computes the open pose by rotating the door centre around that hinge. It logs `DOOR_HINGE_CONFIGURED` and `DOOR_OPEN_POSE_VALIDATED`.

## ENVIRONMENT VISIBILITY

Generic relevance filtering is limited to puzzle keys, clue notes and runtime sphere. Lamps, painting and normal environmental fixtures remain visible unless explicitly changed by a quest state.

## PLACEMENT CAPACITY

Placement application detects multiple objects assigned to a non-multiple anchor. It logs `QUEST_PLACEMENT_CAPACITY_EXCEEDED` and refuses the later conflicting placement rather than stacking it silently.

## C1 KEY INTERACTION

C1 retains voice `USE_WITH` and puzzle keys remain non-grabbable through condition gating.

## C2/C3 GRABBABILITY

`setAffordance(grabbable=true)` continues to update the actual `ExperimentalGrabbableAdapter` on the canonical key. It does not complete a task.

## PHYSICAL KEY INSERTION

Every usable lock receives a trigger `KeyInsertionZone` beside `key_insert_anchor`. A physical insertion occurs only when a held, grabbable key is released while inside the zone and C1 is not active. The zone invokes the existing `QuestLockController.TryUseKey` path.

## WRONG KEY

Wrong keys invoke the same lock validator, remain unlocked from no state change, are not snapped, and retain their grabbability. Logs: `KEY_PHYSICAL_INSERT_REJECTED`.

## C1/C2/C3 SUCCESS PARITY

Voice and physical insertion both reach `QuestLockController.TryUseKey`, publish `LOCK_UNLOCKED`, and let `RuntimeTaskValidator` evaluate completion.

## SCENE CONTEXT

Existing authoritative state transitions retain SceneContext refresh calls. The physical interaction path reuses the lock’s snapshot publication rather than emitting snapshots while the key is moved continuously.

## RESET

Existing reset restores inserted keys, lock state, drawer state, door state, visibility, runtime spheres and grabbability. Insertion zones contain no persistent success state and clear their overlap set when disabled.

## TESTS

Existing edit-mode tests cover canonical A1 conversion and causal completion. New device validation is required for the trigger-based release lifecycle and hinge geometry; Unity batch compilation was not available in this workspace.

## NEXT DEVICE TEST

1. C1: voice USE_WITH correct/wrong key.
2. C2/C3: author key grabbable, release correct/wrong key in zone.
3. Verify `KEY_INSERT_ZONE_*` and `KEY_PHYSICAL_INSERT_*` logs.
4. Verify note face/top alignment values are positive.
5. Verify door hinge edge stays fixed and `DOOR_OPENED` is emitted.
