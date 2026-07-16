#!/usr/bin/env python3
"""
Reconstruction-error anomaly detector for benign intent->code-op sequences.

WHY (RESEARCH_AND_ML_PLAN.md §5, L2 "streaming anomaly + drift"):
  The Rust validator + LLM screen are a *closed-set* defence — they block what we already
  named. Novel attacks are *out-of-distribution*. So we train a model on nothing but
  BENIGN behaviour (the intent->code-op sequences a well-behaved creative session emits)
  and flag anything it cannot reconstruct. That is open-set / novelty detection: the
  attack families are HELD OUT of training entirely, so a low false-negative rate on them
  is real generalisation, not memorisation.

THE MODEL:
  An UNDERCOMPLETE LINEAR AUTOENCODER. A linear autoencoder with a k-unit bottleneck,
  trained to minimise squared reconstruction error, has a closed-form global optimum: the
  top-k principal components of the (standardised) training data (Baldi & Hornik, 1989).
  So instead of gradient descent (non-deterministic, tune-y) we fit the *optimal* weights
  directly with an SVD — a numpy autoencoder that is exactly reproducible.
    encode:  z = (x - mean)/std              (standardise)
             h = z @ Wt                       (project onto k components)   Wt = V[:k].T
    decode:  z_hat = h @ W                    (reconstruct)                 W  = V[:k]
    score:   ||z - z_hat||^2                  (reconstruction error)
  Benign points live in the k-dim subspace the SVD found -> tiny error. Attack points have
  mass in directions/dimensions benign data never used (a `file_io` op, a strobe-by-
  accumulation repeat count) -> the projection cannot represent them -> large error.

THRESHOLD:
  Chosen from a benign quantile (default 99th pct of benign reconstruction error), so the
  benign false-positive rate is controlled by construction and NO attack data is needed to
  set it. `drift.py` can move this threshold as the benign baseline shifts.

FEATURES (`SequenceFeaturizer`):
  Each op-sequence -> a fixed-length vector: a normalised op-unigram histogram over the
  vocabulary PLUS a few aggregates (length, max consecutive-repeat, unique-op ratio). The
  repeat aggregate is deliberate: it is how *compositional* immersive harms (strobe /
  vection / occlusion-by-accumulation) show up even when every individual op is benign.

Deterministic: numpy RNG is always seeded; no time/random used for logic.
"""
from __future__ import annotations

import numpy as np

# --------------------------------------------------------------------------------------
# Op vocabulary. BENIGN ops are what a well-behaved creative session emits; ATTACK ops are
# the code-op tokens that only appear in the attack families (they are NEVER in training).
# --------------------------------------------------------------------------------------
BENIGN_OPS = [
    "spawn_primitive", "set_color", "set_scale", "translate", "rotate",
    "add_light", "instantiate", "destroy", "set_material", "set_parent",
    "add_rigidbody", "play_sound", "set_text",
]
ATTACK_OPS = [
    # generated-code / system access
    "file_io", "net_io", "process_spawn", "reflection", "dllimport",
    # voice-sensor / privacy sensors
    "sensor_camera", "sensor_mic", "read_location", "read_clipboard", "read_contacts",
    # immersive / perceptual-embodied
    "perceptual_flash", "forced_locomotion", "fov_occlude", "chaperone_edit", "vection_spin",
]
VOCAB = BENIGN_OPS + ATTACK_OPS
_OP_INDEX = {op: i for i, op in enumerate(VOCAB)}

# Number of extra aggregate features appended after the unigram histogram.
_N_AGG = 3
FEATURE_DIM = len(VOCAB) + _N_AGG


class SequenceFeaturizer:
    """Turn a list-of-op-token sequence into a fixed-length float feature vector.

    Layout: [ normalised unigram histogram over VOCAB | len_norm | max_repeat_norm |
              unique_ratio ].
    """

    def __init__(self, vocab=VOCAB, len_scale: float = 24.0, repeat_scale: float = 24.0):
        self.vocab = list(vocab)
        self.index = {op: i for i, op in enumerate(self.vocab)}
        self.len_scale = float(len_scale)
        self.repeat_scale = float(repeat_scale)

    def transform_one(self, seq) -> np.ndarray:
        v = np.zeros(len(self.vocab) + _N_AGG, dtype=np.float64)
        if len(seq) == 0:
            return v
        for op in seq:
            j = self.index.get(op)
            if j is not None:
                v[j] += 1.0
        # normalise the histogram to a distribution (robust to sequence length)
        hist = v[: len(self.vocab)]
        hist /= max(1.0, hist.sum())
        # aggregates
        max_repeat = _max_consecutive_repeat(seq)
        unique_ratio = len(set(seq)) / len(seq)
        v[len(self.vocab) + 0] = min(1.0, len(seq) / self.len_scale)
        v[len(self.vocab) + 1] = min(1.0, max_repeat / self.repeat_scale)
        v[len(self.vocab) + 2] = unique_ratio
        return v

    def transform(self, seqs) -> np.ndarray:
        return np.vstack([self.transform_one(s) for s in seqs]) if len(seqs) else \
            np.zeros((0, len(self.vocab) + _N_AGG))


