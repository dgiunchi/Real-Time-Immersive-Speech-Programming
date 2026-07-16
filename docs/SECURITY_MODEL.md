# Security Model

DreamCodeVR+ treats **all** of the following as untrusted: spoken/typed commands,
STT output, LLM output, network messages, admin requests, stored personalization
profiles, and generated C#. Safety is **defence-in-depth**, not a single gate.

> This is a research prototype. See [LIMITATIONS.md](LIMITATIONS.md) and the
> repository [`SECURITY.md`](../SECURITY.md) for what is and is not hardened.

## Layer 1 — Intent screen (before generation)

A keyword classifier (`command-router`) plus an optional LLM classifier inspect
the raw command and **neutralise** malicious/privacy-violating intent (camera,
mic, exfiltration, keylogging, cyber-attack code, …) by replacing it with a
harmless visual, before any code is generated. Benign creative/edgy content is
allowed. The keyword layer works offline; the LLM layer needs an API key.

## Mode C — bounded action plan (default, safest)

The LLM emits a small JSON **action plan** whose only legal instructions are six
allow-listed behaviours (`set_color`, `set_scale`, `move`, `rotate`,
`spawn_primitive`, `set_physics`) with hard numeric bounds. `code-policy`
**approves iff zero violations**; oversized input is rejected *before* parsing.
Unsafe operations are **unrepresentable**, and there is **no runtime code
compilation**. A per-session spawn budget caps object creation.

## Mode B — validated C#

`csharp-policy` runs a tree-sitter lexical scan that reconstructs dotted names
(defeating whitespace / `global::` / `@` tricks) and bans system-access
namespaces/identifiers. It is **hardened** against three evasion classes found by
the red-team: `using` **namespace aliases**, **Unicode-escape identifiers**, and
`dynamic` late-binding. An optional .NET **Roslyn** semantic check
(`services/roslyn-analyzer`) adds a second layer.

- **Important default (`legacy`):** if the Roslyn service is not wired, a mock
  analyzer **approves**, so the Rust lexical layer is the effective gate. Enable
  and require the real analyzer for a stronger Mode-B posture.
- **`hardened` profile:** Mode A/B **requires a real Roslyn analyzer URL** and
  fails closed — the approve-all mock is refused — and each analyzer request is
  bounded by a timeout so a hung analyzer cannot stall the pipeline.
- The lexical guard is a **denylist**: thorough and adversarially tested, but
  completeness is not guaranteed.

## Mode A — original runtime-C# (OFF by default)

`DCVR_MODE_A=false` by default. When enabled, validator-approved C# is sent
(NID 94) to the client for runtime compilation. This widens the trust surface:
the Unity handler runs whatever code arrives on NID 94. In the **`legacy`**
profile peers are not authenticated, so with Mode A on a malicious room member
could get code compiled on a client — keep it to trusted-network research/demos.
The **`hardened`** profile closes this: NID-94 is **Ed25519-signed by the
backend** and the Unity `BackendVerifier` compiles only backend-approved code, so
a room member cannot inject compileable C# even with Mode A on.

## Mode D — sandbox for untrusted C#

`crates/sandbox` runs untrusted C# in a container hardened with `--network none`,
read-only rootfs + tmpfs, `--cap-drop ALL`, `no-new-privileges`, non-root, and
memory/CPU/PID limits; only a structured report crosses back, and a wall-clock
timeout + process-group kill bound runaway code. **gVisor** (`runsc`) is opt-in
(`DCVR_SANDBOX_DOCKER_RUNTIME=runsc`); the default runtime is `runc`, which per
NIST SP 800-190 is a comparatively soft boundary. Mode D is a research arm, not
on the live speech path.

## Admin / debug panel

Binds to **loopback by default**. Mutating routes honour an optional
`X-Admin-Token`; **if no token is set, mutating routes are unauthenticated** — so
the panel now **refuses to bind to a non-loopback address without a token**
(fail-closed; `bind_allowed`, all profiles). The `/api/sandbox` route
**validates** C# only (it does not execute code on the host). Token comparison is
**constant-time** (`ct_eq`), so a set token cannot be recovered by timing. An
authenticated `POST /api/profile/delete` route erases a stored profile.

## Peer authentication — two profiles

Peer authentication is **profile-gated** (`DCVR_SECURITY_PROFILE`):

- **`legacy` (default):** byte-identical to the original build — peers self-assert
  identity and the Ubiq channel is plaintext. This is what the current Quest demo
  runs; nothing on the wire changes.
- **`hardened` (opt-in):** a versioned, canonical **`AuthEnvelope`** binds every
  message to its identity, sequence, expiry, domain (`NetworkId.b`) and a SHA-256
  payload hash. Two directions of cryptography (audited `ring 0.17`):
  **client→backend HMAC-SHA256** admission and **backend→Unity Ed25519**
  signatures, so a leaked client secret cannot forge backend-approved code. A
  strict-monotonic sequence guard rejects replay/reorder. Outgoing NID-94 is
  signed on the live path; **incoming envelope verification activates once the
  Unity client emits envelopes** (the Rust verifier and its adversarial tests are
  already in place). The backend **refuses to start** if hardened is selected
  without its keys (fail-closed, no silent downgrade).

See [`HARDENING.md`](HARDENING.md) for the design and [`DEPLOYMENT.md`](DEPLOYMENT.md)
to run it. **TLS/WSS is still a separate transport step**; message auth already
gives end-to-end integrity through an untrusted relay.

## Privacy

Telemetry is JSONL carrying ids/timestamps/decisions/reason-codes/counts —
**never** audio or transcripts (a test asserts no `audio`/`transcript`/`secret`
field can appear). Personalization state is stored locally and is treated as
untrusted context in prompts (it may nudge aesthetics, never override safety).
Stored profiles are written **owner-only (`0600`)**, support **erasure** (trait
`delete` + admin route) and **TTL purge** of stale records, and — when
`DCVR_PROFILE_ENC_KEY` is set — are **encrypted at rest** with ChaCha20-Poly1305
AEAD (`ring`). With no key the on-disk format is unchanged (plaintext), so the
default behaviour is preserved.
