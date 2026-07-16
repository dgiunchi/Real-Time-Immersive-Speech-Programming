#!/usr/bin/env python3
"""
DreamCodeVR+ voice age-gate — evaluation harness.

Loads (or trains) the small classifier and reports the metrics that matter for a
child-safety gate on a held-out synthetic test set:

  * Accuracy                          — overall child/adult separation.
  * ECE (Expected Calibration Error)  — are the confidences honest?
  * FALSE-ADULT RATE (child -> adult) — THE child-safety-critical error: a child
    wrongly waved through as an adult. Reported overall AND per child subgroup
    (the low-pitch tail is where this concentrates).
  * FALSE-CHILD RATE (adult -> child) — over-restriction of adults (a usability /
    fairness cost, not a safety failure).

It also demonstrates the `decision.AgeGate` session-level fail-safe: aggregating
several utterances per pseudo-speaker and failing safe on low confidence drives
the child->adult leakage down (at the expected cost of more adults temporarily
placed on the strict profile), which is exactly the intended trade.

Deterministic. Run: python3 ml/age_gate/evaluate.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np

from model import AgeClassifier, expected_calibration_error
from decision import AgeGate, BAND_ADULT
import train as trainmod


def _rates(y_true, y_pred):
    """Return (false_adult_rate, false_child_rate) given hard labels."""
    y_true = np.asarray(y_true)
    y_pred = np.asarray(y_pred)
    child = y_true == 1
    adult = y_true == 0
    # child predicted adult (0)
    fa = float(np.mean(y_pred[child] == 0)) if child.any() else 0.0
    # adult predicted child (1)
    fc = float(np.mean(y_pred[adult] == 1)) if adult.any() else 0.0
    return fa, fc


def evaluate_classifier(model, Xte, yte, gte):
    p_child = model.predict_child_proba(Xte)
    y_pred = (p_child >= 0.5).astype(int)
    acc = float(np.mean(y_pred == yte))
    ece = expected_calibration_error(p_child, yte)
    fa, fc = _rates(yte, y_pred)

    print("=" * 66)
    print(" DreamCodeVR+ voice age-gate — EVALUATION (held-out test set)")
    print("=" * 66)
    print(f" test samples          : {len(yte)}")
    print(f" accuracy              : {acc:6.3f}")
    print(f" ECE (calibrated)      : {ece:6.4f}")
    print(f" temperature           : {model.temperature_:6.3f}")
    print("-" * 66)
    print(" CHILD-SAFETY-CRITICAL errors")
    print(f"   false-ADULT rate (child passed as adult): {fa:6.3f}")
    print(f"   false-CHILD rate (adult restricted)     : {fc:6.3f}")
    print("-" * 66)
    print(" per-subgroup false-ADULT rate (children only):")
    child_mask = yte == 1
    for g in sorted(set(gte[child_mask])):
        m = child_mask & (gte == g)
        if m.any():
            r = float(np.mean(y_pred[m] == 0))
            print(f"   {g:16s}: {r:6.3f}   (n={int(m.sum())})")
    print(" per-subgroup false-CHILD rate (adults only):")
    adult_mask = yte == 0
    for g in sorted(set(gte[adult_mask])):
        m = adult_mask & (gte == g)
        if m.any():
            r = float(np.mean(y_pred[m] == 1))
            print(f"   {g:16s}: {r:6.3f}   (n={int(m.sum())})")
    print("=" * 66)
    return {"accuracy": acc, "ece": ece, "false_adult": fa, "false_child": fc}


def evaluate_session_gate(model, Xte, yte, gte, utt_per_speaker=5, seed=7):
    """Show the AgeGate session fail-safe on multi-utterance pseudo-speakers.

    We group test utterances into pseudo-speakers of the same true class, feed
    each speaker's per-utterance P(child) into the gate, and score the gate's
    effective profile: a child is SAFE unless the gate hands out the permissive
    adult profile; an adult is 'restricted' if not given the adult profile.
    """
    rng = np.random.default_rng(seed)
    p_child = model.predict_child_proba(Xte)
    results = {"child_leak": 0, "child_total": 0,
               "adult_restrict": 0, "adult_total": 0}

    for label in (1, 0):
        idx = np.where(yte == label)[0]
        rng.shuffle(idx)
        n_speakers = len(idx) // utt_per_speaker
        for s in range(n_speakers):
            chunk = idx[s * utt_per_speaker:(s + 1) * utt_per_speaker]
            gate = AgeGate()
            gate.observe_many(p_child[chunk])
            decision = gate.decide()
            profile = decision["effective_profile"]
            if label == 1:
                results["child_total"] += 1
                # A child "leaks" only if handed the permissive adult profile.
                if profile == BAND_ADULT:
                    results["child_leak"] += 1
            else:
                results["adult_total"] += 1
                if profile != BAND_ADULT:
                    results["adult_restrict"] += 1

    cl = results["child_leak"] / max(1, results["child_total"])
    ar = results["adult_restrict"] / max(1, results["adult_total"])
    print(" SESSION-LEVEL AgeGate fail-safe "
          f"({utt_per_speaker} utterances/speaker)")
    print(f"   child leaked to ADULT profile : {cl:6.3f}   "
          f"(n={results['child_total']})")
    print(f"   adult temporarily restricted  : {ar:6.3f}   "
          f"(n={results['adult_total']})")
    print("   -> the fail-safe trades adult convenience for child safety.")
    print("=" * 66)
    return {"child_leak": cl, "adult_restrict": ar}


def main():
    # Prefer a model trained by train.py; otherwise train one now so this script
    # runs standalone.
    if os.path.exists(trainmod.MODEL_PATH):
        model = AgeClassifier.load(trainmod.MODEL_PATH)
        # Build a fresh, independently-seeded held-out test set drawn from the
        # (noisier) deployment distribution the model is calibrated for.
        rng = np.random.default_rng(123)
        X, y, g = trainmod.make_synthetic_dataset(
            n=3000, rng=rng, std_scale=trainmod.DEPLOY_STD_SCALE)
        Xte, yte, gte = X, y, g
    else:
        model, _, (Xte, yte, gte) = trainmod.train_pipeline(save=True, verbose=False)

    evaluate_classifier(model, Xte, yte, gte)
    evaluate_session_gate(model, Xte, yte, gte)


if __name__ == "__main__":
    main()
