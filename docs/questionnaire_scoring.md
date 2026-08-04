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

Items 1–5. Order is randomised per participant at render time and subscale
headings are hidden, per the instrument's guidance. Subscale membership lives in
the `sub` field in `questionnaire.html`.

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

## Merging note

Files are per participant (`Logs/<pid>_condition.csv`), and the header is built
from the keys actually submitted. A condition A file therefore has fewer columns
than a B or C file. Any merge must align on column *name*, never on position.
