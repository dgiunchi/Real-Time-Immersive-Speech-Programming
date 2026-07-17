#!/usr/bin/env python3
"""Generate the paper figures from results.json (run the eval CLI first).

  cargo run -p xr-security-eval          # writes ../results.json
  python3 report/make_figures.py         # writes ../figures/*.png

Two figures:
  fig1_by_class.png  — malicious-payload pass rate per class at the 3 defence levels.
  fig2_overall.png   — overall pass-rate funnel + benign creative-freedom bar.
matplotlib only; no seaborn, no network.
"""

import json
import os

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
FIGDIR = os.path.join(ROOT, "figures")
os.makedirs(FIGDIR, exist_ok=True)

with open(os.path.join(ROOT, "results.json")) as f:
    R = json.load(f)

INK = "#141a2e"
COL = {"none": "#e0503f", "sec": "#b9791b", "hard": "#2f9e6f"}  # red / amber / green
LEVELS = ["No defence", "Security-only", "DeployHardened"]

plt.rcParams.update({
    "font.family": "DejaVu Sans", "font.size": 11, "axes.edgecolor": "#c9d0de",
    "axes.linewidth": 0.8, "text.color": INK, "axes.labelcolor": INK, "xtick.color": INK,
    "ytick.color": INK, "figure.facecolor": "white", "axes.facecolor": "white",
})

# ---------------- figure 1: by class ----------------
classes = R["summary"]["attack_classes"]
_LMAP = {"biometric":"Biometric","positional":"Positional /\nmotion","surroundings":"Env-imagery /\noutward-camera","joystick":"Human-joystick","chaperone":"Chaperone /\nboundary"}
labels = [_LMAP.get(c["class"], c["class"]) for c in classes]
n = [c["n"] for c in classes]
succ = np.array([[100 * s / cn for s in c["succeed"]] for c, cn in zip(classes, n)])  # (5,3)

fig, ax = plt.subplots(figsize=(9.6, 5.1))
y = np.arange(len(labels))
h = 0.26
for i, key in enumerate(["none", "sec", "hard"]):
    bars = ax.barh(y - h + i * h, succ[:, i], height=h, color=COL[key],
                   label=LEVELS[i], zorder=3)
    for b, v in zip(bars, succ[:, i]):
        ax.text(b.get_width() + 1.5, b.get_y() + b.get_height() / 2, f"{v:.1f}%",
                va="center", ha="left", fontsize=8.5, color="#555")
ax.set_yticks(y)
ax.set_yticklabels(labels, fontweight="bold", fontsize=9)
ax.invert_yaxis()
ax.set_xlim(0, 108)
ax.set_xlabel("Malicious-payload pass rate  (lower = better)")
ax.set_title("Malicious-payload pass rate by attack class",
             fontweight="bold", pad=12)
ax.grid(axis="x", color="#eef1f7", zorder=0)
ax.legend(loc="upper center", bbox_to_anchor=(0.5, -0.13), ncol=3, frameon=False, fontsize=10)
for s in ("top", "right"):
    ax.spines[s].set_visible(False)
fig.subplots_adjust(bottom=0.18)
fig.tight_layout()
fig.savefig(os.path.join(FIGDIR, "fig1_by_class.png"), dpi=170)

# ---------------- figure 2: overall + benign ----------------
ov = R["summary"]["overall"]
N = ov["n"]
overall_succ = [100 * s / N for s in ov["succeed"]]
mit = R["summary"]["mitigation_rate_hardened"]
ben = R["summary"]["benign"]
ben_pass = [100 * (ben["n"] - o) / ben["n"] for o in ben["overblocked"]]

fig, (a1, a2) = plt.subplots(1, 2, figsize=(9.6, 4.2), gridspec_kw={"width_ratios": [1.35, 1]})

bars = a1.bar(LEVELS, overall_succ, color=[COL["none"], COL["sec"], COL["hard"]], width=0.62, zorder=3)
for b, v in zip(bars, overall_succ):
    a1.text(b.get_x() + b.get_width() / 2, v + 2, f"{v:.1f}%", ha="center", fontweight="bold")
a1.set_ylim(0, 112)
a1.set_ylabel("Overall malicious-payload pass rate")
a1.set_title(f"All {N} payloads — {mit:.1f}% statically rejected", fontweight="bold", pad=10)
a1.grid(axis="y", color="#eef1f7", zorder=0)
for s in ("top", "right"):
    a1.spines[s].set_visible(False)

bars = a2.bar(LEVELS, ben_pass, color="#4c5bd4", width=0.62, zorder=3)
for b, v in zip(bars, ben_pass):
    a2.text(b.get_x() + b.get_width() / 2, v + 2, f"{v:.1f}%", ha="center", fontweight="bold")
a2.set_ylim(0, 112)
a2.set_ylabel("Benign commands allowed")
a2.set_title("Creative freedom preserved", fontweight="bold", pad=10)
a2.grid(axis="y", color="#eef1f7", zorder=0)
for s in ("top", "right"):
    a2.spines[s].set_visible(False)
plt.setp(a1.get_xticklabels(), fontsize=9)
plt.setp(a2.get_xticklabels(), fontsize=9)
fig.tight_layout()
fig.savefig(os.path.join(FIGDIR, "fig2_overall.png"), dpi=170)

print("wrote", os.path.join(FIGDIR, "fig1_by_class.png"))
print("wrote", os.path.join(FIGDIR, "fig2_overall.png"))
