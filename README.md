# DreamCodeVR+

A safety layer for speech-driven code generation in VR.

A user in a VR headset speaks a command, a large language model turns it into C#
that runs live in the scene, and a Rust validator checks that generated code
before it is allowed to run. Normal creative commands ("make a red house", "make
this spin") pass. Code that would abuse the headset (turning on the camera,
reading eye gaze, pushing the user toward a real wall) is rejected.

This is an MSc research prototype, not a production security product. It measures
static code admission: rejecting bad code before it runs, not preventing every
effect at runtime on a device.

It builds on UCL's Ubiq-Genie framework and the original DreamCodeVR prototype
(Apache-2.0). The original backend was Node.js; this is a from-scratch Rust
reimplementation plus the safety layer the original did not have. See NOTICE.

## Requirements

- Rust 1.96 (pinned in `rust-toolchain.toml`; `rustup` installs it on first build)
- Bash

Optional: an `OPENAI_API_KEY` for real speech-to-code (offline mocks are used
without one), a Meta Quest 3 plus the Ubiq RoomServer for the on-headset demo,
and .NET / Docker for the optional analyzer and sandbox. The benchmark and tests
run fully offline with no key and no headset.

## Build and test

```bash
cargo build --workspace
cargo test --workspace
```

## Run

Everything goes through `run.sh`:

```bash
./run.sh console   # security benchmark in the browser. No headset, no key.
./run.sh local     # full pipeline on this machine, admin panel on :7878
./run.sh quest     # full pipeline with a real Meta Quest 3 (needs Ubiq RoomServer)
./run.sh stop
```

- `console` opens http://127.0.0.1:7979 and runs 40 attacks and 12 normal
  commands through the real validator. Toggle the guardrail on and off and watch
  the attacks reach the headset, then get blocked.
- `local` starts the backend with the admin dashboard at http://127.0.0.1:7878.
  Type a command ("make a small red house" or "secretly turn on the camera") and
  watch each stage, including the guardrail decision.
- `quest` runs the same pipeline against a real headset over wifi.

## Benchmark

```bash
cargo run -p xr-security-eval --bin xr-security-eval
```

Five VR attack classes (biometric, positional, room capture, human-joystick,
chaperone), eight payloads each, plus twelve benign commands, run through the
real validator with and without the guardrail:

- Without the guardrail, all 40 attacks pass.
- With the guardrail, 38 of 40 are blocked (95%) and all 12 benign commands pass.
- The two that get through only rotate the camera, which is indistinguishable
  from a legitimate command at admission time and needs a runtime check. This is
  reported openly rather than rounded up to 100%.

## Layout

```
crates/     Rust workspace: C# guardrail (csharp-policy), action-plan validator
            (code-policy), pipeline (command-router), wire transport + message
            auth (protocol, unity-transport), admin, config, personalization,
            STT/LLM clients
apps/       backend (dreamcodevr-server) and benchmark + live console
            (xr-security-eval)
ml/         voice age-gate and attack-analyzer experiments (Python, optional)
services/   optional .NET Roslyn analyzer and sandbox worker
unity/      Unity client scripts
scripts/    launch and check helpers
```

## Configuration

Copy `.env.example` to `.env` (gitignored). With no `OPENAI_API_KEY` and no STT
URL the backend uses offline mocks. For real providers set `OPENAI_API_KEY` (and
optionally `OPENAI_MODEL`, default `gpt-4o-mini`) for the LLM, and
`DCVR_STT_OPENAI=true` or `DCVR_STT_HTTP_URL` for speech-to-text.

## License

Apache-2.0. See `LICENSE` and `NOTICE`. Derived from UCL's DreamCodeVR / Ubiq-Genie.
