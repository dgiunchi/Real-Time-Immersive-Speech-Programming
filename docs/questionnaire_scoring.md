# Questionnaire scoring key

Every instrument below is administered in its published response format. Read
this before computing anything: three of the four have reverse-scored items, and
a reversal missed is a result inverted, silently and without any error to notice.

Raw responses are stored exactly as the participant gave them, in the
`questionnaire` row's `answers` column. That row is the archive copy and can
always be rescored from scratch.

The server *also* applies the scoring below at collection time, writing one
`questionnaire-item` row per answer (with the reversal applied and flagged in
`itemReversed`) and one `questionnaire-score` row per scale. That is a
convenience, not a second definition: **this file is the specification, and
`scoreQuestionnaire()` in `Server/samples/apps/wizard_of_oz/app.js` implements
it.** If the two ever disagree, this file is right and the code is a bug — and
because the raw answers are still in the file, nothing is lost by fixing it
after the fact.

One deliberate difference in representation. `itemScore` keeps each item on its
published response range with the reversal applied (a reversed SUS item scores
`6 - response`, so it stays 1–5), because that is what an item mean should be
computed from. The SUS 0–100 transform below subtracts the extra point itself.
The logged `sus_score_0_100` follows this file exactly.

---

## What is administered, and when

| Instrument | Items | Scale | When |
|---|---|---|---|
| Informed consent | 9 ticks | yes/no | Once, **first**, before anything else |
| Background / demographics | 5 | mixed | Once, before VR |
| SUS | 10 | 1–5 | End of session |
| UES-SF | 12 | 1–5 | End of session |
| Trust (Jian et al.) | 12 | **1–7** | End of session |
| Explanation Satisfaction (Hoffman et al.) | 8 | 1–5 | End of session, **B and C only** |

Between-subjects, so the end-of-session battery is completed **once** per
participant, after all six tasks.

The trust scale is 1–7 while everything else is 1–5. That is deliberate: it is
the published response format, and rescaling an instrument to match its
neighbours makes its scores incomparable to the literature while leaving the data
looking perfectly fine.

---

## SUS — System Usability Scale

Brooke, J. (1996). SUS: A "quick and dirty" usability scale. In *Usability
Evaluation in Industry*. Taylor & Francis.

Items `sus1`–`sus10`, 1–5.

**Odd items are positive, even items are negative.**

```
odd  (1,3,5,7,9):   score = response - 1
even (2,4,6,8,10):  score = 5 - response
SUS = (sum of the ten scores) * 2.5      ->  0..100
```

A SUS score is **not a percentage** and should not be reported as one. Compare it
against the ~68 average, or convert to a letter grade with Sauro & Lewis.

---

## UES-SF — User Engagement Scale, Short Form

O'Brien, H. L., Cairns, P., & Hall, M. (2018). A practical approach to measuring
user engagement with the refined user engagement scale (UES) and new UES short
form. *International Journal of Human-Computer Studies*, 112, 28–39.

Responses 1–5. Order is randomised per participant at render time and subscale
headings are hidden, per the instrument's guidance. Because position therefore
no longer identifies an item, **the subscale is carried in the item id itself**
(`ues_fa1`, `ues_pu2`, …) rather than by its number on the page.

| Subscale | Items |
|---|---|
| FA — focused attention | `ues_fa1` `ues_fa2` `ues_fa3` |
| PU — perceived usability | `ues_pu1` `ues_pu2` `ues_pu3` — **REVERSE** |
| AE — aesthetic appeal | `ues_ae1` `ues_ae2` `ues_ae3` |
| RW — reward | `ues_rw1` `ues_rw2` `ues_rw3` |

```
PU items:  score = 6 - response
subscale  = mean of its three items
overall   = mean of all twelve (after reversing PU)
```

