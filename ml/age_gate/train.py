#!/usr/bin/env python3
"""
DreamCodeVR+ voice age-gate — training harness + synthetic data generator.

There is no real child/adult audio corpus available in this environment, so we
ship a synthetic-but-REALISTIC generator that draws feature vectors from child
and adult distributions *in the same feature space* that `features.py` produces
(pitch, spectral centroid/rolloff, ZCR, energy — in physical-ish units), with
deliberate OVERLAP and a couple of confounding subgroups:

  * child_typical   — clearly high-pitched children.
  * child_low_pitch — older / low-pitched children (HARD: risk of passing as
                      adult; this is the child-safety-critical tail).
  * adult_typical   — clearly low-pitched adults.
  * adult_high_pitch— higher-pitched adults (e.g. some adult women; risk of
                      being flagged as child).

This lets `train.py` and `evaluate.py` run fully end-to-end here while the
metrics (accuracy, ECE, subgroup false-ADULT-rate) mean the same thing they will
on the real aGender / CMU-Kids pipeline documented in the README.

Everything is deterministic: a single np.random.Generator threads through.
Label convention: y == 1 -> CHILD, y == 0 -> ADULT.
"""

import os
import sys

# Make sibling modules importable whether run as a script or imported by tests.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np

from features import FEATURE_NAMES, FEATURE_DIM
from model import AgeClassifier, expected_calibration_error

# Where the trained + calibrated model is persisted.
MODEL_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "age_model.npz")

# We deliberately model a REALISTIC calibration hazard: the model is trained on a
# cleaner/tighter corpus (studio child recordings) than it is deployed on (noisy,
# in-the-wild audio with more overlap). A model fit on the tighter distribution is
# systematically OVER-CONFIDENT on the wider deployment distribution, which is
# exactly the miscalibration temperature scaling exists to fix. These scales
# multiply the per-feature std of the generator.
TRAIN_STD_SCALE = 0.7    # cleaner training corpus
DEPLOY_STD_SCALE = 1.2   # noisier deployment / evaluation distribution

# --------------------------------------------------------------------------- #
# Synthetic feature distributions.
# Each row aligns with features.FEATURE_NAMES. Values are (mean, std) in the
# same physical-ish units the DSP front-end emits. Children sit HIGHER on pitch,
# spectral centroid/rolloff and ZCR; energy is intentionally non-discriminative.
# --------------------------------------------------------------------------- #
#                     child(mean, std)     adult(mean, std)
_DIST = {
    "f0_mean":         ((262.0, 30.0),      (140.0, 28.0)),
    "f0_median":       ((260.0, 30.0),      (138.0, 28.0)),
    "f0_std":          (( 42.0, 12.0),      ( 30.0, 10.0)),
    "f0_p10":          ((205.0, 28.0),      (105.0, 24.0)),
    "f0_p90":          ((325.0, 34.0),      (190.0, 30.0)),
    "f0_range":        ((120.0, 30.0),      ( 90.0, 26.0)),
    "voiced_fraction": ((  0.55, 0.12),     (  0.58, 0.12)),
    "centroid_mean":   ((2600.0, 380.0),    (1750.0, 340.0)),
    "centroid_std":    (( 500.0, 120.0),    ( 450.0, 110.0)),
    "rolloff_mean":    ((4200.0, 560.0),    (3050.0, 520.0)),
    "rolloff_std":     (( 700.0, 150.0),    ( 650.0, 140.0)),
    "zcr_mean":        ((  0.125, 0.028),   (  0.085, 0.026)),
    "zcr_std":         ((  0.050, 0.015),   (  0.042, 0.014)),
    "log_energy_mean": (( 10.5,  0.8),      ( 10.7,  0.8)),
    "log_energy_std":  ((  1.2,  0.3),      (  1.1,  0.3)),
}

# Subgroup shifts: multiply the CHILD/ADULT means toward the opposite class on
# the pitch-family features to create the confounding tails.
_PITCH_FEATURES = ("f0_mean", "f0_median", "f0_p10", "f0_p90",
                   "centroid_mean", "rolloff_mean", "zcr_mean")


def _class_means_stds(label):
    idx = 1  # child column
    means = np.array([_DIST[n][0 if label == 1 else 1][0] for n in FEATURE_NAMES])
    stds = np.array([_DIST[n][0 if label == 1 else 1][1] for n in FEATURE_NAMES])
    return means, stds


def _draw(rng, label, subgroup, n, std_scale=1.0):
    """Draw n samples for a class/subgroup (std_scale widens/tightens spread)."""
    means, stds = _class_means_stds(label)
    means = means.copy()
    # Confounder tails: shrink the pitch gap by 55% toward the other class.
    if subgroup in ("child_low_pitch", "adult_high_pitch"):
        other, _ = _class_means_stds(0 if label == 1 else 1)
        for i, name in enumerate(FEATURE_NAMES):
            if name in _PITCH_FEATURES:
                means[i] = means[i] + 0.55 * (other[i] - means[i])
    X = rng.normal(means, stds * float(std_scale), size=(n, FEATURE_DIM))
    return X.astype(np.float64)


