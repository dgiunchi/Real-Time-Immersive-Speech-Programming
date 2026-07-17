#!/usr/bin/env python3
"""Tests for the honest hard-negative anomaly benchmark (anomaly.hard_negative_eval).

The point of this benchmark is to STOP reporting AUC~1.00 as skill. These tests pin
the honest story:
  * the stealth family uses BENIGN ops only (no attack token) — so a detector that
    leans on an out-of-vocabulary attack column is blind to it;
  * the trivial "attack-op present?" baseline ties the autoencoder on the easy
    families (proving they are a benchmark artifact) but is at chance (~0.5) on stealth;
  * the autoencoder gets a REALISTIC, non-perfect AUROC on stealth via the
    compositional-harm aggregate — real detection, not leakage;
  * metrics are reported at a low attack base rate with AUPRC + detection@1%FPR + CIs.
"""

import unittest

import numpy as np

from ml.attack_analyzer import anomaly
from ml.attack_analyzer.anomaly import (
    ATTACK_OPS,
    hard_negative_eval,
    make_benign_realistic,
    make_stealth_sequences,
)


class HardNegativeBenchmark(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rep = hard_negative_eval(seed=0)

    def test_stealth_uses_benign_ops_only(self):
        rng = np.random.default_rng(1)
        atk = set(ATTACK_OPS)
        seqs = make_stealth_sequences(300, rng)
        for s in seqs:
            self.assertFalse(any(op in atk for op in s),
                             "stealth sequences must contain NO attack-op tokens")

    def test_benign_realistic_uses_benign_ops_only(self):
        rng = np.random.default_rng(2)
        atk = set(ATTACK_OPS)
        for s in make_benign_realistic(300, rng):
            self.assertFalse(any(op in atk for op in s))

    def test_easy_families_are_a_trivial_artifact(self):
        # On code_injection / sensor_privacy the trivial attack-op rule matches the
        # autoencoder at ~perfect AUROC -> the near-1.0 score is separability, not skill.
        for fam in ("code_injection", "sensor_privacy"):
            ae = self.rep["families"][fam]["autoencoder"]["auroc"]
            triv = self.rep["families"][fam]["trivial_attack_op"]["auroc"]
            self.assertGreater(ae, 0.99)
            self.assertGreater(triv, 0.99)

    def test_stealth_is_hard_and_not_trivially_separable(self):
        st = self.rep["families"]["stealth_immersive"]
        ae = st["autoencoder"]["auroc"]
        triv = st["trivial_attack_op"]["auroc"]
        # trivial attack-op rule is BLIND on stealth (no attack tokens) -> ~chance.
        self.assertGreaterEqual(triv, 0.40)
        self.assertLessEqual(triv, 0.60)
        # autoencoder does REAL but IMPERFECT detection: clearly beats the blind
        # baseline, and is nowhere near the artifact's 1.0.
        self.assertGreater(ae, triv + 0.10)
        self.assertLess(ae, 0.95)
        # honest: it is genuinely harder than the trivial families.
        self.assertLess(ae, self.rep["families"]["code_injection"]["autoencoder"]["auroc"])

    def test_max_repeat_dominates_autoencoder_on_stealth(self):
        # The honest finding we OWN rather than hide: a one-line max-repeat rule beats the
        # autoencoder on stealth (the linear AE is structurally weak at repeat-based harm),
        # and this holds ACROSS seeds (not cherry-picked at seed 0).
        st = self.rep["families"]["stealth_immersive"]
        self.assertGreater(st["trivial_max_repeat"]["auroc"],
                           st["autoencoder"]["auroc"] + 0.10)
        for seed in range(4):
            r = hard_negative_eval(seed=seed, n_boot=100)["families"]["stealth_immersive"]
            self.assertGreater(r["trivial_max_repeat"]["auroc"], r["autoencoder"]["auroc"],
                               f"max-repeat must dominate the AE on stealth (seed {seed})")
            self.assertLessEqual(r["trivial_attack_op"]["auroc"], 0.60)  # blind every seed

    def test_no_op_mix_or_length_artifact(self):
        # Regression for the inflation bug the adversarial review caught: benign and stealth
        # share op-mix + length, so scoring benign-vs-benign (no elevated run) is at CHANCE.
        # This proves the AE's stealth signal is the repeat run itself, not a distribution
        # or length shortcut (before the fix this control sat at ~0.65).
        from ml.evalstats import roc_auc
        rng = np.random.default_rng(0)
        fz = anomaly.SequenceFeaturizer()
        det = anomaly.ReconstructionAnomalyDetector(6)
        det.fit(fz.transform(anomaly.make_benign_realistic(800, rng)))
        ben = anomaly.make_benign_realistic(500, rng)
        ctrl = anomaly.make_benign_realistic(60, rng)  # benign used as pseudo-attacks
        y = np.concatenate([np.zeros(len(ben)), np.ones(len(ctrl))]).astype(int)
        auc = roc_auc(y, det.score(fz.transform(ben + ctrl)))
        self.assertGreaterEqual(auc, 0.40)
        self.assertLessEqual(auc, 0.60)

    def test_auprc_ci_is_ordered_and_bounded(self):
        for fam in self.rep["families"].values():
            for m in fam.values():
                lo, hi = m["auprc_ci95"]
                self.assertLessEqual(lo, m["auprc"] + 1e-9)
                self.assertLessEqual(m["auprc"] - 1e-9, hi)
                self.assertGreaterEqual(lo, 0.0)
                self.assertLessEqual(hi, 1.0)

    def test_base_rate_respected(self):
        n_atk = self.rep["n_attack_per_family"]
        n_ben = self.rep["n_benign_test"]
        achieved = n_atk / (n_atk + n_ben)
        self.assertAlmostEqual(achieved, self.rep["base_rate"], delta=0.01)

    def test_deterministic(self):
        rep2 = hard_negative_eval(seed=0)
        self.assertEqual(
            self.rep["families"]["stealth_immersive"]["autoencoder"],
            rep2["families"]["stealth_immersive"]["autoencoder"],
        )

    def test_detection_at_fpr_never_exceeds_budget(self):
        # inherits the ml.evalstats guarantee, checked end-to-end here.
        for fam in self.rep["families"].values():
            for m in fam.values():
                self.assertLessEqual(m["achieved_fpr"], 0.01 + 1e-9)


if __name__ == "__main__":
    unittest.main(verbosity=2)
