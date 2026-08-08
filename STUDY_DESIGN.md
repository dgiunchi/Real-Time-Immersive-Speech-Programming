# DreamCodeVR — Study Design

One page, for supervisors and collaborators. Operational detail lives in
[STUDY_GUIDE.md](STUDY_GUIDE.md); filming in [DEMO_FILMING.md](DEMO_FILMING.md);
the write-up plan, section by section with the column behind each number, in
[docs/PAPER_OUTLINE.md](docs/PAPER_OUTLINE.md).

---

## The problem

Every speech-driven AI agent has a failure mode where the user cannot tell
whether **they phrased it wrong** or **the system cannot do it**. The two need
opposite responses — add detail, versus stop asking and work around it — and
most systems give no signal about which applies.

Guess wrong and you pay twice. You burn turns on a repair that cannot work, and
the correction you produce is misdiagnosed, which makes it useless as feedback
for the system to learn from. A user who says *"your movement is broken"* when
the real problem was their own vague phrasing has supplied noise, not signal.

This is not a VR problem. It is what happens when anyone repeats a command to a
smart speaker, louder each time. People do not do that because they are careless.
They do it because repeating is the only hypothesis a silent failure supports.

## The claim

> **People misattribute system-caused failures as their own, and burn repair
> attempts that cannot possibly succeed.**

That is the headline, and it rests on the within-participant contrast: every
participant meets both user-caused and system-caused failures, so each is their
own control. It is the better-powered comparison and the more surprising one, and
`wastedRepairs` makes it countable rather than merely reported.

The secondary question is whether the **way a failure is explained** changes that.
That sits on the between-subjects factor at n=10 per cell, so it is treated as an
**exploratory moderator** and pre-registered as exploratory. Leading with it would
have hung the paper on the one axis the design cannot power.

## Design

**3 × 2 mixed factorial.**

| Factor | Levels | Assignment |
|---|---|---|
| Feedback modality | **A** none · **B** text panel · **C** embodied agent | Between-subjects |
| Fault type | User-correctable · System-caused | Within-subjects |

- **n = 30**, 10 per modality. Each participant sees one modality across all six
  measured tasks.
- **Six tasks, three per fault type.** The model estimates a per-participant
  random intercept; at four tasks that is two binary observations per cell, too
  thin to identify the random effect and liable to converge badly. Six trials
  roughly halves per-cell noise for about eight extra minutes per session, which
  buys more than ten additional participants would. Trials per person, not
  people, was the binding constraint.
- B and C are **exclusive**: each carries the explanation exactly once, so the
  comparison isolates *modality*, not *amount* of information. The transcript
  strip stays visible in both, since it is not an explanation.
- **Wizard-of-Oz.** A hidden researcher triggers pre-scripted outcomes. No live
  LLM, so every participant meets an identical failure and we hold ground truth
  about what caused it — which no field data ever has.

### The six tasks

Task *is* scenario. Three of each fault type, so every participant meets the
contrast three times.

| Task | Ask | What happens | At fault |
|---|---|---|---|
| 1 | Create an object in your hand | Nothing appears, no hand height given | **User** |
| 2 | Move an object next to a target | Moves the wrong way, no direction given | **User** |
| 5 | Create an object that stands out | Comes out default grey, no colour given | **User** |
| 3 | Create ~1000 objects | Only 8 appear, past the render limit | **System** |
| 4 | Create an object above the campfire | Created correctly, but behind you | **System** |
| 6 | Make an object move on its own | Nothing happens, animation is unsupported | **System** |

The user-fault tasks are repairable by speaking differently, and each turns on a
different kind of omission: a missing trigger condition, an ambiguous spatial
reference, and a stated intention with no parameter to achieve it.

The system-fault tasks are not repairable at all, and are deliberately three
different flavours of that: a ceiling on something supported (3), a correct
execution in an unexpected place (4), and a capability that does not exist (6).
Task 6 is the purest test of the claim, since the only useful response is to stop
asking and pivot, which a participant blaming their own phrasing never reaches.
Telling any of these three to rephrase would be actively misleading, so their
feedback owns the limit instead.

A **practice trial** runs first and is excluded from analysis, so the first
measured task is not doubling as push-to-talk training.

**Order** follows a 6×6 Williams balanced Latin square, which balances not just
position but order-of-precedence. This matters because the system-fault tasks
teach that the system has limits; without balancing, that lesson would leak into
the user-fault tasks asymmetrically.

The order index advances once per full A/B/C cycle rather than per participant.
Indexing order and condition by the same counter would make order a deterministic
function of condition (A would only ever see two of the six orders), which is a
confound wearing a counterbalance's clothes. With n=30 across 3 conditions × 6
orders, coverage within a condition is near-balanced rather than exact; perfect
balance would need n=36.

