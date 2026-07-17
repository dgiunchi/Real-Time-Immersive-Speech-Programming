#!/usr/bin/env python3
"""Pure-numpy evaluation statistics (see package docstring).

Every function is dependency-free (numpy + stdlib `math` only) and deterministic
given a seed, so results in the dissertation regenerate exactly and can be audited
line-by-line. References are named in each docstring.
"""

import math

import numpy as np

# --------------------------------------------------------------------------- #
# Normal CDF / inverse-CDF (no scipy).
# --------------------------------------------------------------------------- #


def _norm_cdf(x):
    """Standard-normal CDF via the error function (exact to erf precision)."""
    return 0.5 * (1.0 + math.erf(x / math.sqrt(2.0)))


def _norm_ppf(p):
    """Standard-normal inverse-CDF (Acklam's rational approximation, |err| < 1.2e-9)."""
    if p <= 0.0:
        return -math.inf
    if p >= 1.0:
        return math.inf
    a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02,
         1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00]
    b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02,
         6.680131188771972e+01, -1.328068155288572e+01]
    c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00,
         -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00]
    d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00,
         3.754408661907416e+00]
    plow, phigh = 0.02425, 1 - 0.02425
    if p < plow:
        q = math.sqrt(-2 * math.log(p))
        return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / \
               ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1)
    if p > phigh:
        q = math.sqrt(-2 * math.log(1 - p))
        return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / \
               ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1)
    q = p - 0.5
    r = q * q
    return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / \
           (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1)


# --------------------------------------------------------------------------- #
# Metrics.
# --------------------------------------------------------------------------- #


def roc_auc(y_true, scores):
    """ROC-AUC via the Mann-Whitney U statistic (ties -> 0.5), pure numpy.

    Equals P(score(pos) > score(neg)) + 0.5 P(tie). Returns nan if only one class
    is present (AUC undefined).
    """
    y = np.asarray(y_true).astype(int)
    s = np.asarray(scores, dtype=float)
    pos = s[y == 1]
    neg = s[y == 0]
    if pos.size == 0 or neg.size == 0:
        return float("nan")
    # Rank all scores (average ranks for ties); U = sum_ranks(pos) - n_pos(n_pos+1)/2.
    order = np.argsort(s, kind="mergesort")
    ranks = np.empty(s.size, dtype=float)
    sorted_s = s[order]
    i = 0
    n = s.size
    while i < n:
        j = i
        while j < n and sorted_s[j] == sorted_s[i]:
            j += 1
        ranks[order[i:j]] = 0.5 * (i + j - 1) + 1.0  # average rank (1-based)
        i = j
    r_pos = ranks[y == 1].sum()
    u = r_pos - pos.size * (pos.size + 1) / 2.0
    return float(u / (pos.size * neg.size))


def average_precision(y_true, scores):
    """Average precision (area under precision-recall), the standard AP estimator:
    AP = sum_n (R_n - R_{n-1}) * P_n over the ranking by descending score.

    AUPRC is the honest headline metric when the positive (anomaly) rate is low —
    AUROC looks flattering under heavy imbalance (Saito & Rehmsmeier 2015).
    """
    y = np.asarray(y_true).astype(int)
    s = np.asarray(scores, dtype=float)
    if y.sum() == 0:
        return float("nan")
    order = np.argsort(-s, kind="mergesort")  # descending score
    y_sorted = y[order]
    s_sorted = s[order]
    tp = np.cumsum(y_sorted)
    fp = np.cumsum(1 - y_sorted)
    # Collapse tied scores to a single threshold (evaluate precision/recall at the END
    # of each tie group) so AP is invariant to the input order among equal scores,
    # matching the standard estimator (sklearn `average_precision_score`).
    group_end = np.concatenate((np.diff(s_sorted) != 0, [True]))
    tp_d = tp[group_end]
    fp_d = fp[group_end]
    precision = tp_d / np.maximum(tp_d + fp_d, 1)
    recall = tp_d / y.sum()
    # AP = sum of precision at each threshold weighted by the recall increment.
    recall_prev = np.concatenate(([0.0], recall[:-1]))
    return float(np.sum((recall - recall_prev) * precision))


