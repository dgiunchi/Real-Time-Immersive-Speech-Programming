#!/usr/bin/env python3
"""Conformal risk control for the age gate (Tier-2 T2.1 — the flagship guarantee).

WHAT IT BUYS: a DISTRIBUTION-FREE, finite-sample guarantee on the one error that matters
for child safety — a true MINOR served the permissive ADULT profile. Instead of a hand-
picked 0.70 confidence cut, we CALIBRATE a threshold on held-out data so that, for an
exchangeable future minor,

        P( served the adult profile | the user is really a minor )  <=  alpha,

with the bound holding in finite samples and for ANY score distribution (split-conformal;
Vovk et al. 2005; Angelopoulos & Bates 2023). The decision is: serve the adult (permissive)
profile iff the adult-score s(x) EXCEEDS the conformal threshold tau; otherwise protect the
user as a minor (or escalate to parental re-verification). Higher alpha = serve adults more
readily; lower alpha = protect more aggressively. tau is derived, not typed.

MONDRIAN (group-conditional) variant: calibrate tau SEPARATELY per subgroup (accent / gender
/ age-boundary), so the <= alpha guarantee holds WITHIN each subgroup — not just marginally.
This is the fairness fix: a purely marginal threshold can silently under-protect a harder
subgroup (e.g. accented or deeper-voiced children) even while the overall rate looks fine.
This module DEMONSTRATES exactly that gap and that Mondrian closes it.

GUARANTEE, HONESTLY (state this in the viva): the bound is MARGINAL — it holds in
expectation over the joint draw of calibration + test data. For one fixed deployed
calibration set the realised rate fluctuates around alpha (standard split-conformal
behaviour); a per-deployment certainty would need a training-conditional / PAC-style bound.
It holds for ANY score distribution — ties only make the strict-'>' decision MORE
conservative. With too few calibration minors in a subgroup the threshold becomes +inf
(protect everyone) — the safe direction, but over-restrictive, so each subgroup needs
enough calibration data.

The conformal machinery is MODEL-AGNOSTIC (it consumes adult-scores), so the guarantee is
tested directly. numpy + stdlib only, deterministic given a seed. Synthetic scores here are
a methods demonstrator; the real-corpus hookup is Tier-1 T1.1.
"""

import math

import numpy as np


def conformal_threshold(cal_minor_scores, alpha):
    """Split-conformal threshold tau on the adult-score.

    Given the adult-scores of a CALIBRATION set of true minors, returns tau such that, for
    an exchangeable future minor, P(score > tau) <= alpha. Serve the adult profile iff
    score > tau. tau is the k-th smallest calibration score with k = ceil((1-alpha)(n+1));
    if k > n (alpha too small for this calibration size) tau = +inf, i.e. NEVER serve adult
    — the fail-safe direction.
    """
    s = np.sort(np.asarray(cal_minor_scores, dtype=np.float64))
    n = s.size
    if n == 0:
        return float("inf")
    k = math.ceil((1.0 - alpha) * (n + 1))  # 1-indexed conformal order statistic
    if k > n:
        return float("inf")
    return float(s[k - 1])


class MondrianConformal:
    """Group-conditional split conformal. Calibrates a marginal threshold AND one per
    subgroup, so the child-served-adult rate is bounded by alpha within each subgroup."""

    def __init__(self, alpha):
        if not (0.0 < alpha < 1.0):
            raise ValueError("alpha must be in (0, 1)")
        self.alpha = float(alpha)
        self.tau_ = None
        self.tau_by_group_ = {}

    def calibrate(self, minor_scores, minor_groups):
        minor_scores = np.asarray(minor_scores, dtype=np.float64)
        minor_groups = np.asarray(minor_groups)
        self.tau_ = conformal_threshold(minor_scores, self.alpha)
        self.tau_by_group_ = {
            g: conformal_threshold(minor_scores[minor_groups == g], self.alpha)
            for g in np.unique(minor_groups)
        }
        return self

    def serve_adult_marginal(self, scores):
        """Serve the adult profile iff score > the single marginal threshold."""
        return np.asarray(scores, dtype=np.float64) > self.tau_

    def serve_adult_mondrian(self, scores, groups):
        """Serve the adult profile iff score > that sample's SUBGROUP threshold.
        Unknown groups fall back to +inf (protect) — the fail-safe direction."""
        groups = np.asarray(groups)
        tau = np.array([self.tau_by_group_.get(g, float("inf")) for g in groups])
        return np.asarray(scores, dtype=np.float64) > tau


