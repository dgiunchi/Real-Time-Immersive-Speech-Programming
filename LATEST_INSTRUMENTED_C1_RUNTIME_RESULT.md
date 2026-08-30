# Latest instrumented C1 runtime result

## LOG

- Filename: `client_20260828T180648Z_run.jsonl`
- Device path: `/sdcard/Android/data/com.VARLab.DreamCodeVR2/files/DreamCodeVR2/logs/client_20260828T180648Z_run.jsonl`
- Local analysis copy: `client_20260828T180648Z_run.jsonl`
- Start timestamp: `2026-08-28T18:06:48.7607630Z`
- Device modification timestamp: `2026-08-28 20:08` (device local listing)
- Size: `1,698,026` bytes

This is the newest Quest log and includes the NID100/NID101 instrumentation.

## SESSION

Latest complete restart: C1 / `set_a_ball_and_drawer` / `set_a_instance_1`.

| Field | Value |
| --- | --- |
| session ID | `514e23e5-4480-43fc-b7ee-08fe78c26793` |
| peer UUID | `0e336474-bfcc-44cb-aa02-c925617e60ba` |
| START timestamp | `2026-08-28T18:07:08.3988920Z` |
| READY timestamp | `2026-08-28T18:07:08.5083560Z` |

## NID100 CAPABILITIES

`NID100_SCENE_CONTEXT_SENT` was emitted 13 times. The latest focused snapshots are:

| object_id | labels | predefined_voice_commands | predefined_presets |
| --- | --- | --- | --- |
| `painting_001` | `painting, wall_object, decoration, movable, rotatable, clue_context, interactive` | none | none |
| `table_drawer_001` | `drawer, desk_drawer, table_drawer, container, openable, unlocked, interactive, ...` | `open, close` | none |
| `table_drawer_002` | `drawer, desk_drawer, table_drawer, container, locked, lockable, golden_key_target, interactive` | none | none |
| `table_drawer_003` | `drawer, desk_drawer, table_drawer, container, openable, unlocked, interactive` | none | none |
| `cabinet_drawer_001` | `drawer, cabinet_drawer, container, openable, unlocked, contains_silver_key, interactive` | none | none |
| `cabinet_drawer_002` | `drawer, cabinet_drawer, container, locked, lockable, golden_key_target, interactive` | none | none |
| `cabinet_drawer_003` | `drawer, cabinet_drawer, container, openable, unlocked, interactive` | none | none |
| `door_001` | `door, exit, openable, lockable, interactive, final_goal` | none | none |
| `key_001` / `key_002` | key labels present | none | none |
| `lamp_001`–`lamp_004` | `lamp, light, feedback_object, interactive` | none | none |
| `sphere_001` | absent | n/a | n/a |
| `basket_001` | `basket, container, receptacle, placement_target, ball_target, puzzle_mechanism, interactive` | none | none |

The full NID100 JSON is logged and agrees with the focused snapshots.

## CAPABILITY FAILURES

`C1_CAPABILITY_EXPECTED_MISSING` is emitted repeatedly. Every expected capability is missing except `open`/`close` on `table_drawer_001`:

- painting: `move_to_preset`, preset `aligned`;
- drawers 002/003 and all cabinet drawers: `open`, `close`;
- door: `open`, `close`;
- keys: `use_with`;
- lamps: `activate`, `deactivate`, `toggle`.

`sphere_001` is absent because sphere creation failed before a new context packet could include it.

## NID101 RAW DELIVERY

The latest session physically received both required packets:

| Timestamp | Raw message type | Task ID |
| --- | --- | --- |
| `18:07:08.484Z` | `NextTaskGenerated` | `set_a_instance_1:T1` in `task.task_id` |
| `18:07:08.497Z` | `NextTaskActivationRequest` | `set_a_instance_1:T1` |

The raw JSON peer is `0e336474-bfcc-44cb-aa02-c925617e60ba`; transport source metadata is unavailable (`peer: null`). It also received two command rejections, one proposal, and two more command rejections in this session.

## NID101 PARSING

Across the log: 13 raw packets, 9 `NID101_PARSED`, 4 `NID101_PARSE_FAILED`.

Every `NextTaskGenerated` failed before dispatch. Exact error:

```text
Cannot deserialize the current JSON object into type 'System.String[]'.
Path 'task.allowed_authoring_scope', line 1, position 616.
```

The server sends `"allowed_authoring_scope": {}` (and `allowed_solution_scope: {}`), while the client DTO expects `string[]`. Activation requests parse normally, but the generated task is never stored. Thus the generated packet has `task.task_id = set_a_instance_1:T1` and `quest_instance` present, but no successful parsed event.

## GENERATED ACTIVATION CORRELATION

