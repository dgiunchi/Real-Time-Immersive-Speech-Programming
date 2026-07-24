# DreamCodeVR+ — Project Guide (the one doc that has everything)

This is the single source of truth for the project: what it is, how it grew, how to
run it, how the security works, the benchmark numbers, the 128-vector threat model,
and what the audit found and fixed. If you only read one file, read this one.

- **Viva / examiner questions, vector by vector:** see [`VIVA_QA.md`](VIVA_QA.md).
- **The formal paper:** see [`apps/xr-security-eval/PAPER.md`](apps/xr-security-eval/PAPER.md).
- **Quick, friendly start:** see [`README.md`](README.md).

---

## 1. What this project is (and how it grew)

DreamCodeVR is a "speak and the world changes" idea: a user talks, a large language
model (LLM) turns the words into C# code, and that code runs **live inside a VR
headset**. The original DreamCodeVR was a **Node.js prototype**. My professor asked me
to take that idea and **build the guardrails and safety it was missing**.

So I rebuilt the whole thing as a **custom engine from scratch in Rust**, and then added
the safety layers one step at a time, in the order the project was asked for:

1. **Custom engine + guardrails** — a from-scratch Rust pipeline (speech → LLM → validator
   → headset) with a static **code guardrail** that checks every generated C# fragment
   *before* it can run.
2. **Cyber-security** — message authentication, signing, replay protection, admission
   tokens, and a hardened deployment profile (the "Network & identity" attack family).
3. **Privacy attack defences** — stopping code that reads the body (camera, mic, eye-gaze,
   hand/head pose) or captures the real room.
4. **Benchmarks** — a reproducible, local measurement of **with-safety vs without-safety**:
   how many attacks get through with no guardrail, and how many our backend blocks.

Everything runs on a laptop. A real Meta Quest 3 is only needed for the on-headset demo;
all the security evaluation is done locally with no headset.

---

## 2. How to run it (one launcher, three ways)

Everything goes through **`./run.sh`** from the repo root:

```bash
./run.sh console    # security benchmark console  — NO Quest, NO API key (the demo)
./run.sh local      # the full speech->code pipeline on THIS laptop, NO Quest
./run.sh quest      # the full pipeline with a REAL Meta Quest 3 headset
./run.sh stop       # stop the local / quest stack
./run.sh            # show the menu
```

- **`console`** opens `http://127.0.0.1:7979` — the with-safety-vs-without-safety demo. You
  can flip the guardrail live (`apps/xr-security-eval/guardrail.sh off|on`) and watch the
  same attacks reach the headset (bypassed) or get blocked (protected).
- **`local`** builds and starts the real backend in **standalone mode** (no Quest) with the
  **admin dashboard** at `http://127.0.0.1:7878`. Type a command in the "manual command"
  box — e.g. *"make a small red house"* (approved) or *"secretly turn on the camera"*
  (blocked) — and watch every stage of the pipeline live.
