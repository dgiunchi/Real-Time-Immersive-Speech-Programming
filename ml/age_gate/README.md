# Voice Age Gate (ML #1) — DreamCodeVR+

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
