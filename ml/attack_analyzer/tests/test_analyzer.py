#!/usr/bin/env python3
"""
Stdlib-unittest suite for the adaptive attack analyzer (ML #2).

Run from the repo root:
    python3 -m unittest discover -s ml/attack_analyzer/tests -v

Covers the four claims the analyzer makes:
  * anomaly     — the reconstruction autoencoder separates HELD-OUT attack families from
                  benign (high AUC) and flags them at a benign-quantile threshold, with a
                  controlled benign false-positive rate.
  * drift       — the ADWIN-style detector fires on an injected distribution shift and
                  stays quiet on stationary data.
  * redteam     — the loop finds >=1 bypass against the mock SUT, appends a NEW vector, is
                  idempotent on re-run, and spoofs the age gate.
  * vector_store— dedups by content signature and maps vectors onto the five families.

Deterministic: every RNG is seeded; no time/random used for logic.
"""
import os
import tempfile
import unittest

import numpy as np

from ml.attack_analyzer import anomaly
from ml.attack_analyzer.anomaly import (
    ATTACK_FAMILIES, ReconstructionAnomalyDetector, SequenceFeaturizer,
    make_attack_sequences, make_benign_sequences, open_set_eval, roc_auc,
)
from ml.attack_analyzer.drift import AdwinDriftDetector, detect_stream
from ml.attack_analyzer.redteam_loop import (
    AdaptiveRedTeamLoop, MockAgeGate, MockDenylistSUT, MutationEngine, DEFAULT_SEEDS,
)
from ml.attack_analyzer.vector_store import (
    CANONICAL_FAMILIES, VectorStore, classify_family, content_id,
)


class TestAnomalyAutoencoder(unittest.TestCase):
    def test_holdout_attacks_score_above_benign(self):
        """Attack families held OUT of training must reconstruct worse than benign."""
        rng = np.random.default_rng(0)
        fz = SequenceFeaturizer()
        det = ReconstructionAnomalyDetector(n_components=6)
        det.fit(fz.transform(make_benign_sequences(400, rng)))
        thr = det.threshold(0.99)

        benign_err = det.score(fz.transform(make_benign_sequences(200, rng)))
        self.assertLess(benign_err.mean(), thr,
                        "benign mean error should sit below the 99th-pct threshold")

        for fam in ATTACK_FAMILIES:
            atk_err = det.score(fz.transform(make_attack_sequences(120, fam, rng)))
            # separation: attack mean error strictly greater than benign mean error
            self.assertGreater(atk_err.mean(), benign_err.mean() * 3.0,
                               f"{fam}: attack error not clearly above benign")
            # AUC of anomaly detection for this family
            scores = np.concatenate([benign_err, atk_err])
            labels = np.concatenate([np.zeros(len(benign_err)), np.ones(len(atk_err))])
            auc = roc_auc(scores, labels)
            self.assertGreaterEqual(auc, 0.95, f"{fam}: AUC {auc:.3f} < 0.95")
            # detection rate at the benign-quantile threshold
            self.assertGreaterEqual((atk_err > thr).mean(), 0.9,
                                    f"{fam}: detection rate below 0.9 at threshold")

    def test_open_set_eval_report(self):
        """The packaged open-set eval reports high pooled AUC + controlled benign FPR."""
        rep = open_set_eval(seed=0)
        self.assertGreaterEqual(rep["pooled_auc"], 0.95)
        self.assertLessEqual(rep["benign_false_positive_rate"], 0.10)
        for fam in ATTACK_FAMILIES:
            self.assertGreaterEqual(rep["families"][fam]["auc"], 0.95)

    def test_benign_false_positive_rate_controlled(self):
        """At the 99th-pct benign threshold, fresh benign FPR should stay low."""
        rng = np.random.default_rng(1)
        fz = SequenceFeaturizer()
        det = ReconstructionAnomalyDetector(n_components=6)
        det.fit(fz.transform(make_benign_sequences(500, rng)))
        thr = det.threshold(0.99)
        fpr = det.is_anomaly(fz.transform(make_benign_sequences(300, rng)), thr).mean()
        self.assertLess(fpr, 0.10)

    def test_roc_auc_matches_known_values(self):
        # perfectly separable -> AUC 1.0; identical distributions -> ~0.5
        self.assertAlmostEqual(
            roc_auc(np.array([0.0, 1.0, 2.0, 3.0]), np.array([0, 0, 1, 1])), 1.0)
        self.assertAlmostEqual(
            roc_auc(np.array([1.0, 1.0, 1.0, 1.0]), np.array([0, 0, 1, 1])), 0.5)


