# Phase 6/7 client security drop-ins (authored — ON-DEVICE PENDING)

These are **authored, default-off** Unity C# components for the client side of the
hardened profile. **None of them has been run on a device** (no Quest until
2026-07-23; Mode A is Mono/Quest-1-2 only). No runtime claim is made here — only the
**pure logic** is verified, in the Unity Test Runner (EditMode), which is *not* part
of the Rust `cargo` gate.

Everything defaults OFF, so dropping these into a scene changes nothing until armed:
the current unsigned/unconfirmed demo path stays byte-identical.

## Components

| File | Phase | What it adds | Default | Pure logic (EditMode-tested) |
|---|---|---|---|---|
| `VoiceCompileConfirmationGate.cs` | 7 | Require an explicit user confirmation before a Mode-A runtime C# compile | `requireConfirmation = false` | `CompileConfirmationState` (submit → confirm → expire/reset) |
| `PerceptualDisclosureHud.cs` | 6 | On-screen log of covert-manipulation disclosures (the missing consumer for `PerceptualDisclosure`) | `showHud = false` | `DisclosureFeed` (bounded ring + rapid-repeat coalescing) |
| `DisclosureBackendForwarder.cs` | 6 | Forward disclosures OFF the headset to the backend safety log (out-of-process transparency) under **NID 97** | `forwardToBackend = false` | `EncodeNotice` (JSON escaping) |

Tests: `unity/Editor/Phase67SecurityTests.cs` (11 EditMode tests).

## Wiring (opt-in; do on-device)

The on-device demo (`ModeCNetworkedDemo.cs`) is intentionally **not modified** here,
so the verified Quest build is untouched. To arm the hardened client behaviour later:

1. **Confirmation gate (Phase 7).** Add `VoiceCompileConfirmationGate` to the demo
   object; set `requireConfirmation = true`. In the `mtype == "code"` branch, call
   `SubmitOrPassthrough(code, nowMs)`: if it returns `false`, show a confirm prompt
   instead of compiling; on the user's explicit "yes" (button or voice via the intent
   screen) call `Confirm()` and pass the returned code to
   `RuntimeCSharpCompiler.CompileAndAttach`. Call `ExpireIfStale(nowMs)` from `Update`
   and `ResetPending()` on disconnect/reset.
2. **BackendVerifier (auth, already source-complete).** Sequence the confirmation gate
   *after* `BackendVerifier.TryVerify(rawNid94Bytes, nowUnix)` so you only ever prompt
   to run code the backend already signed. This needs the raw NID-94 `byte[]` (today
   the demo stringifies it early), and an `IEd25519Verifier` plugin (BouncyCastle/NaCl)
   for the signature leg — both on-device tasks. Keep `RequireSignature = false` by
   default (legacy passthrough, byte-identical).
3. **Disclosure surface (Phase 6).** Add `PerceptualDisclosureHud` (`showHud = true`)
   and/or `DisclosureBackendForwarder` (`forwardToBackend = true`). For the forwarder,
   drain `TryDequeue` in the main loop and enqueue `(DisclosureBackendForwarder.NidDisclosure, json)`
   onto the demo's `_ctrlOut` queue. NID 97 is dedicated (does not collide with 95
   feedback / 96 compile).

## Honest status

- Verified: the pure state machine, ring/coalescing feed, and JSON encoder (EditMode).
- NOT verified: any MonoBehaviour lifecycle, the compile path, the HUD rendering, the
  Ed25519 signature leg, and end-to-end backend delivery — all require a Quest and are
  deferred to the on-device pass (≥ 2026-07-23).
