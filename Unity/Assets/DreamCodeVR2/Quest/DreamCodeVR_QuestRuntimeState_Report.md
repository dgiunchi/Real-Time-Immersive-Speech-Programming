# DreamCodeVR Quest Runtime State Report

## Files Added
- `Assets/DreamCodeVR2/Quest/QuestRuntimeState.cs`
- `Assets/DreamCodeVR2/Quest/QuestTaskValidator.cs`
- `Assets/DreamCodeVR2/Quest/DreamCodeVR_QuestRuntimeState_Report.md`

## Files Modified
- `Assets/DreamCodeVR2/Quest/QuestScenarioController.cs`
- `Assets/DreamCodeVR2/UI/DreamCodeVRAuthoringUIController.cs`
- `Assets/DreamCodeVR2/UI/DreamCodeVRAuthoringUIBootstrap.cs`

## Runtime State Model
- `QuestRuntimeState` stores the active `QuestPlan`, ordered per-task runtime entries, current task index, completion counts, failure counts, completion state, and latest task result text.
- Task statuses are tracked with `QuestTaskStatus`:
  - `NotStarted`
  - `Active`
  - `Completed`
  - `Failed`
  - `Skipped`
- Starting a quest marks task 1 as `Active` when tasks exist.

## Integration with QuestScenarioController
- Applying a quest now starts `QuestRuntimeState`.
- This is wired for:
  - local mock apply
  - server contract mock apply
  - server quest apply
- Runtime logs now report:
  - quest started
  - current task index and objective
  - manual task completion
  - quest completion

## Debug Keys
- `F2`: print current quest progress summary and validator note
- `F3`: manually complete the current task and advance
- `F4`: reset active quest progress
- Existing keys `F6` to `F12` remain unchanged

## UI Behavior
- Compact UI now shows:
  - active quest title
  - current progress (`completed/total`)
  - current objective
- Latest runtime result is routed through the existing feedback/status card.
- No changes were made to STT, ContextBridge, SceneContext, or Ubiq networking.

## QuestTaskValidator v0
- `QuestTaskValidator` was added as a deterministic skeleton.
- It does not fake behavior execution.
- Current implementation keeps supported task types manual for now and explicitly points to `F3` as the debug completion path.
- `ReadClue` is not auto-completed because there is no persistent inspection event/state wired yet.

## Future Hook Surface
- `QuestRuntimeState` exposes placeholder hook methods/events for:
  - `OnSceneActionApplied(actionResult)`
  - `OnObjectInspected(objectId)`
  - `OnObjectCreated(objectId)`
  - `OnObjectPlaced(objectId, anchorId)`
  - `OnObjectUnlocked(targetId, keyId)`
  - `OnTaskCompleted(task)`

## Remaining Limitations
- Task completion is still manual in this pass.
- No automatic observation of object movement, clue reading, object placement, or unlocking is implemented yet.
- The validator is intentionally conservative until scene events are formalized.

## Next Recommended Step
- Wire real scene events into `QuestRuntimeState` and then upgrade `QuestTaskValidator` one task type at a time using observable scene state.

## Manual Test Checklist
1. Start server on public HTTP port `50001`.
2. In Unity set `QuestPlannerClient.serverBaseUrl` to `http://130.136.2.161:50001`.
3. Press Play.
4. Press `F10` to request quest.
5. Press `F11` to apply quest.
6. Verify `QuestRuntimeState` starts.
7. Verify current task is task 1.
8. Press `F2` and verify progress logs.
9. Press `F3` and verify current task advances.
10. Press `F3` repeatedly until quest completes.
11. Verify quest completed state.
12. Press `F4` and verify quest progress resets.
13. Repeat with fixed, ball, cube, and `llm_generated_v1` modes.
14. Verify `F7`/`F8`/`F9` still work.
15. Verify `F10`/`F11`/`F12` still work.
16. Verify ContextBridge still works.
17. Verify SceneContext still works.
18. Verify STT still works.