```text
task_id        = set_a_instance_1:T1
generated_seen = false
fallback_used  = true
```

The IDs match. The generated packet arrived but failed parsing, so this is neither packet loss nor a task-ID mismatch.

## QUEST INSTANCE

The raw, unparsed generated payload contains:

- instance `set_a_instance_1`, set `set_a_ball_and_drawer`;
- task target `painting_001`, required objects `painting_001` and `clue_note_001`;
- drawer target `table_drawer_001`;
- bindings `key_001 → lock_drawer_001` and `key_002 → lock_001`;
- first-clue text “Align the painting and inspect the clue it reveals.”;
- initial state: drawer 001/lock 001 locked, door closed, lamp 001 inactive;
- anchor assignments including `table_drawer_001.drawer_inside_anchor` and `table_001.desk_surface_anchor`;
- C1 quest setup: non-grabbable `sphere_001` at `table_001.desk_surface_anchor`, soccer-ball preset.

Because parsing failed, none of this reached `HandleNextTaskGenerated`; the client applied its local fallback.

## PLACEMENT ANCHORS

No `PLACEMENT_ANCHOR_REGISTERED`, `PLACEMENT_ANCHOR_MISSING`, or `PLACEMENT_ANCHOR_AMBIGUOUS` event appears.

| Required Set A anchor | Result | Evidence |
| --- | --- | --- |
| `table_001.desk_surface_anchor` | FAIL | sphere start anchor unavailable |
| `table_drawer_003.drawer_inside_anchor` | FAIL | sphere start anchor unavailable in A2 |
| `basket_001.basket_inside_anchor` | NOT TESTED | no sphere and no registration event |

At `18:06:48.848Z`, bootstrap throws `NullReferenceException` in `NetworkContext.Send`, called by `SceneContextTransmitter.SendSceneContextSnapshot` from `ConfigureVerticalSliceObjects` / `VerticalSliceRuntimeBootstrap.Install`, while the NetworkScene has zero connections. This interrupts bootstrap before later capability and placement-anchor configuration can complete.

## C1 SPHERE

No sphere was created.

| Result | sphere_id | requested_start_anchor | resolved_anchor |
| --- | --- | --- | --- |
| failed | `sphere_001` | `table_001.desk_surface_anchor` | null |
| failed (A2) | `sphere_001` | `table_drawer_003.drawer_inside_anchor` | null |

## VOICE COMMANDS

No transcript text is logged, so utterance wording is not invented.

| Utterance / context | Pointed object | Capability advertised | Server result | Intent | Preset | Reason |
| --- | --- | --- | --- | --- | --- |
| transcript absent | `painting_001` | none | rejected | unavailable | unavailable | `That type of modification is not available.` |
| transcript absent | `cabinet_drawer_001` | none | rejected | unavailable | unavailable | same reason |
| transcript absent; context ends on drawer 001 | `table_drawer_001` | `open, close` | proposal | `OPEN table_drawer_001` | none | later cancelled by participant |
| transcript absent | null | none | rejected | unavailable | unavailable | `That type of modification is not available.` |

All contexts used `current_task_id = set_a_instance_1:T1`.

## PAINTING

Painting is present in NID100 but advertises neither `move_to_preset` nor `aligned`. It is pointed/selected with the current task set, but receives no proposal and is rejected with `That type of modification is not available.` No reason code or target is supplied by the server.

## DRAWERS

- `table_drawer_003`: no advertised `open`/`close`; no latest-A1 voice test. Its A2 sphere anchor fails.
- `cabinet_drawer_001`: no advertised `open`/`close`; pointed/selected; rejected.
- `table_drawer_001`: publishes `open`/`close`; receives an `OPEN` proposal, then participant cancellation; no execution request.

The evidence confirms that only `table_drawer_001` currently works at the capability-publication layer.

## COMPARISON WITH PREVIOUS TEST

| Previous fact | New result |
| --- | --- |
| client did not observe `NextTaskGenerated` | FIXED at transport; STILL FAILING at parsing |
| fallback used | STILL FAILING |
| painting/non-first drawers rejected | STILL FAILING |
| desk anchor unavailable | STILL FAILING |

## ROOT CAUSE

**MULTIPLE_INDEPENDENT_BLOCKERS**

1. `NID101_CLIENT_PARSING`: `NextTaskGenerated` reaches the Quest but fails because object-valued `allowed_authoring_scope` is deserialized as `string[]`. This directly causes `generated_seen=false` and fallback use.
2. Bootstrap/capability/anchor setup is incomplete: an early null-reference during `NetworkContext.Send` interrupts `ConfigureVerticalSliceObjects`. The observed outcomes are missing C1 capabilities except drawer 001, no placement-anchor diagnostics, and unavailable sphere anchors.

No fix is proposed in this read-only analysis.
