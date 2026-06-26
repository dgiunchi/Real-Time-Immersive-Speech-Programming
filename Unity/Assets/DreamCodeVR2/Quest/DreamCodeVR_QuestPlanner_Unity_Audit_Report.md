# DreamCodeVR Quest Planner Unity Audit Report

## Scripts Found
- `QuestScenarioController.cs`
- `QuestPlannerClient.cs`
- `QuestPlanApplier.cs`
- `QuestScenarioMode.cs`
- `QuestPlan.cs`
- `Resources/MockQuestPlans/MockQuestA_Ball.json`
- `Resources/MockQuestPlans/MockQuestB_Cube.json`
- `Resources/MockQuestPlans/MockQuestDebug.json`
- `Resources/MockQuestPlans/MockQuest_ServerContract.json`
- `DreamCodeVRAuthoringUIBootstrap.cs`
- `DreamCodeVRAuthoringUIController.cs`

## Compile Status
- `QuestScenarioController` exists and is part of the active Unity scripts set.
- `QuestPlannerClient` exists and is part of the active Unity scripts set.
- `QuestPlanApplier` exists and is part of the active Unity scripts set.
- This audit assumes normal Unity compile state because the recent import/build errors tied to these files were already fixed before this pass.

## Scene Attachment Status
- `DreamCodeVR2_EscapeRoom_Testbed.unity` does not contain serialized scene objects with:
  - `QuestScenarioController`
  - `QuestPlannerClient`
  - `QuestPlanApplier`
- The scene file also does not contain:
  - `DreamCodeVR_AuthoringUI`
  - `DreamCodeVR_QuestRuntime`
- That is expected because these services were being created at runtime, not authored into the scene.

## Prefab Attachment Status
- No prefab or authored asset under `Assets/` was found with:
  - `QuestScenarioController`
  - `QuestPlannerClient`
  - `QuestPlanApplier`
- Quest mocks exist only as JSON resources, not as prefabs.

## Current Shortcut Implementation Status
- `QuestScenarioController.Update()` implements:
  - `F6`
  - `F7`
  - `F8`
  - `F9`
  - `F10`
  - `F11`
  - `F12`
- Therefore the shortcut logic is present in code and depends on a live `QuestScenarioController` instance at runtime.

## Why F7/F8/F9 Were Working
- `DreamCodeVRAuthoringUIBootstrap` uses `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`.
- On scene start in Play mode, it created `DreamCodeVR_AuthoringUI` dynamically.
- In earlier integration passes, that same runtime object also received:
  - `QuestScenarioController`
  - `QuestPlannerClient`
  - `QuestPlanApplier`
- That is why local quest shortcuts could work even though no authored scene GameObject visibly contained those components before Play.

## Current Bootstrap / Runtime-Service Pattern
- This project already uses runtime bootstrap patterns.
- Relevant example:
  - `DreamCodeVRAuthoringUIBootstrap`
  - `InteractionContextTransmitter` / `SceneContextTransmitter` use dynamic `Find` and room-client resolution at runtime
- Quest runtime therefore fits the existing pattern better than a manual scene-only setup.

## Recommended Minimal Fix
- Use an explicit quest runtime bootstrap path rather than hiding quest runtime components on the UI root.
- Create or ensure a dedicated runtime GameObject:
  - `DreamCodeVR_QuestRuntime`
- Auto-attach and wire:
  - `QuestScenarioController`
  - `QuestPlannerClient`
  - `QuestPlanApplier`
  - `RuntimeCreatableObjectCatalog` if missing
- Keep the existing UI bootstrap, but make quest runtime creation explicit and logged.

## Implemented Minimal Fix
- Updated `DreamCodeVRAuthoringUIBootstrap` so that:
  - UI still bootstraps when missing
  - quest runtime is now ensured explicitly
  - a dedicated runtime root named `DreamCodeVR_QuestRuntime` is created if needed
  - references are auto-wired
- Added logs:
  - `[QuestRuntimeBootstrap] Created DreamCodeVR_QuestRuntime`
  - `[QuestRuntimeBootstrap] Found existing QuestScenarioController`
  - `[QuestRuntimeBootstrap] Attached QuestPlannerClient`
  - `[QuestRuntimeBootstrap] Wired QuestPlannerClient into QuestScenarioController`

## Manual Setup Instructions
- Open `DreamCodeVR2_EscapeRoom_Testbed`
- Press Play
- Confirm `DreamCodeVR_AuthoringUI` exists
- Confirm `DreamCodeVR_QuestRuntime` exists
- Confirm `QuestScenarioController` is on `DreamCodeVR_QuestRuntime`
- Confirm `QuestPlannerClient` is on `DreamCodeVR_QuestRuntime`
- Configure `QuestPlannerClient.serverBaseUrl` if needed
- Use:
  - `F6/F7/F8/F9` for local quest flow
  - `F10/F11/F12` for server quest flow

## Risks
- Because the quest runtime is still runtime-created, it will not appear in the scene hierarchy until Play mode starts.
- If someone manually adds duplicate quest runtime components to the scene later, the bootstrap depends on `FindFirstObjectByType` and may wire to the first instance found.
- `QuestPlannerClient` still assumes the server returns a top-level `QuestPlan` JSON object rather than a wrapped envelope.
