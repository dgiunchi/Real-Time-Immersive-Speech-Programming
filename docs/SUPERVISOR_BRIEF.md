# Say It Again

### Misattributed blame and wasted repair in speech-driven agents

**Akbar Juraev** · MSc project brief for supervisors · 10 August 2026

Operational detail is in `STUDY_DESIGN.md`; the analysis plan is in
`docs/PAPER_OUTLINE.md`. This document is the argument, the hypotheses, and the
three decisions I would like your view on.

---

## 1. The problem

Every speech-driven agent has a failure mode where the user cannot tell whether
**they phrased it wrong** or **the system cannot do it**.

The two require opposite responses — add detail, or stop asking and work around
it — and most systems give no signal about which applies. Guess wrong and you pay
twice: you burn turns on a repair that cannot possibly work, and the correction
you produce is misdiagnosed, which makes it useless as training signal for the
system that caused it.

This is not a VR problem. It is what happens when anyone repeats a command to a
smart speaker, louder each time. People do not do that because they are careless.
They do it because repeating is the only hypothesis a silent failure supports.

## 2. The question, and why the answer is not obvious

> **When a speech agent fails without saying why, who do people blame — and what
> does guessing wrong cost them?**

The non-obvious part is the direction of the prediction.

Classical attribution research (self-serving bias) says people attribute
**failure externally** to protect self-esteem: it should be the machine's fault.
I predict the opposite — that people blame themselves — and the reason is
mechanistic rather than emotional:

> External attribution requires a causal model of the other party. An opaque
> system denies the user one, so the only hypothesis available is the one about
> their own behaviour: *"I must have said it badly."*

If that holds, it is a boundary condition on a robust and well-established
effect, not merely an observation about voice interfaces. If it fails, the design
still yields a usable result, because the cost measure below is recorded
independently of what participants say.

## 3. Hypotheses

**H1 — Misdiagnosis.** Attribution accuracy is lower on system-caused failures
than on user-caused ones.
*Test:* `attributionCorrect ~ scenarioType * condition + (1|participant)`,
mixed-effects logistic. The **main effect of `scenarioType`** is the headline. It
is within-participant with three observations per cell, so it is the
best-powered comparison in the design and every participant is their own control.

**H2 — The cost.** Misdiagnosis is not merely a stated belief; it buys wasted
turns. On system-caused failures participants spend more repair attempts that
cannot succeed, and the belief that rewording would work mediates this.
*Test:* `wastedRepairs ~ scenarioType * condition + (1|participant)`, count
model; then the mediation `repairConfidence → wastedRepairs`.

This is the half of the claim with a price attached, and it is **observable
without a wizard** — repeated near-identical utterances are already in every
voice assistant's logs. That is the bridge from a lab study to a deployed system.

**H3 — Explanation (exploratory).** Whether the failure is explained, and how,
changes both of the above.
*Status:* between-subjects at n=10 per cell, so it detects only large effects.
**Pre-registered as exploratory.**

The interesting term here is an **interaction, not a main effect**. That feedback
improves accuracy when the user is at fault is trivially true and not a finding.
The non-trivial prediction is asymmetric:

> Participants will accept an explanation that blames them, and **discount** the
> same system's explanation when it admits its own limitation.

If that holds, conditions B and C do not reach ceiling on exactly the half of the
design that matters, and the reason is psychological rather than informational.

## 4. Design

**3 × 2 mixed factorial. N = 30, n = 10 per condition.**

| Factor | Levels | Assignment |
|---|---|---|
| Feedback delivery | **A** none · **B** agent's words as text · **C** agent's words spoken by an avatar | Between |
| Fault type | User-correctable · System-caused | Within |

**Condition A is the control the claim needs.** In B and C the feedback states
the cause outright, so A is the only cell where a participant must diagnose
unaided — the only cell where misattribution can occur freely. It is also the
design's hedge: H1 in B and C depends on participants discounting an admission of
limitation, which is plausible but untested.

**B and C carry the same sentence** and differ only in delivery, so any
difference between them is attributable to delivery rather than wording.

