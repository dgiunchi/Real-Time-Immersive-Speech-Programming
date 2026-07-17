#!/usr/bin/env python3
"""Tests for ml.evalstats.stats — self-consistency AND independent reference values.

Where a closed-form / hand-computed answer exists (average precision, exact McNemar
p, normal quantiles, AUC with ties) we assert the exact number, not just an invariant.
The DeLong variance is cross-checked against an independent paired bootstrap of the
AUC difference — two different estimators of the same quantity must agree.
"""

import math
import unittest

import numpy as np

from ml.evalstats import stats
from ml.evalstats.stats import (
    _norm_cdf,
    _norm_ppf,
    average_precision,
    bootstrap_ci,
    delong_roc_test,
    detection_rate_at_fpr,
    mcnemar_test,
    roc_auc,
)


class NormalFunctions(unittest.TestCase):
    def test_cdf_reference_points(self):
        self.assertAlmostEqual(_norm_cdf(0.0), 0.5, places=12)
        self.assertAlmostEqual(_norm_cdf(1.959963985), 0.975, places=6)
        self.assertAlmostEqual(_norm_cdf(-1.959963985), 0.025, places=6)

    def test_ppf_reference_points(self):
        self.assertAlmostEqual(_norm_ppf(0.5), 0.0, places=9)
        self.assertAlmostEqual(_norm_ppf(0.975), 1.959963985, places=5)
        self.assertAlmostEqual(_norm_ppf(0.025), -1.959963985, places=5)

    def test_cdf_ppf_are_inverses(self):
        for p in (0.01, 0.2, 0.5, 0.8, 0.99):
            self.assertAlmostEqual(_norm_cdf(_norm_ppf(p)), p, places=6)


class RocAuc(unittest.TestCase):
    def test_perfect_and_reversed(self):
        y = np.array([0, 0, 1, 1])
        self.assertEqual(roc_auc(y, [0.1, 0.2, 0.3, 0.4]), 1.0)
        self.assertEqual(roc_auc(y, [0.4, 0.3, 0.2, 0.1]), 0.0)

    def test_ties_reference_value(self):
        # Hand-computed: pos={2,3}, neg={1,2} -> (2>1)+0.5(2=2)+(3>1)+(3>2) = 3.5, /4.
        self.assertAlmostEqual(roc_auc([0, 0, 1, 1], [1, 2, 2, 3]), 0.875, places=12)

    def test_single_class_is_nan(self):
        self.assertTrue(math.isnan(roc_auc([1, 1, 1], [0.1, 0.2, 0.3])))

    def test_matches_probability_definition_random(self):
        rng = np.random.default_rng(3)
        y = rng.integers(0, 2, 400)
        s = rng.normal(y, 1.0)
        pos, neg = s[y == 1], s[y == 0]
        brute = (np.sum(pos[:, None] > neg[None, :]) +
                 0.5 * np.sum(pos[:, None] == neg[None, :])) / (pos.size * neg.size)
        self.assertAlmostEqual(roc_auc(y, s), brute, places=12)


class AveragePrecision(unittest.TestCase):
    def test_reference_value(self):
        # ranking 1(pos),0(neg),1(pos),0(neg): precisions at the two positives = 1 and 2/3.
        ap = average_precision([1, 0, 1, 0], [0.9, 0.8, 0.7, 0.6])
        self.assertAlmostEqual(ap, (1.0 + 2.0 / 3.0) / 2.0, places=12)

    def test_perfect_ranking_is_one(self):
        self.assertAlmostEqual(average_precision([0, 0, 1, 1], [0.1, 0.2, 0.3, 0.4]),
                               1.0, places=12)

    def test_no_positive_is_nan(self):
        self.assertTrue(math.isnan(average_precision([0, 0, 0], [0.1, 0.2, 0.3])))

    def test_tie_order_invariant(self):
        # AP must not depend on the input order among equal scores (tie-collapse fix).
        y = np.array([1, 0, 1, 0, 1, 0])
        s = np.array([0.8, 0.8, 0.5, 0.5, 0.2, 0.2])  # three tied pairs
        base = average_precision(y, s)
        rng = np.random.default_rng(4)
        for _ in range(20):
            perm = rng.permutation(y.size)
            # permute WITHIN nothing special — any permutation keeps (y,s) paired, and
            # ties across the pairs must not move AP.
            self.assertAlmostEqual(average_precision(y[perm], s[perm]), base, places=12)