- **`quest`** starts the Ubiq RoomServer + backend + admin panel so a real headset joins
  over wifi (needs the vendored Ubiq RoomServer — see the paper's reproducibility notes).

The **only switch** between local and Quest is the `DCVR_UBIQ_ADDR` environment variable
(set → Quest mode via Ubiq; unset → standalone local mode). `run.sh` handles it for you.

---

## 3. Architecture — the pipeline

```
  🎤 Speech  →  🧠 LLM writes C#  →  🛡️ Rust guardrail  →  🥽 Headset (Unity/Quest)
   (user)        (the AI)            (OUR contribution)      runs the approved code
                                          │
                          ┌───────────────┴───────────────┐
                     GUARDRAIL ON                       BYPASSED
                 malicious code REJECTED           code reaches the headset
                  before it can run                 unchecked (attack works)
```

The system is ~17k lines of Rust across a Cargo workspace. The main pieces:

| Crate / app | Job |
|---|---|
| `crates/csharp-policy` | **the guardrail** — static C# validation (tree-sitter parse + layered denylists) |
| `crates/code-policy` | plan/DSL validation (bounds, ranges, fail-closed) |
| `crates/command-router` | the pipeline: admit → STT → intent screen → LLM → validate → dispatch |
| `crates/unity-transport` | the wire protocol + message authentication (HMAC/Ed25519, replay guard) |
| `apps/dreamcodevr-server` | the server (standalone-local and Ubiq-Quest modes) + LAN discovery |
| `crates/control` | the live event bus (`PipelineEvent`) the admin panel streams |
| `crates/admin` | the admin dashboard (live pipeline, config, safety log, tools) |
| `crates/config` | settings + the hardened deployment profile invariants |
| `crates/sandbox`, `crates/roslyn-client` | optional deeper runtime checks (Roslyn analyzer + sandbox) |
| `crates/personalization`, `crates/stt-client`, `crates/llm-client` | RAG memory, speech-to-text, LLM |
| `apps/xr-security-eval` | **the benchmark + live console** (with/without safety, guardrail hot-swap) |

---

## 4. The guardrail — how the C# is checked

Every generated C# fragment goes through `crates/csharp-policy` before it can run. It:

1. **Parses** the C# with a real tree-sitter grammar (not a regex), and **reconstructs the
   full dotted API names** from the syntax tree — so tricks like whitespace, comments,
   `@`-verbatim identifiers, `\uXXXX` unicode escapes, and `using X = System;` aliases
   cannot hide a banned call.
2. **Matches** the reconstructed names against **two layered denylists**:
   - **System-security layer** — `System.IO`, `System.Net`, `UnityWebRequest`, `Process`,
     reflection, `unsafe`/pointers, `dynamic`. Stops code that sends data out or touches
     the machine.
   - **Perceptual / XR layer** — the actual banned identifiers are `WebCamTexture`,
     `InputTracking`, `XRSettings`/`XRDevice`, `XROrigin`, `TrackedPoseDriver`,
     `OVRManager`/`OVRCameraRig`/`OVRPlugin`/`OVRBoundary`/`OVRInput`,
     `InputDevices`/`XRInputSubsystem`, and `OVRHaptics`/`SendHapticImpulse`/`Vibrate`,
     plus the XR namespaces (`UnityEngine.XR`, `Unity.XR`, `UnityEngine.InputSystem.XR`).
     Stops code that reaches the headset's rig / tracking / haptics / camera. **Note:**
     `Microphone` is **not** banned anywhere, and eye-gaze (`OVREyeGaze`) and the other
     dual-use MR primitives (`OVRPassthroughLayer`, `OVRSpatialAnchor`, `OVRSceneManager`,
     `OVRHand`, `OVRSkeleton`, `OVRFaceExpressions`) are **deliberately allowed** as
     content-authoring APIs — see the `NOTE` in `crates/csharp-policy/src/lexical.rs`.
     Covert capture/manipulation via them is a runtime/disclosure concern (Phase-6
     monitors), not a lexical one; exfiltration through them is already caught by the
     System-security layer.
3. **Checks the shape** — it must be a single `MonoBehaviour` class within size limits.
4. If anything matches → **Reject before dispatch**, with the exact offending token
   recorded so the reason is specific. Otherwise → admit.

Two profiles: **CreativeFreedom** (system-security layer only) and **DeployHardened**
(adds the perceptual/XR layer). Median cost: **84.9 µs** per command (release build) —
effectively free next to LLM generation and Unity compilation (seconds).

---

## 5. Security & privacy — the five XR attack classes, before vs after

The benchmark (`apps/xr-security-eval`) runs 40 hand-authored malicious payloads (8 per
class) + 12 benign creative commands through the **real** validator, before (no guardrail)
and after (DeployHardened). Reproduce with `cargo run -p xr-security-eval` (writes
`results.json`).

| Attack class | Attacks | Bypassed (no guardrail) | **Guardrail ON** |
|---|---|---|---|
| Biometric (camera, mic, eye-gaze, hands) | 8 | 0 blocked | **8/8 blocked** |
| Positional / motion (pose → re-identification) | 8 | 0 blocked | **8/8 blocked** |
| Image of surroundings (outward camera / passthrough)\* | 8 | 0 blocked | **8/8 blocked** |
| Human-joystick (herd the user into a wall) | 8 | 0 blocked | **6/8 blocked** |
| Chaperone / boundary (disable the safety wall) | 8 | 0 blocked | **8/8 blocked** |
| **Overall (40 malicious)** | **40** | **0/40 blocked** | **38/40 = 95%** |
| Benign creative (must pass) | 12 | allowed | **12/12 allowed (0 wrongly blocked)** |

- **Without the guardrail, all 40 attacks would run. With it, 38/40 are rejected before
  execution (95%)**, and every legitimate creative command still works.
- **The headline finding:** a normal code-security filter alone blocks the exfiltration
  attacks but **misses pure XR manipulation** (human-joystick 0/8, chaperone 1/8 with the
  system layer only) — those touch no system API. Only the **perceptual/XR layer** catches
  them. **Defence for speech-driven XR must be layered.**
- **The honest 2/40** (`joy-05`, `joy-06`): they just rotate the camera — indistinguishable
  from a legitimate "turn the view" command — so static checking can't block them without
  breaking creation. Flagged as a **runtime-guardian** case (future work). That is why it is
  95%, not a suspicious 100%.
- **What "biometric 8/8" actually means (honest reading):** biometric 8/8 is achieved
  because those payloads use a banned API (`WebCamTexture`) or exfiltrate via
  `System.Net`/`UnityWebRequest` — the guardrail catches the camera/egress, not a bare,
  local-only `Microphone` or eye-gaze **read**, which is a runtime/disclosure concern. So
  the row is true for the corpus, but it does **not** mean the biometric sensor surface is
  lexically closed.

\* *No real camera, passthrough feed, or physical room was captured — class 3 is tested as
attempted API access with synthetic payloads. This is a static code-admission study, not
on-device runtime testing.*

---

## 6. The 128-vector threat model (be precise about this)

Do **not** say "we neutralised 128 attacks" — that would over-claim. The accurate story is
still strong:

The **128 vectors are the complete threat *model*** for the whole system, across **five
families**:

| Family | Vectors | Covers |
|---|---|---|
| Network & identity | 24 | impersonation, replay, forgery, unauthorised room join, DoS |
| **Generated code** | 33 | the C# the LLM emits — what the guardrail + benchmark cover |
| Immersive / VR | 34 | human-joystick, chaperone, perceptual dark-patterns, disguise |
| Voice & sensors | 13 | audio spoofing, sensor abuse |
| Privacy | 24 | biometric / positional / room inference, profile leakage |

**Honest status tally (of 128):** **33 Solved (built & tested) · 2 Partial · 80 Designed
(planned, mostly on-device / deployment) · 13 N/A.** Every vector `A001–A128` has a
one-line attack + our exact answer + status in [`VIVA_QA.md`](VIVA_QA.md).

The 40-payload benchmark **realises the code-facing slice** of that model (the Generated-code
family plus the code-expressible parts of Privacy and Immersive) as measurable C#. It is
**not** a literal 1-to-1 subset of the 128 list — it's an independent, hand-authored
benchmark that measures the guardrail on those attack classes, where it neutralises **95%**.

---

## 7. The live console and the admin panel

- **Benchmark console** (`./run.sh console`, `apps/xr-security-eval`) — streams all 52
  payloads through the real validator live, with a **guardrail you flip on/off** and a
  paste-your-own-C# lane. This is the safest demo (offline, deterministic, always works).
- **Admin dashboard** (`./run.sh local`/`quest`, `crates/admin`) — the live pipeline for the
  real system. It shows every stage (you said → AI wrote code → guardrail verdict → Unity),
  and for a blocked command it now shows **exactly which API was caught, what that code was
  trying to do, and which guardrail feature caught it** — e.g. *"⛔ WebCamTexture — tried to
  open the headset camera to record you — caught by the Perceptual/XR guardrail"* — not just
  "blocked".

---

## 8. Audit & hardening (what the multi-agent audit found and fixed)

A full multi-agent audit (7 auditors + adversarial verification) was run over the whole
codebase. The confirmed, security-relevant issues were fixed and locked with tests:

- **Guardrail bypass via `unsafe` blocks (HIGH) — fixed.** The `unsafe` ban only matched the
  `unsafe` *modifier*, so an `unsafe { int* p = &x; *p = 2; }` *statement* block with pointer
  memory access slipped through. Now `unsafe_statement` and `pointer_type` are banned too.
- **Alias-chain bypass (medium) — fixed.** Alias resolution was capped at 8 hops, so a
  ≥9-deep `using` chain reached `System.IO`/`Net` unflagged. Now it resolves to a fixpoint
  with a cycle guard.
- **MonoBehaviour substring hole (medium) — fixed.** The "is a MonoBehaviour" check was a raw
  substring test, so `: FakeMonoBehaviourX` or a `/* MonoBehaviour */` comment passed. Now it
  matches a base-type identifier segment exactly.
- **Admin panel detail (fixed):** the "caught malicious intent" event now carries a full
  explanation, and the panel renders per-violation plain-English detail (above).
- **Per-peer memory-growth DoS (medium) — fixed.** The anti-replay map was written before
  auth verification; now it persists only for verified peers.
- **Loopback-URL userinfo trick (medium) — fixed.** `http://localhost:x@evil.com` was treated
  as loopback by the hardened-profile check; now userinfo is rejected.
- **Cleanups:** removed a dead `class_count` field, fixed a garbled docstring, and made the
  inert "Safety guardrails" admin toggle honest (the live pipeline is always guarded).

---

## 9. Limitations & remaining work (stated honestly)

- **Static code-admission, not on-device runtime.** We measure whether malicious code is
  *admitted*, not its physical effect on a headset. Admission never establishes runtime
  safety.
- **The 2/40 residual** (`joy-05`, `joy-06`) needs a **runtime UserFrameGuardian** to bound
  cumulative viewpoint/locomotion changes at execution time.
- **Hand-authored corpus.** The benchmark demonstrates the five classes, not an exhaustive
  attack space; an independently authored holdout corpus is the key next evaluation step.
- **Known items from the audit not yet fixed** (documented, lower priority): the non-Mode-A
  `process_audio` path emits no admin events (the demo uses Mode A, which does); admin **read**
  routes are not token-gated (safe on the default loopback bind, a concern only if exposed on
  `0.0.0.0`); the personalization RAG context interpolates a past command into the prompt
  (prompt-injection surface); the Mode-D sandbox has a stdin-write timeout gap; the personalization
  **TTL purge is coded but unwired** (`purge_expired` has no runtime caller) and RAG retention is
  **opt-out** (`enable_rag` defaults true). These are on the hardening backlog.
- **Hardened analyzer requirement gates on Mode A only.** `enforce_profile_invariants` requires a
  real Roslyn analyzer only when `mode_a` (`crates/config/src/settings.rs:411`); a `hardened`
  deployment running Mode-B-only C# (`DCVR_CSHARP_RESEARCH=true`, `mode_a=false`, no `roslyn_url`)
  silently falls back to the approve-all mock. **Impact is bounded** — only Mode A emits runnable
  code to the device (NID-94), so Mode-B-only never reaches the headset and the in-process lexical
  guardrail stays the effective gate — but `hardened` should require the analyzer for Mode B too.
- **LLM default & the control-bus `model` field.** The deployed generation model defaults to
  **`gpt-4o-mini`** (`crates/config/src/settings.rs:177`), and the server seeds the live-tunable
  `RuntimeConfig.model` (`crates/control/src/lib.rs:101`, literal default `"gpt-5.5"`) **from**
  that setting — so the admin/control-bus `model` field is **decorative for generation**: editing
  it does not change the model actually used (only `reasoning_effort` / `verbosity` /
  `max_completion_tokens` are pushed live). Set the model via `OPENAI_MODEL`, not the panel.

---

## 10. Where things are

```
run.sh                          one launcher (console / local / quest)
apps/xr-security-eval/          the benchmark + live console (present.sh, guardrail.sh)
apps/dreamcodevr-server/        the server (local + Quest modes)
crates/csharp-policy/           the guardrail (the security core)
crates/command-router/          the pipeline
crates/admin/                   the admin dashboard (ui.html)
crates/unity-transport/         wire protocol + message auth
VIVA_QA.md                      the 128-vector Q&A (viva prep)
apps/xr-security-eval/PAPER.md  the formal paper
scripts/                        lower-level launch helpers (wrapped by run.sh)
```

---

## 11. Security model (Modes A–D)

DreamCodeVR+ treats **everything** as untrusted — spoken/typed commands, STT output, LLM output, network messages, admin requests, stored personalization profiles, and generated C#. Safety is **defence-in-depth**, not a single gate. (The guardrail internals are in §4; this section is the mode-by-mode architecture and the cyber-security layer.)

**Layer 1 — intent screen (before generation).** A keyword classifier (`command-router`) plus an optional LLM classifier inspect the raw command and **neutralise** malicious/privacy-violating intent (camera, mic, exfiltration, keylogging, cyber-attack code) by replacing it with a harmless visual *before any code is generated*. Benign creative/edgy content is allowed. The keyword layer is offline; the LLM layer needs an API key.

- **Mode C — bounded action plan (default, safest).** The LLM emits a small JSON action plan whose only legal instructions are six allow-listed behaviours (`set_color`, `set_scale`, `move`, `rotate`, `spawn_primitive`, `set_physics`) with hard numeric bounds. `code-policy` approves **iff zero violations**; oversized input is rejected *before* parsing. Unsafe operations are **unrepresentable**, there is **no runtime code compilation**, and a per-session spawn budget caps object creation.
- **Mode B — validated C#.** `csharp-policy` runs a tree-sitter lexical scan that reconstructs dotted names and bans system-access namespaces/identifiers, hardened against three red-team evasion classes (`using` aliases, Unicode-escape identifiers, `dynamic` late-binding). An optional .NET Roslyn semantic check (`services/roslyn-analyzer`) adds a second layer. **Default (`legacy`): if Roslyn is not wired, a mock analyzer approves**, so the Rust lexical layer is the effective gate. The lexical guard is a denylist — thorough and adversarially tested, but not provably complete.
- **Mode A — original runtime-C# (OFF by default, `DCVR_MODE_A=false`).** When enabled, validator-approved C# is sent on NID 94 to the client for runtime compilation. This widens the trust surface. In `legacy`, peers are unauthenticated, so a malicious room member could get code compiled on a client — keep it to trusted-network research/demos. `hardened` closes this by Ed25519-signing NID-94.
- **Mode D — sandbox for untrusted C# (`crates/sandbox`).** Runs untrusted C# in a container hardened with `--network none`, read-only rootfs + tmpfs, `--cap-drop ALL`, `no-new-privileges`, non-root, memory/CPU/PID limits and `nofile`/`nproc` ulimits; only a structured `SandboxReport` crosses back, bounded by a wall-clock timeout + process-group kill. **gVisor (`runsc`) is opt-in** (`DCVR_SANDBOX_DOCKER_RUNTIME=runsc`); the default `runc` is a comparatively soft boundary per NIST SP 800-190. Mode D is a research arm, **not** on the live speech path.

**Cyber-security layer — two profiles (`DCVR_SECURITY_PROFILE`).**

- **`legacy` (default)** — byte-identical to the original build: peers self-assert identity, the Ubiq channel is plaintext, new controls are off. This is what the current Quest demo runs.
- **`hardened` (opt-in)** — a versioned, canonical **`AuthEnvelope`** binds every message to protocol version, security profile (downgrade detection), message domain (`NetworkId.b`), a per-session monotonic sequence, an expiry, session/peer/request/target ids, and a **SHA-256 payload hash**. Two deliberately different crypto directions (audited `ring 0.17`): **client→backend HMAC-SHA256** admission and **backend→Unity Ed25519** signatures — so a leaked client secret cannot forge backend-approved code. A strict-monotonic sequence guard rejects replay/reorder; verification is constant-time. Outgoing NID-94 is signed on the live path; incoming verification is wired into `run_ubiq_peer` and **activates once the Unity client emits envelopes**. Hardened Mode A/B additionally **requires a real Roslyn analyzer** (no approve-all mock) with a per-request timeout. The backend **refuses to start** if `hardened` is selected without its keys (fail-closed, no silent downgrade); the seam is `apps/dreamcodevr-server/auth_gate.rs`.
- **`test`** — deterministic CI: loopback, mock STT/LLM, deterministic local keys.

**Admin / debug panel.** Binds to **loopback by default**. Mutating routes honour an optional `X-Admin-Token`; if no token is set they are unauthenticated, so the panel **refuses to bind to a non-loopback address without a token** (fail-closed, all profiles). Token comparison is **constant-time**. `/api/sandbox` validates C# only (never executes on the host); an authenticated `POST /api/profile/delete` erases a stored profile.

**Liveness & input bounds (live path).** The per-utterance pipeline holds a shared router lock across its `.await`s, so one hung external call could stall every peer. Every external await is therefore bounded: STT and LLM have per-step timeouts; the Layer-1 `screen_intent` classifier and both RAG embedding calls are wrapped too (they **fail open** — a timeout maps to the "proceed / no context" path). Optional `DCVR_UTTERANCE_TIMEOUT_MS` adds a **fail-closed** per-utterance deadline (default off). Under `hardened`, NID-98 audio is validated against `AudioBounds` (size / 16 kHz-mono-16-bit / ≤30 s) before a paid/slow STT backend. A per-peer in-flight cap (`DCVR_MAX_INFLIGHT_PER_PEER`) bounds a task-flood DoS.

**Privacy.** Telemetry is JSONL carrying ids / timestamps / decisions / reason-codes / counts — **never** audio or transcripts (a test asserts no `audio`/`transcript`/`secret` field can appear). Personalization state is stored locally and treated as untrusted prompt context (it may nudge aesthetics, never override safety). Stored profiles are written **owner-only (`0600`)**, support **erasure** and **TTL purge**, and — when `DCVR_PROFILE_ENC_KEY` is set — are **encrypted at rest** with ChaCha20-Poly1305. With no key the on-disk format is unchanged (plaintext).

---

## 12. Setup & build

The offline Rust path needs **only Rust**; everything else is optional. `./run.sh` (§2) is the friendly wrapper.

| Tool | Purpose | Required? |
|---|---|---|
| Rust + Cargo (+ rustfmt, clippy) | build + test the backend (1.96.0, pinned by `rust-toolchain.toml`) | **Required** |
| Bash | helper scripts | **Required** |
| cargo-deny | supply-chain gate | Optional |
| .NET SDK 10 | Mode-B analyzer, Mode-D harness | Optional |
| Python 3 | red-team tooling (stdlib only) | Optional |
| Docker (+ gVisor `runsc`) | Mode-D sandbox | Optional |
| Node.js ≥ 18 | run the fetched Ubiq RoomServer | Optional (live VR) |
| Unity 6000.5.x | the VR client | Optional (live VR) |

Install Rust via `rustup`; the repo's `rust-toolchain.toml` selects 1.96.0. Run **`bash scripts/doctor.sh`** to check prerequisites (it fails only if a required tool is missing). `cp .env.example .env` (gitignored — never commit); with no `OPENAI_API_KEY` and no STT URL the backend uses **offline mocks**.

**The validation gate** (identical to CI; `scripts/check.sh` runs the same):

```bash
cargo fmt --all -- --check
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace --locked
cargo build --workspace --release --locked
cargo deny check            # optional; needs cargo-deny
```

The workspace is **`unsafe`-free** (`unsafe_code = forbid`) and panic-averse (`unwrap`/`expect`/`panic` denied in library crates). Everything runs fully offline (mock STT/LLM/Roslyn, no key, no network).

**Offline mock demo** (no key, no Unity, no network — two terminals):

```bash
cargo run -p dreamcodevr-server      # backend, mock STT/LLM, binds 127.0.0.1:9098
cargo run -p fake-quest-client       # built-in demo -> validated action-plan decision
```

**Fetching the Ubiq RoomServer (not vendored).** The live VR loop needs the Ubiq RoomServer, which is **not included** (third-party UCL/Ubiq code). Obtain a compatible RoomServer from the Ubiq project and run it on TCP `:8009`; `scripts/run-roomserver.sh` expects it under `vendor/ubiq-roomserver/`, and the backend joins it via `DCVR_UBIQ_ADDR` (set → Ubiq/Quest mode; unset → standalone local). The proprietary **RoslynCSharp** Unity asset (for on-device Mode A) is likewise **not included** — obtain it separately, or use Mode C (no runtime compilation).

**Real providers (optional).** LLM: `OPENAI_API_KEY` (+ optional `OPENAI_MODEL`, `OPENAI_BASE_URL`, `DCVR_LLM_TIMEOUT_MS`). STT: `DCVR_STT_OPENAI=true` (OpenAI Whisper) or `DCVR_STT_HTTP_URL` (faster-whisper). The test suite **never** calls a paid API.

---

## 13. Deployment & transport security

**Running the hardened profile.** Generate secrets with `cargo run --bin keygen` (prints an admission secret, a backend Ed25519 signing seed, a profile-encryption key, and the backend Ed25519 **public** key). Keep the private values in a secret manager (never commit; never send to the admin panel); only the public key goes to Unity. Then:

```bash
export DCVR_SECURITY_PROFILE=hardened
export DCVR_PEER_AUTH_SECRET=...          # from keygen
export DCVR_BACKEND_SIGNING_SEED=...      # from keygen (32-byte Ed25519 seed)
export DCVR_PROFILE_ENC_KEY=...           # profiles encrypted at rest
export DCVR_MODE_A=true
export DCVR_ROSLYN_URL=http://127.0.0.1:5099   # a REAL analyzer (no mock in hardened)
export DCVR_ADMIN_TOKEN=...               # if the admin panel is enabled
```

If a required control is missing, the backend **refuses to start** — it never silently downgrades. On the Unity side, provision `BACKEND_ED25519_PUBLIC_KEY` to the `BackendVerifier`, wire an `IEd25519Verifier`, and set `RequireSignature = true` so unsigned NID-94 is rejected.

**Transport confidentiality (TLS/WSS).** In `hardened`, message auth already gives integrity, authenticity, and anti-replay through an untrusted relay; the one thing TLS adds is **confidentiality** (stopping an eavesdropper reading transcripts/code in transit) — so TLS is a **deployment step, not a code change**. The Ubiq RoomServer is untrusted-by-design; its encrypted channel is **WSS on `:8010`**. Recommended: a **TLS-terminating proxy** in front of the relay (`stunnel`, `nginx stream {}`, `caddy` layer4, or Ubiq's native WSS with a real cert), and **pin the server certificate / CA** on the connecting side. Point `DCVR_UBIQ_ADDR` at the local TLS proxy and the Quest client at the client-side proxy / WSS URL. TLS protects the wire, not a compromised endpoint.

**On-device compile-time hardening (RoslynCSharp, Mode A).** Configure the proprietary RoslynCSharp asset in the Unity project so the runtime compiler itself refuses the dangerous surface: enable the security check **fail-closed** (reject on any violation, not warn-only; do not exempt hot-reload), **restrict referenced assemblies** to the minimum (exclude `System.Net*`, `System.Diagnostics`, unneeded `System.IO*`/`System.Reflection*`, and Meta XR/OVRPlugin), and mirror the Rust `DeployHardened` denylist for namespace/type restrictions. Dual-use APIs that are lexically indistinguishable from legitimate creation (`Camera.main.transform`, `OnRenderImage`, `GameObject.Find`, Quest-3 MR APIs) are **deliberately not banned lexically** — they are runtime-enforced by `UserFrameGuardian` instead. Verify on-device that a benign build compiles and a `System.Net` probe is refused by both layers.

---

## 14. Wire protocol

DreamCodeVR+ speaks the **Ubiq** wire protocol so it can join the same room as the unmodified Unity client. Framing/codec live in `crates/protocol` (no I/O, `unsafe`-free, golden-byte tested). A Ubiq frame length counts **`NetworkId` (8 bytes) + payload**; the `Join` handshake's `args` field must be **stringified JSON**; each application payload is `{ peer_uuid, body }`.

| NID | Direction | Purpose |
|---|---|---|
| **93** | client → backend | selected object id (per-peer target) |
| **98** | client → backend | push-to-talk audio (16 kHz mono PCM) + `__STT_CONTROL__:start/stop` |
| **94** | backend → client | backend decision — action plan or `{type:"code",…}` (Mode A/B); Ed25519-signed in `hardened` |
| **95** | client → backend | like / dislike feedback (personalization) |
| **96** | client → backend | runtime compile result (surfaced to the admin panel) |
| **97** | client → backend | authored (default-off) Phase-6 disclosure safety-log channel |

Peers **self-assert** `peer_uuid` (no enforced peer auth in `legacy`); inbound audio is size-bounded; malformed frames are dropped. NID-94 code is applied without a peer check in `legacy`, so Mode A is trusted-network / `hardened` only. **Internal interfaces:** Rust → Roslyn analyzer over HTTP `POST DCVR_ROSLYN_URL/analyze`; Rust → sandbox streams code to a .NET harness (only a `SandboxReport` returns); the admin panel (axum) exposes SSE + JSON routes on loopback with the optional `X-Admin-Token`.

---

## 15. Security policy & honest scope

DreamCodeVR+ is a **research / dissertation prototype — NOT a production security boundary**. Do not expose it on an untrusted network without the `hardened` profile. (The runtime-vs-admission caveats, the 2/40 residual, and the audit backlog are in **§9**; this adds the boundary status and the publication gate.)

**Boundary vs not.** Peer auth is **off in `legacy`** (self-asserted, plaintext, no TLS); `hardened` enforces it. Admin mutating routes are **unauthenticated unless a token is set** (loopback default; refuses off-loopback bind without a token). Mode B's semantic layer is **mock-by-default** unless Roslyn is wired, leaving the lexical denylist as the effective gate. Mode A widens the trust surface and is **off by default**. Mode D uses `runc` by default (soft per NIST SP 800-190); gVisor is opt-in.

**Verified (host-side).** The Rust workspace builds, lints (`clippy -D warnings`), and tests fully offline with mocks; `cargo deny check` is clean. The safe Mode-C path and the validator-gated Mode-A path were demonstrated in the Unity 6 editor and on a Quest 1/2 Mono sideload; Mode D under Docker and gVisor. Hardened adds (Rust-verified): message auth, fail-closed startup + analyzer + timeout, privacy-at-rest, an adversarial campaign at **0% bypass / 0% false-positive**, and deterministic **fuzz corpora** proving both wire parsers never panic over ~95k malformed inputs.

**Not yet covered.** No **on-device (Quest 3) end-to-end run** (all current evidence is host-side automated tests). **Incoming envelope verification is not yet active on the wire** (the Unity client must emit envelopes first; only outgoing NID-94 signing is live). **No TLS/WSS confidentiality** (a deployment step, §13). In-process C# limits inside Mode A/B are *contained, not prevented* — OS-level containment (Mode D) is future work. Quest 3 / Store builds (IL2CPP/ARM64) can't compile C# at runtime, so Mode A is limited to sideloaded Quest 1/2 Mono; Mode C is the deployable-by-construction path.

**Ownership & publication (needs human confirmation before any public release).** DreamCodeVR+ is a derivative of UCL's **Apache-2.0 DreamCodeVR / Ubiq-Genie**, and is **MSc dissertation work (University of Birmingham)**. Before releasing, confirm: Apache-2.0 terms satisfied (licence + NOTICE + statement of changes); university/supervisor consent (no embargo); no employer/client IP; the proprietary **RoslynCSharp** asset excluded (it is); author copyright line completed in `NOTICE`/`CITATION.cff`. Report issues confidentially to the author (`CITATION.cff`) — no SLA. No credentials are committed; the OpenAI key is read from `.env` (never commit it).

---

## 16. Changelog highlights

Versions are informal (dissertation prototype).

- **0.1.0 — initial snapshot.** A **Rust workspace** replacing the original Node.js Ubiq-Genie backend, joining the same Ubiq room; the fail-closed action-plan validator + six-action IR (**Mode C**); lexical (tree-sitter) + optional Roslyn C# validation (**Mode B**) hardened against alias/unicode/`dynamic` evasion; the two-layer intent screen; the Docker/gVisor sandbox (**Mode D**); Mode A retained validator-gated and off by default; privacy-safe telemetry, the admin panel, personalization/RAG, the red-team harness. Mocks by default (fully offline). **164 tests.**
- **Unreleased — opt-in `hardened` profile** (this branch; `legacy` stays byte-identical to 0.1.0; host-side verified, **on-device pending hardware**). Security profiles with fail-closed invariants; message authentication (HMAC + Ed25519, `ring 0.17`); fail-closed Mode A/B; privacy at rest (erasure, `0600`, TTL, optional ChaCha20-Poly1305); admin hardening (constant-time token, off-loopback refusal); live-path liveness bounds; hardened STT `AudioBounds`; the perceptual denylist extension; Mode-D ulimits + watchdog; Unity Phase 6/7 (default-off); a `keygen` utility. Adversarial campaign (0% bypass), fuzz corpora (~95k inputs), and (after the audit pass in §8) the guardrail bypass fixes + admin-panel detail.
```
