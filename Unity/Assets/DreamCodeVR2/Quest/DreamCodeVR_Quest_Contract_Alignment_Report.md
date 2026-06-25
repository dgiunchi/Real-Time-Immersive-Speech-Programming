# Unity Quest Contract Alignment Report

## Files Modified
- `Assets/DreamCodeVR2/Quest/QuestInitialSetupAction.cs`
- `Assets/DreamCodeVR2/Quest/QuestClueSpec.cs`
- `Assets/DreamCodeVR2/Quest/QuestPlanApplier.cs`
- `Assets/DreamCodeVR2/Quest/QuestScenarioController.cs`
- `Assets/DreamCodeVR2/Quest/Resources/MockQuestPlans/MockQuestA_Ball.json`
- `Assets/DreamCodeVR2/Quest/Resources/MockQuestPlans/MockQuestB_Cube.json`
- `Assets/DreamCodeVR2/Quest/Resources/MockQuestPlans/MockQuestDebug.json`
- `Assets/DreamCodeVR2/Quest/Resources/MockQuestPlans/MockQuest_ServerContract.json`
- `Assets/DreamCodeVR2/Quest/DreamCodeVR_Quest_Contract_Audit.md`
- `Assets/DreamCodeVR2/Quest/DreamCodeVR_QuestPlan_JSON_Contract.md`

## Canonical Field Names
- `initial_setup[].object`
- `clues[].object`
- `tasks[].target`
- `tasks[].key`
- `tasks[].lock`
- `tasks[].object_to_create`
- `tasks[].target_anchor`

## Unity Parsing Strategy
- Unity now exposes a canonical JSON field `object` in both setup actions and clue specs.
- Internal code reads these through normalized `ObjectReference` accessors.
- Anchor resolution accepts both simple anchor names and server-style fully qualified placement keys.

## Backward Compatibility
- Legacy `object_id` is still parsed as fallback.
- Legacy usage logs a warning during deserialization.
- New mocks now use canonical `object` only.

## QuestPlanApplier Changes
- Setup actions now use normalized object references for:
  - `PlaceObject`
  - `HideObject`
  - `ShowObject`
  - `SetClueText`
  - `SetParent`
  - `ResetCreatedObject`
  - `SetMaterial`
- Clue updates now resolve from `clues[].object`
- Unique variable placement validation still works with canonical object references

## Mock JSON Updates
- Updated all existing mock plans to use `object`
- Added `MockQuest_ServerContract.json` as a canonical server-style example
- Updated setup anchor strings to tolerate server-style placement keys

## Manual Test Checklist
- `F6` scenario mode still cycles
- `F7` preview still works
- `F8` apply mock ball quest still works
- `F8` apply mock cube quest still works
- `F9` apply `MockQuest_ServerContract.json`
- Verify `SetClueText` works using `object`
- Verify `PlaceObject` works using `object`
- Verify duplicate variable placement using `object` is rejected
- Verify `ContextBridge` still works
- Verify `SceneContext` still works
- Verify STT still works

## Remaining Risks
- This alignment pass still relies on `JsonUtility`, so any future server contract changes should avoid shapes that need custom converters.
- Legacy `object_id` support is temporary and should be removed once the server and local templates are fully migrated.
