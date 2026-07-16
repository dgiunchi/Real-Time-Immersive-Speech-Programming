#!/usr/bin/env python3
"""
DreamCodeVR+ voice age-gate — REAL acoustic DSP feature front-end (numpy only).

This module turns a chunk of 16 kHz mono int16 PCM into a small, FIXED-LENGTH
feature vector that genuinely separates children (<13) from adults. The physics
we exploit is real and well established:

  * Children have a SHORTER vocal tract and lighter vocal folds  -> HIGHER
    fundamental frequency (F0 ~ 250-300 Hz vs ~110-180 Hz in adults) and
    HIGHER formant / spectral energy (higher spectral centroid & rolloff).
  * Their speech tends to have higher zero-crossing rate (more high-frequency
    energy) and a wider, more variable pitch contour.

All features are computed with numpy's FFT + autocorrelation only — no scipy,
no librosa, no torch. The extractor is defensive: empty, silent, or very short
input never raises; it returns a finite fixed-length vector so the rest of the
pipeline (model.py -> decision.py) can always run.

Design intent: this is the *tiny/on-device* path. The production upgrade that
swaps these hand-crafted features for self-supervised speech embeddings lives in
`wav2vec2_features.py` and is a drop-in for `extract_features(...)`.

Convention used everywhere in the age_gate package: label y == 1 -> CHILD,
label y == 0 -> ADULT.
"""

import numpy as np

# --------------------------------------------------------------------------- #
# Public contract: a fixed-length, named feature vector.
# --------------------------------------------------------------------------- #
FEATURE_NAMES = (
    "f0_mean",          # mean fundamental frequency over voiced frames (Hz)
    "f0_median",        # robust central pitch (Hz)
    "f0_std",           # pitch variability (Hz)
    "f0_p10",           # 10th percentile pitch (Hz)
    "f0_p90",           # 90th percentile pitch (Hz)
    "f0_range",         # p90 - p10 (Hz) -> pitch span
    "voiced_fraction",  # fraction of active frames that were voiced (0..1)
    "centroid_mean",    # spectral centroid, mean (Hz)
    "centroid_std",     # spectral centroid, std (Hz)
    "rolloff_mean",     # 85% spectral rolloff, mean (Hz)
    "rolloff_std",      # 85% spectral rolloff, std (Hz)
    "zcr_mean",         # zero-crossing rate, mean (crossings / sample)
    "zcr_std",          # zero-crossing rate, std
    "log_energy_mean",  # log short-time energy, mean
    "log_energy_std",   # log short-time energy, std
)
FEATURE_DIM = len(FEATURE_NAMES)

# Framing / analysis constants (tuned for 16 kHz speech).
_FRAME_MS = 25.0        # 25 ms analysis window
_HOP_MS = 10.0          # 10 ms hop (standard for speech)
_MIN_F0 = 70.0          # lowest pitch we look for (deep adult male)
_MAX_F0 = 500.0         # highest pitch we look for (young child)
_VOICING_AC_THRESH = 0.30   # normalized autocorrelation peak to call a frame voiced
_ROLLOFF_PCT = 0.85     # spectral rolloff percentile


def _to_float_mono(pcm, sample_rate):
    """Coerce arbitrary input into a finite 1-D float32 signal in ~[-1, 1]."""
    x = np.asarray(pcm)
    if x.size == 0:
        return np.zeros(0, dtype=np.float32)
    # Flatten stereo/2-D just in case; average channels if a trailing axis exists.
    if x.ndim > 1:
        x = x.reshape(x.shape[0], -1).mean(axis=1)
    x = x.astype(np.float64, copy=False)
    # int16 PCM -> normalize; float input is assumed already ~[-1,1] but we
    # normalize by max if it is clearly integer-scaled.
    peak = np.max(np.abs(x)) if x.size else 0.0
    if peak > 1.5:  # looks like raw PCM counts, not normalized floats
        x = x / 32768.0
    x = np.nan_to_num(x, nan=0.0, posinf=0.0, neginf=0.0)
    return x.astype(np.float32, copy=False)


def _framed(x, frame_len, hop):
    """Return a (n_frames, frame_len) float array. Never empty for non-empty x."""
    n = x.shape[0]
    if n == 0:
        return np.zeros((0, frame_len), dtype=np.float32)
    if n < frame_len:
        # Pad a single short frame up to frame_len so we still emit something.
        pad = np.zeros(frame_len, dtype=np.float32)
        pad[:n] = x
        return pad[None, :]
    n_frames = 1 + (n - frame_len) // hop
    idx = np.arange(frame_len)[None, :] + hop * np.arange(n_frames)[:, None]
    return x[idx]


def _frame_pitch(frame, sample_rate, min_lag, max_lag):
    """Autocorrelation pitch for one (already windowed) frame.

    Returns (f0_hz, is_voiced). f0_hz is 0.0 when unvoiced.
    """
    frame = frame - frame.mean()
    energy = float(np.dot(frame, frame))
    if energy <= 1e-8:
        return 0.0, False
    # Full autocorrelation; keep non-negative lags.
    ac = np.correlate(frame, frame, mode="full")
    ac = ac[frame.shape[0] - 1:]
    r0 = ac[0]
    if r0 <= 0.0:
        return 0.0, False
    hi = min(max_lag, ac.shape[0] - 1)
    if hi <= min_lag:
        return 0.0, False
    seg = ac[min_lag:hi + 1]
    k = int(np.argmax(seg))
    peak = seg[k]
    lag = min_lag + k
    if peak / r0 < _VOICING_AC_THRESH or lag <= 0:
        return 0.0, False
    return sample_rate / float(lag), True


