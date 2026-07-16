#!/usr/bin/env python3
"""
DreamCodeVR+ voice age-gate — PRODUCTION feature upgrade path (wav2vec2 / WavLM).

This module is the drop-in replacement for `features.extract_features`. Instead
of hand-crafted DSP it pools frozen early-layer embeddings from a self-supervised
speech model (audEERING's public `wav2vec2-large-robust` age/gender backbone, or
WavLM / HuBERT). The literature (see README) reports ~4-5 yr MAE and ~97% child
vs adult accuracy on CMU-Kids with exactly this recipe, and layer-wise probing
shows the EARLY layers (roughly 1-7) carry the most child-relevant signal — so we
default to a mid-early layer and mean+std pool it into a fixed-length vector.

IMPORTANT: this file is NOT executed in the numpy-only environment. It imports
torch + transformers lazily and, if they are missing, raises a CLEAR, actionable
message. Importing this module never crashes; only *using* it requires torch.

To use in production, train `model.AgeClassifier` (or a small MLP head) on the
embeddings this module returns instead of on `features.extract_features` output —
the rest of the pipeline (decision.AgeGate) is unchanged.
"""

import numpy as np

# ------------------------------------------------------------------ #
# Guarded, lazy dependency check. Import never fails on a numpy-only box.
# ------------------------------------------------------------------ #
_TORCH_OK = True
_IMPORT_ERROR = ""
try:  # pragma: no cover - exercised only where torch is installed
    import torch  # noqa: F401
    import transformers  # noqa: F401
except Exception as exc:  # ImportError or partial install
    _TORCH_OK = False
    _IMPORT_ERROR = repr(exc)

_INSTALL_HINT = (
    "wav2vec2_features requires PyTorch + HuggingFace Transformers, which are "
    "NOT part of the numpy-only age_gate runtime.\n"
    "This module is the documented PRODUCTION upgrade path; install it only in "
    "the training/export environment:\n"
    "    pip install 'torch>=2.1' 'transformers>=4.40' 'torchaudio>=2.1'\n"
    "and use the audEERING backbone 'audeering/wav2vec2-large-robust-24-ft-age-"
    "gender' (or microsoft/wavlm-base-plus). For on-device Quest, export the "
    "frozen early layers to ONNX and int8-quantize."
)

# audEERING's public age/gender backbone is the recommended warm start.
DEFAULT_MODEL = "audeering/wav2vec2-large-robust-24-ft-age-gender"
# Early layers carry the most child-relevant signal (layer-wise probing).
DEFAULT_LAYER = 6


def is_available() -> bool:
    """True iff torch + transformers imported successfully in this process."""
    return _TORCH_OK


def _require_torch():
    if not _TORCH_OK:
        raise RuntimeError(_INSTALL_HINT + f"\n(original import error: {_IMPORT_ERROR})")


class Wav2Vec2FeatureExtractor:
    """Lazy wrapper around a frozen SSL speech backbone.

    Parameters
    ----------
    model_name : str
        HuggingFace model id (default: audEERING age/gender backbone).
    layer : int
        Hidden-state layer to pool (default: an early layer, best for children).
    device : str
        "cpu" or "cuda".
    """

    def __init__(self, model_name=DEFAULT_MODEL, layer=DEFAULT_LAYER, device="cpu"):
        _require_torch()  # fail clearly, early, if torch is unavailable
        import torch
        from transformers import AutoModel, AutoFeatureExtractor

        self.device = device
        self.layer = int(layer)
        self._torch = torch
        # output_hidden_states so we can pick an early layer.
        self.model = AutoModel.from_pretrained(
            model_name, output_hidden_states=True).to(device).eval()
        try:
            self.processor = AutoFeatureExtractor.from_pretrained(model_name)
        except Exception:
            self.processor = None  # some age/gender heads bundle preprocessing

    def embed(self, pcm: np.ndarray, sample_rate: int = 16000) -> np.ndarray:
        """Return a fixed-length (mean+std pooled) embedding for one utterance."""
        torch = self._torch
        x = np.asarray(pcm)
        if x.dtype.kind in ("i", "u"):  # int PCM -> [-1, 1] float
            x = x.astype(np.float32) / 32768.0
        else:
            x = x.astype(np.float32)
        with torch.no_grad():
            wav = torch.from_numpy(x).float().unsqueeze(0).to(self.device)
            out = self.model(wav)
            hs = out.hidden_states[self.layer]  # (1, T, H)
            mean = hs.mean(dim=1)
            std = hs.std(dim=1)
            vec = torch.cat([mean, std], dim=-1).squeeze(0)
            return vec.cpu().numpy().astype(np.float32)


# Module-level singleton so the drop-in function mirrors features.extract_features.
_EXTRACTOR = None


def extract_features(pcm: np.ndarray, sample_rate: int = 16000) -> np.ndarray:
    """Drop-in replacement for features.extract_features using SSL embeddings.

    Same call signature and return contract (a finite fixed-length float32
    vector) as the numpy DSP path — only the vector's meaning/length differ, so
    retrain the classifier head on these embeddings. Raises a clear error if
    torch/transformers are not installed.
    """
    _require_torch()
    global _EXTRACTOR
    if _EXTRACTOR is None:
        _EXTRACTOR = Wav2Vec2FeatureExtractor()
    return _EXTRACTOR.embed(pcm, sample_rate)


if __name__ == "__main__":
    if not is_available():
        print("wav2vec2_features: torch/transformers NOT installed (expected on "
              "the numpy-only box).")
        print(_INSTALL_HINT)
    else:
        print("wav2vec2_features: torch/transformers available; backbone =",
              DEFAULT_MODEL)
