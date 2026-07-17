#!/usr/bin/env python3
"""Tests for ml/age_gate/conformal.py — the distribution-free child-safety guarantee.

The load-bearing test is COVERAGE: over many fresh splits the realised minor-served-adult
rate must stay <= alpha (marginally, and — for Mondrian — within each subgroup), and the
marginal-only threshold must be shown to UNDER-PROTECT the hard subgroup. The threshold
formula is also checked against hand-computed conformal order statistics.
"""

import math
import os
import sys
import unittest

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
_AGE_GATE_DIR = os.path.dirname(_HERE)
if _AGE_GATE_DIR not in sys.path:
    sys.path.insert(0, _AGE_GATE_DIR)

import conformal  # noqa: E402
from conformal import (  # noqa: E402
    MondrianConformal,
    conformal_threshold,
    coverage_eval,
    make_scores_synthetic,
)


class Threshold(unittest.TestCase):
    def test_reference_order_statistic(self):
        cal = np.array([0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0])  # n=10
        # alpha=0.2 -> k = ceil(0.8*11) = ceil(8.8) = 9 -> 9th smallest = 0.9
        self.assertAlmostEqual(conformal_threshold(cal, 0.2), 0.9, places=12)
        # alpha=0.1 -> k = ceil(0.9*11) = 10 -> 10th smallest = 1.0
        self.assertAlmostEqual(conformal_threshold(cal, 0.1), 1.0, places=12)

    def test_infinite_when_alpha_too_small_for_n(self):
        cal = np.arange(1, 11) / 10.0  # n=10
        # alpha=0.05 -> k = ceil(0.95*11) = ceil(10.45) = 11 > 10 -> +inf (protect all)
        self.assertEqual(conformal_threshold(cal, 0.05), float("inf"))

    def test_empty_calibration_protects(self):
        self.assertEqual(conformal_threshold([], 0.1), float("inf"))

    def test_monotone_in_alpha(self):
        rng = np.random.default_rng(0)
        cal = rng.random(200)
        taus = [conformal_threshold(cal, a) for a in (0.05, 0.1, 0.2, 0.4)]
        # larger alpha -> lower threshold (serve adults more readily)
        self.assertTrue(all(taus[i] >= taus[i + 1] for i in range(len(taus) - 1)))


class Decision(unittest.TestCase):
    def test_serve_adult_is_strict_and_group_fallback_protects(self):
        mc = MondrianConformal(0.1)
        mc.tau_ = 0.5
        mc.tau_by_group_ = {"A": 0.4}
        np.testing.assert_array_equal(
            mc.serve_adult_marginal([0.5, 0.51, 0.49]), [False, True, False])
        # unknown group "Z" falls back to +inf -> never served adult (fail-safe).
        served = mc.serve_adult_mondrian([0.99, 0.99], ["A", "Z"])
        np.testing.assert_array_equal(served, [True, False])

    def test_alpha_must_be_valid(self):
        with self.assertRaises(ValueError):
            MondrianConformal(0.0)
        with self.assertRaises(ValueError):
            MondrianConformal(1.0)

    def test_mondrian_serve_is_strict_at_group_threshold(self):
        # A score EXACTLY equal to a group's threshold must NOT be served adult (strict >),
        # so the per-group bound holds under ties. (Locks the strict '>' on the Mondrian
        # path — a '>=' regression would silently pass without this.)
        mc = MondrianConformal(0.1)
        mc.tau_by_group_ = {"A": 0.5}
        served = mc.serve_adult_mondrian([0.5, 0.5001], ["A", "A"])
        np.testing.assert_array_equal(served, [False, True])


class CoverageUnderTies(unittest.TestCase):
    def test_guarantee_holds_on_a_discrete_tied_distribution(self):
        # The bound is claimed for ANY score distribution; verify it on a DISCRETE, heavily
        # tied distribution (integer scores) where a non-strict '>=' would break it.
        rng = np.random.default_rng(0)
        alpha = 0.1
        rates = []
        for _ in range(200):
            cal_minor = rng.integers(0, 4, 400).astype(float)   # minors: 0..3, many ties
            te_minor = rng.integers(0, 4, 400).astype(float)
            tau = conformal_threshold(cal_minor, alpha)
            rates.append(float(np.mean(te_minor > tau)))
        self.assertLessEqual(float(np.mean(rates)), alpha + 0.02)


class CoverageGuarantee(unittest.TestCase):
    """The flagship property: the finite-sample distribution-free bound actually holds."""

    @classmethod
    def setUpClass(cls):
        cls.alpha = 0.1
        cls.r = coverage_eval(alpha=cls.alpha, n_trials=150, seed=0)

    def test_marginal_bounds_the_overall_rate(self):
        self.assertLessEqual(self.r["marginal_overall"], self.alpha + 0.02)

    def test_marginal_underprotects_the_hard_subgroup(self):
        # The fairness finding: a marginal-only threshold lets the hard subgroup B exceed
        # alpha (here ~2x), even though the overall rate looks fine.
        self.assertGreater(self.r["marginal_B"], self.alpha + 0.05)

    def test_mondrian_restores_the_guarantee_within_each_group(self):
        self.assertLessEqual(self.r["mondrian_A"], self.alpha + 0.025)
        self.assertLessEqual(self.r["mondrian_B"], self.alpha + 0.025)
        self.assertLessEqual(self.r["mondrian_overall"], self.alpha + 0.02)

    def test_coverage_holds_at_multiple_alphas(self):
        for a in (0.05, 0.2):
            r = coverage_eval(alpha=a, n_trials=120, seed=1)
            self.assertLessEqual(r["marginal_overall"], a + 0.02)
            self.assertLessEqual(r["mondrian_B"], a + 0.03)


class Synthetic(unittest.TestCase):
    def test_hard_group_scores_higher_for_minors(self):
        s, y, g = make_scores_synthetic(2000, seed=0)
        a_minor = s[(y == 1) & (g == "A")].mean()
        b_minor = s[(y == 1) & (g == "B")].mean()
        self.assertGreater(b_minor, a_minor)  # B minors look more adult (harder)

    def test_deterministic(self):
        r1 = coverage_eval(alpha=0.1, n_trials=40, seed=3)
        r2 = coverage_eval(alpha=0.1, n_trials=40, seed=3)
        self.assertEqual(r1, r2)


if __name__ == "__main__":
    unittest.main(verbosity=2)
