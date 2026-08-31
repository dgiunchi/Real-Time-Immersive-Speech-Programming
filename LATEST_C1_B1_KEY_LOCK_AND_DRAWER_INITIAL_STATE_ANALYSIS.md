# Latest C1 B1 Key/Lock and Drawer Initial-State Analysis

## RUN IDENTIFICATION

- Device log: `client_20260831T140046Z_run.jsonl` on Quest external app storage.
- B1 selected at `2026-08-31T14:04:31.4641640Z`.
- Condition: `voice_command_baseline` (C1).
- Quest set / instance: `set_b_search_and_locks` / `set_b_instance_1`.
- Peer: `a2c06dcb-8e64-4384-acd2-a29bb9c718ea`.
- Researcher session: `012018e8-d35a-4619-8828-f202bacaf5df`.
- Active task during the failed attempts: `set_b_instance_1:T3` — **Unlock Drawer**.

## FAILED COMMAND TIMELINE

The latest failed key attempt began at `14:04:59.128Z` and ended at `14:05:01.978Z`.

1. `PTT_PRESS` / `PTT_START`.
2. Interaction context at recording start: selected and pointed `key_001`; current task `set_b_instance_1:T3`.
3. Audio was captured and sent; the client has no logged final transcript/original utterance for this request.
4. `14:05:01.977808Z`: server sent `PredefinedCommandRejected` with reason `That type of modification is not available.`
5. `PREDEFINED_COMMAND_REJECTED_BY_SERVER` was raised. The envelope supplied no `command_id`, interpretation, target, proposal, or execution request.
6. The participant UI showed failure from `server_rejection`.

The same result occurs for the later key-targeted requests in the run (`14:05:07.602Z`, `14:05:15.088Z`, `14:05:33.310Z`, and `14:05:39.951Z`). There is no `PredefinedCommandProposal`, `PredefinedCommandExecutionRequest`, `USE_WITH_RESOLUTION`, `LOCK_USE_ATTEMPT`, `LOCK_USE_SUCCESS`, or `LOCK_WRONG_KEY` for any of them.

## FAILURE STAGE

The failure is **before `QuestLockController`**. Unity never receives a concrete predefined command to execute, so it cannot resolve a key/lock pair, invoke `TryUseKey`, or publish a lock state event. Evidence identifies a server-side command/intent or task-scope rejection; the supplied response is insufficient to distinguish those sub-causes further.

`Command failed.` is a participant-safe rendering of the server rejection path. The internal reason received by the client is `That type of modification is not available.` No technical IDs are exposed to the participant.

## B1 RESOLVED PHYSICAL MODEL

The B1 quest payload and the resolved Unity binding agree:

| Role | Canonical Unity ID | Runtime/GameObject |
| --- | --- | --- |
| Golden Key | `key_001` | `key_001` / **Golden Key** |
| selected cabinet drawer | `cabinet_drawer_001` | `cabinet_drawer_001` / **Cabinet Drawer 1** |
| cabinet lock | `lock_003` | `lock_003` / **Cabinet Drawer Lock** |
| required key | `key_001` | Golden Key |
| exit pair | `key_002` -> `lock_001` | Silver Key -> exit lock |

The server logical ID `lock_drawer_003` is normalized to Unity `lock_003`. `QUEST_LOCK_TARGET_BOUND` confirms controller instance `-874` is associated with `cabinet_drawer_001`, which has a BoxCollider.

## KEY LOCK BINDINGS

| Server lock | Unity lock | Required key | Associated target | Status |
| --- | --- | --- | --- | --- |
| `lock_drawer_003` | `lock_003` | `key_001` | `cabinet_drawer_001` | applied correctly |
| `lock_001` | `lock_001` | `key_002` | `door_001` | applied correctly |

`QUEST_LOCK_BINDING_SUMMARY` at `14:04:33.779966Z` reports `lock_003` locked, required key `key_001`, target `cabinet_drawer_001`. T3 independently evaluates the same `lock_003` controller (`-874`). No binding mismatch is present.

## POINTED / RAYCAST

At the first failed key attempt, both `selected_object_id` and `pointed_object_id` are `key_001`. The VR selection log resolves `selection_proxy_collider` to `key_001` / **Golden Key**. Therefore the recent open-drawer raycast fallback is not the cause of this failure.

The server rejection has no target fields, so it is impossible to prove the server's intended secondary lock from this response. The client did nevertheless send a live context containing the correct selected/pointed Golden Key and active B1 T3 task.

## CAPABILITIES

The B1 context snapshot includes:

- `key_001`: labels include `key`, `golden_key`, `drawer_key`, `unlock_item`, and `interactive`; predefined command `use_with`.
- `cabinet_drawer_001`: cabinet drawer labels; predefined commands `open`, `close`.
- `cabinet_drawer_002`: locked/lockable cabinet drawer labels; predefined commands `open`, `close`.