class DetectionAtFpr(unittest.TestCase):
    def test_clean_separation(self):
        neg = np.arange(100.0)          # 0..99
        pos = np.arange(200.0, 230.0)   # all well above
        y = np.concatenate([np.zeros(100), np.ones(30)]).astype(int)
        s = np.concatenate([neg, pos])
        tpr, thr, fpr = detection_rate_at_fpr(y, s, 0.01)
        self.assertEqual(tpr, 1.0)
        self.assertLessEqual(fpr, 0.01 + 1e-9)

    def test_single_class_is_nan(self):
        tpr, _, _ = detection_rate_at_fpr([1, 1, 1], [0.1, 0.2, 0.3], 0.01)
        self.assertTrue(math.isnan(tpr))

    def test_overshoot_regression_case(self):
        # Bug caught by adversarial review: neg=0..99, target=0.025 must give FPR=0.02
        # (floor(2.5)=2 flagged), NOT 0.03 as the old quantile approach returned.
        y = np.concatenate([np.zeros(100), np.ones(10)]).astype(int)
        s = np.concatenate([np.arange(100.0), np.arange(200.0, 210.0)])
        _, _, fpr = detection_rate_at_fpr(y, s, 0.025)
        self.assertLessEqual(fpr, 0.025 + 1e-12)
        self.assertAlmostEqual(fpr, 0.02, places=12)

    def test_tiny_budget_flags_nothing(self):
        # k = floor(0.001 * 200) = 0 -> flag no benign scores -> FPR exactly 0.
        y = np.concatenate([np.zeros(200), np.ones(20)]).astype(int)
        s = np.concatenate([np.arange(200.0), np.arange(500.0, 520.0)])
        _, _, fpr = detection_rate_at_fpr(y, s, 0.001)
        self.assertEqual(fpr, 0.0)

    def test_budget_never_exceeded_property(self):
        # The core guarantee: over many (n_neg, target, distribution incl. ties) the
        # achieved FPR must NEVER exceed the requested budget.
        rng = np.random.default_rng(123)
        for _ in range(300):
            n_neg = int(rng.integers(20, 400))
            n_pos = int(rng.integers(5, 100))
            target = float(rng.choice([0.001, 0.005, 0.01, 0.02, 0.05, 0.1]))
            neg = rng.integers(0, 15, n_neg).astype(float)  # small range -> many ties
            pos = rng.normal(10.0, 3.0, n_pos)
            y = np.concatenate([np.zeros(n_neg), np.ones(n_pos)]).astype(int)
            s = np.concatenate([neg, pos])
            _, _, fpr = detection_rate_at_fpr(y, s, target)
            self.assertLessEqual(fpr, target + 1e-12,
                                 f"FPR {fpr} exceeded budget {target} (n_neg={n_neg})")


class McNemar(unittest.TestCase):
    def test_exact_reference_p(self):
        # n10=8, n01=1 -> n=9, k=1 -> p = 2*(C(9,0)+C(9,1))*0.5^9 = 20/512.
        a = np.array([True] * 8 + [False] * 1 + [True] * 5)   # A correct
        b = np.array([False] * 8 + [True] * 1 + [True] * 5)   # B correct
        _, p = mcnemar_test(a, b, exact=True)
        self.assertAlmostEqual(p, 20.0 / 512.0, places=12)

    def test_no_discordant_pairs_p_is_one(self):
        a = np.array([True, False, True, False])
        _, p = mcnemar_test(a, a, exact=True)
        self.assertEqual(p, 1.0)

    def test_symmetric_in_arguments(self):
        rng = np.random.default_rng(5)
        a = rng.integers(0, 2, 200).astype(bool)
        b = rng.integers(0, 2, 200).astype(bool)
        _, p_ab = mcnemar_test(a, b)
        _, p_ba = mcnemar_test(b, a)
        self.assertAlmostEqual(p_ab, p_ba, places=12)

    def test_nonexact_equal_discordant_is_p_one(self):
        # chi-square branch must clamp the continuity correction: equal discordant
        # counts (n10 == n01) -> statistic 0, p = 1.0 (not a spurious p < 1).
        a = np.array([True, True, False, False])
        b = np.array([True, False, True, False])  # n10 = n01 = 1
        stat, p = mcnemar_test(a, b, exact=False)
        self.assertEqual(stat, 0.0)
        self.assertEqual(p, 1.0)


