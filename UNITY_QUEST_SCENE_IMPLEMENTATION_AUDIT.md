# Quest Scene Implementation Audit

## Implemented runtime world model

- All six canonical drawers receive `ExperimentalDrawerController`, C1 `OPEN`/`CLOSE`, kinematic rigidbody, non-grabbable adapter, explicit C2 allowlist (`SET_AFFORDANCE`, `SET_PROPERTY:color`) and SceneContext refresh through the controller.
- Existing `table_drawer_001` preserves its prefab `DrawerClosedAnchor` / `DrawerOpenAnchor` references and world-space SmoothStep movement.
- The other drawer controllers are created with stable, unique motion anchors. Their equal default poses deliberately fail validation rather than guessing a direction.
- Placement transforms are registered at runtime as `AuthoringAnchor` components with fully qualified IDs such as `table_drawer_002.drawer_inside_anchor`. `basket_001.basket_inside_anchor` is quest restricted, so C2 cannot spawn directly into the success location.
- `QuestLockController` stores `Locked`/`Unlocked`, required key and associated target at runtime. `QuestInstanceController` applies bindings rather than deriving a relation from key colour/labels.
- `QuestDoorController` checks its lock before moving between anchor poses. It fails safely until the open pose is authored.
- `QuestPaintingController` owns `IsAligned`; alignment reveals note 1 only when the physical aligned pose is configured and applied.
- Notes receive `QuestNoteController`; quest instances can set their text and visibility. Lamps get unique display names and `QuestLampController` active/inactive state.
- SceneContext adds nullable world-state fields: `is_open`, `is_locked`, `required_key_id`, `associated_target_object_id`, `is_aligned`, `is_lamp_active`, and `placement_anchor_ids`.

## MANUAL SCENE VIEW ACTIONS REQUIRED

The following poses were intentionally **not guessed**. Until each Open/Aligned pose differs from its Closed/Crooked pose, the respective command fails safely with an actionable error.

| Hierarchy path / owner | Transform(s) to position | Required action |
|---|---|---|
| `table_001` / `table_drawer_001` | `DrawerClosedAnchor`, `DrawerOpenAnchor` | Retain current verified poses; confirm the open anchor remains in the desired direction. |
| Parent of `table_drawer_002` | `DrawerClosedAnchor_table_drawer_002`, `DrawerOpenAnchor_table_drawer_002` | Keep closed at the actual drawer pose; move open pose along its physical drawer travel. |
| Parent of `table_drawer_003` | `DrawerClosedAnchor_table_drawer_003`, `DrawerOpenAnchor_table_drawer_003` | Same. |
| Parent of `cabinet_drawer_001` | `DrawerClosedAnchor_cabinet_drawer_001`, `DrawerOpenAnchor_cabinet_drawer_001` | Same. |
| Parent of `cabinet_drawer_002` | `DrawerClosedAnchor_cabinet_drawer_002`, `DrawerOpenAnchor_cabinet_drawer_002` | Same. |
| Parent of `cabinet_drawer_003` | `DrawerClosedAnchor_cabinet_drawer_003`, `DrawerOpenAnchor_cabinet_drawer_003` | Same. |
| Parent of `door_001` | `DoorClosedAnchor`, `DoorOpenAnchor` | Set closed to the current door pose and open to a physically credible hinge/open pose. |
| Parent of `painting_001` | `PaintingCrookedAnchor`, `PaintingAlignedAnchor` | Preserve current crooked pose; rotate/move aligned pose to the intended straight presentation. |
| `table_001` / `table_drawer_*`, `cabinet_001` / `cabinet_drawer_*`, `basket_001` | existing placement transforms | Check that each transform lies inside the intended physical container/surface. Their fully qualified runtime IDs are created automatically. |

## Regression checklist

- C1 proposal / YES / NO lifecycle and command correlation are unchanged.
- `table_drawer_001` uses the existing controller and anchor references.
- Researcher panel, Ubiq XR interaction, PTT gating, connection and protocol names are untouched by these changes.
- In Unity, test C1 OPEN/CLOSE for the existing table drawer first; test every new controller only after its manual open anchor is authored.
