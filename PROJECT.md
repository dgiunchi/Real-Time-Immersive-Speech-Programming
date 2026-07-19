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
   - **Perceptual / XR layer** — `WebCamTexture`, `Microphone`, `InputTracking`, eye-gaze,
     `OVRManager`, `OVRBoundary`, XR namespaces. Stops code that reads the body or moves
     the user.
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
  (prompt-injection surface); the Mode-D sandbox has a stdin-write timeout gap. These are on
  the hardening backlog.

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
