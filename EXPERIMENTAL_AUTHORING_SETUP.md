# CHI 2027 experimental authoring — setup and migration

## Final conditions

One Escape Room scene is used for all conditions; choose the condition in `StudyConfiguration` before starting the session. `ExperimentConditionManager` freezes it during the playthrough.

| Condition | Voice | World authoring | Quest progression |
| --- | --- | --- | --- |
| `VoiceCommandBaseline` (C1) | Predefined structured commands | No | Fixed sequence |
| `PlayerAuthoring` (C2) | Structured authoring requests | Yes, validated | Fixed sequence |
| `DynamicStorytelling` (C3) | Same as C2 | Yes, validated | Server supplies each next task |

C1 intentionally keeps microphone capture, push-to-talk, STT, object selection and comparable feedback visible. It only rejects generative authoring. Proactive authoring proposals are disabled for every condition.

## Runtime GameObject

On a persistent `ExperimentalAuthoringRuntime` GameObject add and wire:

1. `ExperimentConditionManager`, with a frozen `StudyConfiguration` asset;
2. `AuthoringProtocolClient` (incoming 101, outgoing 102);
3. `AuthoringActionExecutor`, `AuthoringUndoManager`, `ExperimentTelemetry`, `AuthoringProposalPresenter` and `ExperimentalPlaythroughReset`;
4. `PredefinedVoiceCommandExecutor`;
5. `QuestEventBus`, `QuestEventDrivenValidator`, `RuntimeTaskValidator`, and `DynamicStoryTaskController`.

Assign `SceneContextTransmitter` to the executor, protocol client, predefined executor and dynamic controller. This causes immediate scene snapshots after all world changes and before C3 asks the server for a next task.

## SceneAPI / BehaviorAPI Vertical Slice Setup

`VerticalSliceRuntimeBootstrap` runs only for `DreamCodeVR2_EscapeRoom_Testbed` and creates the persistent `ExperimentalAuthoringRuntime` root at load time. It wires the listed runtime components to the existing ContextBridge/SceneContext services and configures the shared micro-puzzle:

- `table_drawer_001`: `ExperimentalDrawerController`, C1 OPEN/CLOSE adapter, C2/C3 `SET_AFFORDANCE: grabbable` and colour capability.
- `key_001`: Rigidbody plus `ExperimentalGrabbableAdapter`, initially grabbable; picking it up publishes `ObjectPickedUp(key_001)`.
- `lock_001` and `door_001`: quest-critical, no authoring operations, protected in the first task.

The fixed plan is `vertical_slice_fixed`: retrieve `key_001`, then use it with `lock_001`. C1 and C2 use the same plan. A researcher must select the condition in `ExperimentConditionManager` and start the session before the server requests are accepted.

SceneAPI messages are mapped by `SceneApiExecutor` to the existing allowlisted dispatcher: `setProperty`, `setAffordance`, `createObject`, `relocateObject`, `setSemanticState`. BehaviorAPI maps only `addBehavior` (`rotate_continuously`, `blink`) and `linkObjects(..., "activate")`.

## Object migration

For every `AIEditableObject`:

- add `VoiceCommandCapabilities` for C1 verbs and assign its `PredefinedVoiceCommandTarget` adapter;
- add `AuthoringCapabilities` for C2/C3 only, explicitly listing allowed operations/properties/behaviours and `SET_AFFORDANCE` where applicable;
- set `questCritical`, protected properties and forbidden affordances on task-critical objects;
- add `AuthoringAnchor` only to approved creation/relocation positions.

`PredefinedVoiceCommandTarget` is deliberately a limited adapter (`Open`, `Close`, active state, named up/down preset, use-with). Wire its state/preset references in the Inspector; do not use reflection or arbitrary component names.

For an existing drawer, expose `OPEN`/`CLOSE` in `VoiceCommandCapabilities`; expose `SET_AFFORDANCE: grabbable` in `AuthoringCapabilities` only if that modification is allowed for the current study configuration.

## Protocol

All 101/102 packets retain the 36-byte peer UUID prefix. C1 accepts `predefined_command`; C2/C3 accept authoring proposal/execution/undo messages. C3 also accepts `next_task` and sends `task_completed`/`next_task_ack`.

`NextTaskSpec` accepts only allowlisted runtime conditions: object-at-anchor, object state/affordance, object grabbed/used, link/behaviour active, sequence complete, AND and OR groups. It never evaluates code or arbitrary expressions.

## Reset and tests

Call `ExperimentalPlaythroughReset.ResetExperimentalPlaythrough()` between participants/conditions. It restores captured transforms, active state, physics, colliders and materials; removes generated objects/behaviours/links; clears undo, processed actions, active quest and pending proposal.

Manual F3 completion is debug-only. In a study build, publish actual interaction events through `QuestEventBus`; `QuestEventDrivenValidator` advances supported fixed tasks automatically.

### Required manual tests

1. C1: `OPEN drawer_001` succeeds; `SET_AFFORDANCE grabbable` is rejected.
2. C2: SET_AFFORDANCE grabbable presents confirmation, applies, updates SceneContext, and undo restores it.
3. C2: direct final-door opening/deactivation is rejected by active-task protection.
4. C3: on task completion, verify SceneContext then `task_completed`; verify the runtime displays a safe waiting state, accepts only a valid `NextTaskSpec`, then acknowledges activation.
5. Verify reset restores the starting scene without reopening it.

## Dependency discrepancy

The current repository does not contain production `DrawerInteraction`, `LampController`, `PlatformController`, or a standard VR grab event source. The included `PredefinedVoiceCommandTarget` is therefore an explicit Inspector-wired adapter, while `OBJECT_GRABBED` requires a small adapter from the project’s chosen grabbing implementation to `QuestEventBus`. This preserves the required safety boundary and avoids guessing component APIs.
