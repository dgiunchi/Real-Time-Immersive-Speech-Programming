# DreamCodeVR2 Context Bridge

This folder contains the Unity-side Context Bridge for GateContext enrichment.

It sends `InteractionContextUpdate` messages over Ubiq `NetworkId(99)` using:

`[36-byte peer UUID][UTF-8 JSON InteractionContextSnapshot]`

It does not change the STT/audio path on `NetworkId(98)` or generated code responses on `NetworkId(94)`.

## Scene Setup

For a manual smoke test:

1. Open `Assets/Demos/DynamicCompiler/DynamicCompiler.unity`.
2. Create a `Context Bridge` GameObject.
3. Add these components to it:
   - `SceneRegistry`
   - `InteractionContextProvider`
   - `InteractionContextTransmitter`
4. Assign the `SceneRegistry` to the provider.
5. Assign the provider to the transmitter.
6. Assign pointer origins on the provider:
   - VR: controller or selector transforms.
   - Desktop smoke test: camera transform.
7. Assign `CodeGenerationManager` to the provider if available.
8. Assign existing `SelectObjectRay` sources if available.
9. Add `AIEditableObject` to one cube with a collider.
10. Configure the cube:
   - `objectId = "cube_001"`
   - `displayName = "Demo Cube"`
   - `labels = ["cube", "demo_object"]`
   - `editable = true`

## Triggering Sends

By default, `InteractionContextTransmitter` sends snapshots when `MicrophoneCapture.RecordingStateChanged` fires:

- recording start
- recording stop

For manual testing:

- Set `manualSendKey` on `InteractionContextTransmitter`, or
- Use the component context menu item `Send Context Snapshot`.

Periodic sending is disabled by default. To enable it while recording:

- set `sendPeriodicallyWhileRecording = true`
- set `periodicHz`, typically `5`

To disable periodic sending again, set `sendPeriodicallyWhileRecording = false`.

## Smoke Test

1. Start the DynamicCompiler scene.
2. Point at the cube with `AIEditableObject`.
3. Trigger a manual context send or press push-to-talk.
4. Check Unity logs for:

```text
[ContextBridge] sent peer=... active_selection=cube_001 pointed_object=cube_001 scene_version=0
```

`active_selection` can be `null` unless the cube is selected through `CodeGenerationManager.targetObject` or an existing `SelectObjectRay` source. `pointed_object` should be `cube_001` when the provider-owned raycast hits the cube.

