# DreamCodeVR UI Simplification Report

## Files Modified
- `Assets/DreamCodeVR2/UI/DreamCodeVRAuthoringUIController.cs`
- `Assets/DreamCodeVR2/UI/DreamCodeVRAuthoringUIBootstrap.cs`
- `Assets/DreamCodeVR2/UI/DreamCodeVRSpeechStatusBridge.cs`
- `Assets/DreamCodeVR2/Quest/QuestScenarioController.cs`

## UI Elements Removed or Hidden
- The main user-facing panel no longer shows quest title details.
- The main user-facing panel no longer shows the old `Objective:` label.
- The Inspect panel is hidden by default.
- The Quest Preview panel is hidden by default.
- The separate feedback card is hidden by default.
- The extra selected/scenario lines are hidden by default and only remain available for debug display.

## Pointed Field Retained
- `Pointed` remains visible in the main `DreamCodeVR Authoring` panel.
- It still updates through the existing `InteractionContextProvider` and pointing pipeline.
- It prefers `AIEditableObject.displayName` and falls back to `objectId`.

## New Compact Layout
- `DreamCodeVR Authoring`
- `Pointed: <name or none>`
- `Current task: X / N`
- `<one concise task instruction>`
- `Speech: ...`
- `Feedback: ...`

## QuestRuntimeState Integration
- The compact task area is driven from `QuestRuntimeState`.
- The UI now shows:
  - current task number
  - total task count
  - concise user-facing task instruction
- When the quest completes, the task instruction switches to `Quest completed.`

## Feedback Simplification
- Main feedback is now shown as one short line in the compact panel.
- Verbose messages are simplified before display.
- Examples:
  - `Quest received.`
  - `Quest applied.`
  - `Task completed.`
  - `Try again.`
  - `Server unavailable.`
- Detailed logs remain in the Unity Console.

## Inspect Panel Handling
- Inspect data is still updated internally.
- The Inspect panel is not shown in normal user-facing mode.
- A debug toggle remains available if inspection UI is needed later.

## Debug Mode
- `showDebugQuestDetails = false` by default
- `showInspectPanel = false` by default
- `debugAlwaysShowAllPanels` still works
- When debug mode is enabled, the old detail panels can still be surfaced for development

## Speech Simplification
- Compact speech states are now more user-facing:
  - `Speech: Ready`
  - `Speech: Listening...`
  - `Speech: Processing...`
  - `Speech: Heard: "..."`
  - `Speech: No speech detected`
  - `Speech: Error`

## Manual Test Checklist
1. Open `DreamCodeVR2_EscapeRoom_Testbed`.
2. Press Play.
3. Confirm the main UI appears.
4. Confirm `Pointed` is visible in the main `DreamCodeVR Authoring` panel.
5. Point at `cabinet_drawer_001` or another object and verify `Pointed` updates.
6. Confirm `Quest` and `Objective` labels are not shown in the main panel.
7. Confirm the `Inspect` panel is not shown in normal mode.
8. Start server on public HTTP port `50001`.
9. Press `F10` and verify UI shows a compact quest received/preview status.
10. Press `F11` and verify UI shows current task `1 / N`.
11. Press `F2` and verify Console progress still works.
12. Press `F3` and verify UI advances to task `2 / N`.
13. Press `F3` until quest complete and verify UI shows quest completed.
14. Press `F4` and verify UI resets cleanly.
15. Press `F12` and verify request+apply updates UI.
16. Test fixed, ball, cube, and `llm_generated_v1` modes.
17. Verify speech status still updates.
18. Verify ContextBridge still works.
19. Verify SceneContext still works.
20. Verify STT still works.
21. Verify no `object_id` or QuestPlan contract changes.

## Remaining Risks
- I did not run a Unity compile from here, so this still needs a quick in-Editor verification pass.
- The hidden debug panels still exist in the hierarchy; this pass changes user-facing display behavior, not the underlying data flow.
- `F2` still logs the full runtime summary to Console, while the compact UI intentionally shows only shortened feedback.