def make_synthetic_dataset(n=4000, rng=None, child_fraction=0.5,
                           confounder_fraction=0.25, std_scale=1.0):
    """Generate a labeled synthetic feature dataset.

    Parameters
    ----------
    std_scale : float
        Multiplies the per-feature standard deviation. <1 -> tighter, cleaner
        distribution (e.g. studio training corpus); >1 -> noisier, more
        overlapping deployment distribution.

    Returns
    -------
    X : (n, FEATURE_DIM) float64 feature matrix
    y : (n,) int labels (1 child, 0 adult)
    groups : (n,) array of subgroup name strings
    """
    if rng is None:
        rng = np.random.default_rng(0)
    n_child = int(round(n * child_fraction))
    n_adult = n - n_child

    def build(label, count, typ_name, conf_name):
        n_conf = int(round(count * confounder_fraction))
        n_typ = count - n_conf
        Xt = _draw(rng, label, typ_name, n_typ, std_scale)
        Xc = _draw(rng, label, conf_name, n_conf, std_scale)
        X = np.vstack([Xt, Xc])
        g = np.array([typ_name] * n_typ + [conf_name] * n_conf)
        return X, g

    Xc, gc = build(1, n_child, "child_typical", "child_low_pitch")
    Xa, ga = build(0, n_adult, "adult_typical", "adult_high_pitch")
    X = np.vstack([Xc, Xa])
    y = np.concatenate([np.ones(len(Xc), dtype=int), np.zeros(len(Xa), dtype=int)])
    groups = np.concatenate([gc, ga])

    # Shuffle deterministically.
    perm = rng.permutation(len(y))
    return X[perm], y[perm], groups[perm]


def split(X, y, groups, val_frac=0.2, test_frac=0.2, rng=None):
    """Deterministic train / val / test split."""
    if rng is None:
        rng = np.random.default_rng(1)
    n = len(y)
    idx = rng.permutation(n)
    n_test = int(round(n * test_frac))
    n_val = int(round(n * val_frac))
    test_i = idx[:n_test]
    val_i = idx[n_test:n_test + n_val]
    train_i = idx[n_test + n_val:]
    pack = lambda i: (X[i], y[i], groups[i])
    return pack(train_i), pack(val_i), pack(test_i)


def train_pipeline(n=4000, seed=0, save=True, verbose=True):
    """Generate data, train the classifier, calibrate temperature, (optionally) save.

    Trains on a cleaner (tighter) corpus and calibrates/evaluates on a noisier
    deployment distribution, so temperature scaling has a real over-confidence to
    correct. Returns (model, (Xval, yval, gval), (Xtest, ytest, gtest)).
    """
    # Clean training corpus.
    Xtr, ytr, gtr = make_synthetic_dataset(
        n=n, rng=np.random.default_rng(seed), std_scale=TRAIN_STD_SCALE)
    # Noisier deployment distribution -> split into val (for calibration) + test.
    Xdep, ydep, gdep = make_synthetic_dataset(
        n=n, rng=np.random.default_rng(seed + 100), std_scale=DEPLOY_STD_SCALE)
    # split() returns (train, val, test); with val=test=0.5 the train part is
    # empty, so we keep the val + test halves.
    _, (Xval, yval, gval), (Xte, yte, gte) = split(
        Xdep, ydep, gdep, val_frac=0.5, test_frac=0.5,
        rng=np.random.default_rng(seed + 1))

    model = AgeClassifier(l2=1e-4, lr=0.5, n_iter=1500)
    model.fit(Xtr, ytr)

    # Calibration BEFORE / AFTER temperature scaling on the validation set.
    p_val_uncal = model.predict_child_proba(Xval)
    ece_before = expected_calibration_error(p_val_uncal, yval)
    model.fit_temperature(Xval, yval)
    p_val_cal = model.predict_child_proba(Xval)
    ece_after = expected_calibration_error(p_val_cal, yval)

    tr_acc = float(np.mean(model.predict(Xtr) == ytr))
    val_acc = float(np.mean(model.predict(Xval) == yval))

    if save:
        model.save(MODEL_PATH)

    if verbose:
        print("=" * 66)
        print(" DreamCodeVR+ voice age-gate — TRAINING")
        print("=" * 66)
        print(f" features            : {FEATURE_DIM}-D DSP vector ({', '.join(FEATURE_NAMES[:4])}, ...)")
        print(f" samples (train/val/test): {len(ytr)} / {len(yval)} / {len(yte)}")
        print(f" model               : logistic regression (l2={model.l2}, "
              f"iters={model.n_iter})")
        print(f" train accuracy      : {tr_acc:6.3f}")
        print(f" val   accuracy      : {val_acc:6.3f}")
        print(f" temperature (fitted): {model.temperature_:6.3f}")
        print(f" val ECE  (uncalib.) : {ece_before:6.4f}")
        print(f" val ECE  (calibrated): {ece_after:6.4f}   "
              f"({'improved' if ece_after <= ece_before else 'worse'})")
        if save:
            print(f" saved model         : {MODEL_PATH}")
        print("=" * 66)

    return model, (Xval, yval, gval), (Xte, yte, gte)


def main():
    train_pipeline(save=True, verbose=True)


if __name__ == "__main__":
    main()
