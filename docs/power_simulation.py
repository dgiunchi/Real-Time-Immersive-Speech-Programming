#!/usr/bin/env python3
"""
Power / minimum-detectable-effect simulation for Say It Again.

WHY A SIMULATION RATHER THAN A FORMULA
The headline test is a binary outcome, measured within participants, with only
three observations per participant per cell. Closed-form power formulas assume a
continuous outcome or a large number of trials per cell; neither holds here. With
three trials, a participant's accuracy can only be 0, 1/3, 2/3 or 1, and that
coarseness costs real power that no formula in a textbook accounts for. Simulating
the actual design is the only way to get an honest number.

NO DEPENDENCIES ON PURPOSE
numpy, scipy, statsmodels and R are all absent on the study machine. Everything
here is standard library, so this runs anywhere the study runs and a supervisor
or reviewer can re-run it without installing anything.

WHAT IS DELIBERATELY CONSERVATIVE
The test simulated is a paired t-test on each participant's accuracy difference
(user-fault minus system-fault). The pre-registered analysis is a mixed-effects
logistic regression, which uses trial-level information and will have somewhat
MORE power than this. So every number here is a floor: the real design detects at
least this much, probably a little more. Reporting the floor is the right way
round — it cannot flatter the study.

    python3 docs/power_simulation.py
"""

import math
import random

# ── Design constants, from STUDY_DESIGN.md ──────────────────────────────────
N_PARTICIPANTS      = 30    # 10 per condition; the scenarioType effect pools all
TRIALS_PER_CELL     = 3     # three user-fault and three system-fault tasks
ALPHA               = 0.05  # two-tailed
TARGET_POWER        = 0.80
N_SIMULATIONS       = 4000

# Accuracy on user-fault trials: how often a participant correctly says the
# failure was their own phrasing. Set from the pilot's plausible range rather
# than optimism — this is the baseline the effect is measured against.
P_USER_FAULT        = 0.65

# Participant heterogeneity on the logit scale. Some people are simply better at
# diagnosing than others, and that variance is what the random intercept absorbs.
# 0.8 is a moderate value; SENSITIVITY below reruns at 0.4 and 1.2 so the answer
# is not resting on one guess.
TAU_LOGIT           = 0.8


# ── Student's t CDF, since scipy is unavailable ─────────────────────────────
def _betacf(a, b, x, itmax=200, eps=3e-12):
    """Continued fraction for the incomplete beta function (Lentz's method)."""
    qab, qap, qam = a + b, a + 1.0, a - 1.0
    c, d = 1.0, 1.0 - qab * x / qap
    if abs(d) < 1e-300:
        d = 1e-300
    d = 1.0 / d
    h = d
    for m in range(1, itmax + 1):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d
        if abs(d) < 1e-300:
            d = 1e-300
        c = 1.0 + aa / c
        if abs(c) < 1e-300:
            c = 1e-300
        d = 1.0 / d
        h *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d
        if abs(d) < 1e-300:
            d = 1e-300
        c = 1.0 + aa / c
        if abs(c) < 1e-300:
            c = 1e-300
        d = 1.0 / d
        delta = d * c
        h *= delta
        if abs(delta - 1.0) < eps:
            break
    return h


def _betai(a, b, x):
    """Regularised incomplete beta function I_x(a, b)."""
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0
    lbeta = (math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b)
             + a * math.log(x) + b * math.log(1.0 - x))
    if x < (a + 1.0) / (a + b + 2.0):
        return math.exp(lbeta) * _betacf(a, b, x) / a
    return 1.0 - math.exp(lbeta) * _betacf(b, a, 1.0 - x) / b


def two_tailed_t_p(t, df):
    """p-value for a two-tailed one-sample t-test."""
    if df <= 0:
        return 1.0
    return _betai(0.5 * df, 0.5, df / (df + t * t))


# ── The design, simulated once ──────────────────────────────────────────────
def logistic(x):
    return 1.0 / (1.0 + math.exp(-x))


def logit(p):
    return math.log(p / (1.0 - p))


def simulate_once(p_user, p_system, tau, rng):
    """
    One whole study. Returns the p-value of the paired test on per-participant
    accuracy differences.

    Each participant gets a random intercept on the logit scale, so a person who
    is good at diagnosing is good at it on BOTH fault types — which is exactly
    the correlation that makes a within-participant design worth running.
    """
    b0 = logit(p_user)
    b1 = logit(p_system) - logit(p_user)   # the effect, on the logit scale

    diffs = []
    for _ in range(N_PARTICIPANTS):
        u = rng.gauss(0.0, tau)
        acc_user = sum(rng.random() < logistic(b0 + u)
                       for _ in range(TRIALS_PER_CELL)) / TRIALS_PER_CELL
        acc_sys = sum(rng.random() < logistic(b0 + b1 + u)
                      for _ in range(TRIALS_PER_CELL)) / TRIALS_PER_CELL
        diffs.append(acc_user - acc_sys)

    n = len(diffs)
    mean = sum(diffs) / n
    var = sum((d - mean) ** 2 for d in diffs) / (n - 1)
    if var <= 0.0:
        # Every participant produced an identical difference. Only happens with
        # a huge effect, in which case the test is significant.
        return 0.0 if mean != 0.0 else 1.0
    t = mean / math.sqrt(var / n)
    return two_tailed_t_p(t, n - 1)


def power_at(p_user, p_system, tau, sims=N_SIMULATIONS, seed=20260810):
    rng = random.Random(seed)
    hits = sum(simulate_once(p_user, p_system, tau, rng) < ALPHA
               for _ in range(sims))
    return hits / sims


# ── Report ──────────────────────────────────────────────────────────────────
def sweep(tau, label):
    print(f"\n  {label}  (participant SD on logit scale = {tau})")
    print(f"  {'system-fault acc':>17} {'difference':>11} {'power':>7}")
    print("  " + "-" * 38)
    mde = None
    p = P_USER_FAULT
    while p >= 0.14:
        pw = power_at(P_USER_FAULT, p, tau)
        diff = P_USER_FAULT - p
        flag = ""
        if mde is None and pw >= TARGET_POWER:
            mde, flag = diff, "   <-- MDE"
        print(f"  {p:>17.2f} {diff:>11.2f} {pw:>7.2f}{flag}")
        p -= 0.05
    return mde


if __name__ == "__main__":
    print(__doc__.strip().split("\n")[0])
    print(f"\n  N = {N_PARTICIPANTS} participants, {TRIALS_PER_CELL} trials per fault type")
    print(f"  alpha = {ALPHA} two-tailed, target power = {TARGET_POWER}")
    print(f"  user-fault accuracy fixed at {P_USER_FAULT}")
    print(f"  {N_SIMULATIONS} simulated studies per point")
    print("  Test simulated: paired t on per-participant accuracy difference")
    print("  (conservative — the pre-registered GLMM will have slightly more power)")

    main = sweep(TAU_LOGIT, "PRIMARY")
    lo   = sweep(0.4, "SENSITIVITY: less participant variability")
    hi   = sweep(1.2, "SENSITIVITY: more participant variability")

    print("\n  " + "=" * 60)
    print("  MINIMUM DETECTABLE EFFECT for H1 (main effect of fault type)")
    for name, v in (("primary (tau=0.8)", main), ("tau=0.4", lo), ("tau=1.2", hi)):
        if v is None:
            print(f"    {name:>22}: not reached within the range tested")
        else:
            print(f"    {name:>22}: {v:.2f} accuracy points "
                  f"({P_USER_FAULT:.2f} vs {P_USER_FAULT - v:.2f})")
    print("  " + "=" * 60)