def detection_rate_at_fpr(y_true, scores, target_fpr=0.01):
    """True-positive rate (detection rate) at the operating point whose false-positive
    rate is the largest value <= `target_fpr`. This is the real deployment number for
    a safety detector: "at a 1% benign false-alarm budget, what fraction of attacks do
    we catch?" Returns (tpr, threshold, achieved_fpr).
    """
    y = np.asarray(y_true).astype(int)
    s = np.asarray(scores, dtype=float)
    pos = s[y == 1]
    neg = s[y == 0]
    if pos.size == 0 or neg.size == 0:
        return float("nan"), float("nan"), float("nan")
    # Enforce the FPR budget by COUNT, not by an interpolating quantile: at most
    # k = floor(target_fpr * n_neg) benign scores may be flagged, so the achieved FPR
    # can never EXCEED the stated budget (a looser point would inflate the detection
    # number). Ties at the threshold are handled by raising it to the next distinct value.
    neg_sorted = np.sort(neg)
    n_neg = neg_sorted.size
    k = int(math.floor(target_fpr * n_neg))
    if k <= 0:
        thr = np.nextafter(neg_sorted[-1], np.inf)  # budget too small: flag nothing (FPR=0)
    else:
        thr = neg_sorted[n_neg - k]  # the k-th largest benign score
        if np.mean(neg >= thr) > target_fpr + 1e-12:  # ties at thr overshot the budget
            higher = neg_sorted[neg_sorted > thr]
            thr = higher[0] if higher.size else np.nextafter(neg_sorted[-1], np.inf)
    achieved_fpr = float(np.mean(neg >= thr))
    tpr = float(np.mean(pos >= thr))
    return tpr, float(thr), achieved_fpr


# --------------------------------------------------------------------------- #
# Bootstrap confidence intervals (percentile + BCa).
# --------------------------------------------------------------------------- #


def bootstrap_ci(n, stat_fn, n_boot=2000, alpha=0.05, method="bca", seed=0):
    """Confidence interval for a statistic computed over `n` paired samples.

    `stat_fn(idx)` takes an int index array (with replacement for bootstrap, or a
    leave-one-out subset for the jackknife) and returns the scalar statistic. This
    decoupling lets the same routine bootstrap an AUC, a detection rate, a mean, etc.

    method="bca" -> bias-corrected & accelerated (Efron 1987); "percentile" -> plain.
    Returns (point_estimate, lo, hi).
    """
    full = np.arange(n)
    theta_hat = float(stat_fn(full))
    rng = np.random.default_rng(seed)
    boot = np.empty(n_boot, dtype=float)
    for b in range(n_boot):
        boot[b] = stat_fn(rng.integers(0, n, n))
    boot = boot[np.isfinite(boot)]
    if boot.size == 0:
        return theta_hat, float("nan"), float("nan")

    if method == "percentile":
        lo, hi = np.percentile(boot, [100 * alpha / 2, 100 * (1 - alpha / 2)])
        return theta_hat, float(lo), float(hi)

    # BCa. Bias correction z0 from the fraction of bootstrap stats below theta_hat.
    prop = float(np.mean(boot < theta_hat))
    prop = min(max(prop, 1.0 / (2 * boot.size)), 1.0 - 1.0 / (2 * boot.size))
    z0 = _norm_ppf(prop)
    # Acceleration a from the jackknife skewness.
    jack = np.empty(n, dtype=float)
    for i in range(n):
        jack[i] = stat_fn(np.delete(full, i))
    jack_mean = np.nanmean(jack)
    diff = jack_mean - jack
    denom = 6.0 * (np.nansum(diff ** 2) ** 1.5)
    a = float(np.nansum(diff ** 3) / denom) if denom > 0 else 0.0

    def _adj(z):
        num = z0 + z
        return _norm_cdf(z0 + num / (1.0 - a * num))

    zl = _norm_ppf(alpha / 2)
    zu = _norm_ppf(1 - alpha / 2)
    a1, a2 = _adj(zl), _adj(zu)
    lo, hi = np.percentile(boot, [100 * a1, 100 * a2])
    return theta_hat, float(lo), float(hi)


# --------------------------------------------------------------------------- #
# Significance tests for comparing two models on the SAME test set.
# --------------------------------------------------------------------------- #


def mcnemar_test(correct_a, correct_b, exact=True):
    """Paired McNemar test (Dietterich 1998) that two classifiers differ on one test
    set. Inputs are per-sample correctness booleans. Uses only the discordant pairs:
    n10 = A right & B wrong, n01 = A wrong & B right.

    exact=True -> two-sided exact binomial p (best for small discordant counts);
    else the chi-square statistic with continuity correction. Returns (statistic, p).
    """
    a = np.asarray(correct_a).astype(bool)
    b = np.asarray(correct_b).astype(bool)
    n10 = int(np.sum(a & ~b))
    n01 = int(np.sum(~a & b))
    n = n10 + n01
    if n == 0:
        return 0.0, 1.0
    if exact:
        k = min(n10, n01)
        # two-sided exact binomial p at prob 0.5
        tail = sum(math.comb(n, i) for i in range(0, k + 1)) * (0.5 ** n)
        p = min(1.0, 2.0 * tail)
        return float(k), float(p)
    # Continuity-corrected chi-square (1 dof), clamped at 0 so equal discordant counts
    # (|n10-n01| < 1) give statistic 0 / p = 1, not a spurious positive.
    stat = max(0.0, abs(n10 - n01) - 1.0) ** 2 / n
    p = 1.0 - _chi2_cdf_1dof(stat)
    return float(stat), float(p)


