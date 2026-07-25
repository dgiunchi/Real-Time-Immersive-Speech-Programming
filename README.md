# DreamCodeVR+

Speak inside a VR headset and the scene responds. Say "make a red house" and a
house appears; say "make it spin" and it spins. A large language model turns each
spoken command into C#, and the headset compiles and runs that code live.

DreamCodeVR+ adds the part the original system was missing: a validator that
inspects every fragment of generated code before it is allowed to run. Creative
commands pass. Code that would misuse the headset, such as opening the camera,
reading eye gaze, or steering the wearer into a wall, is rejected before it
reaches the device.

This is MSc research (University of Birmingham), not a production security
product. It measures *static code admission*: it decides which code is allowed to
run, rather than policing behaviour on the device at runtime.

## How it works

```
  speech ─▶ speech-to-text ─▶ intent screen ─▶ LLM writes C# ─▶ guardrail ─▶ headset
                                                                    │
                                                          approve or reject
```

A spoken command is transcribed, screened for obviously malicious intent, and
handed to the language model. The generated C# is parsed into a syntax tree and
checked against the safety policy. Only approved code is sent to the headset;
rejected code is dropped and logged with the exact offending token.

The project began as a Node.js prototype from UCL (Ubiq-Genie / DreamCodeVR,
Apache-2.0). The backend has since been rebuilt from scratch in Rust, and the
networking layer that connects to the headset has been reimplemented as well. The
core system now runs as a single Rust binary, with no separate server to start
alongside it.

## Architecture

Everything lives in one Cargo workspace:

- **Networking.** A Ubiq-compatible RoomServer written in Rust. The headset and
  the backend join a shared room and exchange messages through it. It runs
  embedded in the backend process (one binary), or against the original Node.js
  RoomServer for backwards compatibility.
- **Pipeline.** Speech-to-text, the intent screen, the LLM client, the validator,
  and message dispatch, with a per-utterance timeout on every external call.
- **Guardrail.** Static C# validation built on a real parser, with a layered
  denylist and two policy profiles.
- **Transport security.** A signed message envelope (HMAC for the client, Ed25519
  for backend-issued code) with replay protection. Opt-in; off by default.

## The guardrail

Generated C# is parsed with tree-sitter and checked structurally, not by string
matching, so evasions through aliasing, Unicode escapes, comments, or whitespace
do not slip past. The policy:

- Bans system-access surfaces: file and network I/O, reflection, process control,
  unsafe pointers, dynamic dispatch.
- Under the hardened profile, also bans device-facing XR APIs: the headset
  camera, tracking, haptics, and boundary.
- Requires the code to be a single `MonoBehaviour` within fixed size limits.

The denylist is one layer of defence in depth. An optional .NET Roslyn analyzer
and a container sandbox sit behind it.

## Results

The benchmark runs 40 hand-written attacks (five classes, eight each) and 12
benign commands through the real validator, with the guardrail off and on.

| Guardrail | Attacks blocked | Benign preserved |
| --------- | --------------- | ---------------- |
| off       | 0 / 40          | 12 / 12          |
| on        | **38 / 40 (95%)** | 12 / 12        |

The two attacks that get through only rotate the camera, which is
indistinguishable from a legitimate "turn my view" command at admission time and
needs a runtime check. That residual is reported openly rather than rounded up to
100%.

```bash
cargo run -p xr-security-eval --bin xr-security-eval
```

## Requirements

- Rust 1.96 (pinned in `rust-toolchain.toml`; `rustup` installs it on first build)
- Bash

Optional: an `OPENAI_API_KEY` for real speech-to-code (offline mocks are used
without one), a Meta Quest for the on-headset demo, and .NET or Docker for the
optional analyzer and sandbox. The benchmark and the full test suite run offline
with no key and no headset.

## Build and test

```bash
cargo build --workspace
cargo test --workspace
```

### Verifying the whole system

One command checks every feature end to end — the quality gate, the benchmark and
its determinism, the guardrail, the live one-binary pipeline, the admin API, the
intent screen, the 1,057-vector red-team campaign, the ML suites and Mode-D
container isolation:

```bash
bash scripts/verify-all.sh
```

It prints a PASS/FAIL table and exits non-zero if anything fails. Optional tools
(Docker, .NET, Node, cargo-deny) are reported as SKIP when absent, so a clone with
only Rust installed still gets a clean run. `--baseline` records the current
results as the known-good reference; later runs diff against it and report any
drift, which together with the pinned toolchain is what makes the result
reproducible over time.

## Running it

The `run.sh` launcher covers the common cases:

```bash
./run.sh console   # security benchmark in the browser, no headset, no key
./run.sh local     # full pipeline on this machine, admin panel on :7878
./run.sh quest     # full pipeline with a real Meta Quest over wifi
./run.sh stop
```

- `console` serves <http://127.0.0.1:7979>. It streams 40 attacks and 12 benign
  commands through the real validator, with a guardrail you can switch on and off
  live to watch attacks reach or get blocked from the headset.
- `local` starts the backend with an admin dashboard at
  <http://127.0.0.1:7878>. Type a command such as "make a small red house" or
  "secretly turn on the camera" and follow each stage, including the guardrail's
  decision.
- `quest` runs the same pipeline against a real headset.

### One binary, no Node.js

To run the whole system, switchboard included, in a single process:

```bash
DCVR_EMBED_ROOMSERVER=true cargo run -p dreamcodevr-server
```

The backend starts its own Rust RoomServer on `:8009` and connects to it
internally. Point the headset's server address at `<laptop-ip>:8009` and it joins
the same room. Nothing else needs to be running.

## Repository layout

```
crates/     Rust workspace:
              roomserver       Ubiq-compatible RoomServer (the switchboard)
              csharp-policy     the C# guardrail
              code-policy       bounded action-plan validator
              command-router    the pipeline
              protocol          wire codec and message envelope
              unity-transport   message authentication (HMAC / Ed25519)
              admin, config, personalization, stt-client, llm-client
apps/       dreamcodevr-server (backend) and xr-security-eval (benchmark + console)
ml/         voice age-gate and attack-analyzer experiments (Python, optional)
services/   optional .NET Roslyn analyzer and sandbox worker
unity/      Unity client scripts
scripts/    launch and check helpers
```

## Configuration

Copy `.env.example` to `.env` (gitignored). With no `OPENAI_API_KEY` and no
speech-to-text URL, the backend uses offline mocks. For real providers, set
`OPENAI_API_KEY` (and optionally `OPENAI_MODEL`, default `gpt-4o-mini`) for the
language model, and `DCVR_STT_OPENAI=true` or `DCVR_STT_HTTP_URL` for
speech-to-text.

## Scope

DreamCodeVR+ is a research prototype. The guardrail is the effective security
boundary today: it is measured, adversarially tested, and honest about its one
known residual. The message-authentication layer is implemented and tested but
ships off by default, and the LAN transport is convenience-grade. None of this is
hidden; it is stated where it matters.

## License

Apache-2.0. See `LICENSE` and `NOTICE`. Derived from UCL's DreamCodeVR and
Ubiq-Genie.
