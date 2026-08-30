# Unity Quest UX, visibility, PTT and key interaction audit

## NATURAL PROPOSAL UI
The C1 proposal now prefers `original_utterance`, `utterance` or `transcript` and displays “You said: … Confirm this action?”. A structured participant-safe fallback is used otherwise.

## HUMAN READABLE OBJECT NAMES
`ParticipantFacingText` is the sole resolver: non-technical display name, semantic mapping, then “the object”. IDs/enums remain diagnostic-only.

## KEY PLACEMENT
Bootstrap registers table surface, cabinet top, drawer interiors and basket anchors. Missing placement emits `QUEST_INSTANCE_PLACEMENT_IGNORED`; there is no floor/world-origin fallback.

## FIXED QUEST OBJECT VISIBILITY
`QuestObjectVisibilityController` reversibly disables non-relevant puzzle objects only when the server provides `relevant_object_ids`. Furniture, anchors, locks and exit infrastructure stay active; reset/switch restores state.

## C3 OBJECT POOL
C3 stays fully visible unless the server explicitly sends `candidate_object_ids`; absent candidate data restores all.

## PTT MIC GAIN
`StudyConfiguration.pttMicGain` is researcher-configurable in range 1–4 (default 2) and affects only recording PCM.

## AUDIO SAFETY
Gain occurs in float before PCM16 conversion. `ApplyPttGain` limits samples to [-1, 1], preventing overflow/wrap.

## MIC DIAGNOSTICS
Every PTT recording emits `PTT_AUDIO_LEVEL` with input/output RMS/peak, gain and clipped count; raw audio is not logged.

## KEY LOCK FLOW
The existing C1 `USE_WITH` calls `QuestLockController.TryUseKey`: correct key unlocks, publishes the event and refreshes scene context; wrong key retains state and shows safe feedback.

## DRAWER AFTER UNLOCK
Existing `OPEN` checks the lock. After successful unlock the ordinary drawer path opens it; unlock alone does not move it.

## TESTS
Static source validation completed for protocol fields, proposal path, anchors, reversible visibility, C3 gating, limiter and key-lock path. Unity EditMode/device tests remain required because no Unity test runner is available in this workspace.

## NEXT DEVICE TEST
Test utterance/fallback proposals, key placement on table/cabinet/drawer, fixed `relevant_object_ids`, C3 with/without candidates, quiet 2x PTT, and correct/wrong key followed by drawer open.
