# C1 runtime sphere and key-capability audit / minimal client fix

## NEW RUN

The Quest is not connected to ADB at audit time (`device 2G0YC1ZF940874 not found`), therefore a newer device log could not be pulled. The newest locally available run remains `client_20260831T153854Z_run.jsonl`, range `15:38:55Z–15:42:36Z`.

It contains:

| Instance | Peer | Session | Coverage |
|---|---|---|---|
| B1 `set_b_instance_1` | `31e5fd29-db93-4f34-ac41-c575b59b6d05` | `e4f490d2-ccbe-482e-944d-6bddf19c5db0` | key USE_WITH rejections |
| A1 `set_a_instance_1` | `2e3a1dc4-8993-44f3-b2da-81395911379f` | `27822626-8665-4333-bb8f-fa5d5f9b086f` | task transitions and open-like rejections |

The file predates the reported A2/C1 post-operational-patch tests; no claim below treats it as evidence for a real A2 or C1 run.

## CLIENT BUILD

Current source contains all requested client features:

* `ServerQuestInstanceDto.required_runtime_objects` and `ServerRequiredRuntimeObjectDto` protocol wire support;
* conversion and `REQUIRED_RUNTIME_OBJECT_RECEIVED` logging in `FixedQuestWireConverter.ConvertRuntimeObjects`;
* `QuestRuntimeObjectFactory`, invoked by `QuestInstanceController.EnsureRuntimeObjects` before binding/placement;
* current canonical lock targets: A1/A2 `lock_002 → table_drawer_002`, B1/C1 `lock_003 → cabinet_drawer_002`;
* current drawer semantic raycast objects/components and BoxCollider-based scene configuration;
* `SceneContextCompiler` serializes `predefined_voice_commands`, while `SceneContextTransmitter` emits C1 capability snapshots.

No build/commit ID is embedded in the client log or available from the disconnected device.

## A1 SPHERE CREATION

No `REQUIRED_RUNTIME_OBJECT_RECEIVED` / `RUNTIME_OBJECT_CREATED` event is present for A1 in the locally available run, so its real declared anchor and final position cannot be reconstructed from that run.

Source trace: a server-declared `required_runtime_objects[].initial_placement_anchor` is converted to `QuestRuntimeObjectSpec.initialAnchorId`, resolved by `QuestRuntimeObjectFactory`, then made the sphere parent. Before this fix, any resolved drawer anchor was accepted. Thus a declaration resolving to a locked drawer could create the reported unreachable sphere.

## A2 SPHERE CREATION

No A2 session exists in the available device log, so A2 is not inferred from A1. The legacy fallback still describes `table_drawer_003.drawer_inside_anchor`; it is not evidence of the new server payload. Under the new client guard, a locked drawer anchor yields `QUEST_REQUIRED_OBJECT_UNREACHABLE` and no sphere, rather than an inaccessible spawned object.

## ANCHOR RESOLUTION

Pre-fix factory behaviour was exact-ID lookup with no duplicate detection and no container-accessibility check. It did not intentionally map an accessible anchor to a drawer; it simply accepted whatever resolved anchor the received runtime spec named.

Applied resolution contract:

* exact unique anchor IDs resolve directly;
* the explicit protocol alias `table_001.soccer_ball_anchor` resolves only to authored `table_001.desk_surface_anchor`;
* duplicate IDs fail as ambiguous;
* there is no fallback to `drawer_inside_anchor`.

The scene currently has `table_001.desk_surface_anchor`, but no authored `soccer_ball_anchor` Transform. The alias retains semantic compatibility without hardcoding a world position.

## POST-CREATION MOVEMENT

For factory-created spheres, creation sets the world pose, applies size/surface offset, and parents once to the resolved anchor. The subsequent instance paths do not reparent the runtime sphere: visibility only changes active state, reset destroys C1 runtime spheres, and placement happens only after an explicit `place_in` command.

The first transform-changing operation after creation is therefore the factory’s deliberate `SetParent(anchor.transform, true)`. No source path was found that moves a created sphere from a safe desk anchor into a drawer.

## REACHABILITY SAFETY

Added `QUEST_REQUIRED_OBJECT_UNREACHABLE` before creation whenever a required runtime object would be placed below a drawer whose associated `QuestLockController` is locked. It includes:

* `quest_instance_id`, `task_id`, `object_id`, `anchor_id`;
* `container_id`, `lock_id`, `lock_state`.

The client does not open/unlock the drawer and does not teleport the sphere. It rejects the invalid/inaccessible placement. The created-object event now records declared/resolved anchor, parent GameObject, parent drawer, world position, scale, and grabbability.

## KEY_001 SCENECONTEXT

In B1 snapshots from the available run, `key_001` is one semantic object, is labelled Golden Key, and serializes `predefined_voice_commands=["use_with"]`. It remains present in snapshots during the relevant session. The old B1 rejection arrived before a client proposal/execution request.

## KEY_002 SCENECONTEXT

The same available snapshots serialize `key_002` with `predefined_voice_commands=["use_with"]`. There is no C1 real-device session in this log, so C1-specific active/visible state must be checked on the next device run.

## CAPABILITY REGISTRATION

`VerticalSliceRuntimeBootstrap.ConfigureVerticalSliceObjects` creates/gets `VoiceCommandCapabilities` for both static puzzle keys and sets `predefinedVoiceActions=["use_with"]`. `QuestInstanceController.Apply` makes C1 keys non-grabbable with `ExperimentalGrabbableAdapter.SetGrabbable(false)` but never changes the voice-capability component.

