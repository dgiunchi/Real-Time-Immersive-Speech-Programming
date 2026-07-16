#!/usr/bin/env python3
"""
DreamCodeVR+ voice age-gate — the SMALL on-device classifier (numpy only).

A deliberately tiny model: standardized-input **logistic regression** trained by
batch gradient descent, with **temperature-scaling** calibration so that the
child-probability it emits is *honest* (a reported 0.8 really means ~80% of such
utterances are children). This matters because `decision.py` fails safe on
low-confidence, and an over-confident model would defeat that safeguard.

Why logistic regression and not a deep net:
  * <1 kB of parameters -> trivially quantizes / runs in-process with STT on Quest.
  * A single linear logit is easy to temperature-calibrate and to reason about.
  * On the frozen self-supervised embeddings from `wav2vec2_features.py` a linear
    head is already the standard, strong probe.

Label convention: y == 1 -> CHILD, y == 0 -> ADULT.
predict_proba(X) returns columns [P(adult), P(child)].

Deterministic: all randomness flows through an explicit np.random.Generator.
"""

import numpy as np


def _sigmoid(z):
    # Numerically stable logistic sigmoid.
    out = np.empty_like(z, dtype=np.float64)
    pos = z >= 0
    out[pos] = 1.0 / (1.0 + np.exp(-z[pos]))
    ez = np.exp(z[~pos])
    out[~pos] = ez / (1.0 + ez)
    return out


