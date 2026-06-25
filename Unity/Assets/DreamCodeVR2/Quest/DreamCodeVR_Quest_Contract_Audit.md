# DreamCodeVR Quest Contract Audit

## Unity Data Model Field Names
- `QuestInitialSetupAction`
  - canonical JSON field: `object`
  - legacy fallback field: `object_id`
  - internal normalized accessor: `ObjectReference`
- `QuestClueSpec`
  - canonical JSON field: `object`
  - legacy fallback field: `object_id`
  - internal normalized accessor: `ObjectReference`
- `QuestTaskSpec`
  - `target`
  - `key`
  - `lock`
  - `object_to_create`
  - `target_anchor`

## QuestPlanApplier Expectations
- Initial setup actions resolve objects through `action.ObjectReference`
- Clue specs resolve objects through `clue.ObjectReference`
- Tasks continue to use:
  - `target`
  - `key`
  - `lock`
  - `object_to_create`
  - `target_anchor`
- Placement anchors are accepted as:
  - simple anchor names like `drawer_inside_anchor`
  - server-style placement keys like `cabinet_drawer_001.drawer_inside_anchor`

## Mock JSON Field Names
- `MockQuestA_Ball.json`
  - uses canonical `initial_setup[].object`
  - uses canonical `clues[].object`
- `MockQuestB_Cube.json`
  - uses canonical `initial_setup[].object`
  - uses canonical `clues[].object`
- `MockQuestDebug.json`
  - uses canonical `initial_setup[].object`
  - uses canonical `clues[].object`
- `MockQuest_ServerContract.json`
  - uses canonical `initial_setup[].object`
  - uses canonical `clues[].object`

## Previous Mismatch With Server Contract
- Unity originally stored setup/clue references as `object_id`.
- The server canonical contract now uses `object`.
- Without normalization, `JsonUtility` would not populate setup/clue object references from server JSON.

## Current Alignment Status
- Canonical JSON field supported: `object`
- Temporary legacy fallback supported: `object_id`
- Legacy usage now logs a warning during plan deserialization.
