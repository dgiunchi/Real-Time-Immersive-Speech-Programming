"""DreamCodeVR+ evaluation statistics — pure-numpy, dependency-free.

A small, auditable harness so every headline number in the dissertation carries a
confidence interval and every model comparison carries a significance test:

  * `roc_auc`, `average_precision`, `detection_rate_at_fpr` — imbalance-aware metrics
    (AUPRC + detection@fixed-FPR are the honest operating point when anomalies are rare).
  * `bootstrap_ci` — BCa (bias-corrected & accelerated) or percentile bootstrap CIs.
  * `mcnemar_test` — exact paired test for two classifiers on one test set.
  * `delong_roc_test` — DeLong's test for two correlated ROC AUCs (Sun & Xu 2014 fast form).

numpy-only by design (matches the age_gate / attack_analyzer ethos): reproducible,
CPU-only, and reviewable line-by-line.
"""

from .stats import (
    average_precision,
    bootstrap_ci,
    delong_roc_test,
    detection_rate_at_fpr,
    mcnemar_test,
    roc_auc,
)

__all__ = [
    "roc_auc",
    "average_precision",
    "detection_rate_at_fpr",
    "bootstrap_ci",
    "mcnemar_test",
    "delong_roc_test",
]
