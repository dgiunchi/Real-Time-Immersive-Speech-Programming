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

    def __init__(self, n_components: int = 6, std_floor: float = 1e-2):
        self.n_components = int(n_components)
        # Floor the per-feature std so a dimension that is CONSTANT in benign data (e.g.
        # an ATTACK_OP that never fires in a well-behaved session) does not blow the
        # standardised attack signal up to ~1e16. With a 1e-2 floor a single unexpected
        # attack op contributes an interpretable ~O(100) to the reconstruction error,
        # cleanly separated from the benign ~O(10) baseline.
        self.std_floor = float(std_floor)
        self.mean_ = None
        self.std_ = None
        self.components_ = None          # (k, d)
        self.benign_errors_ = None       # training reconstruction errors (for thresholds)

    def fit(self, X_benign: np.ndarray) -> "ReconstructionAnomalyDetector":
        X = np.asarray(X_benign, dtype=np.float64)
        if X.ndim != 2 or X.shape[0] < 2:
            raise ValueError("fit expects a 2-D benign matrix with >=2 rows")
        self.mean_ = X.mean(axis=0)
        self.std_ = np.maximum(X.std(axis=0), self.std_floor)
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


# ======================================================================================
# HARD-NEGATIVE, HONEST BENCHMARK.
#
# The eval above reports AUC ~ 1.00 — a BENCHMARK ARTIFACT, not skill: code_injection /
# sensor_privacy carry `ATTACK_OPS` that are identically zero in benign data, so a single
# out-of-vocabulary column separates them and a one-line "attack-op present?" rule scores
# just as well. This benchmark is honest about that AND about the limits of the model:
#   (1) a STEALTH family of compositional immersive harm from BENIGN ops ONLY (no attack
#       token, no explicit perceptual op), drawn from the SAME weighted op distribution and
#       SAME length distribution as benign — so the ONLY discriminative signal is an
#       elevated consecutive-repeat run (strobe/vection/occlusion by accumulation); there is
#       deliberately no op-mix or length shortcut to leak on;
#   (2) honest metrics at a LOW attack base rate (AUPRC + detection@1%-benign-FPR, with
#       bootstrap CIs) from the audited ml.evalstats harness (AUROC flatters under imbalance);
#   (3) THREE detectors side by side — the linear autoencoder and two trivial one-line rules.
#
# The honest finding this SURFACES (and a strong dissertation OWNS rather than hides): on the
# easy families the trivial attack-op rule TIES the autoencoder, and on the stealth family a
# one-line max-consecutive-repeat rule DOMINATES it. A linear autoencoder fit on benign
# reconstructs whatever varies in benign (which already contains occasional repeat bursts),
# so it is structurally weak at repeat-based compositional harm — the explicit bound is the
# right tool there. Knowing WHEN the one-liner beats the model is a result, not a defeat.
#
# NOTE: the detect@1%FPR operating point is chosen in-sample on the test benign scores
# (transductive) — standard for reporting an operating point; state this in the write-up.
# ======================================================================================
def make_benign_realistic(n: int, rng) -> list:
    """Benign sessions whose runs are usually short but OCCASIONALLY moderate (a wall of
    cubes, a burst of colour tweaks). The burst OVERWRITES a segment (does not lengthen the
    sequence), so benign and stealth share an identical length distribution and the only
    thing that separates them is HOW OFTEN / HOW LONG a repeat run occurs."""
    weights = np.array([6, 5, 4, 3, 4, 3, 3, 2, 3, 2, 2, 2, 2], dtype=np.float64)
    weights = weights / weights.sum()
    seqs = []
    for _ in range(n):
        length = int(rng.integers(4, 13))
        ops = _cap_runs(list(rng.choice(BENIGN_OPS, size=length, p=weights)), 3, rng, BENIGN_OPS)
        if rng.random() < 0.25:  # 25% carry a legitimate moderate burst (repeat 4..6)
            burst_len = min(int(rng.integers(4, 7)), length)
            at = int(rng.integers(0, length - burst_len + 1)) if length > burst_len else 0
            ops[at:at + burst_len] = [str(rng.choice(BENIGN_OPS, p=weights))] * burst_len
        seqs.append(ops)
    return seqs


def make_stealth_sequences(n: int, rng) -> list:
    """Compositional immersive harm from BENIGN ops ONLY — drawn from the SAME weighted op
    distribution and SAME length distribution as `make_benign_realistic`, then OVERWRITING a
    contiguous segment with a repeated (weighted-common) op to create an elevated run of
    length 4-9. Because op-mix and length match benign, the ONLY discriminative signal is
    the repeat run itself: a detector leaning on an OOV attack column, or on op-mix/length,
    is structurally blind — which is exactly what this hard benchmark is designed to expose."""
    weights = np.array([6, 5, 4, 3, 4, 3, 3, 2, 3, 2, 2, 2, 2], dtype=np.float64)
    weights = weights / weights.sum()
    seqs = []
    for _ in range(n):
        length = int(rng.integers(4, 13))  # SAME length distribution as benign
        ops = _cap_runs(list(rng.choice(BENIGN_OPS, size=length, p=weights)), 3, rng, BENIGN_OPS)
        run_len = min(int(rng.integers(4, 10)), length)  # 4..9, overwrite (no length shift)
        at = int(rng.integers(0, length - run_len + 1)) if length > run_len else 0
        ops[at:at + run_len] = [str(rng.choice(BENIGN_OPS, p=weights))] * run_len
        seqs.append(ops)
    return seqs