## Measures

**Primary — attribution accuracy.** After each error, scripted verbatim: *"In
your own words, why do you think that happened?"* Coded self / system / unsure
against ground truth.

`correct ~ condition * scenarioType + (1|participant)`, mixed-effects logistic.

**The main effect of `scenarioType` is the headline**: accuracy should fall on
system-caused failures, where people reach for a self-blaming explanation that
the evidence does not support. That term is within-participant and carries three
observations per cell.

The `condition × scenarioType` interaction is the interesting-but-underpowered
term: feedback may help on user-fault tasks while doing nothing, or actively
harming, on system-fault ones. Reported as exploratory.

**Co-primary — what the misattribution costs.** Attribution alone is a stated
belief, and *who cares* is a fair question. The repair move is that belief with a
price on it, coded live per attempt: added detail · shrank the ask · asked the
system · gave up, and the **re-say family** — repeated it · said it slower or
louder · said it word by word · reworded it with nothing added.

`wastedRepairs` counts the re-say family. Those moves cannot fix anything — not
a user fault, since the missing information is still missing, and not a system
limit, since the limit does not care how clearly you speak. It is what
misattribution looks like from outside, and crucially it is **observable without
a wizard**: repeated near-identical utterances are already in every voice
assistant's logs. That is the bridge from this lab study to a deployed system.

The four re-say categories are the observable face of hyperarticulation, and the
per-utterance acoustics say independently which one happened — speech rate and
level measured against that participant's own practice baseline. A wizard coding
live and a waveform agreeing is a considerably stronger claim than either alone,
which is why the live coding is that specific.

**The unit of analysis is the attempt, not the trial.** Thirty participants ×
six tasks is 180 trials; the same sessions contain roughly 500 attempts, each
carrying a continuous outcome rather than a yes/no. Onset latency — trigger
pressed to first word — separates a reflexive repeat from actual planning, and
costs no participant time. Gaze dwell on the panel or agent turns the
manipulation check from a self-report item into a measurement. See
[docs/PAPER_OUTLINE.md](docs/PAPER_OUTLINE.md) §6.4.

**Secondary — correction quality** (`repairContainsSlot`): does the repair
actually address the true cause? This is the training-signal measure. A
correction produced under a wrong diagnosis does not merely fail to help; it
teaches the wrong lesson.

**Self-report — 23 rated items, cut down from 76.** NASA-TLX (Hart & Staveland,
1988), Perceived Support (custom, H3), retrospective attribution (custom, H1),
IPQ short-form presence (Schubert et al., 2001), speech-system self-efficacy
paired pre/post, and a single 0-10 discomfort item also paired pre/post.

SUS, UES-SF, trust in automation and the Explanation Satisfaction Scale were
cut. Each was defensible alone; the battery was not. Past about fifteen minutes
people straight-line, and a straight-lined scale is worse than an absent one
because it still looks like data. ESS duplicated Perceived Support item for item
and was unanswerable in condition A; SUS measures the usability of a system that
is a researcher pressing buttons; UES was already tied to no hypothesis; and half
the trust scale is written for autopilots and medical devices. The full reasoning
is at the top of `questionnaire.html`, and the scoring code still supports every
one of them — restoring a scale means re-adding its items and nothing else.

Each surviving instrument keeps its published response format, including
NASA-TLX's twenty intervals scored 0-100 in fives. Rescaling an instrument to
match its neighbours makes its scores incomparable to the literature while
leaving the data looking fine.

Scoring, reverse-scored items and subscales are fixed in advance in
[docs/questionnaire_scoring.md](docs/questionnaire_scoring.md).

**Manipulation check** per trial: did they register the feedback at all? Without
it, "feedback made no difference" cannot be distinguished from "they never saw
it," and in condition A it records whether they noticed the failure.

### The obvious objection, and the answer

In conditions B and C the feedback states the cause, so stated attribution should
sit near ceiling. "Telling people what went wrong makes them know what went
wrong" is not a finding, and a reviewer will say so.

Two things make it non-trivial, and both are measured.

**People may not believe the system about itself.** On the system-fault tasks the
feedback amounts to an admission of limitation. There is no reason to assume that
is accepted at face value: a plausible and more interesting outcome is that
participants discount it and keep blaming their own phrasing, because "I said it
badly" is a more available explanation than "this tool cannot do this". If that
happens, B and C are *not* at ceiling on exactly the half of the design that
matters, and the reason is psychological rather than informational.

