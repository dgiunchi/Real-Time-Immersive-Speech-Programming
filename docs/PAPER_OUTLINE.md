# Writing it up

**Working title: *Say It Again: Misattributed Blame and Wasted Repair in
Speech-Driven Agents*.**

The title leads on `wastedRepairs` rather than on attribution, for the same
reason the paper does: it is the half of the claim that carries a cost, and it
is the half a deployed system can already observe in its own logs without a
wizard. It also survives whichever way the statistics fall — people will repeat
themselves whether or not the effect reaches significance, so the title is not
hostage to a result. A title built on the acoustic prediction ("Louder, Not
Clearer") would have been sharper and riskier.

Section by section, with the column that produces each number. The argument is
already made in [STUDY_DESIGN.md](../STUDY_DESIGN.md) — this is about turning it
into a paper, and about not overclaiming the half of the design that cannot
carry it.

Target venue shape: CHI / IUI / CSCW full paper, 8–10 pages plus references.

---

## The one ordering decision that matters

Lead with **fault type**, not with **condition**.

Fault type is within-participant, three observations per cell, every participant
their own control. Condition is between-participant at n=10 per cell and can
only detect a large effect. A paper that opens on "does an embodied agent help?"
has hung itself on the one axis the design cannot power, and a reviewer who
checks the n will say so before reading the results.

So the headline is: **people misattribute system-caused failures as their own,
and spend repair turns that cannot work.** Modality is a secondary, explicitly
exploratory question inside that.

Write the abstract last, and write it from the Results you actually have.

---

## 1. Introduction (~1 page)

Take it from STUDY_DESIGN's *The problem*. Three beats:

1. Speech agents fail in a way that hides its own cause. The user cannot tell
   *"I phrased it wrong"* from *"it cannot do this"*.
2. The two need opposite responses — add detail, versus stop and work around it
   — so guessing wrong is not a neutral error. It costs turns, and it produces a
   correction that is misdiagnosed and therefore useless as training signal.
3. Nobody has measured the diagnosis *inside the repair loop*, against ground
   truth, with a cost attached. Prior work measures blame as a post-hoc attitude.

End on the contribution sentence and the three research questions.

> Do not open with VR. VR is the instrument, not the topic — see §7. Opening
> with the headset invites the reader to file this as a VR paper and ask why the
> finding should generalise.

## 2. Related work (~1 page)

Four strands, roughly a paragraph each:

- **Attribution and blame in HCI/HRI** — post-hoc, survey-based. This is the gap.
- **Explainable AI and repair** — ESS lineage (Hoffman et al. 2018), explanation
  satisfaction, and why satisfaction ≠ correct diagnosis.
- **Conversational repair and hyperarticulation** — the speech-science literature
  on what people do when they believe they were misheard. This is what the
  acoustic measures connect to.
- **Embodiment and agent presence** — the B-versus-C manipulation.

## 3. Study design (~1 page)

Straight from STUDY_DESIGN: the 3 × 2 mixed factorial, the six tasks, the
Williams square, and the exclusivity of B and C.

Say the quiet parts out loud, because they are strengths:

- Six tasks rather than four, and *why* — trials per person was the binding
  constraint, not people.
- Order index by `floor((p-1)/3)`, so condition and order are not confounded.
- Condition is pre-registered exploratory. Saying so in the Design section
  disarms the objection before Results.

## 4. Method (~1.5 pages)

Participants, apparatus, the Wizard-of-Oz protocol, procedure, ethics.

State plainly that the failures were scripted and fired regardless of what the
participant said. Then state the flag that exists because of it:
`preInjectHadSlot` marks trials where the participant *did* supply the detail the
scripted error then claimed was missing. Those are excludable, and reporting how
many there were is a rigour signal, not an admission.

Ethics: authorised deception, full debrief, and the audio-consent item is
**separate and enforced** — declining means no recording is written. Say that; it
is the kind of detail an ethics-attentive reviewer looks for and rarely finds.

## 5. Measures (~1 page)

Split into three tiers, because they have different power.

### Trial level — the primary unit for H1

| Measure | Column | Notes |
|---|---|---|
| Attribution | `attribution` | First probe only. `attributionSequence` has all of them |
| Correct? | `attributionCorrect` | vs `correctAttribution`, which is fixed per task |
| Perceived reparability | `perceivedReparability` | The H2 belief |
| Repair moves | `repairSequence` | Full sequence, in order |
| **Wasted repairs** | `wastedRepairs` | The re-say family. Co-primary |
| Correction quality | `repairContainsSlot` | The training-signal measure |
| Noticed feedback | `noticedFeedback` | Manipulation check |
| Time to first repair | `msToFirstRepair` | |

### Attempt level — where the power is

~500 observations rather than 180, at no cost in session time.

| Measure | Column | What it is |
|---|---|---|
| Speech onset latency | `speechOnsetMs` | Trigger pressed → first word. Short = reflexive repeat, long = planning |
| Hold duration | `pttHoldMs` | |
| Loudness | `peakRms`, `meanRms` | **Always as a delta from that participant's practice baseline** |
| Speech rate | `speechRateWps` | The other half of hyperarticulation |
| Lexical overlap | `utteranceSimilarities` | Independent check on the wizard's live coding |
| Audio | `audioFile` | One WAV per press |

### Gaze — the objective manipulation check

`gazeTarget` and `dwellMs` on `head-pose` rows. Dwell on the panel or agent
before speaking again is strictly better evidence than `noticedFeedback` asked
aloud. On task 4, `gazeTarget=object` is the moment they turned and found it.

### Self-report

**Only what is actually administered.** The post-session form is 23 rated items:
NASA-TLX (6), Perceived Support (5, custom, H3), retrospective attribution (4),
IPQ presence (4), speech-system self-efficacy (3, paired pre/post), and a single
0–10 discomfort item paired pre/post. SUS, UES-SF, trust and ESS were cut — see
the note in `questionnaire.html` for the reasoning, and say briefly in the paper
why: a 76-item battery produces straight-lining, and straight-lined data looks
like data.

---

## 6. Results (~2.5 pages)

### 6.1 Manipulation and exclusions

Come clean first. Number of trials excluded for `preInjectHadSlot`, trials where
`noticedFeedback` was no, dwell evidence that the feedback was actually looked
at, and any participant-level exclusions. Then the counterbalance check: order
and variant coverage per condition, straight from `orderIndex` / `variantOffset`.

### 6.2 H1 — attribution accuracy (the headline)

```
attributionCorrect ~ scenarioType * condition + (1 | participant)
```
mixed-effects logistic. **The main effect of `scenarioType` is the result.**
Report the interaction as exploratory, with its CI, and resist writing a story
about it if it is not significant.

Filter: `recordType == "trial-summary"`, `task != "practice"`.

### 6.3 H2 — what it costs

```
wastedRepairs ~ scenarioType * condition + (1 | participant)
```
Poisson or negative binomial — it is a count with a floor at zero and will be
overdispersed. Check.

Then the mediation that is the actual argument:
`perceivedReparability` → `wastedRepairs`, and whether it accounts for the
scenarioType effect. Someone who believes rewording would have worked has a
reason to repeat themselves; on a system-limit trial that belief is false and the
wasted turn follows from it. **Measuring the belief separately from the behaviour
is what lets these be related rather than assumed** — say so.

### 6.4 Attempt-level: what a misdiagnosed repair sounds like

This is the section nobody else has, and it is worth its own space.

For each attempt, against the participant's own practice baseline:

```
Δ loudness   ~ attemptIndex * scenarioType + (1 | participant)
Δ speechRate ~ attemptIndex * scenarioType + (1 | participant)
speechOnsetMs ~ attemptIndex * scenarioType + (1 | participant)
```

The prediction worth stating in advance: on **system-caused** failures,
participants get louder and slower across attempts while adding no new
information — hyperarticulation in response to a failure that hyperarticulation
cannot fix. Pair it with the wizard's live coding (`slower`, `wordbyword`) as
converging evidence from two independent sources.

Control for `wizardLatencyMs` and `asrLatencyMs`. They are logged precisely so
they can be subtracted rather than hand-waved.

### 6.5 H3 — modality (exploratory, and labelled as such)

Perceived Support by condition, gaze dwell by condition, and the interaction from
6.2. Frame throughout as *exploratory, n=10 per cell*. If nothing reaches
significance, that is a reportable and honest result — say the CI is wide and the
design was not built to power this.

### 6.6 Qualitative

Interview responses (`iv1`–`iv5`), thematically coded. Use them for the
*mechanism*: what people were thinking when they repeated themselves. One good
quote from someone describing why they said it again is worth a paragraph of
model output.

---

## 7. Discussion (~1.5 pages)

- **The bridge to deployed systems.** `wastedRepairs` is observable without a
  wizard — repeated near-identical utterances are already in every voice
  assistant's logs. A system could detect the pattern and change its response.
  This is the practical payoff and it deserves its own subsection.
- **Corrections as training signal.** `repairContainsSlot`: a correction produced
  under a wrong diagnosis does not merely fail to help, it teaches the wrong
  lesson. Relevant to anyone doing RLHF on interaction logs.
- **Why VR** — the STUDY_DESIGN argument. Ground truth has to be unarguable, and
  in a text agent whether the output was even wrong is contestable.

## 8. Limitations

Write these before a reviewer does. Being first is worth more than being brief.

- Condition is underpowered by design; treated as exploratory throughout.
- Wizard-of-Oz: the "system" has a human's timing. `wizardLatencyMs` is logged
  and controlled for, which is the honest version of this limitation.
- Repair moves coded live by a single rater running the session. Mitigated by
  three independent automatic measures — `utteranceSimilarities`,
  `repairContainsSlot`, and the acoustics — but not eliminated. **Report the
  agreement between the live coding and the automatic measures.** If they agree,
  that is a result in itself.
- Scripted failures mean some participants are contradicted; `preInjectHadSlot`
  quantifies exactly how often.
- Lab study, 30 participants, one session, novel system — self-efficacy change
  is short-term.

## 9. Conclusion

Two paragraphs. The finding, and the design implication.

---

## Practical notes

**One file per participant.** `Logs/P01.csv`, filtered by `recordType`:
`trial-summary` for §6.2–6.3, `event` + `eventType=utterance-audio` for §6.4,
`eventType=head-pose` for gaze, `questionnaire-score` for the scales. No joining.

**Analysis order.** Write the analysis script against
`docs/example_participant.csv` *before* the last participant runs. If a column
you need turns out not to be there, that is recoverable in August and not in
October.

**Pre-register** the H1 and H2 models and the exploratory status of condition,
before data collection completes. It costs an afternoon and it is the difference
between "exploratory" being a stated position and it being a concession.
