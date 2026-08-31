# Latest C1 client predefined-command runtime audit

## RUN

Analysed client/device log: `client_20260831T153854Z_run.jsonl`.

The file is the latest device run available locally. It starts at `2026-08-31T15:38:55.0305690Z` and contains two voice-command-baseline sessions:

| Session | Peer | Quest instance | Relevant interval |
|---|---|---|---|
| B1 | `31e5fd29-db93-4f34-ac41-c575b59b6d05` | `set_b_instance_1` | 15:39:05–15:40:20Z |
| A1 | `2e3a1dc4-8993-44f3-b2da-81395911379f` | `set_a_instance_1` | 15:41:18–15:41:57Z |

The selected researcher condition is logged as `voice_command_baseline`; this is the client representation of the C1 voice-command condition. The run has a successful predefined command and repeated failures that were intended as key/use and drawer/open interactions. The rejected messages do not preserve an STT transcript or parsed command payload, so their precise spoken wording is unavailable.

## COMMAND LEDGER

| Time (UTC) | Session/task | Context target | Client lifecycle reached | Outcome |
|---|---|---|---|---|
| 15:39:30–15:39:32 | B1 T1 | `painting_001` | proposal → confirmation → execution request → local execution | applied; ACK sent |
| 15:39:41 | B1 T3 | `key_001` | server rejection only | `use_with_not_exposed_by_scene` |
| 15:39:47 | B1 T3 | `key_001` | server rejection only | same |
| 15:39:54 | B1 T3 | `key_001` | server rejection only | same |
| 15:40:03 | B1 T3 | `key_001` | server rejection only | same |
| 15:40:15 | B1 T3 | `key_002` | server rejection only | same |
| 15:40:20 | B1 T3 | `key_002` | server rejection only | same |
| 15:41:24–15:41:26 | A1 T1 | `painting_001` | proposal → confirmation → execution request → local execution | applied; ACK sent |
| 15:41:34 | A1 T2 | `table_drawer_003` | server rejection only | `command_not_allowed` |
| 15:41:37 | A1 T2 | `table_drawer_003` | server rejection only | same |
| 15:41:41 | A1 T2 | `table_drawer_003` | server rejection only | same |
| 15:41:44 | A1 T2 | `table_drawer_001` | server rejection only | same |
| 15:41:48 | A1 T2 | `table_drawer_001` | server rejection only | same |
| 15:41:52 | A1 T2 | `table_drawer_001` | server rejection only | same |
| 15:41:57 | A1 T2 | `cabinet_drawer_001` | server rejection only | same |

## FAILED OPEN

The latest A1 failure that matches an attempted drawer/open action is:

1. `15:41:32.252Z`: PTT starts; `selected_object_id` and `pointed_object_id` are both `table_drawer_003`.
2. The active task is `set_a_instance_1:T2`, **Prepare the soccer ball and place it in the basket**, not an unlock/open task.
3. `15:41:34.020Z`: PTT stops with the same selected/pointed drawer.
4. `15:41:34.196702Z`: the client receives `PredefinedCommandRejected` with `code=command_not_allowed` and message **That command is not permitted for the current task.**
5. `15:41:34.197Z`: failure feedback is shown with source `server_rejection`.

There is no `PredefinedCommandProposal`, no `PredefinedCommandExecutionRequest`, no `PREDEFINED_COMMAND_EXECUTE_LOCAL`, no `DRAWER_OPEN_GATE`, and no drawer-motion event for this attempt. Therefore no Unity OPEN operation was requested in this run.

## FAILED USE_WITH

Representative B1 T3 attempt:

1. `15:39:38Z`: the captured interaction context has selected/pointed `key_001`, while active task is `set_b_instance_1:T3` **Unlock the cabinet drawer**.
2. `15:39:41.681Z`: PTT stops.
3. `15:39:41.894537Z`: client receives `PredefinedCommandRejected`, `code=use_with_not_exposed_by_scene`, message **That type of modification is not available.**
4. `15:39:41.895Z`: `PREDEFINED_COMMAND_REJECTED_BY_SERVER` and failure feedback are logged.

The same failure repeats five further times through `15:40:20Z`. None has a proposal, confirmation, execution request, local execution, `LOCK_USE_ATTEMPT`, `LOCK_USE_SUCCESS`, `KEY_SNAPPED_TO_LOCK`, or `LOCK_UNLOCKED` event.

