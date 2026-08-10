# Related work and positioning — Say It Again

Written to place the study against literature that actually exists. Every source
below was located and checked; none is from memory. **Read the originals before
citing them** — the summaries here are working notes, not a substitute, and two
of them turn on details that a one-line summary can easily get wrong.

---

## A correction to an earlier draft of this argument

An earlier version of the framing said: *self-serving bias is a robust effect
predicting that people blame failure on external causes; this study contradicts
it.* **That overstates the literature and should not be used.**

Miller and Ross's canonical review found support for self-**enhancing**
attribution under *success*, but reported only minimal evidence for
self-**protective** attribution under *failure* — which is precisely the half the
argument was leaning on. Zuckerman replied four years later arguing the
motivational bias survives scrutiny, so the failure side is **contested**, not
settled.

This is better for the project, not worse. Claiming to overturn a robust effect
invites a reviewer who knows the literature to dismantle the framing. Entering a
contested question with a controlled design and ground truth is a stronger and
more defensible position.

**Use this instead:** whether people protect themselves by attributing failure
externally has been disputed since the 1970s and has never been tested in a
setting where the true cause of the failure is known to the experimenter and
withheld from the participant. That is the gap.

---

## Strand 1 — Attribution of failure

**Miller, D. T., & Ross, M. (1975).** Self-serving biases in the attribution of
causality: Fact or fiction? *Psychological Bulletin*, 82(2), 213–225.

The canonical review. Found reasonable support for self-enhancing attribution
after success and **only minimal evidence** for self-protective attribution after
failure. Proposed a non-motivational account: people expect their behaviour to
produce success, and read covariation between behaviour and outcome differently
under improving versus constant-failure conditions.

**Zuckerman, M. (1979).** Attribution of success and failure revisited, or: The
motivational bias is alive and well in attribution theory. *Journal of
Personality*, 47(2), 245–287.

The rebuttal. Cite both, in that order — the disagreement is the point, and it is
what makes the question live rather than settled.

*Why it matters here:* this study measures failure attribution where the cause is
unambiguous to the experimenter. Neither of these could do that; both rest on
tasks where the participant's contribution to the outcome is genuinely uncertain.

## Strand 2 — Attribution and blame with voice assistants

**Baughan, A., Wang, X., Liu, A., Mercurio, A., & Chen, J. (2023).** A
Mixed-Methods Approach to Understanding User Trust after Voice Assistant
Failures. *CHI '23*. https://doi.org/10.1145/3544548.3581152

**This is the closest prior work and must be cited early and honestly.** A
crowdsourced dataset of 199 voice-assistant failures across 12 failure sources.
Found that users are more forgiving of — and tend to blame themselves for —
failures arising from *ambiguity in their own request*, while failures such as
overcapture damaged trust more severely.

*The gap it leaves, precisely:* the failures are **user-reported and
retrospective**. Participants supply incidents they remember, and nobody holds
ground truth about what actually caused any of them. So self-blame is observed
for failures that were, on the participants' own account, caused by their
ambiguity — which is arguably accurate attribution rather than misattribution.
The open question is whether self-blame persists when the cause is
**unambiguously the system**, which needs scripted failures and withheld ground
truth. That is this study.

**Cuadra, A., Li, S., Lee, H., Cho, J., & Ju, W. (2021).** My Bad! Repairing
Intelligent Voice Assistant Errors Improves Interaction. *Proc. ACM Hum.-Comput.
Interact.* 5, CSCW1, Article 27.

Manipulated whether an assistant made errors and whether it self-repaired.
Self-repair improved assessment after a genuine mistake and degraded it when no
correction was needed. Establishes that *how a system owns a failure* changes
user response — the premise conditions B and C rest on.

**Mahmood, A., et al. (2022).** Owning Mistakes Sincerely: Strategies for
Mitigating AI Errors. *CHI '22*.

Agents that shifted blame while apologising were rated worse than controls on
willingness to reuse. Relevant to the system-limitation feedback, which is an
admission rather than a deflection — and to the prediction that participants may
*discount* it.

## Strand 3 — Hyperarticulation and repair in speech interfaces

**Oviatt, S., MacEachern, M., & Levow, G.-A. (1998).** Predicting hyperarticulate
speech during human-computer error resolution. *Speech Communication*, 24(2),
87–110. https://doi.org/10.1016/S0167-6393(98)00005-3