**Knowing is not doing.** `wastedRepairs` and `firstRepairStrategy` are recorded
independently of what the participant says. Someone can state the correct cause
and still repeat themselves, and that gap between stated diagnosis and enacted
repair is not visible in any attribution-only design.

Condition A carries most of the variance in the attribution measure and is the
comparison the claim needs. B versus C is the genuinely exploratory part, and is
labelled as such rather than presented as the study's point.

### Planned secondary analysis: are these corrections usable as training signal?

Every participant utterance is logged verbatim with its trial context, which
yields a corpus of at least 180 real correction attempts, each labelled with the
ground-truth fault and with whether the speaker diagnosed it correctly.

The analysis: present each correction, stripped of context, to an LLM and ask it
to recover what actually went wrong. If corrections produced under a correct
diagnosis are recoverable and those produced under a wrong one are not, then
misdiagnosis does not merely cost the user turns; it destroys the informational
value of their feedback to the system.

This is entirely post-hoc. It needs no protocol change and no extra
participants, and it converts "usable as training signal" from a claim in the
introduction into a measured result. Pre-registered alongside the rest.

## Contribution

Existing work measures blame as a **post-hoc attitude** — surveys after the fact,
asking who people hold responsible. This measures **diagnosis inside a repair
loop**, where being wrong has an immediate, countable cost, against ground truth
the participant does not have.

The 2×2 of *fault type × feedback modality* is the core. The interesting
possibility is not "feedback helps" but that the same feedback that helps when
the user is at fault **backfires** when the system is, by inviting repair
attempts that cannot succeed.

### Why VR, when the problem is not about VR

The substrate is a methodological choice, not the topic. Diagnosis can only be
scored against a known cause, and the cause has to be unarguable. In a text
agent, whether the output was even wrong is itself contestable, so any
attribution measure inherits that ambiguity. Here the ball is in your hand or it
is not, and we hold ground truth about both the outcome and the fault.

The findings are about speech-driven agents whose failures are opaque. VR is
where that can be measured cleanly, not what the claim is about.

## Rigour

| Threat | Handling |
|---|---|
| Experimenter bias in the probe | Ground truth hidden from the wizard until after the answer is recorded; prompt scripted verbatim |
| Order / learning effects | Williams balanced square |
| Feedback never registered | Per-trial manipulation check |
| First task doubles as training | Practice trial, excluded |
| Coder leniency on repairs | Word-boundary matching with fixed synonym sets, no substring matches |

### Ethics

Authorised deception with full debrief. Consent covers VR use, audio recording
and retention, and describes the study truthfully but incompletely as being about
how people respond when a speech-driven system does not do what they expected.
Naming the wizard in advance would destroy the measure.

The debrief reveals that every outcome was researcher-triggered, explains why
identical failures were necessary, and states plainly that the failures were
scripted and would have occurred whatever the participant said. On a study about
self-blame, letting someone leave believing they performed badly is not an
acceptable outcome. Participants may withdraw their data once they know what it
was, and are asked what they believed was happening; anyone who suspected a
wizard is excluded.

**Exclusion criteria are fixed before participant 1**: fewer than six completed
trials, technical failure affecting more than one trial, suspicion of the
deception, or withdrawal. Individual trials are excluded where
`preInjectHadSlot` is true, since the scripted feedback contradicted what the
participant actually said. All exclusions are reported with reasons.

**Audio is recorded for the whole session.** The transcript log captures only
what is said to the system through push-to-talk; the attribution answer is spoken
to the researcher and would otherwise exist nowhere, leaving the inter-rater
reliability plan with nothing to operate on.

**Stated limitations.** The wizard cannot be blind to condition, since they
operate the feedback. Coding vague attribution answers is a judgement call, so a
second coder rates ~20% for inter-rater reliability.

Asking *"why do you think that happened?"* after every error may itself train
participants to look for causes, and six trials makes that more acute than four.
The balanced square handles task order but not probe repetition, which is a
demand characteristic no ordering fixes. Reported as a limitation; trial number
is in the data, so a practice effect across positions is at least visible.

The scripted failure fires whatever the participant said, which is what keeps the
stimulus identical. On user-fault tasks that means someone who happened to supply
the missing detail is told they did not. Those trials are flagged
(`preInjectHadSlot`) and excludable, since for them the feedback was false and
"blamed the system" is the correct reading rather than a mis-attribution.

**Power.** n=10 per cell detects only large between-group effects (d≈1.3 at 80%).
The modality main effect is therefore **exploratory** and reported as such. The
within-participant contrast, user-fault versus system-fault, where each person is
their own control and contributes three trials per cell, is far better powered and
is where the claim lives.

The analysis plan above is pre-registered before participant 1.
