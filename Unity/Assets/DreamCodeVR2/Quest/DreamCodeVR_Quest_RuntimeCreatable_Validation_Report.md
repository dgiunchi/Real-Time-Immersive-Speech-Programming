# DreamCodeVR Quest Runtime-Creatable Validation Report

## Where The Error Came From
- The validation error came from `QuestPlanApplier.ValidatePlan`.
- `error_risk.target` was being checked through the same strict scene-object validator used for references that must already exist in the scene.
- As a result, valid runtime-created references such as `soccer_ball_001` were rejected before apply time.

## Fields Previously Treated As Scene-Object-Only
- `tasks[].target`
- `tasks[].key`
- `tasks[].lock`
- `initial_setup[].object` for most actions
- `clues[].object`
- `error_risk.target`
- `error_risk.correct_target`
- `error_risk.distractor_targets`

## Fields That Should Allow Runtime-Creatable Objects
- `tasks[].object_to_create`
- `initial_setup[].object` when `action == ResetCreatedObject`
- `error_risk.target` when it refers to a runtime-creatable object such as:
  - `soccer_ball_001`
  - `colored_cube_001`
- `error_risk.target` when the object is inferred from:
  - `RuntimeCreatableObjectCatalog`
  - `tasks[].object_to_create`
  - `initial_setup[].ResetCreatedObject`

## Fields That May Be Anchor Or Object References
- `error_risk.correct_target`
- `error_risk.distractor_targets`

These can legitimately contain:
- object ids
- lock ids
- fully qualified anchor references such as:
  - `basket_001.basket_inside_anchor`
  - `table_001.desk_surface_anchor`
  - `cabinet_001.cabinet_top_anchor`

## Fix Strategy
- Keep strict scene validation for keys, locks, clue objects, and normal scene targets.
- Add a dedicated validation path for references that may be runtime-creatable.
- Accept a reference as runtime-creatable if any of the following are true:
  - supported by `RuntimeCreatableObjectCatalog`
  - declared in `tasks[].object_to_create`
  - declared in `initial_setup` with `ResetCreatedObject`
- Add a separate validation path for anchor-or-object fields so valid anchor references are not rejected as missing scene objects.

## Manual Validation Cases
- Valid:
  - `soccer_ball_001` in `error_risk.target` when declared in `object_to_create`
  - `colored_cube_001` in `error_risk.target` when declared in `object_to_create`
  - `basket_001.basket_inside_anchor` in `error_risk.correct_target`
  - `table_001.desk_surface_anchor` in `error_risk.distractor_targets`
- Invalid:
  - `banana_999` in `error_risk.target`
  - `unknown_key_999` in `error_risk.correct_key`
  - `unknown_lock_999` in `tasks[].lock`
  - `unknown_clue_999` in `clues[].object`
