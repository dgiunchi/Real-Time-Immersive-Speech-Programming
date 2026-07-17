#!/usr/bin/env python3
"""Tests for ml/age_gate/ordinal.py — ordinal regression + cost-sensitive decision theory.

Reference values (hand-computed) are asserted for the decision-theory pieces (Bayes
action, Chow reject, net benefit, prior re-weighting, EM prior recovery), plus structural
properties for CORAL (rank consistency, valid probabilities) and the ordinal-vs-nominal
comparison.
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

import ordinal  # noqa: E402
from ordinal import (  # noqa: E402
    MulticlassLogistic,
    OrdinalCoral,
    adjust_for_prior,
    cost_sensitive_decision,
    estimate_test_prior_em,
    make_ordered_synthetic,
    net_benefit,
    net_benefit_treat_all,
    ordinal_mae,
)


class Coral(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        X, y, _ = make_ordered_synthetic(n=3000, seed=0)
        cls.Xtr, cls.ytr, cls.Xte, cls.yte = X[:2000], y[:2000], X[2000:], y[2000:]
        cls.coral = OrdinalCoral(3).fit(cls.Xtr, cls.ytr)

    def test_cumulative_is_rank_consistent(self):
        # P(y>0) >= P(y>1) for EVERY sample (the guarantee CORAL exists to provide).
        Pc = self.coral.cumulative_proba(self.Xte)
        self.assertTrue(np.all(Pc[:, 0] >= Pc[:, 1] - 1e-12))

    def test_predict_proba_is_a_valid_distribution(self):
        P = self.coral.predict_proba(self.Xte)
        self.assertTrue(np.all(P >= -1e-12))
        np.testing.assert_allclose(P.sum(axis=1), 1.0, atol=1e-9)

    def test_predict_equals_thresholds_passed(self):
        Pc = self.coral.cumulative_proba(self.Xte)
        expected = (Pc > 0.5).sum(axis=1)
        np.testing.assert_array_equal(self.coral.predict(self.Xte), expected)

    def test_learns_the_ordered_signal(self):
        acc = float(np.mean(self.coral.predict(self.Xte) == self.yte))
        self.assertGreater(acc, 0.65)  # 3 overlapping bands; well above chance (0.33)

    def test_ordinal_makes_no_more_far_errors_than_nominal(self):
        # Ordinal's point: fewer 2-band (child<->adult) errors. Assert CORAL is at least as
        # good as the nominal softmax on band-MAE and far-error rate (competitive-or-better).
        nom = MulticlassLogistic(3).fit(self.Xtr, self.ytr)
        yc, yn = self.coral.predict(self.Xte), nom.predict(self.Xte)
        self.assertLessEqual(ordinal_mae(self.yte, yc), ordinal_mae(self.yte, yn) + 0.02)
        far_c = np.mean(np.abs(self.yte - yc) == 2)
        far_n = np.mean(np.abs(self.yte - yn) == 2)
        self.assertLessEqual(far_c, far_n + 0.01)

    def test_deterministic(self):
        c2 = OrdinalCoral(3).fit(self.Xtr, self.ytr)
        np.testing.assert_array_equal(self.coral.predict(self.Xte), c2.predict(self.Xte))


class CostSensitiveDecision(unittest.TestCase):
    def test_symmetric_cost_is_argmax(self):
        # C = 1 - I  ->  expected cost of action a = 1 - P(a)  ->  argmin == argmax P.
        P = np.array([[0.2, 0.5, 0.3], [0.7, 0.1, 0.2]])
        C = 1.0 - np.eye(3)
        actions, _ = cost_sensitive_decision(P, C)
        np.testing.assert_array_equal(actions, P.argmax(axis=1))

    def test_asymmetric_cost_derives_a_protective_threshold(self):
        # 2 classes (0 minor, 1 adult). Serving a minor the adult profile costs 8; the
        # reverse costs 1. A sample with P(minor)=0.2 must STILL be treated as a minor,
        # because expected-cost(adult)=0.2*8=1.6 > expected-cost(minor)=0.8*1=0.8.
        P = np.array([[0.2, 0.8]])
        C = np.array([[0.0, 8.0], [1.0, 0.0]])
        actions, chosen = cost_sensitive_decision(P, C)
        self.assertEqual(actions[0], 0)                 # protective, despite P(minor)<0.5
        self.assertAlmostEqual(chosen[0], 0.8, places=12)

    def test_chow_reject_abstains_when_uncertain(self):
        P = np.array([[0.5, 0.5]])
        C = 1.0 - np.eye(2)                              # min expected cost = 0.5
        actions, _ = cost_sensitive_decision(P, C, reject_cost=0.4)
        self.assertEqual(actions[0], -1)                # 0.5 > 0.4 -> abstain
        actions2, _ = cost_sensitive_decision(P, C, reject_cost=0.6)
        self.assertNotEqual(actions2[0], -1)            # 0.5 < 0.6 -> decide


class DecisionCurve(unittest.TestCase):
    def test_perfect_classifier_net_benefit_is_prevalence(self):
        # A perfect ranker flagged at 0.5 -> TP=all positives, FP=0 -> NB = prevalence.
        y = np.array([0, 0, 0, 1, 1])
        nb = net_benefit(y, y.astype(float), 0.5)
        self.assertAlmostEqual(nb, 2.0 / 5.0, places=12)

    def test_treat_all_reference_value(self):
        # NB(treat all) at pt = prevalence - (1-prev)*pt/(1-pt).
        y = np.array([0, 0, 0, 1, 1])          # prevalence 0.4
        self.assertAlmostEqual(net_benefit_treat_all(y, 0.0), 0.4, places=12)
        self.assertAlmostEqual(net_benefit_treat_all(y, 0.5), 0.4 - 0.6 * 1.0, places=12)

    def test_pt_one_with_false_positive_is_minus_inf(self):
        # Adversarial-review fix: at pt=1 the FP odds -> inf, so any false positive gives
        # NB = -inf, not an over-optimistic finite value.
        y = np.array([0, 0, 1, 1])
        self.assertEqual(net_benefit(y, np.array([1.0, 0.2, 1.0, 1.0]), 1.0), float("-inf"))
        self.assertEqual(net_benefit(y, np.array([0.2, 0.2, 1.0, 1.0]), 1.0), 0.5)  # no FP
        self.assertEqual(net_benefit_treat_all(y, 1.0), float("-inf"))  # flags the negatives


class PriorShift(unittest.TestCase):
    def test_no_shift_is_identity(self):
        P = np.array([[0.3, 0.7], [0.6, 0.4]])
        prior = np.array([0.5, 0.5])
        np.testing.assert_allclose(adjust_for_prior(P, prior, prior), P, atol=1e-12)

    def test_reweight_reference_value(self):
        # P=[0.5,0.5], train=[0.5,0.5], test=[0.9,0.1] -> r=[1.8,0.2] -> [0.9,0.1].
        P = np.array([[0.5, 0.5]])
        adj = adjust_for_prior(P, [0.5, 0.5], [0.9, 0.1])
        np.testing.assert_allclose(adj[0], [0.9, 0.1], atol=1e-12)

    def test_em_recovers_a_known_shifted_prior(self):
        # Confident (near one-hot) posteriors drawn with a KNOWN prior -> EM recovers it.
        rng = np.random.default_rng(0)
        y = (rng.random(4000) < 0.8).astype(int)        # true prior P(class1)=0.8
        P = np.where(y[:, None] == np.array([0, 1])[None, :], 0.97, 0.03)
        est = estimate_test_prior_em(P, train_prior=[0.5, 0.5])
        self.assertAlmostEqual(est[1], 0.8, delta=0.03)


class Metrics(unittest.TestCase):
    def test_ordinal_mae_reference(self):
        self.assertAlmostEqual(ordinal_mae([0, 1, 2], [0, 0, 2]), 1.0 / 3.0, places=12)
        self.assertEqual(ordinal_mae([0, 1, 2], [2, 1, 0]), (2 + 0 + 2) / 3.0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
