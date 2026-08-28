# Unity Scene Semantic & Capability Audit

Static, read-only audit of the current repository. Sources inspected: the active scene YAML, `Study Table.prefab`, the runtime bootstrap, SceneContext compiler and quest/authoring code. `NOT DEFINED` means no serialised or runtime-bootstrap source establishes the value. This audit does not claim runtime observations.

## ACTIVE SCENE

- **Study scene:** `Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity` (`DreamCodeVR2_EscapeRoom_Testbed`). The testbed README identifies it as the only active study scene; the bootstrap only installs for this scene name.
- **Scene roots:** `room_floor`, `wall_back`, `wall_back (1)`, `wall_back (2)`, `wall_left`, `wall_right`, `wall_front`, `clue_note_001`, `DreamCodeVR2_RuntimeServices`, `MainLight`, `SceneController`.
- **Gameplay:** room structure; desk/table drawers; cabinet/drawers; keys; locks; exit door; notes; basket; painting; four lamps.
- **Infrastructure:** `DreamCodeVR2_RuntimeServices` (network/STT/SceneContext and UI services), `SceneController`, lighting, Ubiq/XR hierarchy instantiated or referenced by services, and the runtime-created `ExperimentalAuthoringRuntime` service object. The researcher panel/debug controls are runtime infrastructure, not gameplay.
- **XR hierarchy:** NOT fully provable from static scene YAML because Ubiq/XR objects are prefab-driven. The bootstrap finds `HandController` objects and installs a child `DreamCodeVR2_XRUIRaycaster` where missing.

## GAMEPLAY OBJECT INVENTORY

There are **29** `AIEditableObject` records: **21** serialised `editable: true` and **8** static room structures with `editable: false`. The Unity name of several prefab-instance semantic roots cannot be recovered reliably from the scene YAML alone; the canonical runtime ID and display name below are authoritative.

All inventory records carry the displayed labels as both `semantic_types` and `labels`: `SceneContextCompiler` does not define a separate semantic-type field. All are active in the serialised scene unless noted otherwise; no serialised inactive gameplay object was found. No serialised `AuthoringCapabilities`, `VoiceCommandCapabilities`, `ExperimentalDrawerController`, `ExperimentalGrabbableAdapter`, `AuthoringAnchor`, or `AuthoringSemanticState` was found on scene objects; the runtime exceptions are listed below.

## SEMANTIC LABELS

| ID | Display name | Semantic types / labels | Description status |
|---|---|---|---|
| door_001 | Exit Door | door, exit, openable, lockable, interactive, final_goal | Defined; says it should be unlocked with the correct key. |
| lock_001 | Door Lock | lock, door_lock, exit_lock, puzzle_mechanism, interactive | Defined; says silver key. |
| key_001 | Golden Key | key, golden_key, drawer_key, puzzle_item, unlock_item, visible, interactive | Defined. |
| key_002 | Silver Key | key, silver_key, exit_key, puzzle_item, unlock_item, hidden, interactive | Defined. |
| table_001 | Desk | desk, table, furniture, surface, container_parent, interactive | Defined. |
| table_drawer_001 | Desk Drawer 1 | drawer, desk_drawer, table_drawer, container, openable, unlocked, interactive | Defined. |
| table_drawer_002 | Locked Desk Drawer | drawer, desk_drawer, table_drawer, container, locked, lockable, golden_key_target, interactive | Defined. |
| table_drawer_003 | Desk Drawer 3 | drawer, desk_drawer, table_drawer, container, openable, unlocked, interactive | Defined. |
| cabinet_001 | Cabinet | cabinet, dresser, furniture, container_parent, interactive | Defined. |
| cabinet_drawer_001 | Cabinet Drawer 1 | drawer, cabinet_drawer, container, openable, unlocked, contains_silver_key, interactive | Defined. |
| cabinet_drawer_002 | Locked Cabinet Drawer | drawer, cabinet_drawer, container, locked, lockable, golden_key_target, interactive | Defined. |
| cabinet_drawer_003 | Cabinet Drawer 3 | drawer, cabinet_drawer, container, openable, unlocked, interactive | Defined. |
| lock_002 / lock_003 | Desk Drawer Lock / Cabinet Drawer Lock | lock, drawer_lock, respective drawer label, golden_key_target, puzzle_mechanism, interactive | Defined. |
| clue_note_001 / clue_note_002 | First / Second Clue Note | clue, note, readable, puzzle_instruction, first_clue / ball_task_clue, interactive | Defined. |
| basket_001 | Basket | basket, container, receptacle, placement_target, ball_target, puzzle_mechanism, interactive | Defined. |
| painting_001 | Crooked Painting | painting, wall_object, decoration, movable, rotatable, clue_context, interactive | Defined. |
| lamp_001..lamp_004 | Puzzle Lamp | lamp, light, feedback_object, interactive | Defined; identical display names. |
| wall_front, wall_back, wall_back_1, wall_back_2, wall_left, wall_right | Front/Back/Left/Right Wall | wall, room_structure, static | Defined. |
| room_floor | Room Floor | floor, room_structure, static | Defined. |

