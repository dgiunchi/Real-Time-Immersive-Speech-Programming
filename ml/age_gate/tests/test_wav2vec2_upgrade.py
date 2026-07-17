#!/usr/bin/env python3
"""
DreamCodeVR+ voice age-gate — contract tests for the wav2vec2 UPGRADE PATH.

`wav2vec2_features.py` is the documented production upgrade (SSL embeddings)
that is intentionally NOT part of the numpy-only runtime. These tests pin its
*contract* so the module can never silently rot:

  * importing it never crashes on a numpy-only box (torch imported lazily);
  * on the numpy-only runtime, *using* it raises a CLEAR, actionable error
    (never a bare ImportError / AttributeError);
  * its constants point at a real SSL backbone;
  * (skip-gated, torch-only) a forward pass returns a finite, fixed-length
    float32 vector whose length is independent of utterance duration — the same
    return contract as the numpy DSP `features.extract_features`.

The torch-present test SKIPS on the numpy-only runtime and runs only in a
training/export environment where torch + transformers are deliberately
installed, so this suite adds coverage without adding a runtime dependency.
"""

import os
import sys
import unittest

import numpy as np

# Make the age_gate package modules importable regardless of cwd (same pattern
# as test_age_gate.py).
_HERE = os.path.dirname(os.path.abspath(__file__))
_AGE_GATE_DIR = os.path.dirname(_HERE)
if _AGE_GATE_DIR not in sys.path:
    sys.path.insert(0, _AGE_GATE_DIR)

import wav2vec2_features as w2v  # noqa: E402


class Wav2Vec2UpgradeContract(unittest.TestCase):
    def test_import_never_crashes_and_reports_availability(self):
        # The whole point of the lazy guard: importing the module must succeed on
        # the numpy-only box, and is_available() must answer truthfully as a bool.
        self.assertIsInstance(w2v.is_available(), bool)

    def test_constants_point_at_an_ssl_backbone(self):
        self.assertIn("wav2vec2", w2v.DEFAULT_MODEL.lower())
        self.assertIsInstance(w2v.DEFAULT_LAYER, int)
        self.assertGreaterEqual(w2v.DEFAULT_LAYER, 0)

    @unittest.skipIf(w2v.is_available(), "torch present: the torch-absent guard path is not exercised")
    def test_torch_absent_fails_with_actionable_hint(self):
        # On the numpy-only runtime, USING the upgrade path must fail closed with a
        # RuntimeError that names the missing deps and the install command — not a
        # cryptic ImportError deep in transformers.
        with self.assertRaises(RuntimeError) as cm:
            w2v.extract_features(np.zeros(16000, dtype=np.float32))
        msg = str(cm.exception).lower()
        self.assertIn("torch", msg)
        self.assertIn("transformers", msg)
        # Constructing the extractor directly must fail the same clear way.
        with self.assertRaises(RuntimeError):
            w2v.Wav2Vec2FeatureExtractor()

    @unittest.skipUnless(w2v.is_available(), "torch/transformers not installed (numpy-only runtime)")
    def test_torch_present_extracts_finite_fixed_length_vector(self):
        # Runs ONLY where torch is deliberately installed (training/export env).
        # Pins the drop-in contract: a finite float32 vector of a fixed length that
        # does NOT depend on the utterance duration.
        v_short = w2v.extract_features(np.zeros(16000, dtype=np.float32))
        v_long = w2v.extract_features(np.zeros(24000, dtype=np.float32))
        self.assertEqual(v_short.dtype, np.float32)
        self.assertTrue(np.all(np.isfinite(v_short)))
        self.assertEqual(v_short.shape, v_long.shape)


if __name__ == "__main__":
    unittest.main(verbosity=2)
