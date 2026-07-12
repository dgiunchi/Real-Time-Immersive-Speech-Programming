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

- **Important default:** if the Roslyn service is not wired, a mock analyzer
  **approves**, so the Rust lexical layer is the effective gate. Enable and
  require the real analyzer for a stronger Mode-B posture.
- The lexical guard is a **denylist**: thorough and adversarially tested, but
  completeness is not guaranteed.

## Mode A — original runtime-C# (OFF by default)

`DCVR_MODE_A=false` by default. When enabled, validator-approved C# is sent
(NID 94) to the client for runtime compilation. This widens the trust surface:
the Unity handler runs whatever code arrives on NID 94, and (in this release)
peers are not authenticated — so with Mode A on and no peer auth, a malicious
room member could get code compiled on a client. Keep Mode A for research/demos
on trusted networks only.

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
never bind to `0.0.0.0` without a token. The `/api/sandbox` route **validates**
C# only (it does not execute code on the host). Token comparison is a plain
string equality (not constant-time).

## Peer authentication (present, not wired by default)

An HMAC admission-token module exists in `crates/unity-transport` but is **not
invoked** in this release; the Ubiq channel is plaintext and peers self-assert
identity. Wiring per-peer auth + TLS/WSS is required before any untrusted-network
deployment.

## Privacy

Telemetry is JSONL carrying ids/timestamps/decisions/reason-codes/counts —
**never** audio or transcripts (a test asserts no `audio`/`transcript`/`secret`
field can appear). Personalization state is stored locally and is treated as
untrusted context in prompts (it may nudge aesthetics, never override safety).
