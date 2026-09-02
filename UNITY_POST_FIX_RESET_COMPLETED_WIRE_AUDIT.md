# Post-fix RESET_COMPLETED wire audit

## SESSION

Latest complete device log: `diagnostics/device_logs/client_20260901T214842Z_run.jsonl`, UTC `2026-09-01T21:48:42.9630110Z` through `2026-09-01T21:49:53.4619190Z`. First canonical run: peer `2e5dc6f3-790f-47e8-b789-4d6f1e6f472c`, session `f5ed27f1-9062-42f4-b178-656fdff03d17`, set `set_a`.

## RESET REQUEST

Set A request at `21:48:50.9643280Z`: reset request `7dfc0b80-f79a-494f-8be6-e3a529f4a4f8`, session `f5ed27f1-9062-42f4-b178-656fdff03d17`, canonical set `set_a`; embedded instance/set are `set_a`.

## RESET ORDER

`QUEST_RESET_REQUEST_RECEIVED` `21:48:50.9643280Z` → `QUEST_INSTANCE_APPLIED` `21:48:51.4618990Z` → `QUEST_CANONICAL_SET_APPLIED` `21:48:51.5236320Z` → NID102 send `21:48:51.5273600Z` → `RESET_COMPLETED` log `21:48:51.5274930Z`. Canonical apply precedes completion.

## RESET COMPLETED OBJECT

The source object has `protocol_version:1`, generated `event_id`, `event_type:RESET_COMPLETED`, session, canonical set, exact reset ID, and `semantic_state:"reset"`.

## NID102 WIRE PAYLOAD

The device log records a 609-byte NID102 send but not its raw JSON. The actual serializer path is `JsonConvert.SerializeObject` in `AuthoringProtocolClient.SendFlat`; the transmitted envelope is:

`{ "type":"QuestWorldStateEvent", "world_state": { ...RESET_COMPLETED object... } }`.

Thus it is wrapped, not a root-level reset event. All anonymous-object fields, including reset correlation fields, survive Newtonsoft JSON serialization.

## SERIALIZER CONTRACT

`reset_request_id`, `session_id`, `canonical_set_id`, and `semantic_state` are anonymous payload properties passed unchanged through the serializer. The client has no separate restrictive QuestWorldStateEvent DTO that could strip them.

## SEMANTIC STATE

Exact shape is the JSON string `"reset"`; reset metadata appears only in `details` as `{ availability_generation_reset: true, reset_request_id: ... }`. It has no keys for painting, door, locks, drawers, lamps, sphere, keys, or clues.

## EXPECTED VS CLIENT STATE

The server `expected_initial_state` is an object keyed by drawers, door, locks, keys, and clues. The client semantic state is a scalar string. They are schema-incompatible for post-reset state verification.

## TASK MESSAGES

No `NextTaskGenerated` and no `NextTaskActivationRequest` appears in the complete newest file.

## FAILURE CLASSIFICATION

`SEMANTIC_STATE_TOO_SMALL` (also an object-vs-scalar schema incompatibility). Correlation and ordering are correct in this run.

## EXACT FIX LOCATION

`QuestWorldStateReporter.ResetCompleted` creates `semantic_state="reset"`; it must construct the server-compatible post-canonical state representation before `AuthoringProtocolClient.SendQuestWorldStateEvent` serializes the NID102 wrapper.
