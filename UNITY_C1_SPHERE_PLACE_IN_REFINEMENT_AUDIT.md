# C1 Sphere and PLACE_IN Refinement Audit

## C1 QUEST SPHERE

`QuestInstance` now explicitly declares `requiresC1Sphere`, the stable sphere ID, starting
anchor, and placement anchor. `QuestInstanceController.Apply` creates the primitive only when the
active condition is C1 and that flag is true. The default ID is `sphere_001`; no sphere is stored
in the scene or auto-created in C2/C3.

## SOCCER BALL PRESET

The C1 sphere advertises `MOVE_TO_PRESET` and `soccer_ball`. That exact target/preset invokes
`C1QuestSphereController`, preserving its ID and geometry. It applies only the real material at
`Resources/SoccerBall`; if absent it fails without claiming `soccer_ball` semantic success.

## PLACE_IN EXECUTOR

`PLACE_IN` is separate from `USE_WITH`. It verifies the active QuestInstance sphere/receptacle
relation, resolves its configured placement anchor, and calls `QuestPlacementMonitor.NotifyPlaced`.
The monitor changes real parent/anchor state and the normal validator observes it; no command path
directly marks a task complete.

## USE_WITH COMPATIBILITY

The client consumes canonical primary/secondary IDs only. Therefore server transcripts such as
use, put, or insert all work once normalized to `USE_WITH(key, lock)`. Key compatibility and exact
configured lock binding are still enforced.

## C1 NON-GRABBABLE POLICY

The temporary C1 sphere is non-editable and non-grabbable. Applying a C1 instance also disables
key grabbing, while preserving semantic `USE_WITH`; no C1 SET_AFFORDANCE capability is exposed.

## C2/C3 GRABBABLE AUTHORING

Neither C2 nor C3 injects the C1 sphere. Authoring-created spheres retain requested IDs, begin
non-grabbable, and allow `SET_AFFORDANCE grabbable` only through the existing capability and quest
integrity validation path. Both conditions share this path.

## TASK SUCCESS PARITY

Fixed task plans can now contain the same `RuntimeSuccessCondition[]` used by C3. The event-driven
validator delegates these conditions to `RuntimeTaskValidator`, so C1 semantic PLACE_IN and C2/C3
physical containment converge on `OBJECT_AT_ANCHOR`.

## SCENECONTEXT

SceneContext now exports per-object `predefined_presets` alongside voice verbs. The C1 sphere
exports its stable identity, labels, active state, semantic state, affordance state, commands,
presets, and anchor parent while remaining non-editable. Runtime authoring spheres retain only
their validated capability surface.

## RESET

Quest-instance application removes an older C1 temporary sphere before configuring the next
instance. The normal playthrough reset also explicitly removes it; C2/C3 runtime-created spheres
remain covered by the existing `runtime_created` cleanup.

## MANUAL TEST PLAN

1. In C1 Set A, apply an instance with a real start and basket placement anchor; verify sphere
   creation, non-grabbability, preset proposal/ack, and PLACE_IN completion exactly once.
2. Verify correct and incorrect key-lock USE_WITH requests.
3. In C2, verify no automatic sphere, then create a stable-ID sphere, enable grabbable through
   authoring, and physically place it in the basket.
4. Repeat the authoring case in C3; confirm no automatic injection.
5. Reset or switch condition/set and confirm no stale sphere or completed anchor state remains.
6. Validate basket trigger dimensions in Scene View and on device.

## MISSING VISUAL ASSETS

No project asset was found at `Resources/SoccerBall`. Add the approved soccer-ball material at
`Unity/Assets/Resources/SoccerBall.mat` (or adjust the explicit loader to the approved asset) before
expecting visual preset success.

## STATIC UNCERTAINTIES

Unity batchmode was not run. This change was source-inspected only; Unity editor compilation,
server QuestInstance deserialization, and device placement tests remain required.