## SUCCESSFUL COMMAND

The B1 control path is healthy for a command the server permits:

* `15:39:30.415Z`: proposal `318b43a0-1dc5-4cf9-afea-0c02211e4d67`, target `painting_001`, `MOVE_TO_PRESET/aligned`.
* `15:39:32.233593Z`: `PREDEFINED_COMMAND_EXECUTION_REQUEST`.
* `15:39:32.238787Z`: local task evaluation completes T1 from actual world state.
* `15:39:32.288031Z`: `PREDEFINED_COMMAND_EXECUTED`, **Command applied.**
* `15:39:32.291Z`: outbound `PredefinedCommandAck`, status `applied`.

The same lifecycle is repeated for A1 painting alignment at `15:41:24–15:41:26Z`.

## CONDITION

Both analysed sessions explicitly log the condition `voice_command_baseline`. No client-side condition switch, stale condition, or fallback condition is visible in the relevant timelines.

## TASK STATE

* B1 reaches T3 correctly. The received task requires `key_001`, `cabinet_drawer_002`, and `lock_003`, with success condition `lock_unlocked:lock_003`.
* A1 reaches T2 correctly after painting alignment. At every rejected open-like attempt the active task remains T2, whose required objects are the soccer-ball/basket step. The logs contain no transition to A1’s later drawer task.

## INTERACTION CONTEXT

The client does send interaction context and periodic scene snapshots. The context for each representative failure has non-null selected and pointed IDs. Scene-context capability snapshots advertise `open`/`close` for the drawers and scene objects include the corresponding `PredefinedVoiceCommandTarget` and `VoiceCommandCapabilities` components.

The server rejections contain no parsed command, command ID, target ID, or original utterance. The client cannot reconstruct a more specific rejected command after receiving them.

## POINTED / SELECTED

There is no selected-versus-pointed mismatch in the representative failures:

| Attempt | Selected | Pointed | Interpretation supported by log |
|---|---|---|---|
| B1 key attempt | `key_001` | `key_001` | ray/selection resolves to the Golden Key |
| A1 first open-like attempt | `table_drawer_003` | `table_drawer_003` | ray/selection resolves consistently to drawer 3 |
| later A1 attempts | `table_drawer_001`, then `cabinet_drawer_001` | same | ray/selection resolves consistently to the actually targeted drawers |

No raw raycast-hit collider name is emitted during these exact commands, so this run cannot prove the collider geometry is ideal. It does prove that the client’s semantic selected/pointed values agree; it does not support a client-side selected/pointed mismatch as the first failure.

## CANONICAL QUESTINSTANCE

Canonical instance resolution is correct and does not use legacy conversion:

| Instance | Drawer | Lock | Required key | Legacy conversion |
|---|---|---|---|---|
| B1 `set_b_instance_1` | `cabinet_drawer_002` | `lock_003` | `key_001` | `false` |
| A1 `set_a_instance_1` | `table_drawer_002` | `lock_002` | `key_001` | `false` |

The B1 failed key context targets the required key, but no secondary lock/action reaches Unity. The A1 open-like contexts target drawers other than canonical `table_drawer_002`, and do so while T2 is active.

## LEGACY CONVERSION

`QUEST_CANONICAL_INSTANCE_RESOLVED` logs `legacy_conversion_used=false` for both analysed instances. Legacy mapping is not involved in the observed rejections.

## LOCK BINDINGS

The client binds the expected controllers before interaction:

* B1: `lock_003` → `key_001` / `cabinet_drawer_002`, controller instance `-874`.
* A1: `lock_002` → `key_001` / `table_drawer_002`, controller instance `-860`.

The active B1 T3 evaluator reads `lock_003` on controller `-874` and initially finds it locked (`lock_is_unlocked=false`). No use-with request reaches this controller.

## OPEN EXECUTION

Not reached for a failed command in this log. No execution request means the following client paths have no observed invocation: OPEN dispatcher, `ExperimentalDrawerController.TryOpen`, `DRAWER_OPEN_GATE`, lock lookup, motion start, or completion.

## USE_WITH EXECUTION

Not reached for a failed command in this log. No `PredefinedCommandExecutionRequest` arrives for the repeated B1 key attempts; consequently Unity never invokes the lock-use path. There is no evidence here of a client failure in key insertion, lock-state persistence, or drawer association.

