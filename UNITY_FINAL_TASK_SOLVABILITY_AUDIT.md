# Final Task Solvability Audit

## QUESTINSTANCE DELIVERY/APPLICATION

Researcher start/restart responses now deserialize `questInstance` or `quest_instance`. For C1/C2,
the panel resets the current run, starts the selected condition, then applies the returned instance
before READY. Missing instance data fails the start rather than leaving stale state. C3 skips fixed
instance application. The runtime applies bindings, notes, lamp choice, plan, reset state, and
C1 sphere setup; full placement/initial-state fields still depend on server fields represented by
the current QuestInstance DTO.

## RESET

Applying an instance resets C1 sphere, lock state, door, painting/clue and lamps before applying
new bindings. The existing playthrough reset remains in the start path.

## C2/C3 PAINTING

Outside C1, applying an instance enables the existing grasp adapter on painting with Rigidbody.
It detects position and rotation tolerance around the aligned anchor, then uses the same alignment
state/clue/event/context path as C1. C1 disables this physical path.

## C2/C3 LAMPS

Shared typed operational actions `activate`, `deactivate`, `toggle` now use QuestLampController,
return AuthoringAck and update task-observable state. Open/close and use_with share the same
operational layer for C2/C3.

## CREATE SOCCER BALL

Authoring create sphere accepts `parameters.material` or `value` equal to `soccer_ball_material`.
It retains requested ID, applies Resources/SoccerBall, starts non-grabbable, and exposes validated
sphere capabilities.

## SOCCERBALL MATERIAL

`Unity/Assets/Resources/SoccerBall.mat` exists, matching `Resources.Load<Material>("SoccerBall")`.

## GRABBABILITY

Created sphere has the real ExperimentalGrabbableAdapter disabled initially; SET_AFFORDANCE uses
that adapter, not metadata. C1 sphere/keys stay disabled when C1 instance is applied.

## KEY/LOCK SOLUTION

C1 uses semantic USE_WITH. C2/C3 use the typed `use_with` operational interaction against
QuestLockController; grab alone does not complete unlock.

## DRAWER/DOOR SOLUTION

C1 remains predefined. C2/C3 use typed `open`/`close` operational interaction with existing
drawer/door controllers and lock checks; no authoring operation is used.

## C3 GENERATED TASK SOLVABILITY

Activation now rejects missing referenced objects or anchors with C3_TASK_ACTIVATION_REJECTED.
No completion is sent for rejected tasks. Existing supported component validation remains
condition-based.

## SCENECONTEXT

Existing controller state and runtime-created metadata are refreshed on interactions; applied
instance ID itself is not presently serialized as a top-level SceneContext field.

## MANUAL TEST PLAN

Run C1 A1/A2/B1/C1, C2 ball/painting/lamp/key/openable paths, and C3 stale task rejection. Verify
server response provides nested QuestInstance fields and test XR physical grasp/placement on device.

## REMAINING DEVICE-ONLY UNCERTAINTIES

No Unity batchmode or device test ran. Painting rigidbody/grasp behaviour, basket trigger bounds,
and exact server QuestInstance schema must be verified in the editor/device.