def _max_consecutive_repeat(seq) -> int:
    """Longest run of the same op in a row (captures strobe/vection accumulation)."""
    best = run = 0
    prev = object()
    for op in seq:
        run = run + 1 if op == prev else 1
        prev = op
        best = max(best, run)
    return best


class ReconstructionAnomalyDetector:
    """Undercomplete linear autoencoder (PCA-optimal) reconstruction-error detector.

    fit(X_benign)        -> learn mean/std + top-k components from BENIGN data only.
    score(X)             -> per-row reconstruction error (higher = more anomalous).
    is_anomaly(X, thr)   -> boolean mask, error > thr.
    threshold(q)         -> a threshold at the q-quantile of the fitted benign errors.
    """

    def __init__(self, n_components: int = 6, eps: float = 1e-9):
        self.n_components = int(n_components)
        self.eps = float(eps)
        self.mean_ = None
        self.std_ = None
        self.components_ = None          # (k, d)
        self.benign_errors_ = None       # training reconstruction errors (for thresholds)

    def fit(self, X_benign: np.ndarray) -> "ReconstructionAnomalyDetector":
        X = np.asarray(X_benign, dtype=np.float64)
        if X.ndim != 2 or X.shape[0] < 2:
            raise ValueError("fit expects a 2-D benign matrix with >=2 rows")
        self.mean_ = X.mean(axis=0)
        self.std_ = X.std(axis=0) + self.eps
        Z = (X - self.mean_) / self.std_
        # economy SVD: rows of Vt are the principal directions (optimal AE decoder rows).
        _, _, Vt = np.linalg.svd(Z, full_matrices=False)
        k = max(1, min(self.n_components, Vt.shape[0]))
        self.components_ = Vt[:k]                      # (k, d)
        self.benign_errors_ = self._recon_error(Z)
        return self

    def _recon_error(self, Z: np.ndarray) -> np.ndarray:
        # project onto the k-dim subspace and back: z_hat = z @ W^T @ W
        W = self.components_
        Z_hat = (Z @ W.T) @ W
        return np.sum((Z - Z_hat) ** 2, axis=1)

    def score(self, X: np.ndarray) -> np.ndarray:
        if self.components_ is None:
            raise RuntimeError("call fit() before score()")
        X = np.asarray(X, dtype=np.float64)
        if X.ndim == 1:
            X = X[None, :]
        Z = (X - self.mean_) / self.std_
        return self._recon_error(Z)

    def threshold(self, q: float = 0.99) -> float:
        """A detection threshold at the q-quantile of the benign training errors."""
        if self.benign_errors_ is None:
            raise RuntimeError("call fit() before threshold()")
        return float(np.quantile(self.benign_errors_, q))

    def is_anomaly(self, X: np.ndarray, thr: float) -> np.ndarray:
        return self.score(X) > float(thr)


# --------------------------------------------------------------------------------------
# Synthetic data generators. Benign = training distribution; attacks = HELD OUT families.
# --------------------------------------------------------------------------------------
def make_benign_sequences(n: int, rng) -> list:
    """Well-behaved creative sessions: benign ops only, modest repeats (<=3)."""
    seqs = []
    # a skewed, realistic op distribution (spawns/colours/rotations common)
    weights = np.array(
        [6, 5, 4, 3, 4, 3, 3, 2, 3, 2, 2, 2, 2], dtype=np.float64
    )
    weights = weights / weights.sum()
    for _ in range(n):
        length = int(rng.integers(4, 13))
        ops = list(rng.choice(BENIGN_OPS, size=length, p=weights))
        # cap runs to <=3 so benign never looks like strobe/vection accumulation
        ops = _cap_runs(ops, 3, rng, BENIGN_OPS)
        seqs.append(ops)
    return seqs


def make_attack_sequences(n: int, family: str, rng) -> list:
    """Attack sequences for one HELD-OUT family. Never used in fit().

    Families:
      * "code_injection"   — benign scene ops + system-access code ops.
      * "sensor_privacy"   — benign scene ops + sensor/PII read ops.
      * "immersive"        — benign locomotion ops but composed into harm: high-repeat
                             rotate/translate (vection/strobe accumulation) + explicit
                             perceptual ops. Individually bounded, collectively harmful.
    """
    seqs = []
    for _ in range(n):
        base_len = int(rng.integers(4, 9))
        base = list(rng.choice(BENIGN_OPS, size=base_len))
        if family == "code_injection":
            inject = list(rng.choice(
                ["file_io", "net_io", "process_spawn", "reflection", "dllimport"],
                size=int(rng.integers(1, 4))))
            ops = base + inject
            rng.shuffle(ops)
        elif family == "sensor_privacy":
            inject = list(rng.choice(
                ["sensor_camera", "sensor_mic", "read_location", "read_clipboard",
                 "read_contacts"], size=int(rng.integers(1, 4))))
            ops = base + inject
            rng.shuffle(ops)
        elif family == "immersive":
            # compositional harm: long strobe/vection run of a single benign op ...
            harm_op = str(rng.choice(["rotate", "translate", "spawn_primitive"]))
            run_len = int(rng.integers(10, 20))          # >> benign cap of 3
            ops = base + [harm_op] * run_len
            # ... plus an explicit perceptual op sometimes
            if rng.random() < 0.6:
                ops.append(str(rng.choice(
                    ["perceptual_flash", "forced_locomotion", "fov_occlude",
                     "chaperone_edit", "vection_spin"])))
        else:
            raise ValueError(f"unknown attack family: {family}")
        seqs.append(ops)
    return seqs


