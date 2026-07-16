"""ML #2 — the adaptive / continuous attack-surface analyzer for DreamCodeVR+.

This turns the static 128-vector attack model into a self-growing, drift-aware,
ML-driven attack surface (RESEARCH_AND_ML_PLAN.md §5). It has four cooperating parts:

  * `anomaly`      — an undercomplete (PCA-optimal) linear autoencoder trained ONLY on
                     benign intent->code-op sequences; reconstruction error is the
                     open-set (out-of-distribution = attack) anomaly score.
  * `drift`        — an ADWIN-style windowed drift detector so the benign baseline (and
                     its threshold) adapts to distribution shift instead of decaying.
  * `redteam_loop` — the adaptive red-team loop: a `SystemUnderTest` interface + a mock
                     denylist SUT + a mutation/combination generator that hunts for
                     inputs the SUT PASSES but should BLOCK; each bypass becomes a new
                     vector. Includes an age-gate-spoofing attack family.
  * `vector_store` — load/append/dedup the discovered-vector store and map every vector
                     onto the five existing 128-vector families.

Everything here is numpy + Python-stdlib only and deterministic (numpy RNG seeded 0).
The mock SUT + synthetic sequences let the whole pipeline run offline; the real hookup
(spoken -> STT -> LLM -> Rust validator) is documented in README.md.
"""
