# DreamCodeVR2 Scene Context

This folder contains the Unity-side Scene Context Compiler v0.

It sends `SceneContextUpdate` messages over Ubiq `NetworkId(100)` using:

`[36-byte peer UUID][UTF-8 JSON SceneContextPacket]`

The JSON packet includes `schema_version` and a full-scene snapshot of `AIEditableObject` summaries. It does not modify:

- `NetworkId(98)` audio/STT
- `NetworkId(99)` interaction context
- `NetworkId(94)` generated-code output

## Files

- `SceneContextTypes.cs`
- `SceneContextCompiler.cs`
- `SceneContextTransmitter.cs`

## Manual Setup

1. Open the Unity scene you want to test, for example `Assets/Demos/DynamicCompiler/DynamicCompiler.unity`.
2. Create a `Scene Context` GameObject.
3. Add these components:
   - `SceneRegistry`
   - `SceneContextCompiler`
   - `SceneContextTransmitter`
4. Assign the scene's `SceneRegistry` to `SceneContextCompiler.sceneRegistry`.
5. Assign the `SceneContextCompiler` to `SceneContextTransmitter.compiler`.
6. Ensure the scene contains one or more `AIEditableObject` components.
7. Optionally tune:
   - `initialSendDelaySeconds` default `1.5`
   - `snapshotIntervalSeconds` default `15`

## What Gets Sent

For each `AIEditableObject`, the compiler includes compact summary fields:

- `id`
- `display_name`
- `unity_name`
- `semantic_types`
- `labels`
- `description` when present
- `position`
- `rotation`
- `scale`
- `active`
- `editable`
- `parent_id` when a parent `AIEditableObject` exists
- `materials`
- `components`
- `available_operations`

## Runtime Behavior

- One full snapshot is sent shortly after startup.
- One full snapshot is sent every `15` seconds by default.
- No per-frame sending.
- No delta updates in v0.

## Verification

In Unity, look for logs like:

```text
[SceneContext] sent objects=3 bytes=1427 scene_version=0 reason=startup
```

On the server side, verify that:

1. `NetworkId(100)` traffic is arriving.
2. The packet starts with the 36-byte peer UUID prefix.
3. The remaining UTF-8 JSON parses as `SceneContextPacket`.
4. `SceneContextStore` updates the latest scene context for that peer.
5. `GateContext` receives `scene_context` before IntentGate classification.

If the Unity log shows a warning about peer UUID length, the transmitter will not send until `RoomClient.Me.uuid` encodes to exactly 36 UTF-8 bytes.
