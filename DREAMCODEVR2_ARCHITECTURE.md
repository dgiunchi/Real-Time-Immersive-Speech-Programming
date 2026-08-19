# DreamCodeVR2 architecture

The sole study scene is `Unity/Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity`.

`DreamCodeVR2_RuntimeServices` owns the existing Ubiq room and STT transport. Audio is sent on NetworkId 98. ContextBridge collects pointing/selection and quest state; SceneContext serializes the semantic scene snapshot and transmits it on NetworkId 100. The experimental protocol uses NetworkIds 101 (incoming) and 102 (outgoing); NetworkId 99 remains reserved for the existing server integration.

`VerticalSliceRuntimeBootstrap` creates `ExperimentalAuthoringRuntime` and wires the condition manager, deterministic SceneAPI/BehaviorAPI executor, confirmation UI, event bus, quest runtime, reset service, telemetry and researcher panel. It also configures the drawer, key, lock and door for the `vertical_slice_fixed` quest.

SceneAPI supports `setProperty`, `setAffordance`, `createObject`, `relocateObject` and `setSemanticState`. BehaviorAPI supports `rotate_continuously`, `blink` and `activate` links. Actions are validated against object capabilities and task protection before execution.

There is no arbitrary runtime C# generation, Roslyn runtime, NetworkId 94 listener, or mixed-initiative authoring path.
