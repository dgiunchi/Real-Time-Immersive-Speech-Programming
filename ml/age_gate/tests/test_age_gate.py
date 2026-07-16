#!/usr/bin/env python3
"""
DreamCodeVR+ voice age-gate — stdlib unittest suite.

Covers the four pillars:
  * features.py  : never crashes, always finite/fixed-length; pitch actually
                   tracks child (high F0) vs adult (low F0).
  * model.py     : fit -> predict reaches high accuracy on separable synthetic
                   data; temperature scaling reduces ECE.
  * decision.py  : the fail-safe policy — low confidence / ambiguous -> "unknown"
                   (treated as child); clear child -> "child"; clear adult ->
                   "adult"; session aggregation stabilises and resists outliers.
  * end-to-end   : the DSP features feed the model which feeds the gate.
"""

import os
import sys
import unittest

# Make the age_gate package modules importable regardless of cwd.
_HERE = os.path.dirname(os.path.abspath(__file__))
_AGE_GATE_DIR = os.path.dirname(_HERE)
if _AGE_GATE_DIR not in sys.path:
    sys.path.insert(0, _AGE_GATE_DIR)

import numpy as np

import features
import decision
from model import AgeClassifier, expected_calibration_error
from decision import AgeGate, BAND_CHILD, BAND_ADULT, BAND_UNKNOWN
import train as trainmod


def synth_voice_pcm(f0, seconds=0.6, sample_rate=16000, seed=0, formant_boost=1.0):
    """Synthesize a simple voiced tone-complex (fundamental + harmonics) as int16.

    Higher f0 (and formant_boost) => more high-frequency energy => child-like.
    """
    rng = np.random.default_rng(seed)
    n = int(seconds * sample_rate)
    t = np.arange(n) / sample_rate
    sig = np.zeros(n)
    # A few harmonics with decaying amplitude; boost highs a touch for children.
    for k in range(1, 8):
        amp = (1.0 / k) * (formant_boost if k >= 3 else 1.0)
        sig += amp * np.sin(2 * np.pi * f0 * k * t)
    sig /= np.max(np.abs(sig)) + 1e-9
    sig += 0.01 * rng.standard_normal(n)  # a whisper of noise
    return (np.clip(sig, -1, 1) * 32767).astype(np.int16)


class TestFeatures(unittest.TestCase):
    def test_fixed_length_and_finite_on_edge_inputs(self):
        cases = [
            np.zeros(0, dtype=np.int16),                 # empty
            np.zeros(5, dtype=np.int16),                 # tiny (< 1 frame)
            np.array([123], dtype=np.int16),             # single sample
            np.zeros(16000, dtype=np.int16),             # pure silence
            (np.random.default_rng(0).standard_normal(16000) * 3000).astype(np.int16),
        ]
        for pcm in cases:
            v = features.extract_features(pcm, 16000)
            self.assertEqual(v.shape, (features.FEATURE_DIM,))
            self.assertTrue(np.all(np.isfinite(v)),
                            msg=f"non-finite features for input len={len(pcm)}")

    def test_empty_is_neutral_zero(self):
        v = features.extract_features(np.zeros(0, dtype=np.int16))
        self.assertTrue(np.allclose(v, 0.0))

    def test_accepts_float_and_2d_input(self):
        # float in [-1,1] and a fake stereo array should both work.
        f = 0.2 * np.sin(2 * np.pi * 200 * np.arange(16000) / 16000)
        self.assertEqual(features.extract_features(f).shape, (features.FEATURE_DIM,))
        stereo = np.stack([f, f], axis=1)
        self.assertEqual(features.extract_features(stereo).shape,
                         (features.FEATURE_DIM,))

    def test_pitch_tracks_child_vs_adult(self):
        # Child ~ 270 Hz should measure a HIGHER f0 than adult ~ 120 Hz.
        child = synth_voice_pcm(270.0, seed=1, formant_boost=1.6)
        adult = synth_voice_pcm(120.0, seed=2, formant_boost=1.0)
        fc = features.extract_features(child)
        fa = features.extract_features(adult)
        f0_idx = features.FEATURE_NAMES.index("f0_mean")
        self.assertGreater(fc[f0_idx], fa[f0_idx])
        # F0 should land in a physically plausible range near the true pitch.
        self.assertTrue(200.0 < fc[f0_idx] < 340.0, msg=f"child f0={fc[f0_idx]}")
        self.assertTrue(90.0 < fa[f0_idx] < 170.0, msg=f"adult f0={fa[f0_idx]}")
        # Children also carry a higher spectral centroid.
        c_idx = features.FEATURE_NAMES.index("centroid_mean")
        self.assertGreater(fc[c_idx], fa[c_idx])