ATTACK_FAMILIES = ("code_injection", "sensor_privacy", "immersive")


def _cap_runs(ops, max_run, rng, alphabet):
    """Break any run longer than `max_run` by inserting a different op."""
    out = []
    run = 0
    prev = object()
    for op in ops:
        if op == prev:
            run += 1
        else:
            run = 1
            prev = op
        if run > max_run:
            alt = op
            while alt == op:
                alt = str(rng.choice(alphabet))
            out.append(alt)
            prev = alt
            run = 1
        else:
            out.append(op)
    return out


# --------------------------------------------------------------------------------------
# Evaluation helpers (pure numpy — no sklearn).
# --------------------------------------------------------------------------------------
def _rankdata(a: np.ndarray) -> np.ndarray:
    """Average ranks with tie handling (like scipy.stats.rankdata, method='average')."""
    a = np.asarray(a, dtype=np.float64)
    order = a.argsort(kind="mergesort")
    ranks = np.empty(len(a), dtype=np.float64)
    sa = a[order]
    i = 0
    n = len(a)
    while i < n:
        j = i
        while j + 1 < n and sa[j + 1] == sa[i]:
            j += 1
        ranks[order[i:j + 1]] = (i + j) / 2.0 + 1.0
        i = j + 1
    return ranks


def roc_auc(scores: np.ndarray, labels: np.ndarray) -> float:
    """AUC via the Mann-Whitney U statistic. labels: 1 = anomaly/positive, 0 = benign."""
    scores = np.asarray(scores, dtype=np.float64)
    labels = np.asarray(labels)
    n_pos = int((labels == 1).sum())
    n_neg = int((labels == 0).sum())
    if n_pos == 0 or n_neg == 0:
        return float("nan")
    ranks = _rankdata(scores)
    r_pos = ranks[labels == 1].sum()
    return float((r_pos - n_pos * (n_pos + 1) / 2.0) / (n_pos * n_neg))


def open_set_eval(n_benign_train: int = 400, n_benign_test: int = 200,
                  n_attack: int = 120, n_components: int = 6, seed: int = 0) -> dict:
    """End-to-end open-set eval: fit on benign, hold EVERY attack family out of training.

    Returns per-family AUC + detection-rate at the 99th benign-quantile threshold, plus a
    pooled AUC. This is what the test asserts on and what the results chapter reports.
    """
    rng = np.random.default_rng(seed)
    fz = SequenceFeaturizer()

    benign_train = make_benign_sequences(n_benign_train, rng)
    benign_test = make_benign_sequences(n_benign_test, rng)

    det = ReconstructionAnomalyDetector(n_components=n_components)
    det.fit(fz.transform(benign_train))
    thr = det.threshold(0.99)

    benign_scores = det.score(fz.transform(benign_test))
    report = {"threshold": thr,
              "benign_test_mean_error": float(benign_scores.mean()),
              "benign_false_positive_rate": float((benign_scores > thr).mean()),
              "families": {}}

    all_scores = [benign_scores]
    all_labels = [np.zeros(len(benign_scores))]
    for fam in ATTACK_FAMILIES:
        atk = make_attack_sequences(n_attack, fam, rng)
        s = det.score(fz.transform(atk))
        pooled_scores = np.concatenate([benign_scores, s])
        pooled_labels = np.concatenate([np.zeros(len(benign_scores)), np.ones(len(s))])
        report["families"][fam] = {
            "auc": roc_auc(pooled_scores, pooled_labels),
            "detection_rate": float((s > thr).mean()),
            "attack_mean_error": float(s.mean()),
        }
        all_scores.append(s)
        all_labels.append(np.ones(len(s)))

    pooled = np.concatenate(all_scores)
    plabels = np.concatenate(all_labels)
    report["pooled_auc"] = roc_auc(pooled, plabels)
    return report


if __name__ == "__main__":
    import json
    rep = open_set_eval()
    print("=== open-set anomaly eval (attack families HELD OUT of training) ===")
    print(json.dumps(rep, indent=2))
    print(f"\npooled AUC = {rep['pooled_auc']:.4f}   "
          f"benign FPR @99pct = {rep['benign_false_positive_rate']:.4f}")
