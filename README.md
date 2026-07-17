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

## Why this exists — the whole system in one arc

This section frames the entire project as one safety story: the gap, why it
matters, what we actually built, and an honest reckoning of whether it is enough.
Everything below it (build, run, layout, licence) is unchanged.

### The problem

An LLM writing runnable C# straight into a live VR scene is unsafe in two
different ways at once, and the second way is usually ignored.

1. **Code safety.** In Mode A/B the model's output is compiled and executed on
   the device. A single generated line can reach `Reflection`, `Process.Start`,
   `DllImport`, `unsafe`, or file/network IO — arbitrary code execution inside
   the headset process. A plain text screen is not enough on its own:
   guardrail-bypass research shows character-injection attacks passing
   commercial prompt filters at high rates, so the gate has to be structural.
2. **Perceptual / embodied harm.** Even when every op is "valid code," the scene
   it builds can hurt the person wearing the headset — forced locomotion (the
   "human joystick"), boundary/chaperone edits, disorientation and vection,
   occluding overlays, strobing above photosensitive-seizure thresholds. And the
   harm is **compositional**: each op can sit inside its per-action bound while
   the *sequence* composes into harm (herding, progressive occlusion,
   strobe-by-accumulation). Per-action limits provably miss this.
3. **Child safety.** None of the above is age-aware. A scene that is merely
   intense for an adult can be genuinely unsafe for a child, and nothing in the
   base pipeline knows who is speaking.

### Why we deal with it

XR is embodied: the output is not text on a screen, it is motion and light
applied to a person's vestibular system and eyes, so a bad frame is a real-world
physical event rather than a log line — and children are simultaneously the most
at-risk population and the most regulated one. The coupling of code-safety and
perceptual-safety under a *live* speech→LLM→compile loop is genuinely
unaddressed in the 2022–2026 literature (nobody couples an age posterior to a
real-time safety policy, and the two safety literatures are disjoint), which is
also where the novelty sits; meanwhile regulation is closing in — COPPA's final
amended rule (16 CFR 312, 2025, which adds biometric identifiers, compliance
deadline Apr 2026), the UK AADC / ICO Children's Code, EU AI Act Art. 5 (in
force Feb 2025), GDPR Art. 8 child consent, and the age-assurance standards
ISO/IEC 27566-1:2025 and IEEE 2089.1-2024 (five assurance levels) — so getting
this right is at once a safety obligation and the central research contribution.

### What we built

**Fail-closed Rust validator + four execution modes.**
- **Mode C (default):** a bounded 6-action plan, executed in Unity with **no code
  compiled** — unsafe ops are literally unrepresentable.
- **Mode B (opt-in):** validated generated C# (lexical denylist + optional .NET
  Roslyn semantic check).
- **Mode A (off by default):** the original runtime-C# compile path, now
  validator-gated; research/demo only.
- **Mode D (opt-in):** a hardened Docker/gVisor sandbox for untrusted C#.
- Prior adversarial hardening of the C# validator moved its block rate from
  **15% → 100% with 0 bypass** [reported].

**Age-adaptive dual-plane coupling (the novel core)** — `crates/config/src/age.rs`:
- `AgeBand{Child, Teen, Adult, Unknown}`, default `Unknown`; `Unknown` **fails
  safe to the child profile** (test `unknown_fails_safe_to_child`).
- Child/Unknown **tighten BOTH planes vs Adult** (test
  `child_tightens_both_planes_vs_adult`): `perceptual_hardening` on (vs off),
  `require_compile_confirmation` on (vs off), `max_spawn` 20 (vs 40), `flash_hz`
  2.0 (vs 3.0), `fov_coverage` 0.35 (vs 0.70), `rotate_deg_s` 30 (vs 90),
  `luminance_delta` 0.4 (vs 1.0).
- A single detected **minor alone flips the C# validator to the hardened
  `DeployHardened` gate** (test `age_minor_forces_hardened_csharp_gate`). This is
  opt-in via `DCVR_AGE_GATING`; with it **off, behaviour is byte-identical to
  legacy**.
- This is the deliberate "inversion": the same voice inference the XR-privacy
  literature demonstrates as a *surveillance* attack is re-used — on-device,
  ephemeral, unlinked — as a governed *protective* control.

**Two ML models (numpy-only, CPU, reproducible and auditable by design).**
- `ml/age_gate` — a voice age gate emitting a **coarse BAND only** (child <13 /
  teen 13–17 / adult 18+), never a precise age and never emotion: DSP features +
  logistic regression + temperature calibration (T ≈ 1.93), with a session-level
  fail-safe.
