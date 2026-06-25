# DreamCodeVR QuestPlannerClient Report

## Files Added
- `Assets/DreamCodeVR2/Quest/QuestPlannerClient.cs`
- `Assets/DreamCodeVR2/Quest/DreamCodeVR_QuestPlannerClient_Report.md`

## Files Modified
- `Assets/DreamCodeVR2/Quest/QuestScenarioController.cs`

## Server URL Configuration
- `QuestPlannerClient` exposes:
  - `serverBaseUrl`
  - `endpointPath`
  - `defaultMode`
  - `defaultTemplate`
  - `requestTimeoutSeconds`
- Default endpoint:
  - `http://localhost:3002/api/quest/generate`

## Peer UUID Strategy
- First preference: reuse `RoomClient.Me.uuid` if available.
- Fallback: generate and persist a local UUID in `PlayerPrefs`.
- Final emergency fallback in request serialization remains `test-peer` only if the resolved uuid is empty.

## Unity Editor Localhost Note
- In the Unity Editor, `http://localhost:3002` correctly points to the local development machine running the server.

## Quest / Android LAN IP Note
- On Quest/Android, `localhost` refers to the headset itself, not the PC.
- For headset testing, set `serverBaseUrl` to a LAN IP such as:
  - `http://192.168.1.45:3002`

## Keyboard Shortcuts
- `F6`: cycle local scenario mode
- `F7`: preview local/current mock
- `F8`: apply local/current mock
- `F9`: apply canonical local server-contract mock
- `F10`: request QuestPlan from server and preview it
- `F11`: apply the last received server QuestPlan
- `F12`: request QuestPlan from server and apply it immediately

## Request Modes Supported
- `fixed`
- `debug_template` with template `ball`
- `debug_template` with template `cube`
- `llm_generated_v1`

## Manual Test Checklist
1. Start the server diagnostic endpoint on port `3002`.
2. In Unity Editor, set `serverBaseUrl` to `http://localhost:3002`.
3. Press Play.
4. Press `F10` with `serverQuestMode=fixed`.
5. Verify quest preview appears or logs.
6. Press `F11`.
7. Verify setup is applied.
8. Press `F10/F11` with `serverQuestMode=debug_template` and `serverQuestTemplate=ball`.
9. Press `F10/F11` with `serverQuestMode=debug_template` and `serverQuestTemplate=cube`.
10. Press `F10/F11` with `serverQuestMode=llm_generated_v1`.
11. Press `F12` and verify request plus apply works.
12. Confirm no `object_id` errors.
13. Confirm no duplicate `SetClueText`.
14. Confirm `ContextBridge` still works.
15. Confirm `SceneContext` still works.
16. Confirm STT still works.

## Remaining Risks
- This pass assumes the endpoint returns a top-level `QuestPlan` JSON object rather than a wrapper envelope.
- If the server response shape changes, `QuestPlannerClient` may need a small response adapter.
- The current request mode/template are inspector-driven and intentionally simple for debugging.
