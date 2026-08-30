# C1 sphere, clue text, and command-feedback audit

## SPHERE PREVIOUS/NEW SIZE

The C1 quest sphere was created from Unity's default primitive sphere at world scale `1,1,1`: a one-metre diameter sphere. The latest device context confirms this exact scale.

`QuestSoccerBall.CanonicalDiameterMeters` is now the shared canonical value: **0.16 m** (radius **0.08 m**). C1 uses it when creating `sphere_001`; C2/C3's authored soccer-ball material path uses the same helper and preserves a 0.16 m world diameter even below a scaled parent.

`C1QuestSphereController.TryApplySoccerBallPreset` still changes material/semantic labels only. It does not change transform position or scale. The C1 rigidbody remains kinematic and its grabbable adapter remains disabled.

## PLACEMENT MODES

`AuthoringAnchor` now has serialized `placementMode` metadata:

| Mode | Meaning |
| --- | --- |
| `Surface` | center is anchor position plus anchor-local up multiplied by the actual scaled sphere radius |
| `Center` | object origin is placed exactly at the anchor |

The runtime bootstrap explicitly assigns this metadata while registering canonical placement anchors. No object-name inference is used by the sphere spawn code.

## A1 SURFACE PLACEMENT

`table_001.desk_surface_anchor` is registered as `Surface`. After the sphere has its final world scale, `QuestSoccerBall.EffectiveWorldRadius` reads its `SphereCollider` and `QuestSoccerBall.SpawnPosition` offsets it along `anchor.transform.up`. Thus its support point, rather than its centre, sits on the desk plane.

## A2 CONTAINMENT PLACEMENT

`table_drawer_003.drawer_inside_anchor` remains `Center`. A2 therefore puts the sphere centre directly in the containment anchor; no desk-style radius offset is applied.

## BASKET FIT

The scene's basket prefab is scaled to `0.25`. The former `.22` local trigger therefore represented approximately `.055 m` in world space, smaller than the new `.16 m` ball. The basket visual mesh was not enlarged.

The bootstrap now configures only the hidden `QuestPlacementMonitor` trigger to a world side of `.184 m` (ball diameter plus 15% margin), compensating for the parent scale. `place_in` relocates the confirmed C1 sphere to the basket's `Center` anchor before/with notification, so the ball is visibly at the destination and satisfies `OBJECT_AT_ANCHOR` through the existing monitor. A device visual check remains required because mesh interior bounds are not encoded in the YAML scene data.

## CLUE TEXT ROOT CAUSE

`FixedQuestWireConverter` already converted `quest_instance.clue_texts` into `QuestNoteBinding[]`, and `QuestInstanceController.Apply` called `QuestNoteController.Configure`. However, the previous `Configure` stored text only in `QuestText` then toggled the GameObject. It never wrote to the notes' real child `TMPro.TextMeshPro` components (`Text (TMP)` under `clue_note_001` and `clue_note_002`). The default scene message therefore remained rendered.

## CLUE TEXT APPLICATION

`QuestNoteController` is now the single clue-text abstraction. It finds/caches its child `TMP_Text` (including inactive children), captures the scene default once, and writes the exact server text before changing visibility.

On reset or QuestInstance switching, all note controllers restore their deliberate default while hidden; the new instance then writes its own supplied text while still hidden. Missing/blank overrides retain the captured scene default and log `QUEST_CLUE_TEXT_FALLBACK`; actual overrides log `QUEST_CLUE_TEXT_APPLIED`.

## QUESTINSTANCE APPLICATION AUDIT

| Server field | Result |
| --- | --- |
| `clue_texts` | Applied to the rendered TMP text; fixed. |
| `placements` | Was received but ignored; now converted and applied to exact anchors for movable puzzle objects. |
| `anchor_assignments` | Was received but ignored; now merged as non-duplicating placement bindings. Clue-note positions are intentionally preserved from authored scene composition. |
| `initial_states` | Was received but ignored; now normalizes known drawer-lock IDs and applies lock/drawer/door/lamp states. |
| `key_lock_bindings` | Already converted and applied; retained. |
| `task_targets` | Drawer and lamp targets were already applied; retained. |
| target drawer/lamp | Already consumed as the selected drawer and lamp; retained. |

Unresolvable placement/state data is deliberately logged as `QUEST_INSTANCE_PLACEMENT_IGNORED` or `QUEST_INSTANCE_INITIAL_STATE_IGNORED`, rather than silently changing unrelated objects.

## COMMAND FAILURE FEEDBACK

The existing compact proposal feedback area is reused. A failure now shows a participant-safe short message for 2–3 seconds (default 2.5 seconds) and is then automatically hidden. It covers:

- local predefined-command execution failure;
- server `PredefinedCommandRejected` unless it dismissed the matching pending YES/NO proposal;
- failed inbound `PredefinedCommandAck`;
- local C1 gating failure.

`VOICE_COMMAND_FEEDBACK_SHOWN` is logged with `feedback_type`, `reason_code`, `source`, and `command_id` when available. Raw server text, object IDs, task IDs, command IDs, and exceptions are not rendered to participants.

## FAILURE REASON MAPPING

`AuthoringProposalPresenter.ParticipantSafeFailureMessage` is the central mapping. It maps current known local/server reason codes, including `ambiguous_target`, `command_not_allowed`, `target_locked`, `wrong_key`, `invalid_key`, `unsupported_preset`, and `missing_target`, to concise safe wording. Unknown details map to **“Command failed.”**

Intentional NO/cancel remains a cancellation: `DismissRejectedPredefinedProposal` clears the proposal and does not show a failure.

## TESTS

Added EditMode coverage in `Unity/Assets/DreamCodeVR2/ExperimentalAuthoring/Tests/Editor/ExperimentalRuntimeEditModeTests.cs` for:

- canonical ball diameter/radius;
- `Surface` versus `Center` placement computation;
- explicit A1/A2/basket modes;
- converter retention of placements, anchor assignments, and normalized initial states;
- TMP clue override, reveal-safe assignment, reset/default fallback;
- centralized safe reason mapping, cancellation non-failure, local execution feedback, and readable duration default.

The local machine has no .NET SDK and no Unity executable on PATH, so automated compilation/test execution could not be run from this shell. Unity must perform the final compile and EditMode test run.

## NEXT DEVICE TEST

1. Start C1 A1: confirm `sphere_001` has scale `.16,.16,.16`, rests on the desk, and does not clip through it.
2. Apply the soccer-ball preset: verify transform is unchanged.
3. Place it in the basket: verify it visibly centres inside, the task completes, and no basket mesh clipping is objectionable.
4. Align the painting: verify the revealed note immediately shows the instance sentence from `clue_texts`, not the old scene text.
5. Issue a rejected/ambiguous/locked command: verify safe feedback is visible for about 2.5 seconds. Reject a proposal with NO: verify no failure feedback.
6. Start C1 A2: verify the ball starts at the drawer-inside anchor centre, with no surface offset.
