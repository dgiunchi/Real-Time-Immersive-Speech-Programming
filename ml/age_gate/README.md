# Voice Age Gate (ML #1) — DreamCodeVR+

## The problem

A VR headset is usually a **shared, family** device. The platform stores one age
flag per account, set once at sign-up — but that flag cannot tell you **who is
actually wearing the headset right now**. A parent signs in; their 8-year-old
puts the headset on and starts a live voice-coding session. From that moment
every safety limit the system applies is sized for the wrong person. The obvious
"fix" — measure the exact age of whoever is speaking — is worse than the disease:
precise-age or identity-grade voice biometrics are exactly the surveillance-grade
inference the XR-privacy literature warns about, and regulators treat them as a
hazard, not a solution. So the gap is narrow and specific: we need a **runtime**
signal of *"is a child present in this session"* that is strong enough to tighten
safety, yet deliberately too coarse to become an identity or age-estimation
product.

## Why we deal with it

It matters because in XR the harm is **embodied**, not abstract: a flashing,
rotating, or large-FOV effect — or an unvetted spawned object — lands on a real
child's vestibular and visual system, and per-action code checks alone cannot
bound *compositional* perceptual harm. It matters legally: COPPA's final amended
rule (16 CFR 312, 2025) now covers biometric identifiers with an Apr-2026
deadline; the FTC (Mar 2024) explicitly **denied** facial age estimation as a
COPPA consent mechanism; the EU AI Act (Art. 5, in force Feb 2025) prohibits
emotion recognition in many contexts; and the UK AADC/ICO Children's Code plus
GDPR Art. 8 impose child-specific duties — so any age signal must be a *safety
net*, never a legal basis, and must avoid affect inference entirely (which is why
we output an age **band only**, never emotion). And it matters as research:
re-using a surveillance-grade capability — on-device, template-free, and governed —
as a purely protective control is the novel "surveillance→protection inversion"
at the heart of this dissertation.

## What we built

A small, auditable, CPU-only voice age gate plus the coupling that makes it
actually change behaviour:

- **numpy-only DSP features** (`features.py`): pitch/F0 via autocorrelation,
  spectral centroid + 85 % rolloff via FFT, zero-crossing rate, short-time
  energy, and pitch statistics over frames → a 15-D float32 vector. No torch, no
  sklearn — reproducible and readable by design.
- **Logistic regression in numpy** (`model.py`) with **temperature-scaling
  calibration** (shipped `T ≈ 1.93`), so `decision.py`'s confidence-based
  fail-safe is fed honest probabilities rather than an over-confident model.
- **A fail-safe `AgeGate`** (`decision.py`) that aggregates P(child) across a
  session (running median), maps to coarse bands **child / teen / adult /
  unknown**, **never emits a precise age**, and **defaults `unknown` → child**
  (strictest profile) while raising `escalate` so the platform can fall back to
  its authoritative age flag / parental consent.
- **The Rust coupling** (`age.rs`): `AgeBand{Child, Teen, Adult, Unknown}` with
  `default = Unknown`. Proven by tests: `unknown_fails_safe_to_child`, and
  `child_tightens_both_planes_vs_adult`, where Child/Unknown tighten **both**
  planes vs Adult — `perceptual_hardening` true-vs-false,
  `require_compile_confirmation` true-vs-false, `max_spawn` 20-vs-40, `flash_hz`
  2.0-vs-3.0, `fov_coverage` 0.35-vs-0.70, `rotate_deg_s` 30-vs-90,
  `luminance_delta` 0.4-vs-1.0.
- **Router coupling** (`age_minor_forces_hardened_csharp_gate`): a detected minor
  **alone** flips the C# validator to hardened `DeployHardened` — the age signal
  hardens code safety, not just perceptual limits. It is strictly **opt-in**:
  with `DCVR_AGE_GATING` off, behaviour is **byte-identical** to legacy.

Verified test counts (all re-run live 2026-07-17): **`ml/age_gate` 17/17 OK**;
the surrounding Rust workspace **263 tests pass, 0 fail**; the companion
`ml/attack_analyzer` **21/21 OK**.

## Is it enough? — honest evaluation

**What is PROVEN (reproducible figures):**

- **Calibration works.** Via the live `evaluate.py`, validation ECE improves from
  **≈ 0.05 → ≈ 0.007** (`T ≈ 1.93`). (The prose below still says ≈ 0.01 and the
  plan doc says 0.012; the reproducible live figure is **≈ 0.007** — that is the
  one to trust.)
- **The fail-safe holds.** `unknown` maps to the child profile and Child/Unknown
  tighten both planes, each pinned by a named test (see above). `17/17`
  age-gate tests and `263/263` Rust tests are green.
