# Current Runtime Command Capability Reference

Source-only audit. An intent is *advertised* only when runtime code assigns it to `VoiceCommandCapabilities`.

## C1 EXECUTORS

There are eight executable intents: OPEN, CLOSE, MOVE_TO_PRESET, USE_WITH, PLACE_IN, ACTIVATE, DEACTIVATE, and TOGGLE. All require C1 plus an advertised capability; protocol execution returns `PredefinedCommandAck` with the original `command_id`, `applied|failed`, and detail. Local debug execution has no network ACK.

| Intent | Target/runtime path | Failure/state/context/task effect |
|---|---|---|
| OPEN/CLOSE | `ExperimentalDrawerController` or `QuestDoorController` | locked associated target, missing controller, or invalid anchors fail; state event and context refresh; object_open/object_closed/door_open |
| MOVE_TO_PRESET | painting `TryAlign`, or C1 sphere soccer preset | missing anchors/material/preset fails; alignment or semantic state refreshes context |
| USE_WITH | key-compatible primary and secondary `QuestLockController` | wrong/missing key or lock fails without unlock; lock state refresh; lock_unlocked |
| PLACE_IN | active instance relation → `QuestPlacementMonitor` | only configured sphere/basket anchor; monitor updates parent/occupancy/event/context; object_at_anchor |
| ACTIVATE/DEACTIVATE/TOGGLE | `QuestLampController` | missing lamp fails; lamp state/context; object_active/object_inactive |

PICK_UP has no executor and is not advertised. No additional executor cases exist.

## C1 OBJECT CAPABILITIES

| Object ID(s) | Advertised verbs | Presets | Runtime condition/component |
|---|---|---|---|
| painting_001 | MOVE_TO_PRESET | none | Bootstrap / QuestPaintingController |
| table_drawer_001..003 | OPEN, CLOSE | none | Bootstrap / ExperimentalDrawerController |
| cabinet_drawer_001..003 | OPEN, CLOSE | none | Bootstrap / ExperimentalDrawerController |
| key_001, key_002 | USE_WITH | none | Bootstrap; key primary and lock secondary |
| door_001 | OPEN, CLOSE | none | Bootstrap / QuestDoorController |
| lamp_001..004 | ACTIVATE, DEACTIVATE, TOGGLE | none | Bootstrap / QuestLampController |
| sphere_001 | MOVE_TO_PRESET, PLACE_IN | soccer_ball | Only after C1 QuestInstance sphere setup |
| lock_001..003, basket_001 | none | none | USE_WITH secondary / placement receptacle only |

That is 14 currently configured advertised objects, or 15 when the conditional C1 sphere exists.

## SOCCER BALL

`QuestInstanceController.Apply` can create `sphere_001` only for C1 with `requiresC1Sphere`, a valid start anchor, and no ID collision. It creates a non-editable, kinematic, non-grabbable primitive sphere labelled `quest_sphere`, `sphere`, `primitive`; C2/C3 do not auto-create it. The soccer preset loads `Resources/SoccerBall`; no matching soccer/football asset was found under `Unity`, so it currently fails and never sets the `soccer_ball` semantic state. C2/C3 authoring-created spheres start non-grabbable with SET_PROPERTY, SET_AFFORDANCE, RELOCATE_OBJECT, TOGGLE_STATE.

## USE_WITH

Primary ID/labels must contain `key`; secondary must have `QuestLockController`. The controller compares the exact configured `requiredKeyId`; wrong keys preserve locked state. Successful unlock can enable the explicitly associated drawer/door to OPEN. The binding is instance-configurable only when an instance is actually applied.

## PLACE_IN

PLACE_IN is not generic. It accepts only active instance sphere ID → `basket_001` → exact `c1SpherePlacementAnchorId`; it calls the anchor’s placement monitor. The monitor reparents the object, marks occupancy, publishes ObjectPlacedInZone and context. Completion remains normal `OBJECT_AT_ANCHOR` evaluation.

