# C1 Global Capability and Quest UI Audit

## GLOBAL CAPABILITY MATRIX

| Object | Commands | Presets | Executor | Resolver aliases | Task use | Status |
|---|---|---|---|---|---|---|
| painting_001 | move_to_preset | aligned | QuestPaintingController | painting, picture | painting_aligned | fixed |
| six drawers | open, close | — | ExperimentalDrawerController | drawer | object_open/closed | validated at bootstrap |
| door_001 | open, close | — | QuestDoorController | door, exit door | door_open | validated at bootstrap |
| key_001/key_002 | use_with | — | QuestLockController secondary | golden/gold key; silver key | lock_unlocked | primary only |
| lock_001..003 | — | — | QuestLockController | scene labels | secondary only | not advertised |
| lamp_001..004 | activate, deactivate, toggle | — | QuestLampController | puzzle lamp N | active/inactive | fixed |
| sphere_001 | move_to_preset, place_in | soccer_ball | C1QuestSphereController/monitor | sphere labels | object_at_anchor | conditional C1 instance |
| basket_001 | — | — | QuestPlacementMonitor | scene labels | placement secondary | not advertised |

## PAINTING FIX

Painting now advertises canonical lower-case `move_to_preset` and `aligned`. The executor already
routes the command to QuestPaintingController; no duplicate executor was introduced.

## TARGET ALIASES

Runtime bootstrap adds painting/picture, door/exit door, golden/gold key, silver key, unique
table/desk/cabinet-drawer aliases (including ordinal forms), and unique puzzle-lamp labels.
Existing object labels remain intact.

## DRAWERS

All desk/cabinet drawers publish lower-case open/close and retain their existing controller and
anchor validation. No drawer motion code changed.

## DOOR

Door commands are published only after QuestDoorController setup. Controller state refresh and
locked-open rejection remain unchanged.

## KEYS AND LOCKS

Keys publish use_with; locks remain secondaries and do not advertise a primary command. Instance
bindings remain authoritative.

## LAMPS

Each lamp publishes activate/deactivate/toggle with a unique puzzle-lamp label and existing lamp
controller.

## C1 SPHERE/BASKET

Conditional C1 sphere publishes lower-case move_to_preset/place_in plus soccer_ball. Basket stays
a secondary receptacle with no inappropriate advertised primary command.

## CAPABILITY NORMALIZATION

Bootstrap normalizes all configured command strings and presets to lower-case canonical forms.
`ValidateC1Capabilities` logs invalid/empty/noncanonical commands or a missing preset for
move_to_preset once after bootstrap.

## ACTIVE TASK SOLVABILITY DIAGNOSTIC

Bootstrap capability validation prevents the current painting capability mismatch. Full per-task
semantic diagnostic remains dependent on canonical task success-condition payloads at runtime.

## PARTICIPANT QUEST UI

The participant UI now displays CURRENT TASK, canonical instruction, and Completed: N. It no
longer displays task number/total or future tasks. Quest runtime refreshes this view on start,
completion, advance, dynamic activation, and reset.

## RESEARCHER QUEST DIAGNOSTICS

The Advanced researcher panel shows fixed `Current Step N/Total`, task ID and task type; C3 shows
dynamic progression, runtime task ID and completed count. Participant UI remains limited to the
current instruction and completed count.

## FIXED QUEST TRANSPORT

The HTTP start reply is now treated as session identity only, matching the server reference.
For C1/C2, Unity waits for `NextTaskGenerated` plus `NextTaskActivationRequest` on NetworkId 101,
then converts the canonical `quest_instance`, task targets, lock bindings and C1 sphere setup into
the local runtime state. Later fixed tasks can arrive without another `quest_instance`; they append
to the same runtime history on their activation request. This prevents a valid A2 start from being
rejected just because the HTTP response has no nested `quest_instance`.

When a fixed task completes locally, Unity now sends its canonical task ID in the existing
`ExperimentStateEvent/task_completed`, so the server can issue the following task.

### Runtime correction (28 Aug)

Quest device logs showed the deployed server sending `NextTaskActivationRequest` without the
documented preceding `NextTaskGenerated`. Unity now activates a narrow canonical fallback for
the four known `*:T1` instances, and resets the previous run before making the HTTP request so a
fast NetworkId 101 activation cannot be erased by the later HTTP callback.

## RESET / UPDATE FLOW

Quest reset clears participant task text/count; starting or advancing a task republishes current
instruction only.

## REGRESSION STATUS

No NetworkId, STT, proposal YES/NO, C2/C3 authoring, or drawer motion changes were made.

## MANUAL QUEST TEST PLAN

Test painting/picture aliases and YES/NO, all drawers, correct/wrong keys, unique lamps, sphere
preset/basket, locked/unlocked door, participant current-task display, and C3 dynamic display.

## DEVICE-ONLY UNCERTAINTIES

Server resolver alias matching and device proposal routing require an end-to-end test. Unity
batchmode was not run.
