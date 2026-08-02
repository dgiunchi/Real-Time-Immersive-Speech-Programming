# DreamCodeVR — Study Design

One page, for supervisors and collaborators. Operational detail lives in
[STUDY_GUIDE.md](STUDY_GUIDE.md); filming in [DEMO_FILMING.md](DEMO_FILMING.md).

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

## Research question

> When an agent acts on your behalf and gets it wrong, does the **way it explains
> the failure** change whether you correctly diagnose the cause — and does correct
> diagnosis produce corrections that are actually usable as training signal?

## Design

**3 × 2 mixed factorial.**

| Factor | Levels | Assignment |
|---|---|---|
| Feedback modality | **A** none · **B** text panel · **C** embodied agent | Between-subjects |
| Fault type | User-correctable · System-caused | Within-subjects |

- **n = 30**, 10 per modality. Each participant sees one modality across all four
  tasks.
- B and C are **exclusive**: each carries the explanation exactly once, so the
  comparison isolates *modality*, not *amount* of information. The transcript
  strip stays visible in both, since it is not an explanation.
- **Wizard-of-Oz.** A hidden researcher triggers pre-scripted outcomes. No live
  LLM, so every participant meets an identical failure and we hold ground truth
  about what caused it — which no field data ever has.

### The four tasks

Task *is* scenario. Two of each fault type, so every participant experiences the
contrast.

| Task | Ask | What happens | At fault |
|---|---|---|---|
| 1 | Create an object in your hand | Nothing appears — no hand height given | **User** |
| 2 | Move an object next to a target | Moves the wrong way — no direction given | **User** |
| 3 | Create ~1000 objects | Only 8 appear — past the render limit | **System** |
| 4 | Create an object above the campfire | Created correctly, but behind you | **System** |

Tasks 1–2 are repairable by speaking differently. Tasks 3–4 are not, and telling
someone to rephrase would be actively misleading — so the feedback for those owns
the limit instead.

A **practice trial** runs first and is excluded from analysis, so the first
measured task is not doubling as push-to-talk training.

**Order** follows a Williams balanced Latin square, which balances not just
position but order-of-precedence. This matters because tasks 3–4 teach that the
system has limits; without balancing, that lesson would leak into tasks 1–2
asymmetrically.

## Measures

**Primary — attribution accuracy.** After each error, scripted verbatim: *"In
your own words, why do you think that happened?"* Coded self / system / unsure
against ground truth.

`correct ~ condition * scenarioType + (1|participant)`, mixed-effects logistic.
**The interaction is the finding**: feedback may help on user-fault tasks while
doing nothing, or actively harming, on system-fault ones.

**Co-primary — what the misattribution costs.** Attribution alone is a stated
belief, and *who cares* is a fair question. The repair move is that belief with a
price on it, coded live per attempt: added detail · **repeated verbatim** · shrank
the ask · asked the system · gave up.

`wastedRepairs` counts verbatim repetitions. That move cannot fix anything — not
a user fault, since the missing information is still missing, and not a system
limit, since the limit does not care how clearly you speak. It is what
misattribution looks like from outside, and crucially it is **observable without
a wizard**: repeated near-identical utterances are already in every voice
assistant's logs. That is the bridge from this lab study to a deployed system.

**Secondary — correction quality** (`repairContainsSlot`): does the repair
actually address the true cause? This is the training-signal measure. A
correction produced under a wrong diagnosis does not merely fail to help; it
teaches the wrong lesson.

**Manipulation check** per trial: did they register the feedback at all? Without
it, "feedback made no difference" cannot be distinguished from "they never saw
it," and in condition A it records whether they noticed the failure.

## Contribution

Existing work measures blame as a **post-hoc attitude** — surveys after the fact,
asking who people hold responsible. This measures **diagnosis inside a repair
loop**, where being wrong has an immediate, countable cost, against ground truth
the participant does not have.

The 2×2 of *fault type × feedback modality* is the core. The interesting
possibility is not "feedback helps" but that the same feedback that helps when
the user is at fault **backfires** when the system is, by inviting repair
attempts that cannot succeed.

## Rigour

| Threat | Handling |
|---|---|
| Experimenter bias in the probe | Ground truth hidden from the wizard until after the answer is recorded; prompt scripted verbatim |
| Order / learning effects | Williams balanced square |
| Feedback never registered | Per-trial manipulation check |
| First task doubles as training | Practice trial, excluded |
| Coder leniency on repairs | Word-boundary matching with fixed synonym sets, no substring matches |

**Stated limitations.** The wizard cannot be blind to condition — they operate
the feedback. Coding vague attribution answers is a judgement call, so a second
coder rates ~20% for inter-rater reliability.

**Power.** n=10 per cell detects only large between-group effects (d≈1.3 at 80%).
The modality main effect is therefore **exploratory** and reported as such. The
within-participant contrast — user-fault versus system-fault, where each person is
their own control — is far better powered and is where the claim lives.

The analysis plan above is pre-registered before participant 1.