class BootstrapCi(unittest.TestCase):
    def test_brackets_point_and_deterministic(self):
        rng = np.random.default_rng(7)
        x = rng.normal(5.0, 2.0, 300)
        pt, lo, hi = bootstrap_ci(x.size, lambda idx: float(np.mean(x[idx])),
                                  n_boot=800, seed=11)
        self.assertLess(lo, pt)
        self.assertGreater(hi, pt)
        pt2, lo2, hi2 = bootstrap_ci(x.size, lambda idx: float(np.mean(x[idx])),
                                     n_boot=800, seed=11)
        self.assertEqual((lo, hi), (lo2, hi2))  # same seed -> identical

    def test_percentile_ci_of_mean_matches_normal_theory(self):
        rng = np.random.default_rng(9)
        x = rng.normal(0.0, 1.0, 500)
        _, lo, hi = bootstrap_ci(x.size, lambda idx: float(np.mean(x[idx])),
                                 n_boot=3000, alpha=0.05, method="percentile", seed=2)
        se = x.std(ddof=1) / math.sqrt(x.size)
        self.assertAlmostEqual(lo, x.mean() - 1.96 * se, places=1)
        self.assertAlmostEqual(hi, x.mean() + 1.96 * se, places=1)


class DeLong(unittest.TestCase):
    def _data(self, seed=0, n=400):
        rng = np.random.default_rng(seed)
        y = np.array([0] * (n // 2) + [1] * (n // 2))
        good = np.where(y == 1, rng.normal(1.2, 1, n), rng.normal(0, 1, n))
        weak = np.where(y == 1, rng.normal(0.4, 1, n), rng.normal(0, 1, n))
        return y, good, weak

    def test_auc_matches_roc_auc(self):
        y, good, weak = self._data()
        aa, ab, _, _ = delong_roc_test(y, good, weak)
        self.assertAlmostEqual(aa, roc_auc(y, good), places=10)
        self.assertAlmostEqual(ab, roc_auc(y, weak), places=10)

    def test_identical_scores_z_zero_p_one(self):
        y, good, _ = self._data()
        _, _, z, p = delong_roc_test(y, good, good)
        self.assertAlmostEqual(z, 0.0, places=10)
        self.assertAlmostEqual(p, 1.0, places=10)

    def test_symmetric(self):
        y, good, weak = self._data()
        _, _, _, p1 = delong_roc_test(y, good, weak)
        _, _, _, p2 = delong_roc_test(y, weak, good)
        self.assertAlmostEqual(p1, p2, places=12)

    def test_better_model_is_significant(self):
        y, good, weak = self._data()
        _, _, _, p = delong_roc_test(y, good, weak)
        self.assertLess(p, 0.01)

    def test_variance_agrees_with_paired_bootstrap(self):
        # Independent cross-check: DeLong's SD of (AUC_good - AUC_weak) must match a
        # paired bootstrap of the same difference (two different estimators, one truth).
        y, good, weak = self._data(seed=1, n=300)
        aa, ab, z, _ = delong_roc_test(y, good, weak)
        delong_sd = abs(aa - ab) / abs(z)

        def diff(idx):
            yi = y[idx]
            if yi.sum() == 0 or yi.sum() == yi.size:
                return float("nan")
            return roc_auc(yi, good[idx]) - roc_auc(yi, weak[idx])

        rng = np.random.default_rng(0)
        n = y.size
        boots = np.array([diff(rng.integers(0, n, n)) for _ in range(3000)])
        boot_sd = np.nanstd(boots, ddof=1)
        # Agreement within 25% (bootstrap is noisy) is strong evidence the analytic
        # DeLong variance is correct.
        self.assertLess(abs(delong_sd - boot_sd) / boot_sd, 0.25)


if __name__ == "__main__":
    unittest.main(verbosity=2)
