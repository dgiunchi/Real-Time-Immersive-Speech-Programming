# XR Security Evaluation — an automated static code-admission benchmark

An **automated, local, reproducible** evaluation of how well the DreamCodeVR+ safety
backend **statically admits or rejects** generated C# for a speech-to-code XR system,
across five classes of privacy and manipulation attack. It answers the core question
directly: **with no defence, how many malicious payloads would be admitted for execution,
and how many does our backend reject before execution?** No Meta Quest required — every
verdict is produced by the *real* validator (`dcvr_csharp_policy`) on the local machine.

This measures **static code-admission** (in the spirit of SecurityEval, Siddiq & Santos,
*MSR4P&S 2022*): whether the defence rejects the exploit *before* it is dispatched to the
runtime. It does **not** measure the physical effect on a headset — no code is executed.

This is **additive**: it reuses the existing validator, hardening profiles and attack
strings, and changes none of the 128-vector / red-team code.

## Run it

```bash
# 1. numbers (prints the headline table, writes results.json)
cargo run -p xr-security-eval

# 2. the visible demo (browse each payload, see admitted vs rejected, live)
cargo run -p xr-security-eval --bin xr-security-demo      # → http://127.0.0.1:7979

# 3. validator latency microbenchmark (run in release)
cargo run --release -p xr-security-eval --bin xr-latency

# 4. the paper figures (after step 1)
python3 apps/xr-security-eval/report/make_figures.py      # → figures/*.png

# regenerate the corpus if edited
python3 apps/xr-security-eval/attacks/gen_corpus.py
```

## Threat model — five attack classes

Grounded in the XR-security literature and realised as **hand-authored** *speak-to-C#*
payloads representative of code an LLM-enabled pipeline could emit (each a
`GeneratedBehaviour : MonoBehaviour`):

| Class | The harm | Basis |
|---|---|---|
| **Biometric** | reads the user's body — camera, microphone, eye-gaze, hand skeleton, haptics | Nair et al. 2023, *attribute inference* (arXiv:2305.19198) |
| **Positional / motion** | head/hand pose exfiltrated for re-identification / tracking (>94% re-id from motion) | Nair et al., *USENIX Security 2023* (re-identification, arXiv:2302.08927) |
| **Environment-imagery / outward-camera** | attempts to access outward-facing camera / passthrough / spatial-map APIs to capture the user's real room | outward-camera / passthrough API access (**synthetic imagery only — no real camera, passthrough or room data tested**) |
| **Human-joystick** | rotates/moves the world to physically herd the user into a real wall/door | Casey et al., *IEEE TDSC 2021* |
| **Chaperone / boundary** | moves or disables the guardian safety boundary → walks the user into hazards | Casey et al. 2021 |
| *Benign* | 12 legitimate creative commands (house, snowman, recolour, MR authoring) — must always pass |

40 attacks (8 per class) + 12 benign controls.

## Defence — three levels

The backend is evaluated at three settings so the *layering* is visible:

- **No defence** — the original DreamCodeVR path: unvalidated C# is sent straight to the
  runtime, so any generated code would be admitted.
- **Security-only** (`HardeningProfile::CreativeFreedom`) — the always-on system-access
  denylist (`System.IO/Net/Reflection`, `UnityWebRequest`, `Process`, …).
- **DeployHardened** (`HardeningProfile::DeployHardened`) — adds the perceptual / XR-device
  denylist (`WebCamTexture`, `OVRManager`, `OVRBoundary`, `InputTracking`, XR namespaces, …).

## Method

For every payload the harness calls `validate_csharp_freeform_profile(code, profile)` — the
same validator the live pipeline uses — and records whether the code is **rejected before
dispatch to the runtime**. "Malicious-payload pass" = the payload is *not* rejected (it
would be admitted for execution). This is a static code-admission evaluation; it measures
whether the defence rejects the exploit before it reaches the headset, entirely on the local
machine. The payloads are static strings and are never executed.

## Results

```
class                                   N   no-defence   security-only   DeployHardened
biometric                               8     100.0%         37.5%           0.0%
chaperone / boundary                    8     100.0%         87.5%           0.0%
human-joystick                          8     100.0%        100.0%          25.0%
positional / motion                     8     100.0%         62.5%           0.0%
environment-imagery / outward-camera    8     100.0%         25.0%           0.0%
OVERALL                                40     100.0%         62.5%           5.0%
```

- **No defence: 100.0% of malicious payloads would be admitted.**
- **DeployHardened: 95.0% rejected** (38/40 rejected before execution) on this curated
  40-case benchmark.
- **Benign: 100.0% still admitted** at every level (0/12 over-rejected) — creative freedom is
  preserved, including dual-use MR authoring (passthrough, spatial anchors).

### The headline finding — defences must be *layered*
A code-security filter alone (**security-only**) rejects privacy *exfiltration* (which uses
system/network APIs) but **misses pure XR manipulation**: the human-joystick class stays at
**100.0%** admitted and chaperone at **87.5%**, because those attacks touch no system API —
only the **perceptual/XR** layer rejects them. Different attack classes require different
defence layers; a single filter is insufficient for XR.

### Honest residual (2/40)
Two human-joystick payloads (`joy-05`, `joy-06`) rotate/translate `Camera.main.transform` —
lexically indistinguishable from legitimate content authoring, so static admission cannot
reject them without also rejecting creation. They would require the **runtime**
`UserFrameGuardian` (and/or the Mode-D sandbox), which is out of scope for this static,
headset-free evaluation and reported transparently rather than hidden.

## Limitations
- **Static code-admission, not on-device execution.** We measure whether the malicious code
  is admitted, not the physical effect on a headset (no Quest by design). The runtime-only
  residual above is the class this cannot cover statically.
- The corpus is **hand-authored** (grounded in the literature + real fixtures); it
  demonstrates the classes, not an exhaustive attack space. An independently authored holdout
  corpus is proposed as future work in `PAPER.md`.

## Files
- `attacks/gen_corpus.py`, `attacks/corpus.json` — the benchmark (deterministic generator).
- `src/lib.rs` — the reusable eval core (calls the real validator at 3 levels).
- `src/main.rs` — the CLI (table + `results.json`).
- `src/bin/latency.rs` — the validator latency microbenchmark.
- `src/bin/demo.rs` + `src/bin/demo.html` — the localhost visible demo.
- `report/make_figures.py` — the paper figures.
- `tests/eval_tests.rs` — locks the corpus shape and the layered-defence story.
