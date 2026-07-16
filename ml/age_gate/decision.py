#!/usr/bin/env python3
"""
DreamCodeVR+ voice age-gate — the PURE, SYSTEM-CRITICAL decision layer.

This is the safety-critical heart of the age gate and is intentionally tiny,
pure (no I/O, no model, no randomness), and heavily tested. It takes a stream of
per-utterance CHILD probabilities across a SESSION and produces a coarse safety
BAND plus a calibrated confidence — never a precise age.

Non-negotiable safety policy (from the research plan, §4/§7):

  1. Aggregate across the session, not one clip  -> robust to a single noisy
     utterance (we use the running MEDIAN by default).
  2. FAIL SAFE FOR THE CHILD. If we are not confident, or the aggregate falls in
     the ambiguous "Challenge-25" buffer band, we return band == "unknown", which
     the surrounding system MUST treat as the strictest CHILD profile. We never
     grant the permissive ADULT profile on a shaky guess, and we never hard-block
     an adult either — "unknown" simply applies child-safe limits and escalates.
  3. Coarse bands only: child / teen / adult / unknown. No numeric age ever
     leaves this module.
  4. ABSTAIN -> ESCALATE. When we abstain (unknown), we raise `escalate` so the
     platform can fall back to the authoritative account age flag / parental
     consent instead of trusting the ML guess.

Probability convention: p is P(speaker is a CHILD, <13). p high -> child.
"""

import numpy as np

# Bands emitted by the gate.
BAND_CHILD = "child"
BAND_TEEN = "teen"
BAND_ADULT = "adult"
BAND_UNKNOWN = "unknown"

# Which bands are treated with child-safe (strict) limits by the wider system.
# "unknown" and "teen" both fall to restricted handling; only a CONFIDENT adult
# gets the permissive profile.
_STRICT_PROFILE = {
    BAND_CHILD: BAND_CHILD,
    BAND_UNKNOWN: BAND_CHILD,   # fail-safe: unknown is treated AS a child
    BAND_TEEN: BAND_TEEN,       # restricted (minor), between child and adult
    BAND_ADULT: BAND_ADULT,
}


class AgeGate:
    """Session-level aggregator + fail-safe band decision.

    Parameters
    ----------
    conf_threshold : float
        Minimum probability-of-the-decided-class required to leave the
        "unknown" fail-safe band. Below this we abstain and escalate.
    min_utterances : int
        Minimum number of observed utterances before we are willing to emit
        anything other than "unknown".
    teen_buffer : float
        Challenge-25 style buffer on the ADULT side. When the aggregate says
        "probably adult" but P(child) still sits within `teen_buffer` of the
        confident-adult line, we down-grade the verdict to "teen" (a minor, so
        still restricted) rather than granting the full adult profile.
    aggregator : str
        "median" (robust, default) or "mean" running aggregate of P(child).
    """

    def __init__(self, conf_threshold=0.70, min_utterances=3,
                 teen_buffer=0.15, aggregator="median"):
        if not (0.5 < conf_threshold < 1.0):
            raise ValueError("conf_threshold must be in (0.5, 1.0)")
        if aggregator not in ("median", "mean"):
            raise ValueError("aggregator must be 'median' or 'mean'")
        self.conf_threshold = float(conf_threshold)
        self.min_utterances = int(min_utterances)
        self.teen_buffer = float(teen_buffer)
        self.aggregator = aggregator
        self._probs = []  # per-utterance P(child)

    # ------------------------------------------------------------------ #
    # Session state
    # ------------------------------------------------------------------ #
    def reset(self):
        """Forget the session (called per new speaker / session boundary)."""
        self._probs = []
        return self

    def observe(self, child_prob):
        """Record one utterance's P(child). Clipped to [0,1]; NaN ignored."""
        p = float(child_prob)
        if not np.isfinite(p):
            return self
        self._probs.append(min(1.0, max(0.0, p)))
        return self

    def observe_many(self, child_probs):
        for p in np.asarray(child_probs, dtype=float).reshape(-1):
            self.observe(p)
        return self

    @property
    def n_utterances(self):
        return len(self._probs)

    def aggregate(self):
        """Current aggregated P(child) over the session (0.5 if empty)."""
        if not self._probs:
            return 0.5
        arr = np.asarray(self._probs, dtype=np.float64)
        return float(np.median(arr) if self.aggregator == "median" else np.mean(arr))

    # ------------------------------------------------------------------ #
    # The decision
    # ------------------------------------------------------------------ #
    def decide(self, child_prob=None):
        """Return the fail-safe decision for the session so far.

        Optionally pass a final `child_prob` to observe before deciding.

        Returns a dict:
            band            : "child" | "teen" | "adult" | "unknown"
            confidence      : probability of the decided class (0.5..1.0)
            effective_profile: safety profile the system MUST apply
                               ("child" for unknown -> strictest)
            abstain         : True when we declined to commit (band == unknown)
            escalate        : True when the platform should re-verify age /
                              request parental consent
            n_utterances    : how many utterances informed this decision
            p_child         : aggregated P(child) (diagnostics only, never an age)
        """
        if child_prob is not None:
            self.observe(child_prob)

        p = self.aggregate()
        n = self.n_utterances
        # Confidence is the probability of whichever class we would pick.
        conf = max(p, 1.0 - p)

        def result(band, abstain, escalate):
            return {
                "band": band,
                "confidence": float(conf),
                "effective_profile": _STRICT_PROFILE[band],
                "abstain": bool(abstain),
                "escalate": bool(escalate),
                "n_utterances": int(n),
                "p_child": float(p),
            }

        # (a) Not enough evidence yet -> abstain, fail safe to child, escalate.
        if n < self.min_utterances:
            return result(BAND_UNKNOWN, abstain=True, escalate=True)

        # (b) Ambiguous / low-confidence band -> abstain, fail safe to child.
        if conf < self.conf_threshold:
            return result(BAND_UNKNOWN, abstain=True, escalate=True)

        # (c) Confident CHILD.
        if p >= self.conf_threshold:
            return result(BAND_CHILD, abstain=False, escalate=False)

        # (d) Confident that the speaker is NOT a child (p <= 1 - conf_threshold).
        #     Apply the Challenge-25 buffer: if P(child) is still within
        #     `teen_buffer` of the confident-adult line, call it "teen" (a minor,
        #     still restricted) instead of granting the full adult profile.
        adult_line = 1.0 - self.conf_threshold
        if p > adult_line - self.teen_buffer:
            return result(BAND_TEEN, abstain=False, escalate=True)
        return result(BAND_ADULT, abstain=False, escalate=False)


def decide_once(child_probs, **kwargs):
    """Convenience: build a gate, observe a whole session, return decide()."""
    gate = AgeGate(**kwargs)
    gate.observe_many(child_probs)
    return gate.decide()