class TestModel(unittest.TestCase):
    def test_fit_predict_high_accuracy(self):
        rng = np.random.default_rng(0)
        X, y, _ = trainmod.make_synthetic_dataset(n=3000, rng=rng)
        ntr = 2400
        model = AgeClassifier(l2=1e-4, n_iter=1000).fit(X[:ntr], y[:ntr])
        acc = np.mean(model.predict(X[ntr:]) == y[ntr:])
        self.assertGreater(acc, 0.90, msg=f"accuracy too low: {acc:.3f}")

    def test_predict_proba_shape_and_range(self):
        rng = np.random.default_rng(1)
        X, y, _ = trainmod.make_synthetic_dataset(n=500, rng=rng)
        model = AgeClassifier(n_iter=300).fit(X, y)
        proba = model.predict_proba(X)
        self.assertEqual(proba.shape, (len(y), 2))
        self.assertTrue(np.allclose(proba.sum(axis=1), 1.0))
        self.assertTrue(np.all((proba >= 0) & (proba <= 1)))

    def test_temperature_scaling_reduces_ece(self):
        # Realistic miscalibration: train on a CLEAN/tight corpus but calibrate +
        # evaluate on a NOISIER deployment distribution -> the raw model is
        # over-confident, which temperature scaling is designed to fix.
        Xtr, ytr, _ = trainmod.make_synthetic_dataset(
            n=3000, rng=np.random.default_rng(2), std_scale=0.7)
        Xval, yval, _ = trainmod.make_synthetic_dataset(
            n=1500, rng=np.random.default_rng(5), std_scale=1.2)
        Xte, yte, _ = trainmod.make_synthetic_dataset(
            n=1500, rng=np.random.default_rng(9), std_scale=1.2)
        model = AgeClassifier(l2=1e-4, lr=0.5, n_iter=1500).fit(Xtr, ytr)

        ece_before = expected_calibration_error(model.predict_child_proba(Xte), yte)
        acc_before = np.mean(model.predict(Xte) == yte)
        model.fit_temperature(Xval, yval)
        ece_after = expected_calibration_error(model.predict_child_proba(Xte), yte)
        acc_after = np.mean(model.predict(Xte) == yte)

        # Accuracy is preserved (temperature never changes the argmax).
        self.assertAlmostEqual(acc_before, acc_after, places=6)
        # Temperature > 1 softens the over-confident logits.
        self.assertGreater(model.temperature_, 1.0)
        # Calibration strictly improves by a clear margin (not just the tolerance).
        self.assertLess(ece_after, ece_before,
                        msg=f"ECE not reduced: {ece_before:.4f} -> {ece_after:.4f}")
        self.assertLess(ece_after, 0.03)

    def test_save_load_roundtrip(self):
        import tempfile
        rng = np.random.default_rng(3)
        X, y, _ = trainmod.make_synthetic_dataset(n=800, rng=rng)
        model = AgeClassifier(n_iter=300).fit(X, y)
        model.fit_temperature(X, y)
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "m.npz")
            model.save(path)
            loaded = AgeClassifier.load(path)
        self.assertTrue(np.allclose(model.predict_child_proba(X),
                                    loaded.predict_child_proba(X)))
        self.assertAlmostEqual(model.temperature_, loaded.temperature_)