This maintains the required distinction: C1 non-grabbable does **not** imply lack of `USE_WITH`; C2/C3 can separately enable physical grabbability.

## SNAPSHOT FRESHNESS

Current code requests SceneContext publication after runtime-object creation, after lock/key state changes, after visibility-triggered actions, and on periodic cadence. In the available B1 session, C1 capability snapshots occur before the key rejection and contain `use_with`; no stale snapshot explains that rejection. Exact post-patch B1/C1 timestamps require the next device log.

## DUPLICATE KEYS

The available run’s snapshots show one summary per canonical `key_001` and `key_002`; no duplicate semantic key is evidenced. The compiler currently serializes all `AIEditableObject`s and has no duplicate-ID merge policy, so a future duplicate would need an explicit diagnostic if observed. No duplicate-key fix was applied because it is not proven in this audit.

## CANONICAL LOCK BINDINGS

Current source and available B1 log agree:

| Condition/instance | Lock | Required key | Target |
|---|---|---|---|
| A1/A2 | `lock_002` | `key_001` | `table_drawer_002` |
| B1 | `lock_003` | `key_001` | `cabinet_drawer_002` |
| C1 | `lock_003` | `key_002` | `cabinet_drawer_002` |

No canonical mapping was changed.

## ROOT CAUSE — SPHERE

Proven client defect: `QuestRuntimeObjectFactory` trusted a resolved runtime anchor even when it was nested inside a locked drawer, and then parented the sphere to that anchor. This directly permits an unreachable required object. The precise A1/A2 server-declared anchor remains unverified because the new device run was unavailable.

## ROOT CAUSE — KEY CAPABILITY

No client-side missing-capability root cause is proven. The logged B1 client snapshot advertises `use_with` for `key_001`; the request is rejected by received protocol message `use_with_not_exposed_by_scene` before Unity receives `PredefinedCommandProposal` or `PredefinedCommandExecutionRequest`. Therefore Unity’s `QuestLockController.TryUseKey` is not reached. C1 requires a fresh real-device trace.

## APPLIED FIXES

* Exact, duplicate-safe runtime-anchor resolution.
* Explicit safe alias for `table_001.soccer_ball_anchor` to the authored desk-surface anchor only.
* Prevent creation of required objects below locked drawers, with `QUEST_REQUIRED_OBJECT_UNREACHABLE` diagnostic.
* Enriched `RUNTIME_OBJECT_CREATED` placement diagnostics.

No server code, task definition, lock/drawer mapping, key binding, or drawer state was changed.

## TESTS

Added EditMode coverage for:

* A1/A2 accessible declared anchors resolving to the desk surface without drawer fallback;
* factory rejection of a locked-drawer sphere placement;
* factory-created sphere retaining the accessible anchor parent, not a drawer parent;
* non-grabbable key retaining serialized `use_with` in a SceneContext snapshot.

Tests were added but not executed here: no Unity batch-test runner/.NET SDK is available in this environment.

## NEXT DEVICE TEST

Reconnect the Quest, build/install this client, then run A1, A2, B1 and C1. Capture the new JSONL and verify:

1. A1/A2 have `REQUIRED_RUNTIME_OBJECT_RECEIVED`, resolved anchor and either accessible `RUNTIME_OBJECT_CREATED` or the new unreachable diagnostic;
2. no sphere parent drawer is locked;
3. B1/C1 snapshots immediately before key speech include `use_with` for the active key;
4. whether a proposal/execution request finally reaches Unity for USE_WITH.

## DEVICE RUN ADDENDUM — 2026-08-31 16:46Z

After this audit, the Quest was connected and the actual newer log was retrieved:
`client_20260831T164605Z_run.jsonl`, `16:46:05Z–16:55:11Z`, peer `37408a4a-3f6b-46d4-aaec-f1a480e45cb9` (with a later reconnection peer).

This run proves the exact A1/A2 server payload and the installed-build gap:

| Instance | Received runtime object | Server-declared anchor | Installed-client result |
|---|---|---|---|
| `set_a_instance_1` | `sphere_001` | `table_001.soccer_ball_anchor` | `RUNTIME_OBJECT_CREATE_FAILED` at 16:46:14Z |
| `set_a_instance_2` | `sphere_001` | `table_001.soccer_ball_anchor` | `RUNTIME_OBJECT_CREATE_FAILED` at 16:47:23Z |

The current Quest build has the runtime-object wire support but not the new explicit alias resolution. It creates no sphere in these two runs; consequently the earlier “sphere inside a drawer” symptom cannot be attributed to this newer build. Rebuilding/installing the current workspace is required for the alias and reachability guard to execute.

The same run also proves the key capability conclusion for both real T3 cases:

* B1 T3, `16:48:26–16:48:54Z`: selected/pointed `key_001`; `key_001` SceneContext snapshots advertise `use_with`; all attempts receive server rejection before proposal/execution.
* C1 T3, `16:49:25–16:49:34Z`: selected/pointed `key_002`; `key_002` SceneContext snapshots advertise `use_with`; both attempts receive server rejection before proposal/execution.

The client does receive and execute normal OPEN proposals in A1/A2 (for example A1 `table_drawer_003` at 16:46:30Z and `table_drawer_002` at 16:46:57Z), confirming that the client proposal/executor pipeline itself is live. The B1/C1 USE_WITH issue remains upstream of Unity’s executor and lock controller.