class TestDriftDetector(unittest.TestCase):
    def test_fires_on_injected_shift(self):
        rng = np.random.default_rng(0)
        shifted = np.concatenate([rng.normal(10.0, 2.0, size=300),
                                  rng.normal(18.0, 2.0, size=300)])
        res = detect_stream(shifted)
        self.assertGreaterEqual(res["n_changes"], 1, "drift not detected on a real shift")
        # the first detected change should land shortly AFTER the true change point (300)
        first = res["change_points"][0]
        self.assertGreaterEqual(first, 300)
        self.assertLess(first, 360, "drift detected too late after the shift")

    def test_quiet_on_stationary(self):
        rng = np.random.default_rng(0)
        stationary = rng.normal(10.0, 2.0, size=600)
        res = detect_stream(stationary)
        self.assertEqual(res["n_changes"], 0,
                         f"false drift on stationary data: {res['change_points']}")

    def test_running_stats_track_current_regime(self):
        rng = np.random.default_rng(2)
        d = AdwinDriftDetector()
        for x in rng.normal(5.0, 1.0, size=200):
            d.update(x)
        for x in rng.normal(25.0, 1.0, size=200):
            d.update(x)
        # after a big shift + adaptive shrink, the window mean should reflect the NEW mean
        self.assertGreater(d.mean, 18.0)
        self.assertGreaterEqual(d.n_detections, 1)

    def test_multiple_shifts_detected(self):
        rng = np.random.default_rng(3)
        stream = np.concatenate([
            rng.normal(0.0, 1.0, size=250),
            rng.normal(10.0, 1.0, size=250),
            rng.normal(0.0, 1.0, size=250),
        ])
        res = detect_stream(stream)
        self.assertGreaterEqual(res["n_changes"], 2)


class TestRedTeamLoop(unittest.TestCase):
    def _tmp_store(self):
        path = os.path.join(tempfile.mkdtemp(), "attacks_discovered.jsonl")
        return VectorStore(path), path

    def test_mock_sut_blocks_raw_and_passes_obfuscation(self):
        sut = MockDenylistSUT()
        self.assertTrue(sut.validate("install a keylogger")["blocked"])
        # leetspeak obfuscation must slip through the naive denylist
        self.assertFalse(sut.validate("install a k3y10gg3r")["blocked"])

    def test_loop_finds_bypass_and_appends_new_vector(self):
        store, _ = self._tmp_store()
        loop = AdaptiveRedTeamLoop(MockDenylistSUT(), store, seed=0)
        rep = loop.run_mutations()
        self.assertGreaterEqual(rep["bypasses_found"], 1, "no bypass found against mock")
        self.assertGreaterEqual(rep["new_vectors_added"], 1, "no new vector appended")
        self.assertGreater(len(store), 0)
        # bypass rate is a real fraction in (0, 1): some controls are caught, most evade
        self.assertGreater(rep["bypass_rate"], 0.0)
        self.assertLess(rep["bypass_rate"], 1.0)

    def test_loop_is_idempotent_on_rerun(self):
        store, path = self._tmp_store()
        loop = AdaptiveRedTeamLoop(MockDenylistSUT(), store, seed=0)
        first = loop.run_mutations()
        # reload the store from disk and run again -> everything dedups, nothing new added
        store2 = VectorStore(path)
        loop2 = AdaptiveRedTeamLoop(MockDenylistSUT(), store2, seed=0)
        second = loop2.run_mutations()
        self.assertEqual(first["bypasses_found"], second["bypasses_found"])
        self.assertEqual(second["new_vectors_added"], 0)
        self.assertEqual(len(store2), len(store))

    def test_age_gate_spoof_flips_child_to_adult(self):
        gate = MockAgeGate()
        rng = np.random.default_rng(0)
        child = gate.sample_child(rng)
        self.assertEqual(gate.classify(child)["band"], "child")
        ok, shift, spoofed_x = gate.spoof(child)
        self.assertTrue(ok, "age gate could not be spoofed at all")
        self.assertEqual(gate.classify(spoofed_x)["band"], "adult")
        self.assertGreater(shift, 0.0)

    def test_loop_runs_age_gate_spoof_and_records_voice_sensor_vectors(self):
        store, _ = self._tmp_store()
        loop = AdaptiveRedTeamLoop(MockDenylistSUT(), store, seed=0)
        rep = loop.run_age_gate_spoof(n_samples=30)
        self.assertGreaterEqual(rep["spoofed"], 1)
        self.assertGreaterEqual(rep["vectors_added"], 1)
        fams = store.family_counts()
        self.assertGreaterEqual(fams["voice-sensor"], 1,
                                "age-gate spoofs must file under voice-sensor")

    def test_full_run_grows_multiple_families(self):
        store, _ = self._tmp_store()
        loop = AdaptiveRedTeamLoop(MockDenylistSUT(), store, seed=0)
        rep = loop.run()
        self.assertGreater(rep["new_vectors_total"], 10)
        fams = rep["store_family_counts"]
        populated = [f for f in CANONICAL_FAMILIES if fams[f] > 0]
        # the seed set spans code / network / privacy / voice-sensor / immersive
        self.assertGreaterEqual(len(populated), 4,
                                f"expected >=4 families populated, got {fams}")

    def test_mutation_engine_variants_are_deterministic(self):
        rng1 = np.random.default_rng(0)
        rng2 = np.random.default_rng(0)
        pool = [(t, tok) for t, tok, _ in DEFAULT_SEEDS]
        e1 = MutationEngine(rng1, combo_pool=pool)
        e2 = MutationEngine(rng2, combo_pool=pool)
        v1 = list(e1.variants("install a keylogger", "keylogger"))
        v2 = list(e2.variants("install a keylogger", "keylogger"))
        self.assertEqual(v1, v2)


