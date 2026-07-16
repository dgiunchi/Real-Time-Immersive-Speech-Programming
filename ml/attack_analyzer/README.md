# Adaptive / Continuous Attack Analyzer (ML #2)

> Turns the static **128-vector attack model** into a **self-growing, drift-aware,
> ML-driven attack surface.** This is model **#2** of the DreamCodeVR+ ML thrust
> (`RESEARCH_AND_ML_PLAN.md` §5). Model #1 is the on-device voice **age gate**; this
> analyzer also **red-teams that age gate**, which is what unifies the two into one
> threat model.

Everything here is **numpy + Python-stdlib only** and **deterministic** (numpy RNG seeded
`0`; no `time`/`random` used for logic). It runs offline with mocks so the whole pipeline
is reproducible and citable; the real hookup is documented below.

---

## What this is

Four cooperating parts (mapped to the L1/L2 layers in the plan):

| File | Role | Plan layer |
|------|------|-----------|
| `anomaly.py` | Reconstruction **autoencoder** (PCA-optimal linear AE) trained ONLY on benign intent→code-op sequences; reconstruction error = open-set (out-of-distribution = attack) score, thresholded at a benign quantile. | L2 anomaly |
| `drift.py` | **ADWIN-style** windowed drift detector (running mean/variance windows + variance-aware Hoeffding cut) so the benign baseline/threshold **adapts instead of decaying**. | L2 drift |
| `redteam_loop.py` | The **adaptive red-team loop**: a `SystemUnderTest` interface + a mock denylist SUT + a mutation/combination generator that hunts for inputs the SUT **passes but should block**; each bypass becomes a new vector. Includes an **age-gate-spoofing** family. | L1 red-teamer + the unifier |
| `vector_store.py` | The **growing** discovered-vector store (append-only JSON-Lines) with content-hash **dedup** and a **mapping onto the five 128-vector families**. | the store that grows |

---

## Run it

From the **repo root**:

```bash
# 1) the whole test suite (stdlib unittest)
python3 -m unittest discover -s ml/attack_analyzer/tests -v

# 2) the adaptive red-team loop (mock SUT + mock age gate) -> grows attacks_discovered.jsonl
python3 -m ml.attack_analyzer.redteam_loop

# 3) the open-set anomaly eval (attack families HELD OUT of training)
python3 -m ml.attack_analyzer.anomaly

# 4) the ADWIN-style drift demo (fires on a shift, quiet on stationary)
python3 -m ml.attack_analyzer.drift

# 5) the vector-store dedup + family-mapping demo
python3 -m ml.attack_analyzer.vector_store
```

Representative numbers from a clean run (deterministic):

- **Anomaly:** pooled **AUC = 1.00**, benign FPR ≈ **2 %** at the 99th-pct threshold; every
  held-out attack family (code-injection / sensor-privacy / immersive) detected at 100 %.
- **Drift:** **0** false alarms on stationary data; change detected at t≈304 for an injected
  shift at t=300.
- **Red-team:** ~**70 %** mutation bypass-rate against the naive mock (raw / case-flip /
  wrap-only controls are correctly caught), **100 %** age-gate spoof-rate, ~**160** new
  vectors filed across all **five** families in one run; re-running adds **0** (idempotent
  dedup).

---

## What runs NOW vs the real hookup

### Now (offline, in this repo)

- **System Under Test = `MockDenylistSUT`** — a deliberately naive case-insensitive
  substring denylist. It is a *stand-in* so the loop runs with no server. It normalises
  case but **not** spacing/unicode/synonyms, so leetspeak, homoglyph, token-splitting and
  aliasing mutations bypass it — exactly the evasion classes a real static screen also
  leaks (character-injection ASR **70–88 %** against commercial prompt shields,
  arXiv:2504.11168).
- **Sequences = synthetic** benign vs held-out-attack **intent→code-op** sequences
  (`anomaly.make_benign_sequences` / `make_attack_sequences`). Benign = well-behaved
  creative ops with modest repeats; attacks = (a) benign ops + system-access ops, (b)
  benign ops + sensor/PII ops, (c) **compositional** immersive harm (a long strobe/vection
  run of an individually-benign op) — the case per-action bounds provably miss.
