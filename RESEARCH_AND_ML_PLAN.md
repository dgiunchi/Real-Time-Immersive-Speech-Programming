# DreamCodeVR+ — Research & ML Plan (cybersecurity + privacy in XR)

> **One living document** for the ML/research thrust. Everything — literature, the two
> ML models, privacy/ethics/regulation, novelty framing, evaluation, roadmap, and the
> decisions we still need to make — lives here. We keep updating THIS file, not a pile
> of new ones. Grounded in a 7-agent web-search pass over 2022–2026 work; unverified
> numbers are flagged.

---

## 0. TL;DR — the thesis in one line

**Age-adaptive dual-plane safety for a live speech→LLM→C#→runtime-compiled XR loop:**
one privacy-preserving, on-device age signal simultaneously tightens **(a)** the
executable-**code**-safety plane (our Rust/C# validator + LLM screen) **and (b)** a new
**perceptual/embodied**-safety plane that inspects LLM-generated scene code *before*
compile for **compositional** immersive harms — plus **ML that continuously grows the
attack model** (and red-teams the age gate itself).

**The sharp move (the "inversion"):** the exact motion/voice inference the XR-privacy
literature demonstrates as a *surveillance attack* (Nair et al.) is deliberately re-used
— on-device, ephemeral, unlinked, consented — as a **governed protective control.**
Nobody in 2022–2026 (a) couples an age posterior to a real-time safety policy, or (b)
unifies code-safety with perceptual-safety in one gate. **That coupling is the novelty
— not age inference itself (already done), which we treat honestly as prior art.**

---

## 1. What we already have (the platform this builds on)

- Speech → STT → LLM → **Rust safety validator** (lexical + perceptual denylist) →
  authenticated decision → Unity runtime C# compile. Modes A/B/C/D.
- A **128-vector attack model**, HMAC+Ed25519 auth/replay, encryption-at-rest, an
  adversarial red-team harness (the "15%→100%, 0-bypass" C#-validator story), and an
  authored **perceptual-safety layer** (disclosure monitors, UserFrameGuardian).

The ML thrust plugs two learning models on top: an **age gate** and a **continuous
attack analyzer**, and upgrades the perceptual layer into a **detection** plane.

---

## 2. Novel contribution (what we claim)

1. **Age-conditioned enforcement.** A detected child speaker automatically tightens
   *both* the C# allow-list *and* the perceptual bounds. No prior system age-gates
   runtime-generated code or perceptual bounds (kids moderation filters *rendered
   content*, not generated code).
2. **Two-plane safety gate.** Code-safety (SecurityEval/SALLM lineage) and
   perceptual/embodied-safety (Casey "Human Joystick" TDSC 2021; Inception 2403.05721)
   live in *disjoint* literatures; unifying them over an LLM-generates-runnable-code XR
   loop is unexplored.
3. **Compositional-harm detection.** Each generated op can be individually within
   bounds while the *sequence* composes into harm (herding, progressive occlusion,
   strobe-by-accumulation). Per-action bounds provably miss this — the professor's
   stated thrust — and nobody detects these classes *inside LLM-generated scene code
   pre-compile*. **Strongest, least-contested wedge.**
4. **The inversion (surveillance → protection),** governed as a first-class ethics +
   systems contribution.
5. **Continuous, ML-discovered attack surface.** Turn the static 128 vectors into a
   self-growing set via an adaptive red-teamer + streaming anomaly/drift detection, and
   **red-team the age gate itself** as a new vector class — unifying the two ML thrusts
   into one threat model.

---

## 3. Literature grounding (honest state of the art)

### 3a. Age from VR **motion** (prior art — and a caution)
- **Nair et al., "Inferring Private Personal Attributes of VR Users from Head & Hand
  Motion"** (arXiv:2305.19198, 2023; IEEE VRW 2024): age (binary old/young) **78.3%
  per-user** (64.9% per-sequence) — but age is a **weaker** motion attribute than
  **height 86.7% / weight 85.0%**; anthropometrics carry most age-correlated signal.
- **Nair et al., "Unique Identification of 50,000+ VR Users from Head & Hand Motion"**
  (USENIX Sec 2023, arXiv:2302.08927): **94.33%** identification from 100 s (73.2% from
  10 s) over **55,541** users → **motion age inference is NOT privacy-neutral: the same
  stream re-identifies the person.** This is why our design must be on-device/ephemeral.
- **BOXRR-23** (TVCG 2024, arXiv:2310.00430): 4.7 M motion recordings / 105 k users —
  pretraining corpus, but **adult-skewed, no child labels.**
- Gait ceiling (full-body, not VR 3-point): sensor gait age MAE ≈ **4–5 yr**
  (arXiv:2507.11571, 2025) → VR's head+2-controllers will be *worse* unless we exploit
  anthropometrics + reaction time.
- ⚠️ **Unverified:** secondary summaries cite "age ~90% F1 / gender ~82% F1" and a
  "78.5% cross-app" number — **do not quote as load-bearing**; re-check primary tables.

### 3b. Age from **voice** (our recommended primary signal)
- SSL embeddings (wav2vec2 / WavLM / HuBERT) + a small head: **~4–5 yr MAE** on TIMIT
  (arXiv:2502.12007, 2306.16962, 2012.01551); WavLM more noise-robust.
- **Child vs adult is far stronger:** **97.14% age-group accuracy on CMU Kids**
  (novelty-positioning agent); layer-wise probing shows early layers (1–7) best for
  children (arXiv:2508.10332, 2307.16398).
- **Warm-start available:** audEERING's public **wav2vec2-large-robust age/gender
  model** (HuggingFace) + an edge-CNN age model runs **~20 ms on-device**
  (J. Signal Processing Systems 2024).
- Primary datasets: **aGender** (has a <13 children class), CMU Kids / PF-STAR (child
  regime), Common Voice (cross-corpus), TIMIT (clean sanity).

### 3c. ML for XR / system **security**
- **Adaptive red-teaming:** Auto-RT (arXiv:2501.01830), WildTeaming, survey 2410.09097.
- **Guardrails are evadable:** character-injection ASR **70–88%** against Azure Prompt
  Shield / Meta Prompt Guard / ProtectAI (arXiv:2504.11168, 2025) → static screens leak.
- **Code-safety ML:** Devign (NeurIPS 2019), ReVeal, Vul-LMGNNs (2404.14719) — AST /
  code-property-graph GNNs; but **no C#/Unity malicious-benign corpus exists** (we'd
  build a small one → optional/stretch).
- **Streaming anomaly + drift:** LSTM-AE + One-Class-SVM (classic); **SSF continual IDS**
  with strategic forgetting (arXiv:2412.16264); Facade insider-threat (2412.06700).
- **LLM-as-judge overconfidence** (arXiv:2508.06225) → must calibrate (temperature
  scaling / conformal) with an **abstain→escalate** policy.
- **Perceptual attacks:** Human Joystick (TDSC 2021), Inception (2403.05721), False
  Reality (2508.08043); flashing/photosensitive detection (WCAG 2.3 / ITU / Harding;
  s11760-025-04608-4); cybersickness prediction (2501.01212).

### 3d. XR **privacy** & biometric inference (the tension)
- Motion/gaze reveal identity, age, gender, ethnicity, body, health, even ADHD
  (Kröger et al.; MetaData Berkeley RDI; Nat. Sci. Reports). **Our age model is itself
  this inference capability** — the central paradox we must govern.
- Privacy-preserving XR ML: **Deep Motion Masking** (IEEE VR 2024, 2311.05090) ~96%
  de-anon reduction; Going Incognito / MetaGuard (UIST/CCS 2023, 2208.05604) local DP;
  DP for eye-tracking (PLoS ONE 2021). **DP-vs-age-accuracy trade-off is severe** —
  pair DP with a non-biometric fallback.

### 3e. Age assurance + **regulation**
- **COPPA** final amended rule (16 CFR 312, 2025; adds biometric identifiers; compliance
  Apr 2026). **FTC DENIED facial age estimation as a COPPA consent mechanism (Mar 2024)**
  → **inference can gate *experience* but cannot replace verifiable parental consent.**
- **UK AADC / Children's Code** (ICO): high-privacy defaults, DPIA required, "best
  interests of the child," Challenge-25-style buffer.
- **EU AI Act Art. 5** (in force Feb 2025): **emotion recognition** in many contexts and
  certain biometric categorisation are **prohibited** → we **exclude any affect/emotion
  inference**; age is *not* a banned special category (Recital 16) but the pathway matters.
- **GDPR Art. 8** (child consent); **ISO/IEC 27566-1:2025** (age-assurance framework);
  **IEEE 2089.1-2024** (five assurance levels).
- **Meta Quest** preteen/teen/adult age-group API (Apr 2024) → **authoritative source;
  our ML age is a secondary safety net, never the legal basis.**
- Industry "good" bar: Yoti facial age **MAE ~1.2–1.3 yr** (6–17); Roblox ~1.4 yr.
- **Live/unsettled:** position paper "Age Estimation Models Do Not Process Biometric
  Data" (arXiv:2605.17347, 2026) supports our transient-on-device argument — **but it is
  contested; state that honestly.**

---

## 4. ML Model #1 — on-device age gate (voice-primary, motion-secondary)

- **Primary:** binary **child(<13)/adult** from voice, aligned to the COPPA line, with a
  coarse band (child<13 / teen13–17 / adult18+) + **calibrated confidence**. Warm-start
  from audEERING wav2vec2; frozen early-layer embeddings → **<1 M-param MLP head**;
  int8-quantise for Quest; run **in-process with STT**. Aggregate per-utterance across
  the **session** (not one clip).
- **Secondary (fusion, honestly caveated):** Nair-style 6DoF features (HMD-height,
  wingspan, reach, velocity-peak count, jerk, head angular velocity) → LightGBM **+ the
  free feature no prior work had: speech-to-action latency** (our pipeline already
  timestamps speech-in → scene-action; older users ~1.3× slower). Late-fuse logits;
  ablate voice-only / motion-only / fused.
- **Ground truth:** **Meta Quest platform age flag is authoritative**; ML is a net /
  anomaly signal (e.g. adult-flagged account on a shared family headset behaving like a
  child → trigger re-assurance).
- **On-device, ephemeral, template-free:** raw audio/motion + all embeddings discarded
  per window; persist only `{band, confidence, timestamp, model-version}` with TTL +
  erasure. **No stored voiceprint/template** (this is what supports the "not Art. 9
  biometric" argument — contested).
- **Fail-closed:** buffer zones near 12/13 and 17/18 → default to **strictest child
  profile** + escalate to platform re-verify / parental consent. **Never hard-block an
  adult on a low-confidence guess.**

## 5. ML Model #2 — continuous / adaptive attack-vector analysis

Three layers → one self-growing attack store:
- **L1 Adaptive red-teamer** (offline+online): Auto-RT/WildTeaming-style generator emits
  **spoken** prompts → STT → LLM → C#, hunting inputs that pass the Rust validator + LLM
  screen yet yield unsafe code/scene ops. Each new bypass **auto-augments the 128-vector
  model**; report **new-vectors/hour** + **bypass-rate-over-time** under *adaptive* (not
  static) attack — extends the existing hardening narrative.
- **L2 Streaming anomaly + drift** (online): LSTM/transformer **autoencoder** on *benign*
  intent→code-op sequences (reconstruction error) + One-Class-SVM/Isolation-Forest on
  its latent space for **open-set** novelty; wrap with **ADWIN/DDM drift detection +
  SSF-style continual updates** (memory buffer, strategic forgetting) so it doesn't decay.
  Evaluate with attacks **held out** of training.
- **L3 Structural code gate (stretch/optional):** Roslyn AST+CFG+data-flow → GGNN /
  GraphCodeBERT second-opinion on generated C#, fused with the LLM screen via a
  **temperature-scaled, fail-closed ensemble.** Requires **building a small labelled
  C#/Unity corpus** (Reflection, Process.Start, DllImport, unsafe, file/net IO as
  positives) — real cost, hence optional.
- **Unifier:** **red-team the age gate itself** (voice pitch/formant shift, voice
  conversion, replay, deepfake child/elderly voices; motion mimicry) → gate-bypass rate
  feeds L1 as a new vector class.

## 6. Perceptual-safety plane + compositional harm (the wedge)

Detect, *inside LLM-generated scene code pre-compile*, the immersive-attack classes from
the Casey/Inception taxonomy: forced locomotion / human-joystick, chaperone/boundary
edits, disorientation/vection, occluding overlays, flashing (WCAG 2.3 / ITU / Harding).
**Key argument + eval:** a *sequence* of individually-bounded ops composes into harm →
model the cumulative envelope (FOV coverage, net displacement, luminance/flash rate) and
**condition the bounds on the age band** (child bounds ≪ adult bounds).

---

## 7. Privacy / ethics / regulation guardrails (non-negotiable)

1. **On-device, ephemeral, template-free** (as §4).
2. **Platform flag authoritative; inference is a safety net** — ML age never substitutes
   for verifiable parental consent (FTC Mar 2024).
3. **Fail-closed to strictest child profile** under uncertainty; Challenge-25 buffer.
4. **Confront the paradox up front:** consent-gated activation, strict purpose limitation
   (age signal *only* tightens safety — never ads/profiling/identity), no persistent age
   label, access-controlled model.
5. **Quantify detector-as-risk:** shadow-probe identity/disability/gender leakage from the
   age model's features; show DP / deep-motion-masking reduces it (target ~96% de-anon
   reduction) — *the tension turned into a measured result.*
