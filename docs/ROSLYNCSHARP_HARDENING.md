# RoslynCSharp compile-time hardening (Mode-A, on-device)

The backend already validates generated C# (Rust lexical denylist + optional real
Roslyn analyzer). This adds a **second, in-engine layer**: configure the proprietary
**RoslynCSharp** asset (Trivial Interactive) so the Unity runtime compiler itself
refuses the dangerous surface — defence-in-depth for the Mode-A runtime-compile path
(A027–A057).

> This is an **on-device** step: the RoslynCSharp asset lives in the Unity project
> (it is not redistributed in this repo), so its exact serialized `.asset` fields
> depend on your installed version. Below is the intended configuration, mapped to
> RoslynCSharp's documented settings; apply it in the Unity Inspector on the
> `ScriptSecurity`/`RoslynCSharp` settings asset and commit the resulting `.asset`
> alongside the project.

## 1. Turn security ON and fail-closed

- **Enable code security verification** (`ScriptSecurityMode` / "Security Check": on).
- Reject on any violation (do not "warn only").
- **Do not exempt hot-reload:** set the hot-reload security check ON
  (`hotReloadSecurityCheckCode = 1`) and/or disable hot-reloading for Mode-A
  (`allowHotReloading = 0`). This closes A053 (hot-reload verifier bypass).

## 2. Restrict referenced assemblies (allow-list)

Compile generated code against the **minimum** assemblies. Include the Unity engine
+ your gameplay assembly; **exclude**:

- `System.Net*`, `System.Net.Http`, `UnityEngine.UnityWebRequestModule` (A034/A050 network)
- `System.Diagnostics.Process` / `System.Diagnostics` (A035 process)
- `System.IO*` beyond what is needed (A033 filesystem)
- `System.Reflection*`, `System.Runtime.InteropServices` (A032/A036 reflection/native)
- `System.Threading` (A042 threads)
- The **Meta XR / OVRPlugin** assemblies (device/rig/haptics/biometric surface)

## 3. Namespace / type restrictions (mirror the Rust DeployHardened set)

Deny these namespaces/types in the RoslynCSharp reference validator so they match the
Rust `csharp-policy` `DeployHardened` denylist (`crates/csharp-policy/src/lexical.rs`):

**Always banned (system access):** `System.IO`, `System.Net`, `System.Net.Sockets`,
`System.Reflection`, `System.Diagnostics`, `System.Threading`,
`System.Runtime.InteropServices`, `UnityEngine.Networking`, plus the identifiers
`Process`, `Assembly`, `AppDomain`, `Environment`, `Activator`, `Marshal`, `DllImport`,
`UnityWebRequest`, `GetType`, `Resources`, `PlayerPrefs`, `Application.Quit`,
`SendMessage*`; ban `dynamic` and `unsafe`.

**Perceptual/device (the DeployHardened extension — deploy-hardened builds only):**
namespaces `UnityEngine.XR`, `Unity.XR`, `UnityEngine.InputSystem.XR`; identifiers
`XRSettings`, `XRDevice`, `InputTracking`, `XROrigin`, `TrackedPoseDriver`, `OVRManager`,
`OVRCameraRig`, `OVRPlugin`, `OVRBoundary`, `OVRInput`, `InputDevices`,
`XRInputSubsystem`, `OVRHaptics`, `SendHapticImpulse`, `Vibrate`, `WebCamTexture`.

**Deliberately NOT banned lexically** (dual-use / lexically indistinguishable from
legitimate creation — enforced at runtime by `UserFrameGuardian` / the perceptual
monitors instead): `Camera.main.transform`, viewport `fieldOfView`/`rect`,
`OnRenderImage`/`Blit`/`RenderTexture`, `GameObject.Find`/`FindObjectsOfType`,
`Renderer.enabled`/`SetActive`, and the Quest-3 MR/interaction APIs `OVRPassthroughLayer`,
`OVRSpatialAnchor`/`OVRSceneAnchor`/`OVRSceneManager`, `OVRHand`/`OVRSkeleton`/
`OVREyeGaze`/`OVRFaceExpressions`. (See the same file's docstring for why.)

## 4. Verify on-device

1. Enable Mode A (`DCVR_MODE_A=true`) + the hardened profile.
2. Speak/type a benign build ("make a small house") → compiles + runs.
3. Send a probe that references a banned API (e.g. `System.Net`) → RoslynCSharp must
   **refuse to compile** it, and the backend must already have rejected it (so it
   never reaches Unity). Confirm both layers fire.

## Cross-references

- Rust denylist + rationale: `crates/csharp-policy/src/lexical.rs`.
- Backend-signature gate before compile: `unity/Runtime/Security/BackendVerifier.cs`.
- Overall hardened profile: `docs/HARDENING.md`, `docs/SECURITY_MODEL.md`.
