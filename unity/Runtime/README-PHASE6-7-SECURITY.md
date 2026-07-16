# Phase 6/7 client security drop-ins (EditMode-verified — ON-DEVICE PENDING)

These are **default-off** Unity C# components for the client side of the hardened
profile. The **pure logic is now verified**: on 2026-07-16 these four files + the
EditMode tests were compiled and run headless in **Unity 6000.5.1f1**
(`-runTests -testPlatform EditMode`) — **11/11 tests passed, 0 compile errors**. So
the code compiles and the confirmation state machine, disclosure feed, and JSON
encoder behave as specified.

**Still ON-DEVICE PENDING** (no Quest until 2026-07-23; Mode A is Mono/Quest-1-2
only): the MonoBehaviour lifecycle, the actual runtime compile path, HUD rendering,
and the Ed25519 signature leg. The EditMode run is *not* part of the Rust `cargo`
gate — reproduce it with:

```
Unity -runTests -batchmode -nographics -projectPath <proj> \
      -testPlatform EditMode -testResults results.xml
```

(a minimal project with these `Runtime/` files + `Editor/Phase67SecurityTests.cs`,
built-in `com.unity.test-framework`).

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

- **Verified (2026-07-16, Unity 6000.5.1f1 headless EditMode, 11/11 passed):** these
  four files compile, and the confirmation state machine, ring/coalescing disclosure
  feed, and JSON encoder behave as specified.
- **NOT verified:** any MonoBehaviour lifecycle, the runtime compile path, HUD
  rendering, the Ed25519 signature leg, and end-to-end backend delivery — all require
  a Quest and are deferred to the on-device pass (≥ 2026-07-23).