6. **Fairness/confound audit (mandatory):** motion age confounds with body-size/disability
   (mis-ages short adults, tall children, wheelchair users); voice age biased by
   accent/L1-L2/atypical speech. Report **subgroup false-ADULT-rate** (child passes as
   adult = the child-safety-critical metric) and false-child-rate; set thresholds to fail
   safe *for the child.*
7. **Regulatory mapping as an appendix:** DPIA (AADC), EU AI Act analysis (age not a
   banned category, but **explicitly exclude emotion/affect** inference, Art. 5), map to
   ISO/IEC 27566-1 + IEEE 2089.1; **state residual legal uncertainty honestly.**
8. **Exclude eye-tracking by default** (special-category, Quest Pro only).
9. **Consent + transparency** age-appropriate + parent-facing, **revocable per-modality**
   (run motion-only if voice-age consent declined).

---

## 8. Biggest risks (and how we blunt them)

- **No public child-labelled VR 3-point motion dataset** → make **voice load-bearing**,
  motion a caveated secondary cue; state the gap as a finding.
- **"You built the surveillance you critique"** → pre-empt structurally (on-device,
  ephemeral, unlinked, DP, consent-gated, fail-closed) *and* philosophically (the
  inversion is the point).
- **Age gate is spoofable; voice is a voiceprint** → defense-in-depth (platform flag
  authoritative), red-team spoofing as a first-class *result*.
