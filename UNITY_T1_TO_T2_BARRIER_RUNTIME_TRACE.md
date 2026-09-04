# T1 → T2 barrier — newest real Quest runtime trace

## Session

This audit uses exclusively the JSONL copied directly from the Quest after the requested new run:

- Device log: `client_20260902T185453Z_run.jsonl`
- Local copy: `diagnostics/device_logs/client_20260902T185453Z_run.jsonl`
- Session: `5e47c5ff-aac0-4ec9-8813-e582a9e860a5`
- Peer: `0905ce3a-6d3c-415e-b5b2-2a9ae051ea7a`
- Canonical set: `set_a`
- Condition: `voice_command_baseline` (C1)
- Initial task: `set_a:T1`, activated at `2026-09-02T18:55:01.7524650Z`.

## T1 completion timeline

| UTC timestamp | Event | Result |
|---|---|---|
| `18:55:11.0036700Z` | `SCENE_CONTEXT_SENT` | Reason `painting aligned`; `current_task=1`. |
| `18:55:11.0096570Z` | `QUEST_WORLD_STATE_WIRE_PAYLOAD` | `QuestWorldStateEvent`, `event_type=PAINTING_STATE_CHANGED`, event ID `098f2ce63e094ec989e06c9fc70fba4c`, semantic key `painting_001`. |
| `18:55:11.0100760Z` | `PAINTING_SEMANTIC_STATE_CHANGED` | `painting_001`: `crooked` → `aligned`; physical alignment confirmed; world state emitted. |
| `18:55:11.0125690Z` | `TASK_SUCCESS_EVALUATION` | `set_a:T1`, `PAINTING_ALIGNED=true`, triggered by `ObjectStateChanged` on `painting_001`. |
| `18:55:11.0141800Z` | `TASK_COMPLETED` | `set_a:T1`, source `RuntimeTaskValidator.IsSatisfied`. |
| `18:55:11.0167630Z` | `NID102_SENT` | `ExperimentStateEvent`, task `set_a:T1`, event `task_completed`. |
| `18:55:11.0172590Z` | `PAINTING_ALIGNED` | Local quest event for `painting_001`. |

T1 completed successfully. The world-state event is present and reports the aligned painting state.

## T1 consequences and ACKs

| UTC timestamp | Instruction / ACK | ID and payload | Apply result |
|---|---|---|---|
| `18:55:11.0637260Z` | Incoming `QuestConsequenceInstruction` | `7bca7745-83a7-4349-b29c-ce8a3fffd354`; `SET_OBJECT_VISIBILITY`; target `clue_note_001`; `{ "visible": true }`; session `5e47c5ff-aac0-4ec9-8813-e582a9e860a5`; set `set_a`. | `QUEST_CONSEQUENCE_APPLIED` at `18:55:11.0880850Z`, semantic state `visible`. |
| `18:55:11.0911610Z` | Outgoing `QuestConsequenceAck` | Matching instruction ID; session `5e47c5ff-aac0-4ec9-8813-e582a9e860a5`; canonical set `set_a`; `success=true`, `reason_code=null`. | Sent successfully. |
| `18:55:11.0942500Z` | Incoming `QuestConsequenceInstruction` | `0f039175-0fde-42be-b523-0653cdeeea54`; `SET_CLUE_TEXT`; target `clue_note_001`; `{ "text": "Search for a sphere and prepare it for the basket." }`; same session and set. | `QUEST_CONSEQUENCE_APPLIED` at `18:55:11.1153510Z`, semantic state `text_set`. |
| `18:55:11.1155570Z` | Outgoing `QuestConsequenceAck` | Matching instruction ID; session `5e47c5ff-aac0-4ec9-8813-e582a9e860a5`; canonical set `set_a`; `success=true`, `reason_code=null`. | Sent successfully. |

Expected ACK count: **2**. Actual matching ACK count: **2**. There is no correlation mismatch.

`clue_note_001` is made visible by the received `SET_OBJECT_VISIBILITY` instruction.

## T2 traffic, registration, and activation

| UTC timestamp | Event | Result |
|---|---|---|
| `18:55:11.1613070Z` | Incoming `NextTaskGenerated` | T2 received: `task_id=set_a:T2`, step 2, instruction “Find the sphere.”, type `drawer_discovery`, primary object `cabinet_drawer_003`. |
| `18:55:11.1618330Z` | `NID101_PARSED` | Deserializes as `NextTaskGenerated`, task `set_a:T2`. |
| `18:55:11.1620690Z` | `FIXED_QUEST_WIRE_CONVERSION_FAILED` | **First broken step**: `Unsupported success condition: drawer_discovery:cabinet_drawer_003:sphere_001`. T2 is not converted to a fixed runtime task and is not registered. |
| `18:55:11.1754780Z` | Incoming `NextTaskActivationRequest` | Request for `set_a:T2` arrives. |
| `18:55:11.1758750Z` | `NID101_ACTIVATION_CORRELATION` | `generated_seen=true`, `fallback_used=false`. |
| `18:55:11.1759160Z` | `FIXED_QUEST_ACTIVATION_FAILED` | `No matching fixed quest task is pending.` Activation fails because conversion/registration already failed. |

No T2 registration event exists. No successful activation exists. T2 never becomes `currentTask`.

## Waiting UI

The UI stays on “Preparing the next objective...” because the server sends T2 and its activation request, but Unity rejects T2 during wire-to-runtime conversion. With no pending registered `set_a:T2`, activation cannot set a current task.

## First broken step and classification

First broken step: client conversion of the received `NextTaskGenerated(T2)` at `2026-09-02T18:55:11.1620690Z`.

```text
FIXED_QUEST_WIRE_CONVERSION_FAILED
Unsupported success condition: drawer_discovery:cabinet_drawer_003:sphere_001
```

Failure classification: `T2_RECEIVED_NOT_REGISTERED`.

The activation error is downstream; this run does not show a T1 consequence, ACK, or server-traffic failure.