## ACK

ACK is correct on the successful painting command: the client sends `PredefinedCommandAck` with `status=applied` at `15:39:32.291Z`. No ACK is possible or sent for rejected attempts because the server never produces a command ID/proposal/execution request.

## WORLD STATE

The reset/setup sequence runs before each instance is bound:

* all six drawers are reset closed;
* B1 locks `lock_003` and `lock_001` are set locked; B1 binds `lock_003` to `cabinet_drawer_002` and `key_001`;
* A1 locks `lock_002` and `lock_001` are set locked; A1 binds `lock_002` to `table_drawer_002` and `key_001`.

No later command reaches the affected lock or drawer controller, so no world-state mutation for OPEN/USE_WITH exists to audit.

## TASK VALIDATOR

The task validator is functioning for observed local work:

* B1 T1 completes from `PAINTING_ALIGNED` after the local painting command.
* B1 T2 completes from `OBJECT_REVEALED:key_001` when activated.
* B1 T3 evaluates `LOCK_UNLOCKED:lock_003` as false using controller `-874`, as expected before a key use.

There is no validator event for a successful unlock, because no local unlock occurs.

## DRAWER GATE

No `DRAWER_OPEN_GATE` exists in the analysed run. It is therefore not possible to classify the drawer gate, lock-controller identity, or drawer motion as failed from this log. The first observed failure happens earlier, at protocol rejection.

## RESET

The reset is not the first failure: it occurs before canonical bindings and task activation. A separate, unrelated client error repeats during initial capture: TextMeshPro material `LiberationSans SDF Material (Instance)` lacks `_Color`, from `ExperimentalPlaythroughReset.CaptureInitialState()`. It is logged before the sessions and does not interrupt setup, proposals, execution, or ACK. There are also `QUEST_INSTANCE_INITIAL_STATE_IGNORED` warnings for visibility-style key/note initial states; they are not correlated with the immediate server rejections.

## CLIENT ERRORS

No exception, request serialization failure, executor failure, or state reset follows either rejected command. The client visibly receives and displays the rejection. The early TextMeshPro `_Color` errors should be cleaned up separately, but are not supported as the cause of OPEN/USE_WITH rejection.

## FAILURE STAGE MATRIX

| Command family | STT/interaction context | Proposal | Confirmation | Execution request | Unity executor | ACK | First observed failure |
|---|---|---|---|---|---|---|---|
| `MOVE_TO_PRESET` painting | present | yes | yes | yes | applied | yes | none |
| B1 intended `USE_WITH` | context present (`key_001`) | no | no | no | not reached | no | received server `use_with_not_exposed_by_scene` |
| A1 intended `OPEN` | context present (drawers 003/001/cabinet 001) | no | no | no | not reached | no | received server `command_not_allowed` while T2 active |

## CLIENT ROOT CAUSE

Proven client-side finding: the Unity command executor is not the first failing component. The client successfully follows the proposed-command lifecycle for painting, but for both failing families it receives a server-side rejection before any local executable command is supplied.

For A1, the interaction context is additionally inconsistent with the later locked-drawer objective: the player points ordinary drawers while the active task is still the ball/basket T2. For B1, the correct key is selected while T3 is active, but the received protocol rejection says use-with is not exposed by the scene. The client log cannot establish why the upstream command interpreter made that decision, because the rejection omits parsed intent, allowed-command set, required secondary object, and policy evaluation.

## RECOMMENDED CLIENT FIX

No behavioural Unity patch is justified from this evidence. The minimal safe client improvement is diagnostic only:

1. When a `PredefinedCommandRejected` arrives, log the locally submitted STT transcript/correlation ID plus the immediately preceding interaction context.
2. Log the server’s received/returned allowed command families, target and secondary-target IDs, and policy reason when those fields are present.
3. Preserve the rejection correlation so a rejected intended OPEN or USE_WITH is distinguishable from another command.

These logs would make the next test decisive without forcing a command or bypassing server task policy.

## SERVER INFORMATION NEEDED

To determine the upstream cause, the server/vendor trace needs, for each rejection:

* raw STT/interpretation and correlation ID;
* resolved command family and target/secondary target;
* active task and allowed commands at policy evaluation;
* advertised scene capability set used by the policy;
* reason that maps to `use_with_not_exposed_by_scene` or `command_not_allowed`.

No server internals were inspected for this audit.
