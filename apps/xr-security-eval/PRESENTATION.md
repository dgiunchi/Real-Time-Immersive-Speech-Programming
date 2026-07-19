# DreamCodeVR+ — Presentation Pack (one folder, live-swappable guardrail)

Everything you need to demo and defend the project lives in **this one folder**
(`apps/xr-security-eval/`). It runs locally, needs no Meta Quest, and lets you
**flip the guardrail on/off with one command** while presenting — so you can show
the attack *working* (guardrail bypassed) and then *blocked* (guardrail on), on
the same screen, in seconds.

---

## 1. The system in one picture

```
  🎤 Speech  →  🧠 LLM writes C#  →  🛡️ Rust guardrail  →  🥽 Headset
   (user)        (the AI)            (OUR contribution)     (Unity/Quest)
                                          │
                          ┌───────────────┴───────────────┐
                     GUARDRAIL ON                     BYPASSED
                 malicious code REJECTED         code reaches the headset
                  before it can run                 unchecked (the attack works)
```

A user speaks; a large language model turns the words into C# that is compiled
and **run live inside the headset**. That is powerful — and dangerous: if the AI
is tricked, the code it emits can read the user's body, capture their room, or
physically move them. **Our contribution is the Rust guardrail** that validates
every generated fragment *before* it can run. The whole system is ~17k lines of
Rust; this folder is the ~2k-line evaluation + live demo built on the **same real
validator** the pipeline uses.

**Your professor's point** — *"you can bypass that thing to make it work"* — is
exactly the demo: bypass the guardrail and the attack reaches the headset; turn
it on and the same attack is blocked. That switch is now one command.

---

## 2. Run it (two terminals)

**Terminal 1 — start the console:**
```bash
apps/xr-security-eval/present.sh
```
Open **http://127.0.0.1:7979**. It starts **protected** (guardrail on).

**Terminal 2 — flip the guardrail live (the "command change"):**
```bash
apps/xr-security-eval/guardrail.sh off     # BYPASS  → attacks reach the headset
apps/xr-security-eval/guardrail.sh on      # PROTECT → attacks blocked
apps/xr-security-eval/guardrail.sh security # security-only (misses XR manipulation)
```
The page's big banner flips **red ⚠ BYPASSED / green 🛡 GUARDRAIL ON** within a
second — no restart. You can also click the toggle in the UI; it drives the same
server state. (Or start already bypassed: `DCVR_GUARDRAIL=off apps/xr-security-eval/present.sh`.)

Press **▶ RUN LIVE CODE-ADMISSION SWEEP** and the 52 payloads stream through the
real validator live: in **BYPASS** every attack shows **→ HEADSET** (red); in
**GUARDRAIL ON**, 38/40 show **BLOCKED** (green).

---

## 3. The evaluation — five XR attack classes, before vs after

Everything below is produced by the **real validator** and reproduced by
`cargo run -p xr-security-eval` (writes `results.json`).

| Attack class | Attacks | Bypassed (no guardrail) | **Guardrail ON** |
|---|---|---|---|
| Biometric (camera, mic, eye-gaze, hands) | 8 | 0 blocked → all reach headset | **8/8 blocked** |
| Positional / motion (pose → re-identification) | 8 | 0 blocked | **8/8 blocked** |
| Image of surroundings (outward camera / passthrough)\* | 8 | 0 blocked | **8/8 blocked** |
| Human-joystick (herd the user into a wall) | 8 | 0 blocked | **6/8 blocked** |
| Chaperone / boundary (disable the safety wall) | 8 | 0 blocked | **8/8 blocked** |
| **Overall (40 malicious)** | **40** | **0/40 blocked** | **38/40 = 95%** |
| Benign creative commands (must pass) | 12 | allowed | **12/12 allowed (0 wrongly blocked)** |

- **Bypassed: all 40 attacks would run.** **Guardrail on: 38/40 rejected before execution (95%)**, and every legitimate creative command still works.
- **The finding (your contribution):** a normal code-security filter alone (the
  "Security-only" mode) blocks the exfiltration attacks but **misses pure XR
  manipulation** (human-joystick 0/8, chaperone 1/8) — those touch no system API.
  Only the added **perceptual/XR layer** catches them. **Defence for XR must be layered.**
- **The honest 2/40** (`joy-05`, `joy-06`): they just rotate the camera —
  indistinguishable from a legitimate "turn the view" command — so static checking
  can't block them without breaking creation. Flagged as a **runtime-guardian
  case** (future work). That's why it's 95%, not a suspicious 100%.

\* *No real camera/passthrough/room was captured — class 3 is tested as attempted
API access with synthetic payloads. Stated honestly in the paper.*