## GRABBABILITY

| Condition | Initial / possible state |
|---|---|
| C1 applied instance | sphere and keys disabled; no C1 authoring path |
| C2 | keys bootstrap grabbable, drawers false; editable objects with SET_AFFORDANCE, valid task state, and an adapter may change |
| C3 | same client authoring path/checks as C2 |

## C2/C3 AUTHORING CAPABILITIES

C2/C3 share `IsAuthoringAvailable`; no condition-specific capability surface exists. Generic editable scene objects default to SET_PROPERTY/color unless serialized caps override. Configured drawers advertise SET_PROPERTY/color and SET_AFFORDANCE. Protected lock_001 and door_001 advertise no operations. Created cube/bridge/platform defaults: SET_PROPERTY, ADD_BEHAVIOR (rotate_continuously/blink), RELOCATE_OBJECT, TOGGLE_STATE. Created sphere: SET_PROPERTY (color/scale/kinematic/gravity_enabled), SET_AFFORDANCE, RELOCATE_OBJECT, TOGGLE_STATE. Create is anchor-gated for cube/sphere/bridge_segment/platform. SET_MATERIAL/texture is absent; LINK_OBJECTS has executor code but is not advertised by default caps.

## SUCCESS CONDITIONS

Implemented evaluator cases: OBJECT_AT_ANCHOR, PAINTING_ALIGNED, OBJECT_REVEALED, OBJECT_HELD, OBJECT_OPEN, OBJECT_CLOSED, LOCK_UNLOCKED, OBJECT_ACTIVE, OBJECT_INACTIVE, DOOR_OPEN, AUTHORING_OBJECT_CREATED, AUTHORING_PROPERTY_SET, OBJECT_HAS_STATE, OBJECT_HAS_AFFORDANCE, OBJECT_GRABBED, OBJECT_LINK_ACTIVE, OBJECT_BEHAVIOR_ACTIVE, MULTIPLE_CONDITIONS_ALL, MULTIPLE_CONDITIONS_ANY. `OBJECT_USED_WITH` and `SEQUENCE_COMPLETED` are allowlisted but have no evaluator case and always return false.

## CONDITION MATRIX

| Action | C1 | C2 | C3 | Runtime mechanism |
|---|---|---|---|---|
| straighten painting | advertised | no authoring align operation | same C2 | painting controller |
| open/close drawer | advertised | no authoring operation | same C2 | drawer controller |
| unlock lock | advertised USE_WITH | no generic authoring unlock | same C2 | lock controller |
| put object in receptacle | conditional semantic PLACE_IN | physical after valid grabbable setup | same C2 | placement monitor |
| activate lamp | advertised | no generic lamp authoring operation | same C2 | lamp controller |
| create sphere | no | anchor-gated create | same C2 | authoring executor |
| change property / make grabbable | no | capability/task-gated | same C2 | authoring executor |
| physical grab/place | disabled for C1 sphere/keys | adapter/trigger dependent | same C2 | XR adapter/receptacle |
| task generation | no | no | yes | NextTask pipeline |

## SERVER-CLIENT MISMATCHES

1. No server implementation is present here, so server parsing cannot be verified.
2. Researcher Panel sends set/instance IDs, but no client source receives/applies a QuestInstance from them. Thus C1 sphere setup and variable bindings are not reachable from the visible session path alone.
3. StudyConfiguration defaults `allowedPredefinedCommands` to OPEN/CLOSE, while object capability/executor surfaces expose eight intents; the field has no enforcement call site.
4. OBJECT_USED_WITH and SEQUENCE_COMPLETED cannot evaluate true.
5. `Resources/SoccerBall` is missing, so the advertised C1 sphere preset cannot succeed.

## STATIC UNCERTAINTIES

Unity, server, and device paths were not executed. Scene inventory is bootstrap-derived; serialized values may be overridden at runtime. Trigger dimensions, XR grasp availability, server normalization, and server-side QuestInstance application require manual integration testing.
