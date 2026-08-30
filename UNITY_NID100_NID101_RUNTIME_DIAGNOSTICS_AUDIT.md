# Unity NID100/NID101 runtime diagnostics audit

## NID100 LOGGING

`SceneContextTransmitter.SendSceneContextSnapshot()` now logs `NID100_SCENE_CONTEXT_SENT` immediately after sending the NetworkId 100 message.

The event records the packet timestamp, send reason, and the serialized JSON used to create the transmitted payload. The JSON field is capped at 20,000 characters. When that cap is reached, `relevant_quest_objects` additionally contains complete `SceneObjectSummary` entries for the diagnostic C1 objects, so their IDs, names, semantic types, labels, voice commands/presets, editable state, and authoring capability fields remain inspectable.

No packet fields or NID100 wire format changed.

## C1 CAPABILITY SNAPSHOT

For every present focused object, NID100 now emits one `C1_CAPABILITY_SNAPSHOT` with exactly:

- `object_id`
- `labels`
- `predefined_voice_commands`
- `predefined_presets`

Focused IDs are: `painting_001`, all three table drawers, all three cabinet drawers, `door_001`, both keys, all four lamps, `sphere_001`, and `basket_001`.

`C1_CAPABILITY_EXPECTED_MISSING` is emitted only as a diagnostic warning when the live packet lacks an expected command/preset. It never adds or changes a capability. Expected values checked are: painting `move_to_preset` + `aligned`; drawers and door `open`/`close`; keys `use_with`; lamps `activate`/`deactivate`/`toggle`; and the C1 sphere `move_to_preset`/`place_in` + `soccer_ball`.

## NID101 RAW RECEIVE

`AuthoringProtocolClient.ProcessMessage()` now emits `NID101_RAW_RECEIVED` as its first receive-side diagnostic, before JSON deserialization or dispatch. It contains:

- `timestamp_unix_ms`
- `peer` when the 36-byte Ubiq peer prefix is present
- `raw_json` (capped at 20,000 characters)

The pre-existing `NID101_SERVER_PAYLOAD` remains after successful parsing.

## NID101 PARSING

Every NID101 receive attempt now has one terminal parse result:

- `NID101_PARSED` with `message_type`, `task_id`, and, for `NextTaskGenerated`, `task_task_id` and `quest_instance_present`.
- `NID101_PARSE_FAILED` with `task_id: null` and the deserialization error.

No NID101 parser or dispatch semantics were changed.

## GENERATED/ACTIVATION CORRELATION

Successfully parsed `NextTaskGenerated.task.task_id` values are stored for the current client process/session for diagnostics only. Every `NextTaskActivationRequest` emits `NID101_ACTIVATION_CORRELATION` with:

- `task_id`
- `generated_seen`
- `fallback_used`

The existing `FixedQuestActivationFallback` is invoked under exactly the same conditions as before; the new field only reports whether that existing path was used.

## PLACEMENT ANCHORS

The existing bootstrap registration remains before quest activation:

`ConfigureVerticalSliceObjects()` → `RegisterPlacementAnchors()` → validation/start.

The current owner-relative registration emits exactly one of `PLACEMENT_ANCHOR_REGISTERED`, `PLACEMENT_ANCHOR_MISSING`, or `PLACEMENT_ANCHOR_AMBIGUOUS` per configured anchor. The next C1 Set A log must include, before quest activation:

```text
table_001.desk_surface_anchor
table_drawer_003.drawer_inside_anchor
basket_001.basket_inside_anchor
```

## SPHERE DIAGNOSTICS

`C1_QUEST_SPHERE_CREATED` and every `C1_QUEST_SPHERE_CREATE_FAILED` branch now include:

- `sphere_id`
- `requested_start_anchor`
- `resolved_anchor` (or `null`)

Sphere creation behavior is unchanged.

## NEXT DEVICE TEST

Build and install a fresh APK. The modified source files are under `Unity/Assets/...` and are explicitly listed by `Unity/Assembly-CSharp.csproj`, so they are included by the Unity project used for the next APK.

The local generated C# build could not be run because this machine has no .NET SDK installed. No device success is claimed.

For the next C1 Set A run, collect the client JSONL from launch through condition/task activation and verify this ordering:

1. `NID100_SCENE_CONTEXT_SENT` and focused `C1_CAPABILITY_SNAPSHOT` entries;
2. required `PLACEMENT_ANCHOR_*` entries;
3. `NID101_RAW_RECEIVED` for `NextTaskGenerated` and `NextTaskActivationRequest`;
4. their `NID101_PARSED` entries;
5. `NID101_ACTIVATION_CORRELATION` with `generated_seen: true`;
6. `C1_QUEST_SPHERE_CREATED`, or the expanded failure diagnostic.