- **The coupling is real and opt-in.** A minor alone hardens the C# validator;
  `DCVR_AGE_GATING` off is byte-identical to legacy.
- **Ecosystem evidence** (companion analyzer, re-run): anomaly detector
  (PCA/SVD undercomplete autoencoder) pooled **AUC 1.00**, benign **FPR ~2 %**
  at the 99th-percentile threshold, per-family detection **1.0**; drift detector
  (ADWIN2) **0 false alarms** on a stationary stream, injected shift detected at
  **t = 304**; the curated **128**-vector baseline feeds a discovered-vector
  store (`attacks_discovered.jsonl`) that **grows to 164 entries**. For context,
  the prior C# validator hardening moved block-rate **15 % → 100 %, 0 bypass**.

**What is a LIMITATION (stated plainly):**

- **The acoustic gate is spoofable.** Our own red-team pushes a child sample
  across the adult boundary with a **100 % spoof-rate** against this gate. That
  is *why* the design fails safe, escalates, and is never the legal basis — the
  Meta Quest age-group API (Apr 2024) remains the authoritative source; our ML
  age is a secondary safety net only.
- **The data is synthetic and numpy-only.** No real corpus, no torch/sklearn —
  chosen for auditability and CPU-only reproducibility, but it means the numbers
  above are on modelled, not in-the-wild, audio.
- **The strong external accuracy numbers are LITERATURE ANCHORS, not our
  results.** ~97.14 % child-vs-adult age-group (CMU Kids), ~20 ms edge-model
  latency, ~78 % per-user motion-age (Nair, arXiv:2305.19198), and 94.33 %
  re-identification of 55,000+ users from head+hand motion (Nair, USENIX
  Security 2023) are cited to motivate the work — **we have not measured any of
  them here.**

**What is PENDING (on-device):**

- **Real mic audio on a real Quest is not yet run** (scheduled **≥ 2026-07-23**).
  The ~20 ms latency figure in particular has **not** been measured on-device.
- The **wav2vec2 upgrade path** (`wav2vec2_features.py`) is written but not run in
  this environment.
- The full Phase-4 per-peer-lock refactor is written and testable but pending
  live multi-peer sign-off. (Today the `Router` struct has no global lock — it is
  per-peer sessions — but the server holds `Arc<Mutex<Router>>` across the
  STT/LLM/validate awaits in `spawn_utterance`, so peers serialise; the DoS is
  already bounded by per-step timeouts plus an overall `with_deadline`.)

**Bottom line:** as a *governed secondary safety net* the gate is enough — it is
calibrated, it fails safe for the child, it hardens both planes, and it is
opt-in and byte-identical when off. As a *standalone age verifier* it is not, and
is not meant to be: it is spoofable, trained on synthetic data, and its headline
external accuracy is borrowed from the literature until on-device evaluation on a
real Quest closes the loop.

---

On-device, privacy-preserving **child (<13) / adult** decision from voice. This is
the age signal that tightens *both* safety planes of DreamCodeVR+ (the C# code
validator and the perceptual-safety layer). It is built to be **tiny** — a small
classifier over acoustic features — not a deep net at runtime.

> Label convention everywhere: **`y == 1` → CHILD**, **`y == 0` → ADULT**.
> Probability `p` = **P(speaker is a child, <13)**.

## What runs **now** (numpy + stdlib only)

The whole pipeline runs in this repo's numpy-only environment on **synthetic**
data:

```
16 kHz int16 PCM ──▶ features.py ──▶ model.py ──▶ decision.py
   (DSP)              (15-D vector)    (calibrated    (session fail-safe
                                        P(child))       band + confidence)
```

| File | Role |
|------|------|
| `features.py` | **Real acoustic DSP** (numpy only): pitch/F0 via autocorrelation, spectral centroid + 85 % rolloff via FFT, zero-crossing rate, short-time energy, and pitch statistics over frames. `extract_features(pcm, sr=16000) → 15-D float32`. Never crashes on empty/short/silent input. Children have higher pitch/formants, so these genuinely separate the classes. |
| `model.py` | Small **logistic regression** in numpy: `fit / predict_proba / save / load` (npz) **+ temperature-scaling** calibration (`fit_temperature`) so confidences are honest. Includes `expected_calibration_error`. |
| `decision.py` | **Pure, system-critical** `AgeGate`: aggregates per-utterance P(child) across a **session** (running median), applies a **fail-safe** buffer, maps to bands `child / teen / adult / unknown`, never emits a precise age, exposes `decide() → {band, confidence, ...}` with an **abstain → escalate** flag. |
| `train.py` | Training harness **+ synthetic-but-realistic data generator** (child vs adult feature distributions with overlap and confounding subgroups). Trains, calibrates, saves `age_model.npz`. |
| `evaluate.py` | Reports **accuracy, ECE, subgroup false-ADULT-rate** (child passed as adult — the child-safety-critical error) and false-child-rate, plus the session-level gate fail-safe result. |
| `tests/test_age_gate.py` | stdlib `unittest` suite (17 tests). |

