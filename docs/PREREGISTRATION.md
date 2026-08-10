# Pre-registration — Say It Again

**Misattributed blame and wasted repair in speech-driven agents**

Akbar Juraev · University of Birmingham · written before participant 1

At the time of writing: **0 participants collected.** Earlier pilot sessions ran
under a materially different protocol (76-item questionnaire, three-category
confidence, different feedback wording, no `both` attribution code) and are
archived, excluded, and will be reported as pilot work rather than pooled.

---

## 1. Question

When a speech-driven agent fails without saying why, who do users blame, and what
does guessing wrong cost them?

## 2. Hypotheses

### Confirmatory

**H1 — Misdiagnosis.** Attribution accuracy is lower on system-caused failures
than on user-caused failures.

*Direction is specified in advance.* Whether people protect themselves by
attributing failure externally has been disputed since Miller and Ross (1975)
reported only minimal evidence for self-protective attribution under failure, and
Zuckerman (1979) replied that the motivational bias survives scrutiny. We predict
self-attribution here on a non-motivational basis: external attribution requires
a causal model of the other party, and an opaque system supplies none, leaving
the participant's own phrasing as the only available hypothesis.

Note that self-blame for voice-assistant failures is already documented
(Baughan et al., CHI 2023) — but for failures users themselves attributed to
ambiguity in their own requests, collected retrospectively and without ground
truth. H1 tests whether it persists when the cause is unambiguously the system.
See `docs/RELATED_WORK.md`.

**H2 — Cost.** On system-caused failures participants expend more repair
attempts that cannot succeed (`wastedRepairs`) than on user-caused failures, and
per-trial repair confidence mediates this relationship.

### Exploratory

**H3 — Explanation.** Feedback condition moderates H1 and H2.

Declared exploratory **in advance** because n = 10 per condition detects only
large effects. The term of interest is the `scenarioType × condition`
interaction, not the main effect of condition: that explanation improves accuracy
when the user is at fault is trivially true. The non-trivial prediction is that
participants accept an explanation that blames them and discount the same
system's explanation when it admits its own limitation.

## 3. Design

3 (feedback delivery: none / agent text / agent voice + avatar; between) × 2
(fault type: user-correctable / system-caused; within) mixed factorial.

Wizard-of-Oz: a hidden researcher triggers pre-scripted outcomes, so every
participant meets an identical failure and ground truth about the cause is held
by the experimenter and not available to the participant.

Six measured tasks, three per fault type. Order counterbalanced by a Williams
square. One practice trial, excluded from all analyses.

## 4. Sampling plan and sensitivity

**N = 30**, 10 per condition. Stopping rule: collection ends at 30 completed
sessions meeting the inclusion criteria in §7. No optional stopping, no interim
inferential analysis.

**Minimum detectable effect for H1**, from Monte Carlo simulation of this exact
design (`docs/power_simulation.py`, 4,000 simulated studies per point, α = .05
two-tailed, target power .80, baseline user-fault accuracy .65):

| Effect (accuracy points) | Power |
|---|---|
| 0.10 | .25 |
| 0.15 | .46 |
| 0.20 | .69 |
| **0.25** | **.87 — MDE** |
| 0.30 | .96 |

**The study is powered to detect a drop of about 25 accuracy points** (e.g. .65
on user-fault trials versus .40 on system-fault trials) and is **not** reliably
powered for differences of 15 points or less. This is stated in advance so that a
null result is interpreted as "no large effect" rather than as "no effect".

The MDE is stable across assumed participant heterogeneity (τ = 0.4, 0.8, 1.2 on
the logit scale all yield 0.25), so it does not depend on that guess. The
simulated test is a paired t-test on per-participant accuracy differences, which
is **conservative** relative to the pre-registered mixed model; the real analysis
will have somewhat more power, so 0.25 is a floor.

No sensitivity analysis is offered for H3. At n = 10 per cell the interaction is
underpowered for anything but a very large effect, which is the reason it is
declared exploratory rather than a defect to be argued away.

## 5. Measures

**Primary (H1).** Attribution, probed verbatim immediately after each failure and
**before any repair attempt**: *"In your own words, why do you think that
happened?"* Coded live as `self` / `system` / `both` / `unsure` against ground
truth that is hidden from the researcher until the response is recorded.
`attributionCorrect` is a strict match; `both` is **not** scored as correct.

**Co-primary (H2).** `wastedRepairs` — the count of repair attempts in the re-say
family (repeated verbatim · slower or louder · reworded with nothing added),
coded live, one per attempt.

**Mediator (H2).** `repairConfidence`, 0–10, once per trial, asked before the
participant tries again: *"How confident are you that there is something you
could say differently that would make that work?"*

**Co-primary (secondary claim).** `repairContainsSlot` — whether the repair
addresses the true cause, using fixed word-boundary synonym sets defined before
data collection.

**Manipulation check.** Per trial, researcher-judged: did the participant
register the feedback? Objective counterpart: gaze dwell on the panel or agent
before speaking again.

**Attempt level.** Speech onset latency, hold duration, peak and mean level, and
speech rate, each expressed relative to that participant's own practice-trial
baseline.

## 6. Analysis plan

```
H1   attributionCorrect ~ scenarioType * condition + (1 | participant)
     mixed-effects logistic. Inference on the scenarioType main effect.

H2   wastedRepairs ~ scenarioType * condition + (1 | participant)
     Poisson; negative binomial if overdispersion is present, tested by
     comparing the two on AIC. This decision rule is fixed here.

H2b  Mediation: scenarioType -> repairConfidence -> wastedRepairs,
     bootstrapped indirect effect, 5,000 resamples.

H3   The scenarioType x condition interaction from the two models above,
     reported with confidence intervals and labelled exploratory.
```

Alpha is .05 two-tailed throughout. H1 and H2 are the confirmatory family; no
correction is applied across them, and this is stated rather than decided later.
Everything else in the dataset — including all self-report scales, the
attempt-level acoustics, and `attributionSequence` — is exploratory, and any
result from it will be reported as such regardless of how it turns out.

## 7. Exclusions

Fixed before participant 1:

- Fewer than six completed measured trials.
- Technical failure affecting more than one trial.
- Participant reports suspecting the failures were staged, in the debrief probe.
- Trials flagged `preInjectHadSlot` — the participant supplied the detail the
  scripted error then claimed was missing — are excluded from H1 and H2 at the
  trial level. The count is reported whatever it is.

Participants may withdraw their data after the debrief reveals the deception.
The number who do so is reported.

## 8. Manipulation validation

Three checks, all reported regardless of outcome:

1. **Believability.** The debrief asks what the participant thought was
   happening. Anyone who suspected a wizard is excluded (§7), and the count is
   reported as a manipulation-integrity figure rather than omitted.
2. **Feedback registered.** Per-trial check plus gaze dwell, so "feedback made no
   difference" can be distinguished from "they never saw it".
3. **Failure noticed.** In condition A, where nothing is explained, the same
   per-trial check records whether the participant registered that anything went
   wrong at all.

## 9. Deviations

Any departure from this document will be reported in the paper with the reason
and the date it was decided, and any analysis not specified here will be labelled
exploratory. This file is committed to version control; its git history is the
timestamp.