The lock itself is a semantic target supplied in the quest task (`lock_drawer_003`) and binding, but it is not advertised as a primary voice command object. That is expected for a `USE_WITH` secondary target. The Golden Key capability and B1 task data are sufficient for the server to propose `USE_WITH`; it did not do so.

## FAILURE FEEDBACK SOURCE

Source: `AuthoringProtocolClient` receives `PredefinedCommandRejected`, then invokes `ShowC1Failure` with source `server_rejection`.

- Raw server reason: `That type of modification is not available.`
- UI source: `server_rejection`.
- Received command ID/target: `null` / `null`.
- Participant outcome: generic failure.

The message is appropriately non-technical, but the server should return a stable high-level reason (for example, unsupported command or missing key/lock target) if more helpful participant feedback is desired.

## DRAWER INITIAL STATES

At the start of `QuestInstance.Apply`, `ResetControlledState` closes every drawer. B1 sends no required runtime objects, so runtime-object auto-close does not alter any drawer.

| Drawer | B1 `initial_states` | Final state / source |
| --- | --- | --- |
| `table_drawer_001` | absent | closed by client reset |
| `table_drawer_002` | absent | closed by client reset |
| `table_drawer_003` | `open` | open explicitly requested by server |
| `cabinet_drawer_001` | `locked` | transform remains closed; lock enforced by `lock_003` binding |
| `cabinet_drawer_002` | absent | closed by client reset |
| `cabinet_drawer_003` | `open` | open explicitly requested by server |

The B1 payload's `cabinet_drawer_001: locked` is logged as `QUEST_INSTANCE_INITIAL_STATE_IGNORED` because drawers do not own a `locked` state. This is not a functional lock failure: the separately applied `lock_003` binding is authoritative and remains locked. The two visibly open drawers are deliberate server quest-design state, not a client auto-close regression.

## RUNTIME AUTO-CLOSE

The auto-close rule is not exercised in B1 because `required_runtime_objects` is empty. It does not force-close `table_drawer_003` or `cabinet_drawer_003`: both are explicitly opened by B1 initial state after the reset phase.

## B1 OBJECT LOCATIONS

| Object | B1 declared placement | Initial/reveal behavior | Finding |
| --- | --- | --- | --- |
| `clue_note_001` | `table_drawer_003.drawer_inside_anchor` | inactive; reveal on painting aligned | client deliberately preserves authored note composition rather than moving note transforms |
| `key_001` | `table_001.desk_surface_anchor` | visible/active | correct Golden Key starting location |
| `clue_note_002` | `cabinet_drawer_001.drawer_inside_anchor` | inactive; reveal on opening cabinet drawer 001 | coherent with selected locked drawer |
| `key_002` | `cabinet_drawer_003.drawer_inside_anchor` | inactive; reveal trigger says opening `cabinet_drawer_001` | server metadata inconsistency: placement is drawer 003, trigger is drawer 001 |

Because B1 explicitly opens `cabinet_drawer_003`, placing an inactive Silver Key there while revealing it from opening locked `cabinet_drawer_001` is a server quest-design inconsistency. No client placement/remapping is changed by this analysis.

## CABINET PHYSICAL MAPPING

The active scene defines `lock_003` as **Cabinet Drawer Lock**, described as attached to the locked cabinet drawer. The B1 runtime binding maps it to `cabinet_drawer_001`; no suffix-based remapping is used. This exact association is confirmed by `QUEST_LOCK_TARGET_BOUND`, not inferred from object names.

## ROOT CAUSE

**Proven root cause:** the server rejects the key command before it emits a predefined command proposal or execution request. Unity's B1 lock binding, key capability, current task, and pointing state are coherent; `QuestLockController` is never reached.

## RECOMMENDED MINIMAL FIX

No Unity client patch is justified by this log. The server C1 resolver must accept/propose the B1 `USE_WITH` interaction for `key_001` and B1 target `lock_drawer_003` (or return a structured rejection reason explaining why it cannot).

## SERVER-SIDE DESIGN ISSUES

1. B1 deliberately opens `table_drawer_003` and `cabinet_drawer_003`; change those only in quest design if undesired.
2. `key_002` is assigned to `cabinet_drawer_003` but has a reveal trigger on `cabinet_drawer_001`.
3. `PredefinedCommandRejected` omits command/target/interpretation, preventing exact server-side resolution diagnosis.

## REMAINING UNCERTAINTIES

- The device log does not record the final STT transcript for the rejected commands; the exact spoken words cannot be recovered faithfully.
- Since the server rejected before proposal, the server's intended secondary lock resolution and ambiguity count are unavailable from the client log.
