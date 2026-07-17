# XR Security Evaluation — an automated attack/defence benchmark

An **automated, local, reproducible** evaluation of how well the DreamCodeVR+ safety
backend defends a speech-to-code XR system against five classes of privacy and
manipulation attack. It answers the core question directly: **how many attacks succeed
with no defence, and how many does our backend stop?** No Meta Quest required — every
verdict is produced by the *real* validator (`dcvr_csharp_policy`) on the local machine.

This is **additive**: it reuses the existing validator, hardening profiles and attack
strings, and changes none of the 128-vector / red-team code.

## Run it

```bash
# 1. numbers (prints the headline table, writes results.json)
cargo run -p xr-security-eval

# 2. the visible demo (browse each attack, see it succeed vs be blocked, live)
cargo run -p xr-security-eval --bin xr-security-demo      # → http://127.0.0.1:7979

# 3. the paper figures (after step 1)
python3 apps/xr-security-eval/report/make_figures.py      # → figures/*.png

# regenerate the corpus if edited
python3 apps/xr-security-eval/attacks/gen_corpus.py
```

## Threat model — five attack classes

Grounded in the XR-security literature and realised as concrete *speak-to-C#* exploits
(each a `GeneratedBehaviour : MonoBehaviour`, the exact shape the pipeline emits):

| Class | The harm | Basis |
|---|---|---|
| **Biometric** | reads the user's body — camera, microphone, eye-gaze, hand skeleton, haptics | Nair et al., *USENIX Security 2023* (attribute inference) |
| **Positional** | head/hand pose exfiltrated for re-identification / tracking (>94% re-id from motion) | Nair et al., 2023 |
| **Surroundings** | passthrough cameras / spatial map capture the user's real room | XR passthrough-privacy |
| **Human-joystick** | rotates/moves the world to physically herd the user into a real wall/door | Casey et al., *IEEE TDSC 2021* |
| **Chaperone** | moves or disables the guardian safety boundary → walks the user into hazards | Casey et al., 2021 |
| *Benign* | 12 legitimate creative commands (house, snowman, recolour, MR authoring) — must always pass |

40 attacks (8 per class) + 12 benign controls.

## Defence — three levels

The backend is evaluated at three settings so the *layering* is visible:

- **No defence** — the original DreamCodeVR path: unvalidated C# is sent straight to the
  runtime, so any generated code runs.
- **Security-only** (`HardeningProfile::CreativeFreedom`) — the always-on system-access
  denylist (`System.IO/Net/Reflection`, `UnityWebRequest`, `Process`, …).
- **Fully hardened** (`HardeningProfile::DeployHardened`) — adds the perceptual / XR-device
  denylist (`WebCamTexture`, `OVRManager`, `OVRBoundary`, `InputTracking`, XR namespaces, …).

## Method

For every payload the harness calls `validate_csharp_freeform_profile(code, profile)` — the
same validator the live pipeline uses — and records whether the code is **blocked before it
can run**. "Attack success" = the malicious code is *not* blocked (it would execute). This
is a code-admission evaluation (in the spirit of SecurityEval / SALLM): it measures whether
the defence stops the exploit from reaching the headset, entirely on the local machine.

## Results

```
class          N   no-defence   security-only   fully-hardened
biometric      8      100%           38%              0%
chaperone      8      100%           88%              0%
joystick       8      100%          100%             25%
positional     8      100%           62%              0%
surroundings   8      100%           25%              0%
OVERALL       40      100%           62%              5%
```

- **No defence: 100% of attacks succeed.**
- **Fully hardened: 95% mitigated** (38/40 blocked before execution).
- **Benign: 100% still allowed** at every level (0/12 over-blocked) — creative freedom is
  preserved, including dual-use MR authoring (passthrough, spatial anchors).

### The headline finding — defences must be *layered*
A code-security filter alone (**security-only**) stops privacy *exfiltration* (which uses
system/network APIs) but **misses pure XR manipulation**: the human-joystick class stays at
**100%** and chaperone at **88%**, because those attacks touch no system API — only the
**perceptual/XR** layer catches them. Different attack classes require different defence
layers; a single filter is insufficient for XR.

### Honest residual (2/40)
Two human-joystick attacks (`joy-05`, `joy-06`) rotate/translate `Camera.main.transform` —
lexically indistinguishable from legitimate content authoring, so static validation cannot
block them without also blocking creation. They require the **runtime** `UserFrameGuardian`
(and/or the Mode-D sandbox), which is out of scope for this static, headset-free evaluation
and reported transparently rather than hidden.

## Limitations
- **Code-admission, not on-device execution.** We measure whether the malicious code is
  admitted, not the physical effect on a headset (no Quest by design). The runtime-only
  residual above is the class this cannot cover statically.
- The corpus is hand-authored (grounded in the literature + real fixtures); it demonstrates
  the classes, not an exhaustive attack space.

## Files
- `attacks/gen_corpus.py`, `attacks/corpus.json` — the benchmark (deterministic generator).
- `src/lib.rs` — the reusable eval core (calls the real validator at 3 levels).
- `src/main.rs` — the CLI (table + `results.json`).
- `src/bin/demo.rs` + `src/bin/demo.html` — the localhost visible demo.
- `report/make_figures.py` — the paper figures.
- `tests/eval_tests.rs` — locks the corpus shape and the layered-defence story.