No object has missing labels. Repeated `Puzzle Lamp`, repeated `Back Wall`, repeated generic `drawer_inside_anchor`, and distinct identifiers with near-identical drawer wording are resolution risks.

## C1 PREDEFINED COMMANDS

| Object ID | Advertised C1 commands | Relevant component/source |
|---|---|---|
| table_drawer_001 | `OPEN`, `CLOSE` | Runtime `VerticalSliceRuntimeBootstrap.ConfigureVerticalSliceObjects`: `VoiceCommandCapabilities.predefinedVoiceActions`; `PredefinedVoiceCommandTarget` delegates to `ExperimentalDrawerController`. |

No other object advertises a C1 command. The executor supports additional command names in theory, but they are **not advertised/configured** for this scene and are intentionally excluded.

## C2/C3 AUTHORING CAPABILITIES

### Currently advertised/configured

- `table_drawer_001`: runtime capabilities are `SET_AFFORDANCE`, `SET_PROPERTY`; editable property `color`; no allowed behaviours. It is made kinematic and explicitly non-grabbable by the bootstrap. `SceneContext` advertises the generic editable-affordance vocabulary (`grabbable`, `movable`, `interactable`, `gravity_enabled`, `kinematic`, `collision_enabled`), but the actual permitted C2 operations remain the object's allowlist.
- `door_001`, `lock_001`: runtime `AuthoringCapabilities` are added and mark them quest-critical; `allowedOperations` is empty and `grabbable` is forbidden. Their protection is imposed by the fixed quest at runtime.
- All other serialised editable objects: `AIEditableObject.editable=true`, but **no per-object `AuthoringCapabilities` is serialised or added by this bootstrap**. They appear as `available_operations: ["edit"]` in SceneContext only; the action executor requires a capability component for concrete operations. Concrete C2 operations are therefore **NOT DEFINED / not exposed** for them.

### API capabilities not currently exposed on an object

The authoring API contains support for properties, relocation, toggle state, behaviours, links and object creation, but no scene object currently advertises them except as described above. Generic defaults in the `AuthoringCapabilities` class do not constitute a per-object configuration.

## KEY LOCK DOOR RELATIONSHIPS

- **Current fixed quest:** step 1 `RetrieveKey` targets `key_001`; step 2 `UseKeyWithLock` targets `lock_001` and names `key_001`. This is the only executable explicit key-lock link currently installed.
- `door_001` and `lock_001` are both runtime quest-critical; the `lock_001` semantic description says silver key while the fixed quest names `key_001`. The static sources therefore conflict.
- `key_002` / `lock_001` and `key_001` / `lock_002` or `lock_003` associations are described by labels/descriptions and by the **example** JSON contract, but no serialised key-lock component, required-key field, unlock state component, or operational unlock link was found.
- Drawer/key containment is semantic/descriptive, not an explicit container-state relation. `cabinet_drawer_001` says `contains_silver_key`; no runtime containment component was found.

## DRAWERS / OPENABLES

| Object | Actual open/close support | Anchors/state/context |
|---|---|---|
| table_drawer_001 | Yes: runtime C1 `OPEN`/`CLOSE`; `ExperimentalDrawerController`, animated (`duration` default 0.5 s). | `DrawerClosedAnchor` and `DrawerOpenAnchor` come from `Study Table.prefab`; exact current transform values require Scene View/prefab-instance verification. State is `IsOpen`; controller publishes `ObjectStateChanged` and requests a SceneContext snapshot. Placement child `drawer_inside_anchor` exists. |
| table_drawer_002, table_drawer_003 | Semantic `openable` / `locked` labels only. | No C1 command, motion controller or state owner found. `drawer_inside_anchor` exists. |
| cabinet_drawer_001, cabinet_drawer_002, cabinet_drawer_003 | Semantic `openable` / `locked` labels only. | No C1 command, motion controller or state owner found. `drawer_inside_anchor` exists. |
| door_001 | Semantic `openable`, `lockable` only. | No door-motion/open controller, key relation or state component found. |

