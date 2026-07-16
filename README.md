# DreamCodeVR+

**Safe-by-construction speech programming for immersive VR.** DreamCodeVR+ lets a
person in VR **speak** and change the scene — *"make this cube red," "build a
small house," "spin it"* — while a Rust safety backend validates everything the
AI produces before it can touch the scene.

> **Status: research / dissertation prototype.** It is not production-hardened
> and is not a production security boundary. See [`SECURITY.md`](SECURITY.md) and
> [`docs/LIMITATIONS.md`](docs/LIMITATIONS.md).

DreamCodeVR+ extends UCL's **DreamCodeVR** by **replacing its Node.js
"Ubiq-Genie" backend with a new Rust workspace** and adding validation,
observability, an admin panel, personalization, and a red-team harness. The
original UCL/Ubiq/Unity components are **not** authored here — see
[`NOTICE`](NOTICE).

## How it works

```
VR speech ─► STT ─► LLM ─► SAFETY VALIDATION ─► safe action  OR  validated C#
 (Ubiq)     (Whisper/  (mock or   (fail-closed)   (Mode C, no    (Mode A/B,
            mock)       OpenAI)                    compilation)   gated)
```

The backend joins the same Ubiq room as the Unity/Quest client, receives
push-to-talk audio, transcribes it, asks an LLM for a plan or C#, **validates**
it, and dispatches a safe result back to Unity.

### Execution modes

| Mode | What it does | Default | Safety |
|---|---|---|---|
| **C** | Bounded 6-action plan, executed in Unity **without compiling code** | on | Unsafe ops are unrepresentable (fail-closed validator) |
| **B** | Validated generated C# (lexical + optional .NET Roslyn) | opt-in | Denylist + semantic check; defence-in-depth |
| **A** | Original runtime-C# compile path, now **validator-gated** | **off** | Widens trust surface; research/demo only |
| **D** | Hardened Docker/gVisor sandbox for untrusted C# | opt-in | Containment; gVisor optional |

> **Mode B note:** if the .NET Roslyn analyzer is not configured, the mock analyzer
> **approves** (fail-open) and the Rust lexical denylist is the effective gate. Wire the
> real analyzer for semantic enforcement — see [`docs/SECURITY_MODEL.md`](docs/SECURITY_MODEL.md).

### Known limitations (details in [`docs/LIMITATIONS.md`](docs/LIMITATIONS.md))

- **Peer authentication is profile-gated.** The default `legacy` profile is
  byte-identical to the original (peers self-assert; plaintext channel). An opt-in
  **`hardened`** profile adds cryptographic peer auth (HMAC admission +
  Ed25519-signed backend output + replay guard); outgoing NID-94 signing is on the
  live path, incoming verification activates once Unity emits envelopes, and
  TLS/WSS remains a deployment step. See [`docs/HARDENING.md`](docs/HARDENING.md).
- **Quest 3 / Store (IL2CPP, ARM64) deployment is future work.** Mode A is
  demonstrated on sideloaded Quest 1/2 (Mono); **Mode C is the deployable path.**
- **Mode B semantic enforcement is off by default** (see the note above).
- Not production-hardened — a research / dissertation prototype (see [`SECURITY.md`](SECURITY.md)).

## Quick start (offline, no credentials)

With no API key and no STT URL set, DreamCodeVR+ uses **mock** STT/LLM clients,
so the whole pipeline runs locally and deterministically.

```bash
# 0. check prerequisites (installs nothing)
bash scripts/doctor.sh

# 1. build + test the Rust workspace (reproducible from Cargo.lock)
cargo build --workspace --locked
cargo test --workspace --locked     # 254 tests, fully offline

# 2. run the backend (offline mocks by default)
cargo run -p dreamcodevr-server

# 3. drive it with the test client (built-in demo scenario, no Unity needed)
cargo run -p fake-quest-client            # connects to 127.0.0.1:9098
```

To use a real LLM/STT, copy `.env.example` to `.env` and set `OPENAI_API_KEY`
(and optionally `DCVR_STT_OPENAI=true`). **Never commit `.env`.**

For the complete step-by-step setup — all modes, real providers, and network
configuration — see **[`docs/BUILD_AND_RUN.md`](docs/BUILD_AND_RUN.md)**. For the full
VR loop see also [`docs/REPRODUCIBILITY.md`](docs/REPRODUCIBILITY.md) and
[`docs/UNITY_INTEGRATION.md`](docs/UNITY_INTEGRATION.md).

## Prerequisites

| Tool | Version used | Needed for |
|---|---|---|
| Rust | 1.96 (edition 2021) | the backend + tests |
| .NET SDK | 10.0 | optional Mode-B Roslyn analyzer, Mode-D harness |
| Node.js | for the Ubiq RoomServer (fetched separately) | the live VR loop |
| Docker (+ optional gVisor `runsc`) | — | Mode-D sandbox |
| Unity | 6000.5.x | the VR client |

The core backend + tests need **only Rust**. Everything else is optional.

## Repository layout

```
crates/         15 Rust libraries (protocol, transport, router, validators, …)
apps/           4 binaries (dreamcodevr-server, fake-quest-client, ubiq-probe, sandbox-runner)
tests/          workspace integration tests
services/       .NET Roslyn analyzer (Mode B) + sandbox worker (Mode D)
scripts/        run / build / red-team / network helpers
redteam/        reproducible adversarial corpus generator + runner (Python)
unity/          authored Unity C# drop-ins (Runtime + Editor)
unity-examples/ example Unity project scripts (Mode C / networked)
docs/           architecture, security model, protocol, reproducibility, limitations
```

## Documentation

- [docs/BUILD_AND_RUN.md](docs/BUILD_AND_RUN.md) — **canonical build & run guide (start here)**
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — components, crates, and data flow
- [docs/SECURITY_MODEL.md](docs/SECURITY_MODEL.md) — trust boundaries, the four modes, known risks
- [docs/PROTOCOL.md](docs/PROTOCOL.md) — Ubiq NetworkIds and message formats
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) — local build / test / lint workflow
- [docs/REPRODUCIBILITY.md](docs/REPRODUCIBILITY.md) — offline smoke test, live loop, red-team regeneration
- [docs/UNITY_INTEGRATION.md](docs/UNITY_INTEGRATION.md) — authored Unity drop-ins (no proprietary asset)
- [docs/LIMITATIONS.md](docs/LIMITATIONS.md) — honest scope and unfinished areas
- [docs/HARDENING.md](docs/HARDENING.md) — the opt-in **hardened** security profile (auth, replay, fail-closed)
- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) — running the hardened profile (`keygen`, env vars, Unity provisioning)
- [SECURITY.md](SECURITY.md) — security policy & vulnerability reporting · [CHANGELOG.md](CHANGELOG.md) — release notes
- Component: [services/roslyn-analyzer/README.md](services/roslyn-analyzer/README.md) — Mode-B .NET analyzer setup & `/analyze` API

## Licence & attribution

Apache-2.0 (see [`LICENSE`](LICENSE)). DreamCodeVR+ is a **derivative** of UCL's
Apache-2.0 DreamCodeVR/Ubiq-Genie; attribution and the statement of changes are
in [`NOTICE`](NOTICE). The proprietary RoslynCSharp Unity asset is **not**
included. Public redistribution may require permission from UCL and the
University of Birmingham — see [`docs/LIMITATIONS.md`](docs/LIMITATIONS.md).