**How the guardrail works:** it parses the generated C# (tree-sitter) and
reconstructs the full dotted API names (so obfuscation can't slip past), then
matches them against two layered denylists — a **system-security** layer
(`System.Net`, `System.IO`, `UnityWebRequest`, `Process`, reflection) and a
**perceptual/XR** layer (`WebCamTexture`, `InputTracking`, `OVRManager`,
`OVRBoundary`, XR namespaces). Any hit → **reject before dispatch**. Median cost
**84.9 µs** per command (release build) — effectively free.

---

## 4. The 128 attack vectors — what they are, honestly

Do **not** say "we neutralised 128 attacks" — that would be overclaiming and an
examiner will catch it. Here is the accurate story (it's still strong):

The **128 vectors are the complete threat *model*** we mapped for the whole
system, across **five families**:

| Family | Vectors | What it covers |
|---|---|---|
| Network & identity | 24 | impersonation, replay, message forgery, unauthorised room join, DoS |
| **Generated code** | 33 | **the C# the LLM emits — this is what the guardrail + this demo cover** |
| Immersive / VR | 34 | human-joystick, chaperone, perceptual dark-patterns, disguise |
| Voice & sensors | 13 | audio spoofing, sensor abuse |
| Privacy | 24 | biometric/positional/room inference, profile leakage |

**Honest status tally (of 128):** **33 Solved (built & tested) · 2 Partial · 80
Designed (planned, mostly on-device/deployment) · 13 N/A.** Each vector `A001–A128`
has a one-line attack + our exact answer + status in **`VIVA_QA.md`** (repo root).

**How the live benchmark relates to the 128:** the 40-payload benchmark in this
folder *realises the code-facing slice* of that model — the Generated-code family
plus the code-expressible parts of the Privacy and Immersive families — as
concrete, measurable C#. It is **not** a literal 1-to-1 subset of the 128 list;
it's an independent, hand-authored benchmark that measures the guardrail on those
attack *classes*. On it, the guardrail neutralises **95% (38/40)**.

So the accurate sentence is:
> "We mapped a **128-vector threat model** across five families; the ones that are
> built-and-tested today (33) include the code guardrail this demo measures, where
> we neutralise **95%** of a 40-payload benchmark. The remaining vectors are mostly
> on-device/deployment work, documented and designed as future work."

---

## 5. Suggested demo sequence (~4 minutes)

1. Open the console (guardrail **ON**). Point at the green banner and the pipeline:
   *speech → LLM → guardrail → headset.*
2. **Run the sweep** → 38/40 blocked, 12/12 benign allowed. "Our backend stops the attacks and keeps creation working."
3. In terminal 2: `guardrail.sh off`. The banner flips **red — BYPASSED**.
4. **Run the sweep again** → every attack now shows **→ HEADSET**. "This is the professor's point: bypass the guardrail and every attack reaches the headset."
5. `guardrail.sh security` → show human-joystick/chaperone still get through → "a normal security filter isn't enough for XR; you need the perceptual layer." Then `guardrail.sh on`.
6. Paste a custom C# in the left lane (or `joy-05`) to show the honest 2/40 runtime residual.

---

## 6. Likely questions → answers

- *"Did you test on a real headset?"* → "No — it's a local static-admission study, deterministic and reproducible. I measure whether malicious code is rejected *before* it reaches the headset. On-device runtime testing is my stated future work."
- *"So you neutralised 128 attacks?"* → "The 128 is the full threat model across five families. 33 are built-and-tested today, including the code guardrail I'm demoing, which neutralises 95% of a 40-payload benchmark. The rest are designed/on-device future work — I document each one's status in VIVA_QA.md."
- *"Why 95%, not 100%?"* → "Two human-joystick cases just rotate the camera — identical to a legitimate command — so static analysis can't block them without breaking creation. That boundary is exactly why a runtime guardian is needed; I report it, not hide it."
- *"How do I know it's real?"* → "Every verdict comes from the same validator the live pipeline uses. `cargo run` reproduces the table, and you can flip the guardrail live and paste your own C#."

---

## 7. What's in this folder

| Path | What |
|---|---|
| `present.sh` | launch the console (builds release, starts the server) |
| `guardrail.sh` | hot-swap the guardrail live: `on` / `off` / `security` |
| `src/server.rs` | the run-based API + SSE + the live-swappable `/api/mode` |
| `src/live.rs` | the code-admission verdict logic (reuses the real validator) |
| `src/bin/demo.html` | the console UI (banner, pipeline, live sweep, custom lanes) |
| `src/main.rs` / `attacks/` | the batch CLI + the 40-attack + 12-benign corpus |
| `PAPER.md` / `EVALUATION.md` | the write-ups; `results.json` the numbers |
| `tests/` | 24 automated tests (verdict correctness, streaming, hot-swap, security) |

## 8. Pushing it

It's a normal crate in the workspace (branch `hardening/mode-a-security`), all
committed. To push to your GitLab remote when ready:
```bash
git push -u origin hardening/mode-a-security
```
(Nothing is pushed automatically — that's your call.)