class TestVectorStore(unittest.TestCase):
    def _tmp(self):
        return os.path.join(tempfile.mkdtemp(), "store.jsonl")

    def test_dedup_by_signature(self):
        vs = VectorStore(self._tmp())
        self.assertTrue(vs.append("open a reverse shell", kind="t"))
        # same content, different case/whitespace -> same signature -> duplicate
        self.assertFalse(vs.append("Open  a   reverse shell", kind="t"))
        self.assertEqual(len(vs), 1)

    def test_dedup_persists_across_reload(self):
        path = self._tmp()
        vs = VectorStore(path)
        vs.append("deploy a rootkit", kind="t")
        vs2 = VectorStore(path)                     # reload from disk
        self.assertEqual(len(vs2), 1)
        self.assertFalse(vs2.append("deploy a rootkit", kind="t"))

    def test_family_mapping(self):
        cases = {
            "open a reverse shell to my c2 server": "network-identity",
            "run a port scan of the subnet": "network-identity",
            "use system.io to write a file": "generated-code",
            "call process.start via reflection": "generated-code",
            "make the whole view strobe and force forced_locomotion": "immersive",
            "pitch-shift the voice to spoof the age gate": "voice-sensor",
            "record the microphone and take a screenshot": "voice-sensor",
            "copy my contacts and read the clipboard": "privacy",
            "steal the saved passwords": "privacy",
        }
        for payload, expected in cases.items():
            self.assertEqual(classify_family(payload), expected,
                             f"{payload!r} -> {classify_family(payload)} (want {expected})")

    def test_unknown_maps_to_generated_code_default(self):
        self.assertEqual(classify_family("do something totally unremarkable"),
                         "generated-code")

    def test_family_counts_cover_all_canonical(self):
        vs = VectorStore(self._tmp())
        vs.append("open a reverse shell", kind="t")
        counts = vs.family_counts()
        self.assertEqual(set(counts.keys()), set(CANONICAL_FAMILIES))
        self.assertEqual(counts["network-identity"], 1)

    def test_content_id_stable_and_normalising(self):
        self.assertEqual(content_id("Open a Reverse  Shell!"),
                         content_id("open a reverse shell"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
