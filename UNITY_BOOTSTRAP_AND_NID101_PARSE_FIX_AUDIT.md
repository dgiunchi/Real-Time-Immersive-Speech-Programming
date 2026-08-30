# Unity bootstrap and NID101 parsing fix audit

## ROOT CAUSE A — TASK DTO

The 2026-08-28 device payload delivered `NextTaskGenerated` before its activation request, but deserialization stopped at `task.allowed_authoring_scope`. The Unity wire DTO declared the scope as `string[]`, while the canonical payload contains an object (`{}`). The same incompatibility applied to `allowed_solution_scope`.

## TASK SCOPE SCHEMA

`ServerNextTaskDto` and its runtime `NextTaskSpec` now use `TaskPolicyScopeDto` for both scope fields. The DTO explicitly retains named policy entries: `allowed_operations`, `operations`, `actions`, `object_ids`, `by_condition`, and `by_action`. A narrowly scoped JSON converter accepts the canonical object form (including `{}`) and the previous string-array form as a backward-compatible legacy operations list. Empty objects stay empty; no permissions are invented and no scope is silently discarded.

## PARSING FIX

`NextTaskWireConverter` carries both scope DTOs into `NextTaskSpec`. Dynamic-task activation derives its existing operation list from `allowedAuthoringScope.GetAllowedOperations()`, so existing task-policy consumers retain the explicitly supplied operation names.

The representative payload test contains the real A1 fields: task ID, instruction, task type, required objects, success conditions, both object-valued scopes, `task.quest_setup`, and a nested `quest_instance`. It verifies conversion and resolves the A1 sphere start anchor as `table_001.desk_surface_anchor`.

## ROOT CAUSE B — EARLY NETWORK SEND

During `VerticalSliceRuntimeBootstrap.Install()`, local object configuration invokes SceneContext publication through a quest controller. Before Ubiq has a connection, `NetworkContext.Send` was called with zero connections and threw a `NullReferenceException`. That exception terminated `ConfigureVerticalSliceObjects` before placement-anchor registration and C1 capability validation.

## SAFE SEND / DEFERRED SEND

`SceneContextTransmitter.SendSceneContextSnapshot()` now validates the transport immediately before serializing/sending:

- no `NetworkContext`/scene: defer with `network_context_unavailable`;
- zero connections: defer with `no_network_connection`;
- unavailable Room peer or invalid UUID: defer without sending;
- a connection lost between validation and `Send`: defer in a narrow transport-boundary catch.

Deferred publication logs `SCENE_CONTEXT_SEND_DEFERRED` exactly once per deferred reason. `RoomClient.OnJoinedRoom` flushes a deferred request when the room becomes ready; the existing periodic snapshot is retained only as a recovery path. The actual flush regenerates the snapshot at send time, so the first sent NID100 contains the complete post-bootstrap scene rather than an early partial snapshot. A successful flush logs `SCENE_CONTEXT_DEFERRED_SEND_COMPLETED`.

## BOOTSTRAP COMPLETION

The bootstrap is not wrapped in a broad catch. Instead, only network transmission is guarded, allowing the existing sequence to complete even offline:

`ConfigureVerticalSliceObjects` -> `RegisterPlacementAnchors` -> `ValidateC1Capabilities` -> `StartFixedQuest`.

## C1 CAPABILITIES

Capability construction remains in `VerticalSliceRuntimeBootstrap`, not in the transmitter. Once bootstrap completes, NID100 is expected to advertise:

- `painting_001`: `move_to_preset`, preset `aligned`;
- table and cabinet drawers `001..003`, and `door_001`: `open`, `close`;
- `key_001` and `key_002`: `use_with`;
- lamps `001..004`: `activate`, `deactivate`, `toggle`;
- conditional `sphere_001`: `move_to_preset`, `place_in`, preset `soccer_ball`.

Existing `NID100_SCENE_CONTEXT_SENT`, `C1_CAPABILITY_SNAPSHOT`, and `C1_CAPABILITY_EXPECTED_MISSING` diagnostics are retained.

## PLACEMENT ANCHORS

The existing owner-relative recursive resolver remains intact. Since it now runs after a safe deferred send rather than after a thrown transport exception, it will register the canonical IDs:

- `table_001.desk_surface_anchor`;
- `table_drawer_003.drawer_inside_anchor`;
- `basket_001.basket_inside_anchor`;
- the remaining configured table/cabinet drawer anchors.

The existing one-result-per-required-anchor diagnostics are retained: `PLACEMENT_ANCHOR_REGISTERED`, `PLACEMENT_ANCHOR_MISSING`, or `PLACEMENT_ANCHOR_AMBIGUOUS`.

## SPHERE

No Quest Set or `QuestInstance` value changed. With bootstrap completion restored, C1 Set A1 can resolve `table_001.desk_surface_anchor`; A2 can resolve `table_drawer_003.drawer_inside_anchor`. The existing sphere diagnostics remain `C1_QUEST_SPHERE_CREATED` and `C1_QUEST_SPHERE_CREATE_FAILED`.

## TESTS

Added EditMode coverage in `ExperimentalRuntimeEditModeTests.cs` for:

- a representative object-valued-scope `NextTaskGenerated` payload and nested quest instance;
- safe SceneContext deferral when no network context is available, with no exception;
- recursive resolution of a nested desk anchor from the canonical `table_001` owner.

The Unity test runner was not available from this shell, and no .NET SDK/compiler is installed, so these tests require execution in the Unity Editor. The actual Ubiq-ready resend remains device/integration verification because `NetworkContext` is provided by the live Ubiq scene.

## NEXT DEVICE TEST

Build and run the Escape Room scene, then confirm:

1. no bootstrap `NetworkContext.Send` exception;
2. initial `SCENE_CONTEXT_SEND_DEFERRED` when appropriate, followed by one complete NID100 after room readiness;
3. placement-anchor registration for desk, A2 drawer, and basket;
4. `NID101_PARSED` for `NextTaskGenerated`, then `FIXED_QUEST_WIRE_RECEIVED` and activation correlation with `generated_seen=true`, `fallback_used=false`;
5. C1 Set A creates the sphere at its canonical A1/A2 anchor and exposes the expected complete capability snapshot.