## ANCHORS

Task-relevant serialised placement references: **8** child transforms: `desk_surface_anchor` under `table_001`; `basket_inside_anchor` under `basket_001`; and one `drawer_inside_anchor` under each of the three desk and three cabinet drawers. They are transforms only: **no `AuthoringAnchor` component / anchor ID / occupancy policy is currently serialised**, so the relocation/create API cannot resolve them as authoring anchors today.

The repaired table prefab additionally supplies the two motion anchors `DrawerClosedAnchor` and `DrawerOpenAnchor` for `table_drawer_001`; they are not `AuthoringAnchor` placement references. The source contains an editor repair tool that can add placement transforms, but that is tooling, not evidence of runtime `AuthoringAnchor` configuration.

## CONTAINMENT / INITIAL PLACEMENT

- Scene hierarchy establishes desk and cabinet drawer membership, and `lock_002` under `table_drawer_002`, `lock_003` under `cabinet_drawer_002`, and `key_002` under `cabinet_drawer_001` according to the repair-tool hierarchy policy. Exact current child transform/prefab overrides require Scene View verification.
- `cabinet_drawer_001` semantically identifies itself as containing/hiding the silver key. No container inventory or occupancy state is implemented.
- The fixed quest does not execute an initial-placement plan. The JSON contract's placement list is an example only and does not establish active runtime placement.

## STATEFUL OBJECTS

| Object/state | Owner | SceneContext / success-condition status |
|---|---|---|
| table_drawer_001: open / closed | Runtime `ExperimentalDrawerController.IsOpen` | Emits a SceneContext refresh, but its open flag is not explicitly serialized in the object summary; no current fixed-task success condition consumes it. |
| key_001: held / released | Runtime `ExperimentalGrabbableAdapter.IsHeld` | `currently_held` is emitted. Pickup emits `ObjectPickedUp`; current step 1 can complete from it. |
| door_001, lock_001: protected | Runtime capabilities plus fixed quest protection | `quest_critical` and `protected_for_current_task` are emitted. No lock-open state is implemented. |
| All editable objects: active / transform / material data | Unity component state | Active, transform and materials are emitted; semantic state only exists after authoring creates `AuthoringSemanticState`. |

## SCENECONTEXT COVERAGE

For each `AIEditableObject`, the compiler supplies: id, display name, Unity name, labels/semantic types, description, transform, active, editable, parent ID, material summaries and attached-component names. It conditionally supplies available operations, editable properties, allowed behaviours, quest critical, semantic state, held state, affordance state, predefined commands, fixed generic editable-affordance names, parent anchor and current-task protection.

Current gaps: no distinct semantic type source; no explicit key/lock/door relationship field; no container contents field; no drawer open flag field; no serialised authoring-anchor metadata; no per-object concrete C2 capability on most objects; no lock/key state. Runtime-created C1/capability components become visible as component names and relevant fields after bootstrap.

## C1-SUITABLE OBJECTS

`table_drawer_001`: `OPEN`, `CLOSE`.

## C2-SUITABLE OBJECTS

`table_drawer_001`: `SET_AFFORDANCE`; `SET_PROPERTY` limited to `color`. No other object has a currently exposed concrete authoring operation.

## C3-ELIGIBLE OBJECTS

The current C3 validator can evaluate only conditions such as object held, object at an actual `AuthoringAnchor`, semantic state, affordance and active runtime behaviour. Static eligibility cannot be final because task specs come from the server. Likely available in SceneContext and non-quest-critical at bootstrap: the 19 editable objects other than `door_001` and `lock_001`. Practically useful current state support is strongest for `key_001` (held) and `table_drawer_001` (motion, but not exposed open flag). Objects requiring `AuthoringAnchor` placement are ineligible until actual anchor components exist.

## SPEECH-RESOLUTION RISKS

- `lamp_001` through `lamp_004` share display name **Puzzle Lamp** and labels; speech target resolution is ambiguous.
- Three desk drawers and three cabinet drawers share generic labels; "drawer" is ambiguous.
- Three `drawer_inside_anchor` transform names recur and do not provide a unique runtime anchor ID.
- `lock_001` description says silver key whereas the active fixed task requires golden `key_001`.
- Unity/prefab names are not always equivalent to display names; speech should use the emitted ID/display/labels, not inferred hierarchy names.

## TASK-DESIGN GAPS