def _attack_op_count(seqs) -> np.ndarray:
    """Trivial baseline: number of attack-op tokens present. Reproduces the naive
    benchmark's ~perfect score on the easy families and is BLIND (0) to stealth."""
    atk = set(ATTACK_OPS)
    return np.array([sum(1 for op in s if op in atk) for s in seqs], dtype=np.float64)


def _max_repeat_score(seqs) -> np.ndarray:
    """Trivial baseline: longest consecutive repeat (the compositional-harm signal)."""
    return np.array([_max_consecutive_repeat(s) for s in seqs], dtype=np.float64)


def hard_negative_eval(n_benign_train: int = 800, n_benign_test: int = 500,
                       n_attack_pool: int = 400, n_components: int = 6,
                       base_rate: float = 0.05, seed: int = 0, n_boot: int = 1000) -> dict:
    """Honest open-set benchmark (see the section banner above). Fits the autoencoder on
    realistic benign, then for each family reports AUROC, AUPRC (+ bootstrap CI) and
    detection@1%-benign-FPR at a low attack `base_rate`, for the autoencoder AND two
    trivial baselines. Metrics come from the audited ml.evalstats harness."""
    # aliased: this module ALSO defines a local roc_auc(scores, labels) with the opposite
    # argument order (used by open_set_eval); ev_roc_auc is (y_true, scores).
    from ml.evalstats import average_precision, bootstrap_ci, detection_rate_at_fpr
    from ml.evalstats import roc_auc as ev_roc_auc

    rng = np.random.default_rng(seed)
    fz = SequenceFeaturizer()
    det = ReconstructionAnomalyDetector(n_components=n_components)
    det.fit(fz.transform(make_benign_realistic(n_benign_train, rng)))

    benign_test = make_benign_realistic(n_benign_test, rng)
    families = {
        "code_injection": make_attack_sequences(n_attack_pool, "code_injection", rng),
        "sensor_privacy": make_attack_sequences(n_attack_pool, "sensor_privacy", rng),
        "stealth_immersive": make_stealth_sequences(n_attack_pool, rng),
    }
    scorers = {
        "autoencoder": lambda seqs: det.score(fz.transform(seqs)),
        "trivial_attack_op": _attack_op_count,
        "trivial_max_repeat": _max_repeat_score,
    }
    # attacks subsampled so P(attack) = base_rate relative to the benign test set
    n_atk_br = max(1, int(round(base_rate / (1.0 - base_rate) * len(benign_test))))

    report = {"base_rate": base_rate, "n_benign_test": len(benign_test),
              "n_attack_per_family": n_atk_br, "families": {}}
    for fam, atk_pool in families.items():
        atk = atk_pool[:n_atk_br]
        seqs = benign_test + atk
        y = np.concatenate([np.zeros(len(benign_test)), np.ones(len(atk))]).astype(int)
        fam_rep = {}
        for name, scorer in scorers.items():
            sc = np.asarray(scorer(seqs), dtype=np.float64)
            tpr, _, fpr = detection_rate_at_fpr(y, sc, 0.01)

            def _ap_idx(idx, _y=y, _sc=sc):
                yi = _y[idx]
                return average_precision(yi, _sc[idx]) if yi.sum() > 0 else float("nan")

            ap, ap_lo, ap_hi = bootstrap_ci(len(y), _ap_idx, n_boot=n_boot,
                                            method="percentile", seed=1)
            fam_rep[name] = {
                "auroc": round(ev_roc_auc(y, sc), 4),
                "auprc": round(ap, 4),
                "auprc_ci95": [round(ap_lo, 4), round(ap_hi, 4)],
                "detect_at_1pct_fpr": round(tpr, 4),
                "achieved_fpr": round(fpr, 4),
            }
        report["families"][fam] = fam_rep
    return report


if __name__ == "__main__":
    import json
    rep = open_set_eval()
    print("=== naive open-set eval (attack families HELD OUT of training) ===")
    print(json.dumps(rep, indent=2))
    print(f"\npooled AUC = {rep['pooled_auc']:.4f}   "
          f"benign FPR @99pct = {rep['benign_false_positive_rate']:.4f}")
    print("  ^ AUC~1.00 here is a BENCHMARK ARTIFACT (out-of-vocabulary attack columns).\n")

    hard = hard_negative_eval()
    print("=== honest hard-negative eval (base rate {:.0%}, AUPRC + detect@1%FPR) ==="
          .format(hard["base_rate"]))
    print(f"  {'family':18} {'AUROC: autoenc':>15} {'attack-op':>10} {'max-repeat':>11}   "
          f"{'AE AUPRC[CI]':>22} {'AE det@1%':>10} {'maxrep det@1%':>13}")
    for fam, r in hard["families"].items():
        ae, tao, tmr = r["autoencoder"], r["trivial_attack_op"], r["trivial_max_repeat"]
        print(f"  {fam:18} {ae['auroc']:>15.3f} {tao['auroc']:>10.3f} {tmr['auroc']:>11.3f}   "
              f"{str(ae['auprc'])+' '+str(ae['auprc_ci95']):>22} "
              f"{ae['detect_at_1pct_fpr']:>10.3f} {tmr['detect_at_1pct_fpr']:>13.3f}")
    print("  ^ HONEST FINDINGS: (easy families) the trivial attack-op rule TIES the "
          "autoencoder -> AUC~1.0 is an artifact.\n"
          "    (stealth family) a one-line MAX-REPEAT rule DOMINATES the autoencoder -> a "
          "linear AE fit on benign is structurally weak\n    at repeat-based compositional "
          "harm; the explicit bound is the right tool. Knowing when the one-liner wins is a result.")