The PU items are worded negatively ("I felt frustrated", "I found this task
confusing"). Forgetting to reverse them makes a frustrating system look engaging.

---

## Trust — Jian, Bisantz & Drury (2000)

Jian, J.-Y., Bisantz, A. M., & Drury, C. G. (2000). Foundations for an
empirically determined scale of trust in automated systems. *International
Journal of Cognitive Ergonomics*, 4(1), 53–71.

Items `trust01`–`trust12`, **1–7**, presented in published order (not
randomised — the order is part of the validated instrument).

| Items | Subscale | Scoring |
|---|---|---|
| `trust01`–`trust05` | Distrust | **REVERSE**: `score = 8 - response` |
| `trust06`–`trust11` | Trust | as given |
| `trust12` | Familiarity | as given, **reported separately** |

```
trust_total = mean(reversed 01..05, raw 06..11)     # eleven items
familiarity = trust12                                # NOT folded into the total
```

Item 12 measures familiarity, not trust, and folding it in is the usual mistake.
In this study it is close to a manipulation check on novelty: everyone meets the
system for the first time, so a high value is worth looking at.

---

## Explanation Satisfaction — Hoffman, Mueller, Klein & Litman (2018)

Hoffman, R. R., Mueller, S. T., Klein, G., & Litman, J. (2018). Metrics for
explainable AI: Challenges and prospects. *arXiv:1812.04608*. See also *Frontiers
in Computer Science*, 5 (2023).

Items `ess1`–`ess8`, 1–5. **No reverse-scored items.**

```
ess = mean(ess1..ess8)
```

### Condition A has no ESS data, by design

There is no explanation in condition A, so every item is unanswerable. The card
is hidden and the columns are absent from that participant's file.

This is **not missing data and must not be imputed.** When merging participant
files, condition A rows will have no ESS columns at all; make the merge
tolerate that rather than filling zeros, which would read as maximal
dissatisfaction.

The comparison this scale supports is **B versus C**, which is the
between-participants contrast at n=10 per cell. Report it descriptively
alongside the exploratory modality analysis; it is not powered to carry a claim.

---

## Perceived Support — custom (H3)

Written for this study. Items `psup1`–`psup4` are 1–5; `psup5` is **1–7**.

```
perceived_support             = mean(psup1..psup4)        # the 1-5 items only
perceived_support_overall_1_7 = psup5                     # reported separately
```

`psup5` is deliberately **not** folded into the mean. Averaging a 1–7 item with
four 1–5 items yields a number on neither scale — and one that still looks
entirely plausible in a results table, which is what makes it dangerous.

No reverse-scored items.

---

## IPQ presence — Schubert, Friedmann & Regenbrecht (2001)

Four items (`pres1`–`pres4`), 1–7. Not the full fourteen: presence has to be
reported in a VR paper, but it is not what this study is about.

| Item | Scoring |
|---|---|
| `pres2` — "I did not feel present in the virtual space." | **REVERSE**: `8 - response` |
| all others | as given |

```
presence = mean(pres1, reversed pres2, pres3, pres4)
```

---

## Godspeed I & III — Bartneck, Kulić, Croft & Zoltowski (2009)

**Condition C only.** Semantic differentials, 1–5, where the two anchor words
carry the meaning. Condition B's text panel is not an agent, and asking whether
it is "conscious" or "lifelike" would produce numbers about nothing.

```
godspeed_anthropomorphism = mean(gs_anthro1..gs_anthro5)
godspeed_intelligence     = mean(gs_intel1..gs_intel5)
```

No reversals: each pair already runs negative → positive, so a high score means
a high rating on the named dimension.

Condition A and B files have no Godspeed columns. As with ESS, that is
**structural absence, not missing data** — do not impute.

---

## SSQ — Kennedy, Lane, Berbaum & Lilienthal (1993)

Sixteen symptoms (`ssq1`–`ssq16`), each **0–3** (None / Slight / Moderate /
Severe), in the standard printed order.

This is **not a mean of anything.** Each subscale sums a specific, *overlapping*
subset and multiplies by a published constant — several symptoms count toward
two subscales, which is why it cannot be expressed as a simple item list.

| Subscale | Item numbers | Weight |
|---|---|---|
| Nausea | 1, 6, 7, 8, 9, 15, 16 | × 9.54 |
| Oculomotor | 1, 2, 3, 4, 5, 9, 11 | × 7.58 |
| Disorientation | 5, 8, 10, 11, 12, 13, 14 | × 13.92 |

```
ssq_total = (raw_nausea_sum + raw_oculomotor_sum + raw_disorientation_sum) * 3.74
```

The total uses the **raw** subscale sums, weighted once by 3.74 — it is *not*
the sum of the three weighted subscale scores. If any subscale is incomplete no
total is written, rather than a total computed from partial data.

`discomfort_pre` / `discomfort_post` (0–10) are kept alongside as a separate
paired single-item measure; they are not part of the SSQ.

---

## Retrospective attribution — custom (H1)

Items `attr1`–`attr5`, 1–7 (Never … Always). **Never summed into a scale.**

`attr1` ("how often was it something you said") and `attr2` ("how often was it
something the system could not do") are not opposite ends of one construct —
both can be legitimately high, since half the failures were of each kind.
Reverse-scoring one against the other, or averaging them, would destroy exactly
the distinction H1 is about. Analyse them item by item.

These are the retrospective counterpart to the per-trial attribution probe. The
per-trial measure is the primary H1 test; disagreement between the two is itself
a finding, not an error to reconcile.

---

## Speech-system self-efficacy — custom, paired

`se_pre1`–`se_pre3` in the background form and `se_post1`–`se_post3` in the
post-session form are the **same three items, worded identically**, 1–7.

```
self_efficacy_post = mean(se_post1..se_post3)
change             = self_efficacy_post - mean(se_pre1..se_pre3)
```

The change score is the point of the pair, and it must be computed at analysis
time: the two forms are separate submissions, so no single row contains both.

---

## Fatigue and focus — custom, post-session

`fatigue_mental` and `fatigue_energy`, 1–7, in the post-session form.

Reported descriptively. Not a scale — do not average the two, since one is
tiredness and the other is remaining capacity.

These were originally a separate two-item page handed over mid-session, at
`/fatigue`. That page has been removed. A form handed over in the middle of a
session becomes the rest break whose effect it is measuring, and the session is
short enough that the end is close to the tiring point anyway. **The cost is
real and must be reported as such: these are an end state, not a curve, and they
cannot distinguish "tired by halfway" from "tired by the end".** If a fatigue
trajectory is ever needed, `msSinceSessionStart` on the trial-summary rows is
the behavioural substitute — trial duration and attempt count against position
in the session.

---

## Graded attribution — custom, post-session (H1)

| Item | Scale | Meaning |
|---|---|---|
| `blame_split` | **0–10** | 0 = entirely me, 10 = entirely the system |
| `attr_confidence` | 1–5 | how sure they are about that split |

Neither is reverse-scored and neither is averaged with anything. `blame_split`
is 0-based: **0 is a real answer meaning "entirely my fault"**, so an empty cell
and a `0` are different facts and must not be conflated.

### These used to be per-trial, and the move cost power

Both were asked aloud by the wizard after every failure, giving six observations
per participant, each tied to a known scenario type. They are now asked once,
about the session as a whole.

What that changed:

- **Lost.** The graded measure can no longer be split by scenario type. A
  within-participant contrast of "blame on system-caused failures" versus "blame
  on user-caused failures" is no longer available from these items.
- **Kept.** The primary H1 test is the per-trial three-way attribution probe
  (`attribution` versus `correctAttribution` on each trial-summary row), which is
  untouched, still six per participant, and still scenario-linked.
- **Therefore.** Treat `blame_split` as a single descriptive summary that
  corroborates the per-trial probe. It is not a second test of H1 and must not
  be written up as one.

`attr_confidence` is a moderator on that summary only: a confident global
mis-split is a different claim from a hesitant one.

---

## Informed consent — record only, not a measure

Submitted from `/consent` before the session as `questionnaire: "consent"`.
Nine tick items (`c_read`, `c_questions`, `c_voluntary`, `c_deception`,
`c_publication`, `c_logging`, `c_audio`, `c_age`, `c_takepart`), stored as
`yes`/`no`, plus `consent_date`, `researcher` and `consent_version`.

**Not scored and never analysed.** It is an audit record.

`c_audio` is the only optional tick. A participant may take part without
agreeing to be audio-recorded; in that case the interview is taken as written
notes only. Every other box must be ticked for the session to proceed, and the
page refuses to submit otherwise.

**No participant name is stored.** The name and signature exist on the paper
form only. The information sheet promises anonymisation by participant ID, and a
name column in the log file would break that promise for the entire file.

---

## Merging note

Files are per participant (`Logs/<pid>_condition.csv`), and the header is built
from the keys actually submitted. A condition A file therefore has fewer columns
than a B or C file. Any merge must align on column *name*, never on position.