- **Scope explosion for one MSc** → see §10 recommended scope; drop the C#-AST-GNN corpus.
- **Unverified numbers** in the evidence base → re-verify before the write-up.
- **No benchmark for unsafe LLM-generated VR scenes** → building a credible,
  non-cherry-picked test set is itself work; budget for it.
- **DP-vs-accuracy is severe for age** → pair DP with a non-biometric fallback.

---

## 9. Evaluation plan (what goes in the results chapter)

- **Voice age:** child/adult accuracy + **calibration (ECE, Brier, reliability curves)**;
  subgroup false-adult/false-child by gender/accent/L1-L2/atypical speech; aGender primary
  + CMU Kids/PF-STAR + Common Voice cross-corpus + MUSAN noise/reverb/codec to simulate
  the Quest mic.
- **Fusion ablation:** voice-only vs motion-only vs fused (+ the speech-to-action latency
  feature's lift).
- **Age-gate spoofing:** pitch/formant/voice-conversion/replay/deepfake/motion-mimicry →
  bypass rate; feed into the analyzer; before/after retrain (mirrors 15%→100%).
- **Rust validator coverage baseline:** SecurityEval/SALLM CWE-mapped probes → *measured*
  false-negative rate (not an assertion).
- **Continuous analyzer:** new-vectors/hour, bypass-rate-over-time, open-set detection
  (held-out attacks), drift-recovery, LLM-judge calibration + abstain/escalate rates.
- **Perceptual plane:** labelled safe/unsafe generated-scene set incl. **compositional**
  cases; precision/recall; **age-conditioned-bound ablation** (child vs adult bounds).
- **Privacy-utility curves:** age accuracy vs DP / motion-masking; shadow-probe leakage
  before/after.
- **End-to-end on the real Quest:** live speech→code→age-conditioned dual-plane
  safety→compile, logging *policy decisions only* (never raw biometrics).

---

## 10. Recommended scope for one MSc (my recommendation — you decide, §11)

- **2 headline thrusts:** (i) **age-adaptive dual-plane safety** + (ii) **on-device voice
  age gate.**
- **1 supporting:** **continuous/adaptive attack-vector analysis** (incl. red-teaming the
  age gate).
- **Drop / make optional:** the **C#-AST-GNN malicious-code corpus** (biggest build, no
  existing dataset).

