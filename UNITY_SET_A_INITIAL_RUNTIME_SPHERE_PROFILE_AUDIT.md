# Unity Set A initial runtime sphere profile audit

## CURRENT SUPPORT

Before this change, the client could create `sphere_001`, position it at the declared runtime anchor and attach `C1QuestSphereController`, but did not invoke `TrySetProfile` during initial runtime setup. `preset_id` / `semantic_profile` affected sizing and available presets only; they did not change the material or `SphereProfile`.

## SERVER PROFILE FIELD

The explicit supported field is:

```text
quest_instance.required_runtime_objects[].sphere_profile
```

It is deserialized by `ServerRequiredRuntimeObjectDto.sphere_profile` and copied to `QuestRuntimeObjectSpec.sphereProfile`.

## RUNTIME OBJECT APPLY PATH

```text
QuestResetRequest
  -> QuestConsequenceDispatcher.ReceiveReset
  -> QuestConsequenceDispatcher.ToRuntime
  -> QuestInstanceController.Apply
  -> QuestInstanceController.EnsureRuntimeObjects
  -> QuestRuntimeObjectFactory.Ensure
  -> C1QuestSphereController.TrySetProfile
  -> SceneContext snapshot
  -> QUEST_CANONICAL_SET_APPLIED
  -> RESET_COMPLETED
```

The NetworkId 101 fixed-task path uses the same `QuestRuntimeObjectSpec` through `FixedQuestWireConverter.ConvertRuntimeObjects`.

## SPHERE CONTROLLER

The implementation reuses `C1QuestSphereController.TrySetProfile`:

- `football` applies the existing `Resources/SoccerBall` material and reports `SphereProfile = football`;
- `neutral` restores the captured authored material and reports `SphereProfile = neutral`.

No condition-based branch was introduced. The server owns the choice of `football` for C1 and `neutral` for C2.

## PATCH REQUIRED YES/NO

Yes. The profile field was previously not represented in the DTO and profile application was absent from the initial runtime-object path.

## PATCH APPLIED

The patch:

- adds `sphere_profile` to `ServerRequiredRuntimeObjectDto`;
- preserves it as `QuestRuntimeObjectSpec.sphereProfile` through both reset and fixed-task conversion paths;
- applies it through `TrySetProfile` immediately after creating or reusing the sphere controller;
- emits `QUEST_RUNTIME_OBJECT_PROFILE_APPLIED` with object, requested/resulting profile, canonical set, condition, source and success;
- emits the resulting `sphere_profile` in `QUEST_CANONICAL_SET_APPLIED`;
- leaves failed applications visible: the event is a warning and `RESET_COMPLETED` reports the controller's actual state rather than echoing the requested value.

## C1 INITIAL PROFILE

When the server sends:

```json
{ "object_id": "sphere_001", "sphere_profile": "football" }
```

the sphere is created/placed at `cabinet_drawer_003.drawer_inside_anchor`, then visually and semantically becomes `football` before `RESET_COMPLETED`.

## C2 INITIAL PROFILE

When the server sends `sphere_profile: neutral`, the exact same path restores/applies the neutral profile. There is no local condition check; this remains neutral until a valid authoring action changes it.

## RESET SEMANTIC STATE

`QuestWorldStateReporter.BuildResetState` reads `C1QuestSphereController.SphereProfile`, so `RESET_COMPLETED.semantic_state.sphere_001` reports the actual applied `football` or `neutral` value.

## A-T2 NON-MUTATION

Drawer discovery does not call the initial runtime-object factory or `TrySetProfile`. Opening `cabinet_drawer_003` only produces normal drawer/reveal world-state evidence; it does not mutate the sphere profile.

## SET C NON-REGRESSION

`QuestConsequenceDispatcher` retains its existing `SET_SPHERE_PROFILE` consequence handling and still calls `C1QuestSphereController.TrySetProfile`. No Set C timing or reveal logic was changed.

## TESTS

Added editor tests for:

- deserializing `required_runtime_objects[].sphere_profile` into the runtime spec;
- applying `football` during runtime sphere creation;
- reapplying `neutral` to an existing runtime sphere without a condition-specific branch.

## DEVICE TEST

After the server sends the explicit field:

1. Start Set A in C1 and inspect the closed `cabinet_drawer_003`: the sphere must already be football.
2. Confirm logs contain `QUEST_RUNTIME_OBJECT_PROFILE_APPLIED` with `profile=football`, `success=true`, `source=initial_runtime_setup` before `RESET_COMPLETED`.
3. Complete A-T2; the profile must not change merely because the drawer is discovered.
4. Start Set A in C2; the sphere must start neutral and remain neutral through A-T3 unless authored.
5. Retest Set C to confirm its later consequence-driven profile change still works.
