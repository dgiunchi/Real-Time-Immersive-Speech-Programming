# Researcher Quest Selection Audit

## UI CHANGES

The existing world-space Researcher Panel now has compact Quest Set and Quest Instance sections
under Condition. Set labels are A — Ball & Drawer, B — Search & Locks, and C — Alternate Key +
Lamp; compatible instance buttons display A1, A2, B1, and C1. Existing button construction and
selection highlighting are reused.

## SELECTION STATE

`ExperimentConditionManager` now keeps pending `selectedQuestSetId` and
`selectedQuestInstanceId` separately from `activeQuestSetId` and `activeQuestInstanceId`.
Clicking choices changes only pending state. The active values update after a successful start or
restart response.

## START PAYLOAD

The existing researcher-control start/restart requests now include `condition`, `peerUUID`,
`questSetId`, and `questInstanceId` for C1/C2. C3 uses a dedicated minimal serializable body with
only condition and peer UUID, so no fixed quest fields are serialized.

## C1/C2 RULES

C1/C2 Start is blocked before the HTTP request if no set is selected, no instance is selected, or
the instance is not part of the selected set. The panel gives concise selection feedback.

## C3 RULE

Selecting C3 hides the fixed Quest Set and Quest Instance sections. C3 remains selectable and
startable without fixed quest configuration; READY displays Dynamic progression rather than A/B/C.

## READY DISPLAY

READY now displays active condition plus active set/instance for C1/C2. Before READY it shows the
pending selection. END/RESET clears active values while retaining the pending selection.

## ERROR HANDLING

HTTP start/restart errors, health failures, and status mismatches keep READY false, emit a
`RESEARCHER_SESSION_START_FAILED` log, and display the concise server error where available.

## LOGGING

The panel logs `RESEARCHER_CONDITION_SELECTED`, `RESEARCHER_QUEST_SET_SELECTED`,
`RESEARCHER_QUEST_INSTANCE_SELECTED`, `RESEARCHER_SESSION_START_REQUEST`,
`RESEARCHER_SESSION_READY`, and `RESEARCHER_SESSION_START_FAILED` with exact IDs.

## MANUAL TEST PLAN

Test C1 and C2 with A1, A2, B1, C1; verify the exact payload reaches the server, task T1 matches
the selected instance, and changes apply only to the next Start/Restart. Test C3 with controls
hidden and no fixed fields. Recheck left-Y opening, XR hover/click, START/READY, and PTT readiness.

## REGRESSION STATUS

No STT/PTT, Ubiq message protocol/NetworkId, quest, command, XR-ray, or START/READY control-flow
was redesigned. Unity batchmode was not run; compilation and on-device XR interaction remain manual
verification steps.