### Phased roadmap (build order)
1. **Voice age gate** (warm-start audEERING → child/adult head → calibrate) — standalone,
   validatable on public corpora today, no headset needed. *First ML deliverable.*
2. **Age-conditioned policy** wired into the existing Rust validator + perceptual bounds
   (child profile tightens both). *Ties ML to the system we already built.*
3. **Perceptual-detection plane + compositional-harm model** over generated scene code.
4. **Adaptive red-teamer (L1)** growing the 128-vector store + red-teaming the age gate.
5. **Streaming anomaly/drift (L2).**
6. Motion-age fusion, DP/privacy-utility study, regulatory appendix.
7. *(stretch)* C#-AST-GNN code gate (L3).

---

## 11. Decisions for us to discuss BEFORE implementing

1. **Modality priority** — *recommend:* voice-primary + motion-secondary. (Trade-off:
   voice is most accurate but highest-regulatory-risk = a voiceprint.)
2. **Ground truth / data** — *recommend:* consume Meta Quest age-flag as authoritative,
   ML as safety-net; rely on public corpora + adult proxies + honest limits. Decide if
   you'll attempt *any* IRB child-data collection (big ethics burden; gates the motion
   thrust's validatability).
3. **Scope** — confirm the 2+1 headline set and dropping the C#-GNN corpus, or re-rank.
4. **Regulatory venue** — UK/EU (AADC, GDPR-K, EU AI Act, ISO 27566-1, IEEE 2089.1) vs US
   (COPPA). *Likely UK/EU default.*