- `ml/attack_analyzer` — an Auto-RT-style **continuous** analyzer: a PCA/SVD
  undercomplete autoencoder for open-set anomaly detection, an ADWIN2 drift
  detector, and a red-team loop that grows the discovered-vector store and
  red-teams our own age gate.

**Verified numbers (re-run live 2026-07-17):** 263 Rust tests pass, 0 fail.
`ml/age_gate` 17/17 OK; `ml/attack_analyzer` 21/21 OK. Anomaly detector
(PCA/SVD undercomplete autoencoder): pooled **AUC 1.00**, benign FPR ~2% at the
99th-percentile threshold, per-family detection 1.0. Drift detector (ADWIN2):
0 false alarms on a stationary stream; injected shift detected at t=304.
Age-gate calibration: validation **ECE ~0.05 → ~0.007**. Red-team spoof-rate
against our **own** age gate: **100%** (a child sample pushed across the adult
boundary). Attack corpus: a curated baseline of **128 vectors**, with the
discovered-vector store (`attacks_discovered.jsonl`) growing to **164 entries**.

### Is it enough? — honest evaluation

**Proven (measured, re-run 2026-07-17).**
- 263 Rust tests / 0 fail. The age coupling is exercised end-to-end by
  `unknown_fails_safe_to_child`, `child_tightens_both_planes_vs_adult`, and
  `age_minor_forces_hardened_csharp_gate`; with `DCVR_AGE_GATING` off the build
  is byte-identical to legacy.
- ML suites green: `ml/age_gate` 17/17, `ml/attack_analyzer` 21/21.
- Anomaly detector: AUC 1.00, benign FPR ~2% @ 99th-percentile, per-family
  detection 1.0. Drift: 0 false alarms on a stationary stream, shift caught at
  t=304. Calibration: ECE ~0.05 → ~0.007.
- Prior C# validator hardening: 15% → 100% block, 0 bypass [reported].

**Limitations (stated candidly — this is a dissertation, honesty scores).**
- The **100% spoof-rate against our own age gate is a finding, not a win**: our
  voice model is trivially defeated by pushing a child sample across the adult
  boundary. This is exactly why the design treats the **Meta Quest age-group API
  (Apr 2024) as the authoritative age source and our ML age as a secondary safety
  net — never the legal basis.** The FTC (Mar 2024) explicitly **denied facial
  age estimation as a COPPA consent mechanism**, so inference can gate the
  *experience* but can never replace verifiable parental consent.
- The ML models are **numpy-only by design** (no torch/sklearn) — reproducible,
  auditable, CPU-only — which also means the age model is a small DSP +
  logistic-regression head, **not** the wav2vec2-class model the literature
  reports; the documented wav2vec2 upgrade is future work.
- The age model emits a **coarse band only, deliberately**: EU AI Act Art. 5
  prohibits emotion recognition in many contexts, so we exclude affect entirely
  and output child/teen/adult — never a precise age, never emotion.
- **Literature anchors are NOT our measured results** (labelled as such):
  child-vs-adult voice ~97.14% age-group accuracy on CMU Kids; ~20 ms on-device
  edge-model latency (reported elsewhere, **not yet run on a Quest**); motion-age
  ~78% per-user (Nair, arXiv:2305.19198); and the standing caution that Nair
  (USENIX Security 2023) **re-identifies 94.33% of 55,000+ users from head+hand
  motion** — i.e. motion age inference is *not* privacy-neutral, which is why the
  whole design is on-device and ephemeral.
- **Router-lock reality:** the `Router` struct has **no global lock** (per-peer
  sessions), but the server holds `Arc<Mutex<Router>>` across the STT/LLM/validate
  awaits in `spawn_utterance`, so peers currently serialise. The DoS is already
  bounded (per-step timeouts + an overall `with_deadline`). The full Phase-4
  per-peer-lock refactor (so peers stop blocking each other) is written and
  testable but **pending live multi-peer sign-off**.

**Pending (on-device).**
- On-device end-to-end — **real microphone audio on a real Quest** — is
  **PENDING (≥ 2026-07-23)**. Everything above is measured on desktop/CPU with
  recorded or synthetic inputs. No claim in this README depends on a Quest run;
  when a headset is available, the same policy decisions get logged (decisions
  only, never raw biometrics).

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
cargo test --workspace --locked     # 263 tests, fully offline

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