def _chi2_cdf_1dof(x):
    """CDF of chi-square with 1 dof = erf(sqrt(x/2))."""
    if x <= 0:
        return 0.0
    return math.erf(math.sqrt(x / 2.0))


def _midrank(x):
    """Average (mid) ranks of x, 1-based — the core of the fast DeLong algorithm."""
    order = np.argsort(x, kind="mergesort")
    xs = x[order]
    n = x.size
    ranks = np.empty(n, dtype=float)
    i = 0
    while i < n:
        j = i
        while j < n and xs[j] == xs[i]:
            j += 1
        ranks[i:j] = 0.5 * (i + j - 1) + 1.0
        i = j
    out = np.empty(n, dtype=float)
    out[order] = ranks
    return out


def delong_roc_test(y_true, scores_a, scores_b):
    """DeLong's test for two CORRELATED ROC AUCs on one test set (same labels, two
    score vectors), using the fast midrank implementation of Sun & Xu (2014).

    Returns (auc_a, auc_b, z, p_two_sided). The variance uses the structural
    components V10 (over positives) and V01 (over negatives); this is the standard,
    correct way to compare two AUCs that share a test set (a paired bootstrap of the
    AUC difference should agree with it — see the tests).
    """
    y = np.asarray(y_true).astype(int)
    sa = np.asarray(scores_a, dtype=float)
    sb = np.asarray(scores_b, dtype=float)
    pos = y == 1
    neg = y == 0
    m = int(pos.sum())
    n = int(neg.sum())
    if m == 0 or n == 0:
        return float("nan"), float("nan"), float("nan"), float("nan")

    def _components(s):
        xp = s[pos]  # positive scores
        yn = s[neg]  # negative scores
        tz = np.concatenate([xp, yn])
        tx = _midrank(xp)
        ty = _midrank(yn)
        tzr = _midrank(tz)
        auc = (tzr[:m].sum() - m * (m + 1) / 2.0) / (m * n)
        v10 = (tzr[:m] - tx) / n              # over positives
        v01 = 1.0 - (tzr[m:] - ty) / m        # over negatives
        return auc, v10, v01

    auc_a, v10a, v01a = _components(sa)
    auc_b, v10b, v01b = _components(sb)

    # 2x2 covariance of the two AUCs.
    def _cov(u, v):
        return np.cov(np.vstack([u, v]))  # 2x2 sample covariance

    s10 = _cov(v10a, v10b)
    s01 = _cov(v01a, v01b)
    s = s10 / m + s01 / n
    var = s[0, 0] + s[1, 1] - 2 * s[0, 1]
    if var <= 0:
        z = 0.0 if auc_a == auc_b else math.inf
    else:
        z = (auc_a - auc_b) / math.sqrt(var)
    p = 2.0 * (1.0 - _norm_cdf(abs(z))) if math.isfinite(z) else 0.0
    return float(auc_a), float(auc_b), float(z), float(p)


if __name__ == "__main__":
    rng = np.random.default_rng(0)
    y = np.array([0] * 200 + [1] * 30)  # 13% positive (imbalanced)
    good = np.where(y == 1, rng.normal(1.5, 1, y.size), rng.normal(0, 1, y.size))
    weak = np.where(y == 1, rng.normal(0.6, 1, y.size), rng.normal(0, 1, y.size))
    print("AUROC good/weak:", round(roc_auc(y, good), 3), round(roc_auc(y, weak), 3))
    print("AUPRC good/weak:", round(average_precision(y, good), 3),
          round(average_precision(y, weak), 3))
    print("detect@1%FPR good:", detection_rate_at_fpr(y, good, 0.01))
    pt, lo, hi = bootstrap_ci(y.size, lambda idx: roc_auc(y[idx], good[idx]),
                              n_boot=1000, seed=1)
    print(f"AUROC good 95% BCa CI: {pt:.3f} [{lo:.3f}, {hi:.3f}]")
    aa, ab, z, p = delong_roc_test(y, good, weak)
    print(f"DeLong good vs weak: AUC {aa:.3f} vs {ab:.3f}, z={z:.2f}, p={p:.2e}")