# --------------------------------------------------------------------------------------- #
# Synthetic adult-scores with a HARD subgroup, to demonstrate the guarantee + the fairness
# gap. Group "B" minors are shifted toward adult scores (they look older to the model).
# --------------------------------------------------------------------------------------- #
def make_scores_synthetic(n_per_group=1500, seed=0, hard_shift=0.28):
    """Return (scores, is_minor, groups). Two subgroups A (easy) and B (hard): B's minors
    have higher adult-scores (more overlap with adults), so a marginal threshold set on the
    pooled minors under-protects B. Adult-scores are clipped to [0, 1]."""
    rng = np.random.default_rng(seed)
    rows = []
    for g, shift in (("A", 0.0), ("B", hard_shift)):
        n_minor = n_per_group // 2
        n_adult = n_per_group - n_minor
        minor = rng.normal(0.30 + shift, 0.13, n_minor)   # B minors shifted up
        adult = rng.normal(0.80, 0.12, n_adult)
        s = np.clip(np.concatenate([minor, adult]), 0.0, 1.0)
        y = np.concatenate([np.ones(n_minor), np.zeros(n_adult)]).astype(int)  # 1 = minor
        grp = np.array([g] * n_per_group)
        rows.append((s, y, grp))
    scores = np.concatenate([r[0] for r in rows])
    is_minor = np.concatenate([r[1] for r in rows])
    groups = np.concatenate([r[2] for r in rows])
    perm = rng.permutation(scores.size)
    return scores[perm], is_minor[perm], groups[perm]


def _minor_served_adult_rate(served_adult, is_minor, groups=None, group=None):
    """Realised P(served adult AND is a minor) / P(is a minor) — over all, or one group."""
    m = is_minor == 1
    if group is not None:
        m = m & (groups == group)
    return float(np.mean(served_adult[m])) if m.sum() else float("nan")


def coverage_eval(alpha=0.1, n_trials=200, n_per_group=1500, seed=0):
    """Empirically check the guarantee over many fresh calibration/test splits:
      * MARGINAL conformal holds the OVERALL minor-served-adult rate <= alpha, but can
        VIOLATE alpha for the hard subgroup B;
      * MONDRIAN conformal holds <= alpha within BOTH subgroups.
    Returns mean realised rates. This is the experiment the flagship result reports."""
    rng = np.random.default_rng(seed)
    acc = {"marginal_overall": [], "marginal_A": [], "marginal_B": [],
           "mondrian_overall": [], "mondrian_A": [], "mondrian_B": []}
    for t in range(n_trials):
        s, y, g = make_scores_synthetic(n_per_group, seed=int(rng.integers(0, 2**31)))
        n = s.size
        idx = rng.permutation(n)
        half = n // 2
        cal, te = idx[:half], idx[half:]
        cal_minor = y[cal] == 1
        mc = MondrianConformal(alpha).calibrate(s[cal][cal_minor], g[cal][cal_minor])

        served_m = mc.serve_adult_marginal(s[te])
        served_g = mc.serve_adult_mondrian(s[te], g[te])
        acc["marginal_overall"].append(_minor_served_adult_rate(served_m, y[te]))
        acc["marginal_A"].append(_minor_served_adult_rate(served_m, y[te], g[te], "A"))
        acc["marginal_B"].append(_minor_served_adult_rate(served_m, y[te], g[te], "B"))
        acc["mondrian_overall"].append(_minor_served_adult_rate(served_g, y[te]))
        acc["mondrian_A"].append(_minor_served_adult_rate(served_g, y[te], g[te], "A"))
        acc["mondrian_B"].append(_minor_served_adult_rate(served_g, y[te], g[te], "B"))
    return {"alpha": alpha, "n_trials": n_trials,
            **{k: float(np.nanmean(v)) for k, v in acc.items()}}


if __name__ == "__main__":
    for alpha in (0.05, 0.1, 0.2):
        r = coverage_eval(alpha=alpha, n_trials=200)
        print(f"=== alpha = {alpha:.2f}  (target: minor-served-adult rate <= {alpha:.2f}) ===")
        print(f"  MARGINAL conformal : overall {r['marginal_overall']:.3f}   "
              f"group-A {r['marginal_A']:.3f}   group-B(hard) {r['marginal_B']:.3f} "
              f"{'  <-- VIOLATES alpha for B' if r['marginal_B'] > alpha + 0.01 else ''}")
        print(f"  MONDRIAN  conformal: overall {r['mondrian_overall']:.3f}   "
              f"group-A {r['mondrian_A']:.3f}   group-B(hard) {r['mondrian_B']:.3f} "
              f"{'  (B now protected)' if r['mondrian_B'] <= alpha + 0.01 else ''}")
    print("\nMarginal bounds the OVERALL rate but under-protects the hard subgroup; "
          "Mondrian (group-conditional) restores the <= alpha guarantee within each group.")
