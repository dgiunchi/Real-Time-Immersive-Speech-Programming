# DreamCodeVR Quest Clue Text Dedup Report

## Files Modified
- `Assets/DreamCodeVR2/Quest/QuestPlanApplier.cs`
- `Assets/DreamCodeVR2/Quest/Resources/MockQuestPlans/MockQuest_ServerContract.json`
- `Assets/DreamCodeVR2/Quest/Resources/MockQuestPlans/MockQuestA_Ball.json`
- `Assets/DreamCodeVR2/Quest/Resources/MockQuestPlans/MockQuestB_Cube.json`
- `Assets/DreamCodeVR2/Quest/Resources/MockQuestPlans/MockQuestDebug.json`
- `Assets/DreamCodeVR2/Quest/DreamCodeVR_QuestPlan_JSON_Contract.md`

## Behavior Before
- `ApplyQuestPlan` always applied every `initial_setup` action.
- It then applied every entry in `clues[]`.
- Plans containing `SetClueText` in both places produced duplicate clue text updates and duplicate logs.

## Behavior After
- `clues[]` is treated as the canonical source for clue note text.
- `initial_setup` continues to apply all physical/setup actions.
- Legacy `SetClueText` actions in `initial_setup` are skipped when the same clue object already exists in `clues[]`.
- If legacy `SetClueText` exists without a matching `clues[]` entry, it still applies as fallback.
- If both sources define different text for the same clue object, Unity logs a warning and prefers `clues[]`.

## Deduplication Strategy
- Build a canonical clue-text map from `plan.clues`.
- Apply `initial_setup` actions.
- When a legacy `SetClueText` action is encountered:
  - skip it if the object already exists in `clues[]`
  - warn if the texts differ
  - apply it only when `clues[]` has no entry for that object
- Apply `plan.clues` once after setup.

## Mock JSON Updates
- Removed `SetClueText` from `initial_setup` in:
  - `MockQuest_ServerContract.json`
  - `MockQuestA_Ball.json`
  - `MockQuestB_Cube.json`
  - `MockQuestDebug.json`
- Kept clue note text exclusively in `clues[]`.
- Updated `MockQuest_ServerContract.json` to better mirror current server-style task names and fully qualified anchors.

## Manual Test Checklist
- Press `F7` and verify preview still works.
- Press `F8` and verify mock apply still works.
- Press `F9` and verify `MockQuest_ServerContract.json` applies successfully.
- Verify `SetClueText` is logged at most once per clue note.
- Verify `clue_note_001` text is updated.
- Verify `clue_note_002` text is updated.
- Verify `key_001`, `key_002`, and `clue_note_002` placement still works.
- Verify `ContextBridge` still reports `pointed_object`.
- Verify `SceneContext` still sends object snapshots.
- Verify STT still works.

## Remaining Risks
- Legacy plans that still rely on `initial_setup.SetClueText` remain supported, but should be migrated to `clues[]`.
- This pass assumes clue objects are uniquely identified by object id and not duplicated in scene content.
