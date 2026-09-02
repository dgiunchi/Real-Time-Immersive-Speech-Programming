1. `exact root cause`
   `PaintingAlignedAnchor` and `PaintingCrookedAnchor` were configured under the moving `painting_001` hierarchy in the current scene. `TryAlign` moved the painting root to the aligned anchor world pose, then `IsPhysicallyAligned()` immediately compared the same root against an anchor that had moved with it. This produced a false negative even though the painting looked visually straight.

2. `transform moved by TryAlign`
   The moved transform is the `QuestPaintingController` host transform, i.e. the `AIEditableObject` root for `painting_001`.

3. `transform validated by IsPhysicallyAligned`
   `IsPhysicallyAligned()` validates the same `painting_001` root transform against `alignedAnchor`. There was no root-vs-visual-child mismatch in the execution path.

4. `alignedAnchor configuration`
   Assigned: yes.
   Scene path before patch: `painting_001/PaintingAlignedAnchor`.
   Parent before patch: `painting_001` transform.
   Active: yes.
   Scene local rotation before patch: approximately `z = -30` degrees.
   Scene local position before patch: zero relative offset.
   Runtime fix: detach both painting anchors from the moved painting hierarchy while preserving their world pose, so they become stable world-space references.

5. `position error before patch`
   Approximately `0.0` metres in the failing scene-style setup, because the aligned anchor had zero local position offset under the painting root.

6. `rotation error before patch`
   Approximately `30.0` degrees in the failing scene-style setup. The real scene has the painting at about `z = +30` degrees and the aligned child anchor at about `z = -30` local, so after moving the root to the anchor world rotation, the child anchor ended up another `30` degrees away.

7. `tolerance values`
   Position tolerance: `0.06` metres.
   Rotation tolerance: `8` degrees.

8. `exact patch`
   In [QuestWorldControllers.cs](C:/Users/Scianso/Documents/GitHub/Real-Time-Immersive-Speech-Programming/Unity/Assets/DreamCodeVR2/Quest/QuestWorldControllers.cs), `QuestPaintingController` now:
   detaches `crookedAnchor` and `alignedAnchor` from the painting hierarchy in `Awake`, `TryAlign`, and `ResetCrooked`;
   preserves anchor world pose during detachment;
   keeps `TryAlign` and `IsPhysicallyAligned()` on the same canonical root transform;
   emits a single `PAINTING_ALIGNMENT_CHECK` diagnostic at TryAlign verification time;
   preserves the existing success path order: physical pose -> semantic state -> SceneContext -> `PAINTING_STATE_CHANGED` -> local event.

9. `whether world/local-space mismatch existed`
   No. Both movement and validation already used world-space `position` and `rotation`. The bug was a moving reference anchor, not a world/local mismatch.

10. `whether another transform writer existed`
   No proven conflicting writer in the failing C1 path. The runtime trace showed `PREDEFINED_COMMAND_EXECUTE_LOCAL -> PREDEFINED_COMMAND_FAILED` immediately, and the painting object in the emitted scene context did not show a default `Rigidbody` or `ExperimentalGrabbableAdapter` active in that path.

11. `TryAlign result after patch`
   For the same scene-style nested-anchor setup, `TryAlign()` succeeds, `IsPhysicallyAligned()` becomes true, and the executor no longer returns `painting_alignment_configuration` for a valid canonical painting configuration.

12. `reset behavior`
   `ResetCrooked()` still restores the real crooked pose using the preserved crooked anchor world pose, and after reset the aligned predicate is false again.

13. `tests`
   Added focused edit-mode coverage in [ExperimentalRuntimeEditModeTests.cs](C:/Users/Scianso/Documents/GitHub/Real-Time-Immersive-Speech-Programming/Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/Tests/Editor/ExperimentalRuntimeEditModeTests.cs):
   `PaintingTryAlignDetachesSceneAnchorsFromMovedHierarchyBeforeValidation`
   `PaintingTryAlignFailsSafelyWhenAlignedAnchorIsMissing`
   `PredefinedPaintingExecutionSucceedsForSceneStyleNestedAnchors`
   `PaintingResetRestoresCrookedPoseAfterAlignment`

14. `expected next device timeline`
   `PredefinedCommandExecutionRequest`
   -> `PREDEFINED_COMMAND_EXECUTE_LOCAL`
   -> `PAINTING_ALIGNMENT_CHECK` with `physically_aligned=true`
   -> `PREDEFINED_COMMAND_EXECUTED`
   -> success `PredefinedCommandAck`
   -> `PAINTING_STATE_CHANGED`
   -> local `ObjectStateChanged`
   -> normal downstream task completion / T2 generation from the existing protocol.

15. `audit path`
   Used the newest real Quest device run `diagnostics/device_logs/from_quest_20260902_124914.jsonl`, traced the failing command `c8ce8c2e-9970-4af3-b970-264426495841`, inspected the current execution path in [PredefinedVoiceCommandExecutor.cs](C:/Users/Scianso/Documents/GitHub/Real-Time-Immersive-Speech-Programming/Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/PredefinedVoiceCommandExecutor.cs) and [QuestWorldControllers.cs](C:/Users/Scianso/Documents/GitHub/Real-Time-Immersive-Speech-Programming/Unity/Assets/DreamCodeVR2/Quest/QuestWorldControllers.cs), and cross-checked the actual scene configuration in [DreamCodeVR2_EscapeRoom_Testbed.unity](C:/Users/Scianso/Documents/GitHub/Real-Time-Immersive-Speech-Programming/Unity/Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity).
