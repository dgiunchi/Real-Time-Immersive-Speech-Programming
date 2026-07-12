# Unity Integration

DreamCodeVR+ ships **authored Unity C# drop-ins**, not a full Unity project (the
original UCL Unity project and the proprietary RoslynCSharp asset are **not**
included — see [`../NOTICE`](../NOTICE)).

## What's here

- `unity/Runtime/` — the canonical drop-in scripts: the Mode-C action-plan
  executor, the safe behaviour registry (each action → one vetted UnityEngine
  op with re-clamped bounds), the object/spawn tracker, protocol models (mirror
  the Rust bounds), and the perceptual-safety monitors.
- `unity/Editor/` — EditMode tests for the executor.
- `unity-examples/ModeC/Assets/DreamCodeVRPlus/` and
  `unity-examples/Networked/Assets/DreamCodeVRPlus/` — example project scripts,
  including a self-contained Ubiq TCP client and a runtime C# compiler used for
  Mode-A demos in the Unity 6 editor.

## Wiring (Mode C — the safe default)

1. Add `unity/Runtime` (and `unity/Editor`) to a Unity 6 project.
2. Register `ActionPlanNetworkBridge` on Ubiq `NetworkId(94)` and **remove any
   original `CodeGenerationManager` from NID 94** — do not register both.
3. Wire the selection ray and executor references; point the Unity Ubiq config at
   the backend's room.
4. Run the backend in its default action-plan mode; spoken/typed commands change
   the target object's colour/scale/motion with **no runtime compilation**.

## Mode A (runtime C#) — research/demo only

The `unity-examples/Networked` project includes a self-contained
`RuntimeCSharpCompiler` (uses Microsoft.CodeAnalysis directly) so the original
generate-and-compile path can be demonstrated in the Unity 6 editor **without**
the proprietary RoslynCSharp asset. Enable with `DCVR_MODE_A=true` on the
backend. Mode A widens the trust surface — see [SECURITY_MODEL.md](SECURITY_MODEL.md).

## Status and limitations

- The Mode-C executor and Mode-A demo have been verified in the Unity 6 editor
  and on a Quest 1/2 Mono sideload; **headless EditMode tests require a Unity
  licence sign-in** and are not part of the automated Rust gate.
- **Quest 3 / Store (IL2CPP/ARM64)** builds are future work: IL2CPP forbids
  runtime C# compilation, which is exactly why Mode C (no compilation) is the
  deployable path. Mode A is limited to sideloaded Mono builds.
- The perceptual-safety monitors are opt-in and Unity-side; they disclose rather
  than block, preserving creative freedom.
