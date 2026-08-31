# Required runtime objects wire and creation audit

## SERVER CONTRACT

`quest_instance.required_runtime_objects` is deserialized as an array. Supported current fields are `object_id`, `primitive`, `object_type`, `semantic_profile`, `preset_id`, `material_profile`, `initial_placement_anchor`/`placement_anchor`, `initial_semantic_state`, `initial_grabbable`, `canonical_size_m`, and `canonical_scale`.

| Server field | Unity DTO | Resolved field | Consumed by | Status |
|---|---|---|---|---|
| object_id | object_id | objectId | factory | supported |
| primitive/object_type | primitive/object_type | primitive | factory | supported |
| semantic/preset/material profile | semantic_profile/preset_id/material_profile | matching fields | factory | supported |
| initial placement | initial_placement_anchor/placement_anchor | initialAnchorId | factory | supported |
| state/grabbability/size | matching fields | matching fields | factory | supported |

## UNITY DTO

`ServerRequiredRuntimeObjectDto` is attached to `ServerQuestInstanceDto.required_runtime_objects`.

## RESOLVED QUESTINSTANCE

`QuestRuntimeObjectSpec[]` is resolved into `ResolvedQuestInstance.requiredRuntimeObjects` through the canonical ID resolver.

## PRIMARY/FALLBACK PRECEDENCE

The new field is primary. Legacy `quest_setup`, then `c1_setup`, is read only when it is absent. Fallback emits `RUNTIME_OBJECT_LEGACY_FALLBACK_USED`.

## RUNTIME OBJECT FACTORY

`QuestRuntimeObjectFactory` owns creation and canonical-ID reuse. Existing semantic objects emit `RUNTIME_OBJECT_REUSED`; supported current primitive is sphere.

## SPHERE CREATION

The factory creates a tagged `game` sphere, assigns its canonical ID, collider, rigidbody, aliases/capabilities, declared grabbability and optional semantic state. Soccer-ball profile uses `QuestSoccerBall.CanonicalDiameterMeters`; generic spheres use server size metadata.

## PLACEMENT

The factory resolves the declared anchor before creation placement, uses surface/centre semantics, marks anchor occupancy, and does not create a duplicate at origin. Missing anchors and unsupported primitives are diagnostics.

## POINTING AND SCENE CONTEXT

Created spheres retain the `game` tag for raycasting and trigger a fresh SceneContext snapshot.

## C1/C2/C3 PARITY

Creation occurs when the resolved instance declares the object, before condition-specific interaction affordances. C1 may remain non-grabbable; C2/C3 can author grabbability later on the same object.

## TASK COMPLETION NON-REGRESSION

The factory does not publish task completion. `OBJECT_AT_ANCHOR` remains the basket completion predicate.

## C3

Creation is driven by the active resolved instance only; no future C3 object is pre-created.

## RESET

Runtime sphere markers remain owned by the existing runtime cleanup path on instance/reset; authored scene objects are reused rather than destroyed.

## INSTANCE AUDIT

A1/A2 will create `sphere_001` only when the server declares it. B1/C1 requirements are reported directly by received metadata; no quest-name inference remains in the factory.

## TESTS

Static source verification completed. Unity EditMode/device checks remain required for deserialization fixtures, duplicate creation, tag/scale, reset and C2/C3 affordance changes.

## NEXT DEVICE TEST

Send one instance with `required_runtime_objects` and one legacy payload. Verify received/create/reuse/fallback diagnostics, tag `game`, scale, placement and that creation alone does not complete the basket task.