### Run it

```bash
cd /home/monster/Desktop/DreamCodeVRPlus-hardening
python3 -m unittest discover -s ml/age_gate/tests -v   # tests
python3 ml/age_gate/train.py                            # train + calibrate + save
python3 ml/age_gate/evaluate.py                         # metrics report
```

Everything is **deterministic** (numpy RNG seeded via `np.random.default_rng`; no
`time`/`random` in logic).

## The decision policy (fail safe *for the child*)

`AgeGate.decide()` returns coarse bands only, never a number:

- **child** — confident P(child) ≥ threshold.
- **adult** — confident the speaker is *not* a child, past a Challenge-25 buffer.
- **teen** — confident-not-child but still inside the buffer → treated as a minor
  (restricted), and escalates.
- **unknown** — too few utterances **or** low confidence / ambiguous band. The
  surrounding system **must treat `unknown` as the strictest CHILD profile**
  (`effective_profile == "child"`), and `escalate` is raised so the platform can
  fall back to its authoritative age flag / parental consent.

We **never hard-block an adult** on a low-confidence guess — uncertainty applies
child-safe limits and escalates, it does not deny access.

## Calibration matters here

`decision.py` fails safe on **confidence**, so an over-confident model would
defeat the safeguard. `model.py` therefore ships **temperature scaling**. The
generator deliberately models a realistic hazard — training on a *cleaner* corpus
than deployment (studio child audio vs noisy in-the-wild audio) — which makes the
raw model over-confident; temperature scaling on a held-out validation set
restores honest confidence (in the shipped run, val ECE ≈ 0.05 → ≈ 0.01, T ≈ 1.9).

## The production upgrade path (`wav2vec2_features.py`, **not run here**)

`wav2vec2_features.py` is a **drop-in replacement** for `features.extract_features`
that pools frozen **early-layer** self-supervised embeddings (audEERING's public
`wav2vec2-large-robust` age/gender backbone, or WavLM / HuBERT). The literature
reports **~4–5 yr MAE** and **~97 % child-vs-adult accuracy on CMU-Kids** with this
recipe, and layer-wise probing shows early layers carry the most child-relevant
signal. It imports `torch` + `transformers` **lazily** and raises a clear,
actionable message on a numpy-only box (import itself never fails). Retrain
`model.AgeClassifier` (or a <1 M-param MLP head) on these embeddings; `decision.py`
is unchanged.

**Real-data upgrade:** swap the synthetic generator for **aGender** (has a <13
children class), **CMU-Kids / PF-STAR** (child regime), **Common Voice**
(cross-corpus), **TIMIT** (clean sanity). Report subgroup false-ADULT-rate with
thresholds set to fail safe *for the child*.

## Privacy / on-device design (non-negotiable)

- **On-device & ephemeral.** Raw audio and all embeddings are discarded per
  window. Persist only `{band, confidence, timestamp, model-version}` with a TTL
  and erasure. Runs in-process with STT; int8-quantize the head for Quest.
- **Template-free.** No stored voiceprint / speaker template — this is what
  supports the "not an Art. 9 biometric template" argument (contested; treated
  honestly in the research plan).
- **Strict purpose limitation.** The age signal *only* tightens safety — never
  ads, profiling, identity, or emotion/affect inference (explicitly excluded).
- **Platform flag is authoritative;** ML age is a safety net / anomaly signal,
  never a substitute for verifiable parental consent.
- **Coarse output only.** No precise age ever leaves `decision.py`.
- **Fairness caveat.** Voice age is biased by accent / L1–L2 / atypical speech;
  the confounding subgroups in the generator (`child_low_pitch`,
  `adult_high_pitch`) exist so the fairness metric (subgroup false-ADULT-rate) is
  measured, not assumed. Thresholds are set to fail safe *for the child*.

> The age gate is itself the surveillance-grade inference the XR-privacy
> literature warns about — deliberately re-used, on-device and governed, as a
> protective control. Red-teaming the gate (pitch/formant shift, voice
> conversion, replay, deepfake child/elderly voices) is tracked as its own attack
> class in the research plan.