class TestDecision(unittest.TestCase):
    def test_clear_child_probs_give_child(self):
        d = decision.decide_once([0.95, 0.92, 0.97, 0.9, 0.93])
        self.assertEqual(d["band"], BAND_CHILD)
        self.assertEqual(d["effective_profile"], BAND_CHILD)
        self.assertFalse(d["abstain"])
        self.assertGreater(d["confidence"], 0.7)

    def test_clear_adult_probs_give_adult(self):
        d = decision.decide_once([0.03, 0.05, 0.02, 0.06, 0.04])
        self.assertEqual(d["band"], BAND_ADULT)
        self.assertEqual(d["effective_profile"], BAND_ADULT)
        self.assertFalse(d["abstain"])

    def test_low_confidence_is_unknown_and_treated_as_child(self):
        # Aggregate sits right at the ambiguous boundary.
        d = decision.decide_once([0.5, 0.48, 0.52, 0.49, 0.51])
        self.assertEqual(d["band"], BAND_UNKNOWN)
        # THE fail-safe: unknown must be handled as a child.
        self.assertEqual(d["effective_profile"], BAND_CHILD)
        self.assertTrue(d["abstain"])
        self.assertTrue(d["escalate"])

    def test_too_few_utterances_abstains(self):
        gate = AgeGate(min_utterances=3)
        gate.observe(0.98)
        d = gate.decide()  # only 1 utterance so far
        self.assertEqual(d["band"], BAND_UNKNOWN)
        self.assertTrue(d["abstain"])
        self.assertEqual(d["effective_profile"], BAND_CHILD)

    def test_never_emits_precise_age(self):
        d = decision.decide_once([0.9, 0.9, 0.9, 0.9])
        for key in d:
            self.assertNotIn("age", key.lower())
        self.assertIn(d["band"], (BAND_CHILD, "teen", BAND_ADULT, BAND_UNKNOWN))

    def test_session_aggregation_is_robust_to_outlier(self):
        # A stream of clear-child utterances with ONE spurious adult reading.
        gate = AgeGate()
        for p in [0.93, 0.95, 0.02, 0.94, 0.96, 0.92]:  # one outlier at 0.02
            gate.observe(p)
        d = gate.decide()
        # Median aggregation must not let a single outlier flip the verdict.
        self.assertEqual(d["band"], BAND_CHILD)

    def test_session_aggregation_stabilises(self):
        # As more consistent child utterances arrive, confidence should climb
        # and settle (never collapse), and the verdict should lock to child.
        gate = AgeGate(min_utterances=3)
        rng = np.random.default_rng(0)
        confidences = []
        band = None
        for i in range(30):
            gate.observe(float(np.clip(rng.normal(0.9, 0.05), 0, 1)))
            d = gate.decide()
            confidences.append(d["confidence"])
            band = d["band"]
        self.assertEqual(band, BAND_CHILD)
        # Late-session confidence is stable (small spread over the last 10 steps).
        late = np.array(confidences[-10:])
        self.assertLess(np.std(late), 0.05)
        self.assertGreater(late.mean(), 0.8)

    def test_abstain_escalate_flag_present(self):
        d = decision.decide_once([0.55, 0.5, 0.45])  # ambiguous
        self.assertTrue(d["escalate"])
        self.assertTrue(d["abstain"])


class TestEndToEnd(unittest.TestCase):
    def test_dsp_features_feed_model_and_gate(self):
        # Build a tiny labelled set from SYNTHESIZED audio, run the whole chain.
        rng = np.random.default_rng(0)
        X, y = [], []
        for i in range(40):
            f0 = float(rng.normal(275, 20))   # child
            X.append(features.extract_features(synth_voice_pcm(f0, seed=i, formant_boost=1.6)))
            y.append(1)
            f0 = float(rng.normal(120, 15))   # adult
            X.append(features.extract_features(synth_voice_pcm(f0, seed=100 + i)))
            y.append(0)
        X = np.array(X)
        y = np.array(y)
        self.assertTrue(np.all(np.isfinite(X)))
        model = AgeClassifier(l2=1e-3, n_iter=800).fit(X, y)
        acc = np.mean(model.predict(X) == y)
        self.assertGreater(acc, 0.9)

        # A confident child utterance, aggregated over a session, -> child band.
        child_probs = model.predict_child_proba(
            np.array([features.extract_features(synth_voice_pcm(280.0, seed=s, formant_boost=1.6))
                      for s in range(5)]))
        d = decision.decide_once(child_probs)
        self.assertIn(d["band"], (BAND_CHILD, BAND_UNKNOWN))
        self.assertEqual(d["effective_profile"], BAND_CHILD)


if __name__ == "__main__":
    unittest.main(verbosity=2)