- No explicit, operational key-lock-door relationship except the two-step fixed quest key-to-lock reference.
- No lock/unlock controller or lock-state success condition is configured.
- No `USE_WITH` C1 command or secondary-target C1 resolution is advertised.
- Only one C1 object supports deterministic commands.
- Placement transforms are not `AuthoringAnchor` components, so create/relocate and `OBJECT_AT_ANCHOR` conditions cannot use them.
- Drawer open state refreshes SceneContext but is not a dedicated SceneContext field or fixed-task success condition.
- Static containment is hierarchy/description only; no container inventory/state tracking exists.
- Most editable objects have no concrete authoring capability component, despite their `editable` flag.

## CANONICAL OBJECT TABLE

| Object ID | Display Name | Semantic Types | C1 Commands | C2 Authoring | Stateful | Anchors | Quest Critical | Notes |
|---|---|---|---|---|---|---|---|---|
| door_001 | Exit Door | door, exit, final_goal | — | none (runtime protected) | protected only | — | Yes runtime | No open/unlock controller. |
| lock_001 | Door Lock | lock, door_lock, exit_lock | — | none (runtime protected) | protected only | — | Yes runtime | Fixed quest uses key_001; description says silver key. |
| key_001 | Golden Key | key, golden_key, drawer_key | — | NOT EXPOSED | held/released | — | Protected in fixed step 1 | Runtime grabbable. |
| key_002 | Silver Key | key, silver_key, exit_key, hidden | — | NOT EXPOSED | — | hierarchy: cabinet drawer 1 | No | No explicit lock relation. |
| table_001 | Desk | desk, table, surface | — | NOT EXPOSED | — | desk_surface_anchor | No | Container parent. |
| table_drawer_001 | Desk Drawer 1 | drawer, openable, unlocked | OPEN, CLOSE | color; affordance operation | open/closed | drawer_inside + motion anchors | No | Recently repaired C1 drawer. |
| table_drawer_002 | Locked Desk Drawer | drawer, locked, lockable | — | NOT EXPOSED | — | drawer_inside_anchor | No | Lock relationship not operational. |
| table_drawer_003 | Desk Drawer 3 | drawer, openable | — | NOT EXPOSED | — | drawer_inside_anchor | No | Semantic only. |
| cabinet_001 | Cabinet | cabinet, dresser | — | NOT EXPOSED | — | NOT FOUND | No | `cabinet_top_anchor` is only repair-tool intent. |
| cabinet_drawer_001 | Cabinet Drawer 1 | drawer, openable, contains_silver_key | — | NOT EXPOSED | — | drawer_inside_anchor | No | Hierarchy/description relation to key_002. |
| cabinet_drawer_002 | Locked Cabinet Drawer | drawer, locked, lockable | — | NOT EXPOSED | — | drawer_inside_anchor | No | Semantic only. |
| cabinet_drawer_003 | Cabinet Drawer 3 | drawer, openable | — | NOT EXPOSED | — | drawer_inside_anchor | No | Semantic only. |
| lock_002 / lock_003 | Drawer locks | lock, drawer_lock, golden_key_target | — | NOT EXPOSED | — | — | No | No operational lock state. |
| clue_note_001 / clue_note_002 | Clue Notes | clue, note, readable | — | NOT EXPOSED | — | hierarchy / NOT DEFINED | No | Read event only if picked up. |
| basket_001 | Basket | basket, receptacle, ball_target | — | NOT EXPOSED | — | basket_inside_anchor | No | Transform is not an AuthoringAnchor. |
| painting_001 | Crooked Painting | painting, movable, rotatable | — | NOT EXPOSED | — | — | No | Semantic task affordance only. |
| lamp_001..lamp_004 | Puzzle Lamp | lamp, light, feedback_object | — | NOT EXPOSED | — | — | No | Ambiguous duplicate display name. |
| wall_front/back/left/right variants | Walls | wall, room_structure, static | — | — | — | — | No | `editable=false`. |
| room_floor | Room Floor | floor, room_structure, static | — | — | — | — | No | `editable=false`. |

## STATIC UNCERTAINTIES

- Scene YAML cannot prove the final instantiated hierarchy/name of every prefab child, physics/collider shape, or current transforms after editor overrides.
- It cannot prove actual hand/controller presence, pointer hit behaviour, connection state, server-generated C3 task, current quest step, or runtime component configuration after launch.
- `DrawerOpenAnchor` was manually positioned during prior work; its exact current pose must be checked in Unity Scene/Prefab View.
- Verify at runtime that `table_drawer_001`'s prefab `ExperimentalDrawerController` retains its anchor references after bootstrap. The bootstrap configures the controller but does not assign missing anchors.
