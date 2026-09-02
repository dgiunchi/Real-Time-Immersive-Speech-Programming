# DreamCodeVR2 client — current state recap

This document records the client state after the latest Set A C1 device run (`client_20260901T231932Z_run.jsonl`). It describes implemented client behavior and the currently proven external blocker.

## Canonical reset and initial task

- `QuestResetRequest` on NID101 is received, parsed and applied for canonical Set A.
- The client creates/positions `sphere_001` at `cabinet_drawer_003.drawer_inside_anchor`.
- The initial runtime profile is read from `quest_instance.required_runtime_objects[].sphere_profile`.
- The existing `C1QuestSphereController.TrySetProfile` primitive applies the visual and semantic profile before `QUEST_CANONICAL_SET_APPLIED` and `RESET_COMPLETED`.
- The newest C1 run confirms `sphere_profile=football` in `QUEST_CANONICAL_SET_APPLIED`.
- Initial T1 is delivered as `NextTaskGenerated(set_a:T1)` plus `NextTaskActivationRequest(set_a:T1)` and is activated successfully.

## Fixed task progression client behavior

- Fixed C1/C2 tasks are handled as a server stream on NID101.
- The client validates a task from actual world state and sends `ExperimentStateEvent / task_completed` on NID102.
- A single received task is no longer presented as an entire completed quest. Instead, after completion the client waits for the server-approved successor and shows `Preparing the next objective...`.
- A subsequent task-only `NextTaskGenerated` is designed to use `QuestInstanceController.ActivateServerTask` and preserve the current world rather than perform a complete reset.

## Current proven progression blocker

In the latest Set A C1 run, T1 completed at `2026-09-01T23:19:50.1897010Z`, and Unity sent the required completion event at `23:19:50.1933450Z`.

No `NextTaskGenerated(set_a:T2)` and no `NextTaskActivationRequest(set_a:T2)` reached the client. NID101 remained healthy because normal voice command proposal/execution messages continued to arrive afterward.

The current blocker is therefore server progression output, classified as `NEXT_TASK_NOT_RECEIVED`. No local task should be synthesized on the client.

## Lock visibility and reset state

- Locks marked `inactive` by the canonical setup remain visually present on their drawers.
- A semantic inactivity marker preserves `inactive` in `RESET_COMPLETED` without deactivating the authored lock GameObject.
- Inactive locks remain physically locked, avoiding a visual state change at START.

## Materials and reset stability

- `ExperimentalPlaythroughReset` captures renderer references rather than depending on child-renderer order at restore time.
- This prevents reparented quest objects (keys, notes and runtime objects) from causing a color/material restoration to affect a different renderer.
- Materials without `_BaseColor` or `_Color`, including TMP materials, are safely excluded from colour restore/read operations.

## Interaction and world-object state

- Runtime sphere creation uses canonical placement anchors and closes its containing drawer when appropriate.
- SceneContext reports the actual `sphere_profile` from `C1QuestSphereController`.
- Set C keeps its consequence-driven `SET_SPHERE_PROFILE` path; no profile mutation is triggered merely by Set A drawer discovery.
- Lock, drawer, door, key-insertion and placement diagnostics remain available in the device JSONL logs.

## Required server behavior

After a valid event such as:

```json
{ "type": "ExperimentStateEvent", "event": "task_completed", "task_id": "set_a:T1" }
```

the server must emit, in order:

1. `NextTaskGenerated` for `set_a:T2`;
2. `NextTaskActivationRequest` for `set_a:T2`.

Ordinary task transitions should not send a new `QuestResetRequest` or a full world configuration unless a full canonical reset is intentional.

## Next device verification

1. Start Set A C1 and verify initial football sphere state.
2. Complete T1.
3. Verify logs show `NextTaskGenerated(set_a:T2)`, then `NextTaskActivationRequest(set_a:T2)`.
4. Verify the participant UI changes from `Preparing the next objective...` to the T2 instruction.
5. Verify no extra reset and no sphere-profile consequence occurs between A-T1 and A-T2.
