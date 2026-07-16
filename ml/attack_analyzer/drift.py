#!/usr/bin/env python3
"""
ADWIN-style windowed drift detector (pure Python + numpy math).

WHY (RESEARCH_AND_ML_PLAN.md §5, "wrap with ADWIN/DDM drift detection ... so it doesn't
decay"): the benign baseline drifts — new creative APIs, new phrasings, seasonal content.
If the anomaly detector's threshold is frozen it slowly rots (rising false positives, or
blindness as benign error creeps up). A drift detector watches the STREAM of reconstruction
errors and fires when the recent window's distribution has genuinely shifted, so the
analyzer can re-fit / re-threshold (SSF-style continual update) instead of decaying.

ALGORITHM — ADWIN2 (Bifet & Gavaldà, 2007), the real thing, in miniature:
  Keep a window W of the most recent values. After each new value, look for a split of W
  into an OLDER sub-window W0 and a NEWER sub-window W1 whose means differ by more than a
  statistical cut `eps_cut`. If such a split exists, the older data is stale -> DROP W0
  (shrink from the front) and report drift. `eps_cut` uses the variance-aware Hoeffding
  bound so it works on real-valued streams (reconstruction errors), not just [0,1] data:

      m         = 1 / (1/n0 + 1/n1)          # harmonic size of the two sub-windows
      delta'    = delta / n                   # Bonferroni over the n candidate cuts
      eps_cut   = sqrt( (2/m) * var_W * ln(2/delta') ) + (2/(3m)) * ln(2/delta')

  Small `delta` => rare false alarms on stationary data; a true mean shift eventually
  exceeds the bound at some split and fires. The window bounds itself (`max_window`), and
  `min_sub` keeps sub-windows large enough for the bound to be meaningful.

Running mean/variance of the current window are exposed (`mean`, `variance`, `width`) for
the caller to re-threshold on. Deterministic: no randomness, no time — same stream in,
same drift decisions out.
"""
from __future__ import annotations

import math


class AdwinDriftDetector:
    """Adaptive windowing drift detector for a stream of real-valued statistics.

    Usage:
        d = AdwinDriftDetector(delta=0.002)
        for x in stream:
            if d.update(x):        # True on the step a change is detected
                recalibrate(...)   # e.g. re-fit the anomaly threshold on the new regime
    """

    def __init__(self, delta: float = 0.002, max_window: int = 400,
                 min_sub: int = 8):
        if not (0.0 < delta < 1.0):
            raise ValueError("delta must be in (0, 1)")
        self.delta = float(delta)
        self.max_window = int(max_window)
        self.min_sub = int(min_sub)
        self._w = []                 # current window of raw values (oldest -> newest)
        self.n_detections = 0
        self.total_seen = 0
        self.last_change_at = None   # index (in total_seen) of the most recent change

    # -- running stats over the current window -----------------------------------------
    @property
    def width(self) -> int:
        return len(self._w)

    @property
    def mean(self) -> float:
        return sum(self._w) / len(self._w) if self._w else 0.0

    @property
    def variance(self) -> float:
        n = len(self._w)
        if n < 2:
            return 0.0
        mu = self.mean
        return sum((v - mu) ** 2 for v in self._w) / n

    # -- the update step ----------------------------------------------------------------
    def update(self, value: float) -> bool:
        """Add one observation; return True iff a distribution change was detected now."""
        self.total_seen += 1
        self._w.append(float(value))
        if len(self._w) > self.max_window:
            self._w.pop(0)           # keep the window bounded (drop stalest)

        changed = False
        # Repeatedly shrink from the front while a significant split exists. ADWIN drops
        # ALL stale prefix data, not just one element, so re-check after each cut.
        while self._shrink_once():
            changed = True

        if changed:
            self.n_detections += 1
            self.last_change_at = self.total_seen
        return changed

    def _shrink_once(self) -> bool:
        """Find the front-most significant split; if any, drop the older prefix."""
        n = len(self._w)
        if n < 2 * self.min_sub:
            return False

        var_w = self.variance
        ln_term = math.log(2.0 / (self.delta / n))

        # prefix sums for O(n) mean-of-subwindow evaluation
        prefix = [0.0] * (n + 1)
        for i, v in enumerate(self._w):
            prefix[i + 1] = prefix[i] + v
        total = prefix[n]

        for cut in range(self.min_sub, n - self.min_sub + 1):
            n0 = cut
            n1 = n - cut
            mean0 = prefix[cut] / n0
            mean1 = (total - prefix[cut]) / n1
            m = 1.0 / (1.0 / n0 + 1.0 / n1)          # harmonic sub-window size
            eps_cut = math.sqrt((2.0 / m) * var_w * ln_term) + (2.0 / (3.0 * m)) * ln_term
            if abs(mean0 - mean1) > eps_cut:
                # older prefix [0:cut) is a different regime -> discard it
                self._w = self._w[cut:]
                return True
        return False


def detect_stream(values, delta: float = 0.002, max_window: int = 400,
                  min_sub: int = 8) -> dict:
    """Convenience: run the detector over a full sequence; return change indices + count.

    Returns {"change_points": [i, ...], "n_changes": int} where each i is the stream index
    (0-based) at which a change was reported.
    """
    d = AdwinDriftDetector(delta=delta, max_window=max_window, min_sub=min_sub)
    change_points = []
    for i, x in enumerate(values):
        if d.update(x):
            change_points.append(i)
    return {"change_points": change_points, "n_changes": len(change_points),
            "final_width": d.width, "final_mean": d.mean}


if __name__ == "__main__":
    import numpy as np

    rng = np.random.default_rng(0)
    # Stationary stream: benign reconstruction error hovering around a fixed mean.
    stationary = rng.normal(10.0, 2.0, size=600)
    # Shifted stream: an abrupt regime change at t=300 (attacks start slipping in / the
    # benign baseline moves) — mean jumps 10 -> 18.
    shifted = np.concatenate([rng.normal(10.0, 2.0, size=300),
                              rng.normal(18.0, 2.0, size=300)])

    s = detect_stream(stationary)
    d = detect_stream(shifted)
    print("=== ADWIN-style drift demo ===")
    print(f"stationary stream : {s['n_changes']} change(s) detected "
          f"(want ~0) -> {s['change_points'][:5]}")
    print(f"shifted stream    : {d['n_changes']} change(s) detected "
          f"(want >=1, near t=300) -> {d['change_points'][:5]}")