- **Age gate = `MockAgeGate`** — a numpy linear stand-in for ML #1's voice classifier; the
  spoof is a **pitch-shift-like** feature perturbation that pushes a *child* sample across
  the boundary to be read as *adult* (the child-safety-critical direction).

### The real hookup (documented, not required here)

Replace the mock SUT with the live pipeline the platform already runs:

```
spoken utterance ──▶ STT (crates/stt-client) ──▶ LLM intent screen (crates/llm-client)
                 ──▶ Rust lexical + perceptual validator (crates/csharp-policy) ──▶ {blocked, reason}
```

`SystemUnderTest.validate(command)` becomes a client that POSTs the mutated command to the
running server (`apps/dreamcodevr-server`, `POST /api/command` and `/api/validate`) exactly
as `redteam/run_campaign.py` already does, and returns `{"blocked": bool, "reason": str}`.
For the **anomaly** plane, the synthetic sequences are replaced by **real** intent→code-op
sequences logged from generated C# (parsed from the Roslyn/`csharp-policy` op stream —
`spawn`, `set_color`, `file_io`, `process_spawn`, `perceptual_flash`, …). For the **age
gate**, `MockAgeGate` is replaced by the real wav2vec2-warm-start head from ML #1, and the
spoof becomes real pitch/formant shift, voice conversion, replay and deepfake-child audio
(the eval mirrors the existing "15 %→100 %, 0-bypass" C#-validator hardening story).

Nothing else in the loop changes — the mutation engine, drift detector, anomaly detector
and growing vector store are identical against the mock or the real target.

---

## Lineage (Auto-RT / WildTeaming)

The adaptive red-teamer is in the lineage of automated jailbreak-strategy exploration:

- **Auto-RT** (arXiv:2501.01830) — automatic red-team strategy search.
- **WildTeaming** — mining/recombining in-the-wild jailbreak tactics.
- Survey of automated red-teaming (arXiv:2410.09097).
- Guardrails are evadable: character-injection bypass of Azure Prompt Shield / Meta Prompt
  Guard / ProtectAI (arXiv:2504.11168).

Our mutation families (obfuscation, homoglyph, token-splitting, aliasing, benign-wrapping,
seed combination) are the concrete, deterministic instantiation of that idea against *our*
two-layer screen — plus the novel **age-gate-spoofing** family that folds ML #1 into the
same threat model.

The anomaly + drift plane is in the lineage of streaming intrusion detection: LSTM-AE +
One-Class-SVM (classic), **SSF** continual IDS with strategic forgetting (arXiv:2412.16264),
and ADWIN drift detection (Bifet & Gavaldà, 2007).

---

## How it grows the 128-vector model

1. The **red-team loop** discovers a bypass → it is a *new attack vector*.
2. `vector_store.append` computes a content-hash id (dedup), **classifies it into one of
   the five 128-vector families** (network-identity / generated-code / immersive /
   voice-sensor / privacy), and appends it to `attacks_discovered.jsonl`.
3. The **anomaly detector** independently flags out-of-distribution op-sequences the
   denylist never named; those become vectors too (open-set discovery).
4. The **drift detector** watches the reconstruction-error stream; when the benign baseline
   shifts it signals a **re-fit / re-threshold** (SSF-style continual update) so the model
   tracks the new regime instead of decaying.
5. The augmented store re-seeds the next red-team round and can be exported back into the
   Rust validator's ban sets / the LLM screen's few-shot examples — closing the loop.

The result is the headline ML #2 metric the results chapter reports: **new-vectors-found**
and **bypass-rate-over-time** under *adaptive* (not static) attack, and **open-set
detection** of attack families held out of training.

---

## Files

```
ml/attack_analyzer/
├── anomaly.py          # reconstruction autoencoder + featurizer + synthetic data + open-set eval
├── drift.py            # ADWIN-style windowed drift detector
├── redteam_loop.py     # SUT interface + mock + mutation engine + age-gate spoof + the loop
├── vector_store.py     # append-only store, content-hash dedup, family mapping
├── attacks_discovered.jsonl   # the growing store (generated by the loop)
├── README.md
└── tests/
    └── test_analyzer.py       # stdlib unittest: anomaly / drift / redteam / vector_store
```