5. **Age-gate enforcement** — confirm it **never hard-blocks** on inference and instead
   fails safe to the strictest child profile + escalates. (Means age *restricts*, doesn't
   *authorise* — confirm that matches intent.)
6. **Eye-tracking** — include as an extra age signal (strong but special-category, Quest
   Pro, heavy consent) or **exclude** (recommended default).

---

## 12. References (web-verified in this pass; ⚠️ = re-check before quoting)

- Nair et al., *Unique Identification of 50k+ VR Users…*, USENIX Sec 2023 — arXiv:2302.08927
- Nair et al., *Inferring Private Personal Attributes of VR Users…*, 2023/VRW2024 — arXiv:2305.19198
- *BehaVR: User Identification Based on VR Sensor Data*, USENIX Sec 2024 — arXiv:2308.07304
- *BOXRR-23* (4.7 M recordings), TVCG 2024 — arXiv:2310.00430
- *Going Incognito in the Metaverse / MetaGuard*, UIST/CCS 2023 — arXiv:2208.05604
- *Deep Motion Masking*, IEEE VR 2024 — arXiv:2311.05090
- *Demographic Attributes Prediction from Speech (WavLM)*, 2025 — arXiv:2502.12007
- *Speech-based Age & Gender with Transformers* (audEERING), 2023 — arXiv:2306.16962
- *Layer-Wise SSL for Age/Gender*, WOCCI 2025 — arXiv:2508.10332
- *Robust SSL Child-Adult Classification*, 2023 — arXiv:2307.16398
- *Age Estimation from Speech on Edge Devices*, J. Signal Proc. Sys. 2024
- *aGender corpus*, LREC 2010
- *Auto-RT: Automatic Jailbreak Strategy Exploration*, 2025 — arXiv:2501.01830
- *Bypassing Prompt Injection/Jailbreak Detection in LLM Guardrails*, 2025 — arXiv:2504.11168
- *Overconfidence in LLM-as-a-Judge*, 2025 — arXiv:2508.06225
- *Devign*, NeurIPS 2019 — arXiv:1909.03496; *Vul-LMGNNs*, 2024 — arXiv:2404.14719
- *SSF: Continual Learning for Network Intrusion Detection*, 2024 — arXiv:2412.16264
- *Facade: Insider Threat Detection*, 2024 — arXiv:2412.06700
- *Inception Attacks*, 2024 — arXiv:2403.05721; *Human Joystick*, IEEE TDSC 2021
- *False Reality: Sensor-induced VR Vulnerability*, 2025 — arXiv:2508.08043
- *Position: Age Estimation Models Do Not Process Biometric Data*, 2026 — arXiv:2605.17347 (contested)
- COPPA final rule (16 CFR 312, 2025); FTC denial of facial age estimation (Mar 2024)
- UK AADC / ICO Children's Code; EU AI Act Art. 5; GDPR Art. 8
- ISO/IEC 27566-1:2025; IEEE 2089.1-2024; Meta "Age-Appropriate Experiences on Quest" (Apr 2024)
- Yoti Facial Age Estimation White Paper 2024 (industry MAE benchmark)
- *SoK: Privacy-UX Trade-offs in XR*, ASIA CCS 2025; *KidsNanny*, 2026 — arXiv:2603.16181
- ⚠️ secondary "age ~90% / gender ~82% F1", "78.5% cross-app" — unverified, do not quote.

---

_Last updated: 2026-07-17 (research pass). Update THIS file as decisions are made and
models are built._