**The single most important citation for H2.** Documents that users respond to
recognition errors with louder, slower, hyperarticulated speech, and proposes the
two-stage CHAM model (Computer-elicited Hyperarticulate Adaptation Model).
Critically, hyperarticulation is associated with **increased** recognition
errors — the repair makes things worse.

*What this study adds, and it is a clean extension rather than a replication:*
CHAM explains hyperarticulation as an adaptation to being **misheard**. Three of
the six tasks here involve no misrecognition whatsoever — in task 6 the system
understands the request perfectly and simply cannot perform it. If participants
hyperarticulate **there**, they are adapting to a model of the failure that is
false, and the behaviour cannot be explained as a rational response to evidence
of mishearing. That is a case the 1998 model does not cover, and it is exactly
what `wastedRepairs` counts.

Also relevant, and worth reading before writing the method: work on local versus
global hyperarticulation following misrecognition (*Journal of Memory and
Language* / *Speech Communication*, mid-2000s onward) — locate current versions
rather than citing from this note.

## Strand 4 — Explanation, trust, and the gap to behaviour

**Vasconcelos, H., Jörke, M., Grunde-McLaughlin, M., Gerstenberg, T., Bernstein,
M., & Krishna, R. (2023).** Explanations Can Reduce Overreliance on AI Systems
During Decision-Making. *CSCW 2023*. arXiv:2212.06823

Explanations reduce overreliance **only when they lower the cost of verifying the
AI's output**; the authors argue some null results in the literature follow from
explanations that never reduced that cost. A cost–benefit account of when an
explanation changes what someone does rather than what they say.

*Why it matters here:* this is the strongest available support for the design's
insistence that stated attribution and enacted repair are separate measures.
`wastedRepairs` is recorded independently of the probe precisely because a
participant can state the correct cause and still repeat themselves.

**Hoffman, R. R., Mueller, S. T., Klein, G., & Litman, J. (2018).** Metrics for
explainable AI. (Explanation Satisfaction Scale.) Already referenced in the
project. Retained as a citation for *why explanation satisfaction was cut*: it
measures satisfaction with an explanation, not whether the explanation produced a
correct diagnosis, and those come apart.

---

## The gap, in one paragraph

Attribution of failure to self versus system has been studied for fifty years
without ground truth: in psychology because the participant's real contribution
is uncertain, and in HCI because failures are collected retrospectively from
users who cannot know what caused them. Speech research has separately documented
that people respond to recognition failure by hyperarticulating, and that doing so
makes recognition worse. **Nobody has connected the two.** This study holds
ground truth over the cause of every failure, probes the diagnosis before any
repair is attempted, and counts what the diagnosis costs — including on failures
where no amount of clearer speech could ever have helped.

## Three sentences for the introduction

> When a speech agent fails without saying why, the user must diagnose it, and the
> two available diagnoses — *I said it badly* or *it cannot do this* — demand
> opposite responses. Prior work has shown that users blame themselves for
> voice-assistant failures they attribute to their own ambiguity [Baughan et al.
> 2023], and, separately, that users respond to recognition failure by
> hyperarticulating in ways that make recognition worse [Oviatt et al. 1998]; but
> no study has held ground truth over the cause of a failure while measuring the
> diagnosis that drives the next attempt. We do, and we find that the cost of
> misdiagnosis is countable, appears in failures where clearer speech could not
> possibly help, and is already visible in the logs of deployed systems.

(Final clause conditional on results. Do not write it until the data is in.)

---

## Verification notes

- Baughan et al. and Cuadra et al. were confirmed via ACM DOI listings; the ACM
  full text is paywalled from here, so **check the failure taxonomy and the exact
  self-blame finding in the PDF** before relying on the characterisation above.
- Oviatt et al. page range and DOI confirmed via DBLP and ScienceDirect.
- Miller & Ross and Zuckerman confirmed via the Psychological Bulletin and
  Journal of Personality records.
- The Vasconcelos et al. summary comes from the paper's own abstract.
- Strand 3's "local versus global hyperarticulation" line is deliberately
  unattributed. Find the current citation rather than inheriting an approximate
  one from this file.