def _spectral_stats(frame_win, freqs):
    """Spectral centroid and 85% rolloff for one windowed frame."""
    mag = np.abs(np.fft.rfft(frame_win))
    total = mag.sum()
    if total <= 1e-8:
        return 0.0, 0.0, False
    centroid = float(np.dot(freqs, mag) / total)
    cumulative = np.cumsum(mag)
    thresh = _ROLLOFF_PCT * total
    idx = int(np.searchsorted(cumulative, thresh))
    idx = min(idx, freqs.shape[0] - 1)
    rolloff = float(freqs[idx])
    return centroid, rolloff, True


def extract_features(pcm: np.ndarray, sample_rate: int = 16000) -> np.ndarray:
    """Extract a fixed-length acoustic feature vector from int16/float PCM.

    Parameters
    ----------
    pcm : np.ndarray
        Mono audio. int16 PCM counts or float32 in [-1, 1]. Any shape/emptiness
        is tolerated; stereo is down-mixed.
    sample_rate : int
        Sampling rate in Hz (default 16000).

    Returns
    -------
    np.ndarray
        A finite float32 vector of length FEATURE_DIM (see FEATURE_NAMES).
        For empty/silent input a neutral all-zero vector is returned.
    """
    out = np.zeros(FEATURE_DIM, dtype=np.float32)
    x = _to_float_mono(pcm, sample_rate)
    if x.size == 0:
        return out

    frame_len = max(8, int(round(sample_rate * _FRAME_MS / 1000.0)))
    hop = max(1, int(round(sample_rate * _HOP_MS / 1000.0)))
    frames = _framed(x, frame_len, hop)
    if frames.shape[0] == 0:
        return out

    window = np.hamming(frame_len).astype(np.float32)
    freqs = np.fft.rfftfreq(frame_len, d=1.0 / sample_rate)
    min_lag = max(1, int(np.floor(sample_rate / _MAX_F0)))
    max_lag = int(np.ceil(sample_rate / _MIN_F0))

    # Per-frame energy: used to decide which frames are "active" (not silence).
    energies = np.mean(frames.astype(np.float64) ** 2, axis=1)
    active_thresh = max(1e-7, 0.02 * float(np.max(energies)))
    active_mask = energies >= active_thresh

    f0s = []
    centroids = []
    rolloffs = []
    zcrs = []
    log_energies = []
    n_active = 0
    n_voiced = 0

    for i in range(frames.shape[0]):
        if not active_mask[i]:
            continue
        n_active += 1
        frame = frames[i]
        # Pitch from the raw (DC-removed) frame.
        f0, voiced = _frame_pitch(frame, sample_rate, min_lag, max_lag)
        if voiced:
            f0s.append(f0)
            n_voiced += 1
        # Spectral stats from a windowed frame.
        fw = frame * window
        c, r, ok = _spectral_stats(fw, freqs)
        if ok:
            centroids.append(c)
            rolloffs.append(r)
        # Zero-crossing rate (crossings per sample).
        signs = np.sign(frame)
        signs[signs == 0] = 1.0
        zcr = 0.5 * np.mean(np.abs(np.diff(signs)))
        zcrs.append(float(zcr))
        log_energies.append(float(np.log(energies[i] + 1e-10)))

    if n_active == 0:
        return out

    f0s = np.asarray(f0s, dtype=np.float64)
    if f0s.size > 0:
        out[0] = np.mean(f0s)
        out[1] = np.median(f0s)
        out[2] = np.std(f0s)
        out[3] = np.percentile(f0s, 10)
        out[4] = np.percentile(f0s, 90)
        out[5] = out[4] - out[3]
    out[6] = n_voiced / float(n_active)

    if len(centroids) > 0:
        c = np.asarray(centroids, dtype=np.float64)
        r = np.asarray(rolloffs, dtype=np.float64)
        out[7] = np.mean(c)
        out[8] = np.std(c)
        out[9] = np.mean(r)
        out[10] = np.std(r)
    if len(zcrs) > 0:
        z = np.asarray(zcrs, dtype=np.float64)
        out[11] = np.mean(z)
        out[12] = np.std(z)
    if len(log_energies) > 0:
        e = np.asarray(log_energies, dtype=np.float64)
        out[13] = np.mean(e)
        out[14] = np.std(e)

    return np.nan_to_num(out, nan=0.0, posinf=0.0, neginf=0.0).astype(np.float32)


def batch_extract(pcm_list, sample_rate: int = 16000) -> np.ndarray:
    """Convenience: stack extract_features over an iterable of PCM chunks."""
    rows = [extract_features(p, sample_rate) for p in pcm_list]
    if not rows:
        return np.zeros((0, FEATURE_DIM), dtype=np.float32)
    return np.vstack(rows).astype(np.float32)


if __name__ == "__main__":
    # Tiny smoke demo: a synthetic 250 Hz "child-ish" tone vs 120 Hz "adult-ish".
    sr = 16000
    t = np.arange(sr) / sr
    for name, f0 in (("child~250Hz", 250.0), ("adult~120Hz", 120.0)):
        tone = 0.3 * np.sin(2 * np.pi * f0 * t) + 0.1 * np.sin(2 * np.pi * 2 * f0 * t)
        pcm = (tone * 32767).astype(np.int16)
        v = extract_features(pcm, sr)
        print(f"{name:>12}: f0_mean={v[0]:6.1f}  centroid={v[7]:7.1f}  zcr={v[11]:.3f}")