class AgeClassifier:
    """Binary child/adult logistic regression with temperature calibration.

    Parameters
    ----------
    l2 : float
        L2 regularization strength on the weights (not the bias).
    lr : float
        Gradient-descent learning rate (on standardized features).
    n_iter : int
        Number of full-batch gradient steps.
    """

    def __init__(self, l2=1e-3, lr=0.5, n_iter=800):
        self.l2 = float(l2)
        self.lr = float(lr)
        self.n_iter = int(n_iter)
        # Learned parameters (set by fit()).
        self.mean_ = None      # feature means for standardization
        self.std_ = None       # feature stds for standardization
        self.w_ = None         # weight vector
        self.b_ = 0.0          # bias
        self.temperature_ = 1.0  # calibration temperature (1.0 = uncalibrated)

    # ------------------------------------------------------------------ #
    # Training
    # ------------------------------------------------------------------ #
    def _standardize(self, X):
        return (X - self.mean_) / self.std_

    def fit(self, X, y):
        """Fit standardization + logistic weights by full-batch gradient descent."""
        X = np.asarray(X, dtype=np.float64)
        y = np.asarray(y, dtype=np.float64).reshape(-1)
        if X.ndim != 2:
            raise ValueError("X must be 2-D (n_samples, n_features)")
        n, d = X.shape
        self.mean_ = X.mean(axis=0)
        std = X.std(axis=0)
        std[std < 1e-8] = 1.0  # guard constant features
        self.std_ = std
        Xs = self._standardize(X)

        w = np.zeros(d, dtype=np.float64)
        b = 0.0
        for _ in range(self.n_iter):
            z = Xs @ w + b
            p = _sigmoid(z)
            err = p - y                     # gradient of BCE wrt logit
            grad_w = (Xs.T @ err) / n + self.l2 * w
            grad_b = float(np.mean(err))
            w -= self.lr * grad_w
            b -= self.lr * grad_b
        self.w_ = w
        self.b_ = float(b)
        self.temperature_ = 1.0
        return self

    # ------------------------------------------------------------------ #
    # Inference
    # ------------------------------------------------------------------ #
    def decision_function(self, X):
        """Raw (uncalibrated) logit for the CHILD class."""
        if self.w_ is None:
            raise RuntimeError("model is not fitted")
        X = np.asarray(X, dtype=np.float64)
        if X.ndim == 1:
            X = X[None, :]
        Xs = self._standardize(X)
        return Xs @ self.w_ + self.b_

    def predict_child_proba(self, X):
        """Calibrated P(child) as a 1-D array."""
        logits = self.decision_function(X)
        return _sigmoid(logits / self.temperature_)

    def predict_proba(self, X):
        """Calibrated probabilities, columns = [P(adult), P(child)]."""
        p_child = self.predict_child_proba(X)
        return np.stack([1.0 - p_child, p_child], axis=1)

    def predict(self, X):
        """Hard label: 1 -> child, 0 -> adult (0.5 threshold on P(child))."""
        return (self.predict_child_proba(X) >= 0.5).astype(int)

    # ------------------------------------------------------------------ #
    # Temperature-scaling calibration
    # ------------------------------------------------------------------ #
    def fit_temperature(self, X_val, y_val, grid=None):
        """Fit a single temperature T>0 on a validation set (minimize NLL).

        Temperature scaling divides the logit by T before the sigmoid; it cannot
        change the argmax (accuracy is unchanged) but softens/​sharpens confidence
        so calibration (ECE / NLL) improves. We do a robust grid search that
        always includes T = 1, so calibration can never *increase* validation NLL.
        """
        y = np.asarray(y_val, dtype=np.float64).reshape(-1)
        logits = self.decision_function(X_val)
        if grid is None:
            grid = np.concatenate(([1.0], np.geomspace(0.25, 8.0, 96)))
        best_T, best_nll = 1.0, np.inf
        for T in grid:
            p = _sigmoid(logits / T)
            p = np.clip(p, 1e-7, 1 - 1e-7)
            nll = -np.mean(y * np.log(p) + (1 - y) * np.log(1 - p))
            if nll < best_nll:
                best_nll, best_T = nll, float(T)
        self.temperature_ = best_T
        return best_T

    # ------------------------------------------------------------------ #
    # Persistence (npz)
    # ------------------------------------------------------------------ #
    def save(self, path):
        if self.w_ is None:
            raise RuntimeError("cannot save an unfitted model")
        np.savez(
            path,
            mean=self.mean_,
            std=self.std_,
            w=self.w_,
            b=np.array([self.b_], dtype=np.float64),
            temperature=np.array([self.temperature_], dtype=np.float64),
            hyper=np.array([self.l2, self.lr, self.n_iter], dtype=np.float64),
        )

    @classmethod
    def load(cls, path):
        # np.load needs the .npz extension; savez appends it if missing.
        p = str(path)
        if not p.endswith(".npz"):
            p = p + ".npz"
        data = np.load(p)
        l2, lr, n_iter = data["hyper"]
        obj = cls(l2=float(l2), lr=float(lr), n_iter=int(n_iter))
        obj.mean_ = data["mean"]
        obj.std_ = data["std"]
        obj.w_ = data["w"]
        obj.b_ = float(data["b"][0])
        obj.temperature_ = float(data["temperature"][0])
        return obj


# --------------------------------------------------------------------------- #
# Calibration metric used by the tests and evaluate.py.
# --------------------------------------------------------------------------- #
def expected_calibration_error(p_child, y, n_bins=10):
    """Expected Calibration Error (ECE) with equal-width confidence bins.

    Confidence = probability of the predicted class; accuracy = correctness in
    each bin. ECE is the sample-weighted mean |confidence - accuracy|.
    """
    p_child = np.asarray(p_child, dtype=np.float64).reshape(-1)
    y = np.asarray(y, dtype=np.float64).reshape(-1)
    pred = (p_child >= 0.5).astype(int)
    conf = np.where(pred == 1, p_child, 1.0 - p_child)
    correct = (pred == y).astype(np.float64)
    edges = np.linspace(0.0, 1.0, n_bins + 1)
    ece = 0.0
    n = len(y)
    for i in range(n_bins):
        lo, hi = edges[i], edges[i + 1]
        mask = (conf > lo) & (conf <= hi) if i > 0 else (conf >= lo) & (conf <= hi)
        if not np.any(mask):
            continue
        ece += (np.sum(mask) / n) * abs(conf[mask].mean() - correct[mask].mean())
    return float(ece)
