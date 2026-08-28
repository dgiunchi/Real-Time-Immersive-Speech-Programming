# C1/C2/C3 Quest Runtime Completion Audit

## C1 EXECUTORS

`PredefinedVoiceCommandExecutor` executes only verbs exposed by `VoiceCommandCapabilities`.
Configured coverage is OPEN/CLOSE for drawers and doors, MOVE_TO_PRESET for the painting,
USE_WITH for keys and locks, and ACTIVATE/DEACTIVATE/TOGGLE for lamps. Physical pickup remains
an XR-hand interaction and is not advertised as a C1 voice command. No ball-specific C1 verb is
advertised because no server command contract for it is present.

## USE_WITH

The primary object must be key-compatible and the secondary object must carry a
`QuestLockController`. The controller checks the active configured `requiredKeyId`; a wrong key
does not modify lock state and returns a failed correlated predefined-command acknowledgement.

## LOCK/DRAWER/DOOR STATE

Locks explicitly expose `associatedTargetObjectId`. `OPEN` resolves that binding and rejects a
locked drawer before starting its existing anchor motion. Door `door_001` directly references
`lock_001`; unlocking and opening are separate state changes. Quest instances may replace every
key/lock/target binding at runtime.

## PAINTING

`MOVE_TO_PRESET` only invokes `QuestPaintingController.TryAlign`. The controller applies the
authored aligned anchor, sets `IsAligned`, reveals its configured clue, publishes a state event,
and refreshes scene context.

## BALL/BASKET

Authoring-created spheres preserve a requested stable runtime object ID, receive Rigidbody plus
the existing grabbable adapter, and can therefore be used by a task referring to that ID.
`basket_001.basket_inside_anchor` remains authoring-restricted and now has a trigger receptacle;
only physical placement invokes `QuestPlacementMonitor` and `OBJECT_AT_ANCHOR` validation.

## LAMPS

Each configured lamp has an individual `QuestLampController`; ACTIVATE, DEACTIVATE, and TOGGLE
change its real state and publish `is_lamp_active` through SceneContext.

## C2 AUTHORING EXECUTION

The existing capability/QuestIntegrity gate remains authoritative. Supported operations are only
those already allowed on the target/anchor (create, property, affordance, relocate, behavior,
link, and semantic state). Unsupported material/texture requests are rejected with an
`AuthoringAck`; no capability was broadened merely because the wire protocol can name it.

## C2/C3 CONFIRMATION CORRELATION

Proposal IDs remain the action IDs used by the protocol. Confirmation/rejection logs retain this
ID, and a C1 server rejection clears only the matching proposal. Authoring execution acknowledgements
are emitted with the action ID returned by the local executor; stale server status messages do not
alter a different displayed proposal.

## C3 TASK ACTIVATION

Generated tasks are stored separately from C1/C2 proposals. Activation validates the wire task,
replaces the participant instruction, sends `NextTaskAck(activated)`, and does not emit completion.

## SUCCESS CONDITION ENGINE

`RuntimeTaskValidator` is the centralized evaluator. It supports canonical painting, reveal, held,
anchor placement, open/closed, unlocked, active/inactive, door-open, authoring-created, and
authoring-property-set predicates, plus existing compound predicates. `NextTaskWireConverter`
parses the matching server string vocabulary.

## TASK COMPLETION

Dynamic tasks are evaluated from world state on quest events and immediately after activation.
All conditions must pass before `QuestRuntimeState` completes the task. The dynamic controller
records the completed task ID and sends exactly one `ExperimentStateEvent(task_completed)` for it.

## QUEST RESET

Reset now removes runtime-created objects, restores snapshots and drawers, resets locks, door,
painting/clue visibility, lamps, authoring property markers, dynamic task state, undo history, and
pending proposal state.

## LOGGING

Added/retained event families distinguish predefined proposal/confirmation/execution/failure,
authoring confirmation/execution, task activation/condition completion, lock use/unlock/wrong-key,
painting alignment, lamp change, door opening, and physical anchor placement. No PCM logging is
introduced.

## MANUAL QUEST TEST PLAN

- C1: OPEN/CLOSE table, desk and cabinet drawers; paint alignment; correct/wrong key-lock;
  activate/toggle lamps; unlock/open exit door.
- C2: proposal YES/NO, success/failure acknowledgements, create a sphere with stable ID, and place
  it physically in the basket.
- C3: first generated task activation, one completion event, next-task activation, and a task that
  references a runtime-authored object.
- Session: reset/restart and Ubiq reconnect, confirming no state leaks between conditions.

## REMAINING MANUAL SCENE ACTIONS

Configure every non-exit key/lock/drawer association through the active `QuestInstance`; there is
intentionally no name-based fallback. Confirm basket trigger dimensions against the final basket
mesh in the editor and verify painting/door anchors remain manually separated.

## STATIC UNCERTAINTIES

Unity was not run in batch mode and this workspace has no standalone C# compiler configured.
The code received static source and whitespace checks only; editor compilation and the XR physical
placement path still require the manual test plan above.