**Six tasks, three per fault type**, so every participant meets the contrast three
times. User-fault tasks are repairable by speaking differently; system-fault
tasks are not repairable at all, and are three different flavours of that — a
ceiling on something supported, a correct execution in an unexpected place, and a
capability that does not exist.

**Wizard-of-Oz.** A hidden researcher triggers pre-scripted outcomes. No live
LLM, so every participant meets an identical failure and we hold ground truth
about the cause — which no field data ever has. Order is counterbalanced by a
Williams square; a practice trial is excluded.

**Why VR.** The substrate is a methodological choice, not the topic. Diagnosis
can only be scored against a known cause, and the cause has to be unarguable. In
a text agent, whether the output was even wrong is itself contestable. Here the
ball is in your hand or it is not.

## 5. Measures

**Primary — attribution**, probed verbatim after every failure, before any repair
attempt: *"In your own words, why do you think that happened?"* Coded
self / system / both / unsure against ground truth hidden from the researcher
until the answer is recorded.

**Co-primary — wasted repairs.** Repair moves coded live, one per attempt. The
re-say family (same again · slower or louder · reworded with nothing added)
cannot fix anything: not a user fault, since the missing information is still
missing, and not a system limit, since the limit does not care how clearly you
speak.

**Co-primary — correction quality.** Does the repair actually address the true
cause? A correction produced under a wrong diagnosis does not merely fail to
help; **it teaches the wrong lesson.** See §6.

**Mediator** — confidence, 0–10, per trial: *"How confident are you that there is
something you could say differently that would make that work?"* On a
system-limitation trial a high rating **is** the false belief H1 predicts.

**Attempt-level.** ~500 attempts across the study rather than 180 trials, each
with a continuous outcome: speech onset latency, loudness and speech rate against
that participant's own practice baseline. Hyperarticulation is measured
acoustically as well as coded live, so the live coding has independent
corroboration.

**Self-report** — 23 rated items (NASA-TLX, perceived support, retrospective
attribution, IPQ presence, self-efficacy paired pre/post, discomfort paired
pre/post).

## 6. Contribution

**Existing work measures blame as a post-hoc attitude** — surveys after the fact,
asking who people hold responsible. This measures **diagnosis inside a repair
loop**, where being wrong has an immediate countable cost, against ground truth
the participant does not have.

Three claims, in order of how far they travel:

1. **Corrections harvested from users are systematically corrupted by
   misdiagnosis.** A user who wrongly believes they mis-phrased produces a
   "correction" that teaches the wrong lesson. This matters to anyone training or
   fine-tuning on interaction logs, and it is not specific to speech, VR, or any
   current model.
2. **Misattribution has a countable price, and it is detectable in production.**
   Repeated near-identical utterances are already logged by every voice
   assistant. A system could detect the pattern and change its response.
3. **Opacity may reverse self-serving attribution.** A boundary condition on a
   well-established effect.

## 7. Status

| | |
|---|---|
| Instrument | Built, tested, working end to end |
| Participants collected | **0** — pilot sessions archived; the protocol has changed materially since |
| Ethics | Authorised deception with full debrief; audio consent is a separate, enforced tick |
| Ready to collect | Yes, pending the decisions below |

## 8. Three decisions I would like your view on

**(a) Two conditions or three?** H3 is underpowered by design and is the least
durable question in the project — "which delivery is better" dates with the
interface. Dropping to **A and C at n=15** would give a better-powered
explained-versus-unexplained contrast and lose only the delivery comparison
already labelled exploratory. Three cells keeps the modality ladder but spends a
third of the sample on the weakest question.

**(b) How hard to push the mechanism claim.** A versus {B, C} compares *silence*
with *explanation*, which is not a clean manipulation of causal-model
availability — they differ in acknowledgement and timing too. A clean test needs
a fourth cell (acknowledge the failure without stating its cause), which at N=30
would put every cell at n=7–8. My inclination is to state the mechanism as
motivation, test the interaction, and write the confound as a limitation rather
than chase it.

**(c) Pre-registration.** I would like to pre-register H1, H2 and the exploratory
status of condition before collecting. With zero participants this window is open
now and closes at P01.
