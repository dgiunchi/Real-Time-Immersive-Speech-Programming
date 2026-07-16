# Viva / Dissertation-Defence Q&A

**MSc — Cybersecurity + Privacy in XR**
**System:** DreamCodeVR+ — speech → STT → LLM-writes-C# → Rust safety validator → Unity
runtime compile, hardened with HMAC/Ed25519 auth, encryption-at-rest, a 128-vector attack
model and a perceptual-safety layer, and extended with **two ML thrusts** unified by
**age-adaptive dual-plane safety.**

> Grounded in [`RESEARCH_AND_ML_PLAN.md`](RESEARCH_AND_ML_PLAN.md),
> [`SECURITY.md`](SECURITY.md), [`docs/HARDENING.md`](docs/HARDENING.md) and the red-team
> corpus (`redteam/corpus_gen.py`). Every bolded line is a **say-it-aloud** model answer.
> Answers are deliberately honest about limitations — examiners reward candour, not
> over-claiming.

---

## Elevator pitch (one paragraph — memorise this)

DreamCodeVR+ lets someone in VR **speak** and have an LLM write and run scene code live,
behind a fail-closed Rust safety validator. My dissertation adds a single idea:
**age-adaptive dual-plane safety.** One privacy-preserving, on-device age signal
(child-vs-adult, voice-primary) simultaneously tightens **two** planes at once — the
**code-safety** plane (what C#/actions the LLM is allowed to run) and a new
**perceptual-safety** plane that inspects the generated *scene* for immersive harms such
as forced locomotion, occlusion and flashing **before compile**, and crucially catches
**compositional** harm where each op is individually within bounds but the *sequence*
composes into an attack. The sharp move is an **inversion**: the exact motion/voice
inference that the XR-privacy literature (Nair et al., USENIX Sec 2023) demonstrates as a
*surveillance attack* I re-use — on-device, ephemeral, template-free, consented — as a
**governed protective control**, and I red-team that control as its own attack class. The
contribution is the *coupling* of an age posterior to a real-time dual-plane safety
policy, not age inference itself, which I treat honestly as prior art.

---

> ## If you only remember 3 sentences
> 1. **The novelty is the coupling, not the components:** one on-device age signal drives
>    *both* code-safety *and* perceptual-safety, and nobody in 2022–2026 couples an age
>    posterior to a real-time safety policy or unifies those two planes over an
>    LLM-writes-runnable-code XR loop.
> 2. **Compositional harm is the strongest, least-contested wedge:** individually-bounded
>    ops compose into harm (herding, progressive occlusion, strobe-by-accumulation), and
>    per-action bounds *provably* miss it — I model the cumulative envelope instead.
> 3. **I confront the paradox head-on** ("you built the surveillance you critique") — the
>    inversion is deliberate, governed by on-device/ephemeral/template-free/consent-gated/
>    fail-closed design, and the age model is a *safety net*, never the legal basis
>    (Meta's platform flag and verifiable parental consent remain authoritative; FTC, Mar
>    2024).

---

# (A) Contribution & novelty

### A1. In one sentence, what is the novel contribution?

**"Age-adaptive dual-plane safety: a single privacy-preserving, on-device age signal
simultaneously tightens an executable-code-safety plane and a perceptual/embodied-safety
plane over a live speech→LLM→C#→runtime-compile XR loop — the *coupling* is new, not the
age inference, which is prior art."** The three defensible sub-claims are age-conditioned
enforcement of *generated code*, unification of two disjoint safety literatures, and
detection of *compositional* immersive harm inside LLM-generated scene code pre-compile.

### A2. Why is this novel — hasn't age-gating and code-safety each been done?

**"Both components exist in isolation; the combination does not. Content-moderation
age-gates *rendered* content, not runtime-*generated code*. Code-safety (SecurityEval /
SALLM lineage) and perceptual/embodied safety (Casey 'Human Joystick', IEEE TDSC 2021;
Inception, arXiv:2403.05721) live in *disjoint* literatures. No prior system that I found
in 2022–2026 (a) couples an age posterior to a real-time safety policy, or (b) unifies
code-safety with perceptual-safety in one gate over an LLM-generates-runnable-code loop.
That coupling is the wedge."**

### A3. Which of your claims is strongest and which is weakest — be honest.

**"Strongest and least-contested is compositional-harm detection: a sequence of
individually-bounded ops composing into harm, which per-action bounds provably cannot see.
Weakest is the standalone age model's raw accuracy — motion-age is only ~78% (Nair,
arXiv:2305.19198) and voice-age is spoofable — which is exactly why the age signal is a
*safety net*, not an authorisation mechanism, and why child-vs-adult (a much easier
boundary) is the operating point rather than fine-grained age regression."**

### A4. What is "compositional harm" and why do per-action bounds miss it?

**"Each generated operation can individually satisfy every bound — a small rotation, a
single opaque quad, one brief luminance spike — yet the *sequence* composes into an
attack: repeated nudges become forced locomotion / human-joystick herding (Casey, TDSC
2021), stacked overlays become progressive occlusion, and accumulated spikes become a
strobe. Per-action validation is memoryless, so it is structurally blind to this. I model
the *cumulative envelope* instead — net displacement, FOV-coverage fraction, luminance/
flash rate over a window — and age-condition those envelope thresholds."**

### A5. What exactly do the two ML thrusts add on top of the existing system?

**"Two learning models plug onto the platform I already built. Model #1 is an on-device
voice-primary age gate (child<13 / adult) with calibrated confidence. Model #2 is a
continuous/adaptive attack analyzer that turns the static 128-vector attack model into a
self-growing set via an adaptive red-teamer plus streaming anomaly/drift detection — and,
as the unifier, it *red-teams the age gate itself* as a new vector class. The perceptual
layer is upgraded from authored monitors into a learned *detection* plane."**

### A6. Why is this a *cybersecurity + privacy* contribution and not just an HCI/safety one?

**"Because the threat model is adversarial end to end. The input is an untrusted spoken
utterance, the code is written by an LLM that can be jailbroken (character-injection
guardrail bypass runs 70–88% ASR, arXiv:2504.11168), the transport is an untrusted Ubiq
relay (HMAC + Ed25519 + replay guard), and the *defence itself* — the age model — is a
biometric-inference capability that is simultaneously a privacy *risk*. The whole design
is about defending a safety policy against an adaptive attacker while not becoming
surveillance. That is a security-and-privacy contribution."**

---

# (B) The "you built the surveillance you critique" ethics attack

### B1. You criticise VR biometric inference as surveillance, then you build an age-inference model. Isn't that hypocritical?

**"It's deliberate, and it's the intellectual core of the thesis — I call it the
*inversion*. The XR-privacy literature (Nair et al., USENIX Sec 2023, arXiv:2302.08927)
shows head-and-hand motion re-identifies 94.33% of 55,000+ users and leaks age, gender,
body and even health — as a *surveillance attack*. I take that same inference and re-purpose
it, under strict governance, as a *protective control*: on-device, ephemeral,
template-free, unlinked, consent-gated, fail-closed, and with hard purpose-limitation so
the age signal *only* tightens safety and can never feed ads, profiling or identity. The
contribution is showing that a capability the literature frames as attack can be governed
into a defence — and holding myself to that governance as a measured result, not a
promise."**

### B2. "Governed" is easy to say. What structurally stops your age model from becoming surveillance?

**"Six structural controls, not policy promises. (1) It runs *in-process with STT* on the
headset — raw audio, motion and all embeddings are discarded per window; I persist only
`{band, confidence, timestamp, model-version}` with a TTL and an erasure path. (2) **No
stored voiceprint or template** — nothing that can be matched later. (3) Purpose
limitation is enforced in the data path: the only consumer of the age signal is the safety
policy. (4) Consent-gated activation, revocable *per modality* (motion-only if voice-age
consent is declined). (5) The platform flag is authoritative; my inference can only
*restrict*, never *authorise*. (6) I *quantify* the residual risk with a shadow-probe —
see B4."**

### B3. Even on-device, you're inferring a protected attribute about a child without their meaningful consent. Justify that.

**"Two answers. Legally, in the child regime I don't rely on the child's consent —
verifiable parental consent and the Meta platform age flag are the legal basis (FTC
denied facial-age *estimation* as a COPPA consent mechanism in March 2024, so inference
gates *experience* but never replaces consent). Ethically, the design fails safe *for the
child*: under uncertainty it defaults to the strictest child profile, so an error tightens
protection rather than exposing anyone. And I never hard-block an adult on a low-confidence
guess — the asymmetry is chosen so the failure mode harms neither party."**

### B4. How do you *prove* the age model isn't secretly leaking identity or disability?

**"I treat the detector-as-risk as a measurable quantity, not an assurance. I run a
*shadow-probe*: train adversarial probes to recover identity, gender and disability signals
from the age model's features, report the leakage, then show that differential privacy and
deep-motion-masking (arXiv:2311.05090 reports ~96% de-anonymisation reduction) drive it
down — with the privacy-utility curve made explicit. That turns 'trust me' into a number in
the results chapter, and it's honest that the number won't be zero."**

### B5. If the honest answer is "any age model is spoofable and leaky," why build it at all — why not just use the platform flag?

**"The platform flag *is* the primary control — my model is explicitly a secondary safety
net. Its job is the case the flag misses: a shared family headset logged into an
adult-flagged account with a child actually wearing it. The ML signal is an *anomaly
detector* — 'this adult-flagged session is behaving and sounding like a child' → trigger
re-assurance / parental re-verify. That's a genuine gap the flag alone can't close, and
framing it as anomaly-triggered re-verification (not authorisation) is what keeps it
proportionate."**

---

# (C) ML age model — method, accuracy, calibration, fairness, spoofing

### C1. Walk me through the age model architecture.

**"Voice is the primary signal. I warm-start from audEERING's public
wav2vec2-large-robust age/gender model (arXiv:2306.16962), freeze the early layers —
layer-wise probing shows layers 1–7 carry the most child signal (arXiv:2508.10332,
2307.16398) — and train a small (<1M-param) MLP head for binary child(<13)/adult, plus a
coarse child/teen/adult band, int8-quantised to run in-process on Quest (edge age models
run ~20 ms, J. Signal Processing Systems 2024). I aggregate per-utterance across the
*session*, not a single clip. Motion is a caveated *secondary* fusion signal."**

### C2. What accuracy do you actually get, and on what data?

**"Child-vs-adult is the strong regime: the literature reports 97.14% age-group accuracy
on CMU Kids, and SSL voice-age regression sits at ~4–5 yr MAE on TIMIT (arXiv:2502.12007,
2306.16962). Motion-age is much weaker — Nair reports 78.3% per-user binary age
(arXiv:2305.19198), and it's weaker than height (86.7%) or weight (85.0%) because
anthropometrics carry most of the age-correlated motion signal. I evaluate on aGender
(which has a <13 children class) as primary, CMU Kids / PF-STAR for the child regime,
Common Voice for cross-corpus, plus MUSAN noise/reverb/codec augmentation to simulate the
Quest mic. I *don't* quote the unverified secondary '~90% F1' figures floating in
summaries — I flag those as not load-bearing."**

### C3. Accuracy isn't enough for a gate — how is it calibrated?

**"Calibration is first-class because a safety gate acts on *confidence*, not just the
argmax. I report ECE, Brier score and reliability curves, apply temperature scaling (and
conformal prediction for set-valued outputs), and — critically — put buffer zones around
the 12/13 and 17/18 boundaries where the model *abstains and escalates* rather than
deciding. Near the COPPA line I fail closed to the strictest child profile. So an
uncalibrated-overconfident output can't silently mis-gate — it routes to re-assurance."**

### C4. What are the fairness risks and how do you audit them?

**"They're real and I audit them as a mandatory chapter, not a footnote. Voice-age is
biased by accent, L1-vs-L2 speech and atypical/disordered speech; motion-age confounds
with body size and disability — it will mis-age short adults, tall children and wheelchair
users. The safety-critical metric is the **subgroup false-ADULT rate** — a child passing
as an adult is the failure that removes protection — so I report false-adult and
false-child rates *per subgroup* (gender, accent, L1/L2, atypical speech) and set
thresholds to fail safe for the child. I state plainly that fairness gaps are expected and
measured, not eliminated."**

### C5. Your age model is a biometric classifier. Why isn't the voice itself a voiceprint / GDPR Art. 9 data?

**"I'm honest that this is contested. My argument is that I compute a *transient
classification* and discard the audio and all embeddings per window — I never store a
template that could match or re-identify a person, which is what distinguishes
age-estimation from biometric *identification*. A 2026 position paper ('Age Estimation
Models Do Not Process Biometric Data', arXiv:2605.17347) supports the transient-on-device
argument — but it is *contested*, so I don't rest the design on it. I mitigate regardless:
on-device, ephemeral, template-free, and I run a DP / motion-masking privacy-utility study
so the claim is defensible even if the legal question resolves against me."**

### C6. How spoofable is the age gate, and how do you handle it?

**"Very spoofable in the abstract — pitch/formant shifting, voice conversion, replay,
deepfake child/elderly voices, and motion mimicry. I treat spoofing as a *first-class
result*, not a threat I wave away: I red-team the gate with each of those attacks, measure
the bypass rate, feed the successful bypasses into the attack analyzer as a new vector
class, and report the before/after-retrain curve (mirroring the 15%→100% hardening story).
The defence-in-depth answer is that the platform flag stays authoritative and the gate
only *restricts* — so a successful spoof that makes a child *look adult* still can't
authorise anything the parent didn't, and a spoof that makes an adult *look child* only
tightens safety."**

### C7. Why voice-primary and not motion-primary, given motion is always available in VR?

**"Accuracy drives it: child-vs-adult from voice (~97% group accuracy) far exceeds
binary age from 3-point motion (~78%, Nair 2305.19198), and there is **no public
child-labelled VR 3-point-motion dataset** — BOXRR-23's 4.7M recordings are adult-skewed
with no child labels (arXiv:2310.00430). So voice is load-bearing and motion is a caveated
secondary cue. The trade-off I acknowledge: voice is the more accurate signal but the
higher regulatory-risk one, which is why the ephemeral/template-free design and the
per-modality consent (motion-only fallback) matter."**

### C8. What's the one genuinely new feature your fusion has that prior work didn't?

**"Speech-to-action latency. My pipeline already timestamps speech-in → scene-action, so I
get *reaction time* for free — older users are roughly 1.3× slower — and no prior VR-age
work had this feature because their pipelines weren't a live speech→code loop. I late-fuse
it with Nair-style 6DoF anthropometric features (HMD height, wingspan, reach, jerk) in a
LightGBM head and ablate voice-only / motion-only / fused to show its lift honestly."**

---

# (D) Regulation & compliance

### D1. Does your age model satisfy COPPA?

**"It doesn't *satisfy* COPPA on its own, and I'm careful to say so. The FTC **denied
facial age estimation as a COPPA-compliant consent mechanism in March 2024**, and the same
logic applies to voice-age inference — so my model can gate *experience* but cannot replace
**verifiable parental consent**. The COPPA final amended rule (16 CFR 312, 2025, compliance
April 2026) also newly treats biometric identifiers as covered data, which is another
reason I'm template-free. My compliance story is: platform flag + verifiable parental
consent are the legal basis; my inference is a downstream safety net that only tightens
protection."**

### D2. Which regulatory venue are you designing for — US or UK/EU?

**"UK/EU is the default venue, with COPPA mapped as the US analogue. That means the UK
Age-Appropriate Design Code (ICO Children's Code) — high-privacy defaults, a mandatory
DPIA, 'best interests of the child', a Challenge-25-style buffer — plus GDPR Art. 8 for
child consent, the EU AI Act, and the assurance standards ISO/IEC 27566-1:2025 and IEEE
2089.1-2024. I include the regulatory mapping as an appendix with the residual legal
uncertainty stated honestly."**

### D3. The EU AI Act bans emotion recognition — doesn't your model fall foul of Art. 5?

**"No, and I designed around it explicitly. EU AI Act Art. 5 (in force Feb 2025) prohibits
**emotion recognition** in many contexts and certain biometric categorisation — so I
**exclude any affect or emotion inference entirely**; the model outputs an age band and
nothing else. Age is *not* itself a banned special category (Recital 16), but the AI Act
notes the pathway matters, so I keep it minimal, purpose-limited and documented. Excluding
emotion is a hard design constraint, not a preference."**

### D4. How does IEEE 2089.1 / ISO 27566-1 shape the design?

**"ISO/IEC 27566-1:2025 gives the age-assurance *framework* vocabulary and IEEE 2089.1-2024
defines five assurance levels, so I position my ML estimate at the *lower-assurance,
inference* tier and explicitly state it must be *backstopped* by a higher-assurance
mechanism (parental consent / platform verification) for anything with legal effect. Naming
where I sit on those ladders is how I avoid over-claiming the assurance level of a
spoofable inference."**

### D5. Meta already provides an age-group flag on Quest. Why does yours matter, and which wins in a conflict?

**"Meta's preteen/teen/adult age-group API (April 2024) is **authoritative** — in any
conflict, the platform flag wins and my model defers. Mine matters only as a **secondary
anomaly signal** for the shared-headset case the flag can't see: an adult-flagged account
being worn by a child. When my inference and the flag disagree in the child-risky
direction, I don't override — I escalate to re-assurance / parental re-verify. Framing it
as 'never the legal basis, only a safety net' is what keeps it compliant."**

### D6. A DPIA would ask: what's your lawful basis and data-minimisation story in one breath?

**"Lawful basis for the *safety* processing is the child's best interests / legitimate
interests bounded by the AADC, with parental consent as the basis for anything with legal
effect; data minimisation is structural — I process transient audio on-device, store *no*
raw biometrics and no template, persist only a coarse band + confidence + timestamp +
model-version under a TTL with an erasure path, and log **policy decisions only, never raw
biometrics**, even in the end-to-end Quest evaluation."**

---

# (E) ML attack analyzer — drift, open-set, adaptive red-team, LLM-judge

### E1. Why turn a static 128-vector attack model into a learned one — isn't a curated list safer?

**"A static list is only as good as yesterday's imagination, and guardrails demonstrably
leak — character-injection bypasses hit 70–88% ASR against Azure Prompt Shield, Meta Prompt
Guard and ProtectAI (arXiv:2504.11168). So I keep the curated 128 vectors as a regression
baseline *and* grow them: an Auto-RT / WildTeaming-style adaptive red-teamer
(arXiv:2501.01830) emits *spoken* prompts → STT → LLM → C#, hunting inputs that pass the
Rust validator and the LLM screen yet still yield unsafe code. Every new bypass
auto-augments the vector store, and I report new-vectors/hour and bypass-rate-over-time
under *adaptive* (not static) attack."**

### E2. How do you catch attacks you've never seen — the open-set problem?

**"Layer 2 is a streaming anomaly detector trained *only on benign* intent→code-op
sequences: an LSTM/transformer autoencoder flags high reconstruction error, and a
One-Class-SVM / Isolation-Forest over its latent space flags open-set novelty. Crucially I
evaluate with attack families **held out of training**, so the reported open-set detection
rate is honest about generalisation rather than memorisation."**

### E3. Models decay — how do you stop concept drift from silently degrading the analyzer?

**"I wrap the streaming detector with explicit drift detection — ADWIN/DDM — plus
SSF-style continual learning (arXiv:2412.16264): a memory buffer with strategic forgetting
so it adapts to new benign patterns without catastrophically forgetting old attacks. I
report a *drift-recovery* metric — how fast detection recovers after a distribution shift —
rather than a single static accuracy that would hide decay."**

### E4. You use an LLM as part of the screen. LLM judges are overconfident — how is that safe?

**"That's a documented failure mode (arXiv:2508.06225), so I never trust a raw LLM verdict.
I calibrate the judge (temperature scaling / conformal) and wrap it in an
**abstain→escalate** policy: below a confidence threshold it doesn't approve, it defers to
the deterministic Rust validator and, if still uncertain, fails closed. I report the
judge's calibration and its abstain/escalate rates as results — the LLM is a *second
opinion*, never the sole gate."**

### E5. Is the deep structural code-analysis (AST/GNN) part of the thesis or hand-waving?

**"I'm explicit that it's a *stretch/optional* layer, and I'd rather cut it cleanly than
half-build it. A Roslyn AST+CFG+data-flow GGNN / GraphCodeBERT second opinion (Devign
lineage, NeurIPS 2019; Vul-LMGNNs, arXiv:2404.14719) would fuse with the LLM screen via a
temperature-scaled fail-closed ensemble — but it *requires building a small labelled
C#/Unity malicious-benign corpus that doesn't exist*, which is real cost. So for a single
MSc I scope it out and rely on the deterministic lexical+semantic validator plus the
learned anomaly layer, and name the GNN corpus as future work."**

---

# (F) The security core

### F1. Why Rust for the validator?

**"Memory safety and a fail-closed default. The validator sits on the trust boundary
between untrusted LLM output and code that can touch the scene, so a memory-corruption bug
*in the guard itself* would be the worst possible vulnerability — Rust removes that class.
It also lets the Mode-C path make unsafe operations *unrepresentable* in the type system
(a bounded six-action IR), so the safe path is safe by construction, not by remembering to
check. Crypto verification uses the audited `ring` crate, constant-time."**

### F2. Why two different crypto primitives — HMAC *and* Ed25519?

**"Because the two trust directions have different threat models, and I want a compromise
on one leg to be unable to forge the other. Client→backend is symmetric room-admission
trust, so it's an **HMAC-SHA256** MAC over the authenticated envelope — cheap, per-session.
Backend→Unity is where a forged *approved-code* decision would be catastrophic, so it's an
**asymmetric Ed25519 signature**: the private signing key lives *only* in the backend and
Unity holds just the public key — so even a leaked client admission secret cannot forge a
backend-approved NID-94 code decision. Different primitives by design, not by accident."**

### F3. How do you prevent replay?

**"The signed region of the envelope binds a per-session **monotonic sequence**
(`SessionSequence`, a strict-monotonic replay bucket per peer) plus an expiry, so a captured
tag can't be re-sent later. It also binds the message **domain** — the target NetworkId
(`network_id_b`) — so a valid tag for one message type can't be replayed onto another (e.g.
a benign message's tag can't be lifted onto a NID-94 code message), and a **SHA-256 payload
hash** so the exact code is pinned. Protocol version and security profile are bound too, so
a downgrade attack is detectable."**

### F4. What does "fail-closed" concretely mean here — show me it isn't a slogan.

**"Concretely: in the `hardened` profile a *missing* required control — no admission
secret, no backend signing seed, no real Roslyn analyzer — is a **startup error**, never a
silent downgrade to legacy. In `legacy` the mock analyzer approves (fail-*open*) and I say
so plainly; in `hardened` an unwired analyzer refuses to run. Mode-C makes unsafe ops
unrepresentable. And near the age boundary the policy defaults to the strictest child
profile. Fail-closed is enforced at startup and in the type system, not asserted in
prose."**

### F5. Tell me the "15%→100%" story properly — what does it actually measure?

**"It's the adversarial-hardening curve for the generated-C# validator. Against the first
red-team corpus the lexical guard initially caught roughly **15%** of malicious vectors —
because attackers don't write `Process.Start` literally, they use namespace-alias bypass
(`using Sys = System;`), Unicode-escape identifiers (`GetType`), type-aliases,
newline-obfuscation and string-based reflection. I iterated the denylist against each
family until the corpus (now 1000+ vectors across process-spawn, reflection, DllImport,
unsafe, file/net-IO) reached **0 bypass** — 100% caught. I'm careful to frame that as *0
bypass against a known, growing corpus*, not a proof of completeness; the denylist is one
layer of defence-in-depth, which is exactly why the ML analyzer (Section E) exists."**

### F6. What did the cross-language auth campaign actually test, and what were the results?

**"An adversarial campaign across the Rust↔Unity boundary exercising forge, tamper, expire,
wrong-domain, downgrade, truncate, garbage and replay attacks against the envelope — with
a Rust-signed / Unity-verified golden vector so both implementations agree byte-for-byte.
Result: **0% bypass and 0% false-positive** across all eight classes. The honest caveat is
that this is *host-side automated evidence*; on-device Quest verification of the hardened
profile is pending hardware (targeted 2026-07-23), and the default `legacy` profile is
byte-identical to the original (peers self-assert, plaintext channel) — the hardened
controls are opt-in."**

---

# (G) Limitations & threats to validity

### G1. What is the single biggest limitation of the whole thesis?

**"No public child-labelled VR 3-point-motion dataset exists — BOXRR-23 is adult-skewed
with no child labels (arXiv:2310.00430). That's why motion-age is a *caveated secondary*
cue and voice is load-bearing, and I report the dataset gap itself as a finding rather than
papering over it with a synthetic set I'd have to over-trust. Without IRB child-data
collection (a heavy ethics burden I don't take on), the motion-age thrust is validatable
only via adult proxies and honest limits."**

### G2. Your evaluation sets are constructed by you. Isn't that circular / cherry-picked?

**"It's the fair criticism, and I meet it three ways. First, I use *public* corpora
wherever they exist (aGender, CMU Kids, PF-STAR, Common Voice, TIMIT, plus SecurityEval/
SALLM CWE-mapped probes for the code validator baseline) rather than only my own data.
Second, for the parts I *must* construct — the labelled safe/unsafe generated-scene set and
the compositional cases — I hold attack families *out of training*, generate adversarially
rather than hand-pick, and publish the generator (`redteam/corpus_gen.py`) so it's
reproducible, not cherry-picked. Third, I state explicitly that 'no benchmark for unsafe
LLM-generated VR scenes exists' is itself a contribution-shaped gap and a threat to
external validity."**

### G3. 78% motion-age accuracy is barely above chance for a safety decision. Defend it.

**"I agree 78% is unacceptable as a *sole* gate — which is precisely why it's never used
that way. It's a fused *secondary* cue behind a ~97%-group-accuracy voice signal and behind
the authoritative platform flag, and the operating point is coarse child-vs-adult, not
fine-grained age. The system fails safe for the child under low confidence and escalates to
human/parental re-verification rather than acting on a weak posterior. The 78% buys anomaly
sensitivity on a shared headset; it doesn't buy authorisation."**

### G4. Differential privacy destroys age accuracy. Doesn't that collapse your privacy story?

**"The DP-vs-age-accuracy trade-off *is* severe — I don't hide that. My answer is to (a)
pair DP / deep-motion-masking with a *non-biometric fallback* (the platform flag and
per-modality consent) so the safety function survives even when DP degrades the model, and
(b) publish the privacy-utility curve so the trade-off is a *measured* result the reader
can judge, not a claim I've optimised away. The honest position is that strong DP and high
age accuracy are partly incompatible, and the design tolerates that by never depending on
the model alone."**

### G5. How much of this is actually *built* versus proposed?

**"I separate them cleanly. Built and tested: the full speech→STT→LLM→Rust-validator→Unity
loop, the four execution modes, the HMAC/Ed25519/replay hardening (0% bypass across eight
attack classes, host-side), the 128-vector model and the 15%→100% C#-validator hardening,
and the authored perceptual-safety monitors — with Mode-A runtime-C# compile proven on a
real Quest. Proposed/in-progress ML: the voice age gate (validatable on public corpora
today, no headset needed — the first ML deliverable), the age-conditioned policy wiring,
the perceptual-detection plane, and the adaptive analyzer. On-device hardened-profile
verification is pending hardware. I'd rather present a solid built core plus a credibly
scoped ML plan than over-claim a half-finished everything."**

---

# (H) Hardest / adversarial examiner questions

### H1. "Strip away the framing — isn't the actual contribution just an if-statement: `if child then tighter_bounds`?"

**"The *policy* is simple by design — a safety gate should be auditable. The contribution
isn't the branch; it's (1) that one on-device age posterior drives *two disjoint safety
planes* that have never been unified, (2) the *compositional-harm* model behind the
perceptual bound, which is not an if-statement but a cumulative-envelope computation over a
window that per-action bounds provably can't express, and (3) the governance that makes an
attack-grade inference usable as a defence. A simple enforcement surface over a
hard-to-detect harm class and a hard-to-govern signal is a feature, not a shortfall."**

### H2. "You cite Nair's 94% re-identification to justify on-device design — but that same result means your 'ephemeral' motion stream re-identifies the user anyway. Your privacy story is dead on arrival."

**"That's the sharpest version of the paradox and I take it seriously. It's exactly *why*
voice is primary and motion is secondary/optional-by-consent, why nothing leaves the
device, and why I don't store the stream. But you're right that raw motion is re-identifying
by nature — so I don't treat 'on-device' as sufficient on its own: I add DP / deep-motion-
masking (arXiv:2311.05090, ~96% de-anon reduction) *and* measure the residual leakage with
a shadow-probe. The honest claim is not 'zero re-identification risk' — it's 'the risk is
minimised, measured, consented, and never leaves the headset,' and the user can decline the
motion modality entirely."**

### H3. "Every layer you've described is individually bypassable — spoofable age gate, evadable LLM screen, incomplete denylist. So the system is insecure."

**"Individually, yes — and I say so about each. The claim is *defence-in-depth*, not any
single impregnable layer: the deterministic Rust validator (fail-closed, memory-safe), the
learned anomaly detector on held-out attacks, the calibrated abstain→escalate LLM screen,
the authoritative platform flag, and cryptographic message auth each cover a *different*
failure mode of the others. The security question isn't 'is any layer bypassable' — it's
'does a single bypass reach a harmful outcome', and the fail-closed defaults plus the
unrepresentable-unsafe-ops Mode-C path are designed so it doesn't. I also *measure* the
composite bypass rate under adaptive attack rather than assert it."**

### H4. "If the platform's age flag and parental consent are authoritative and legally required anyway, your ML model is redundant. Cut it and the thesis loses nothing."

**"The flag is authoritative for *authorisation* — it is silent about *who is actually
wearing the headset right now*. Shared family devices are the norm, and an adult-flagged
account worn by a child is the exact gap no static flag can close. My model's job is
runtime *anomaly detection* → re-assurance, not authorisation, so it's complementary, not
redundant. Cut it and you lose the only signal that a legitimately-adult-flagged session
has, in the moment, a child at the controls — which is precisely the child-safety case the
regulation cares most about."**

### H5. "Compositional harm sounds elegant but unfalsifiable — how do you *know* a sequence of safe ops is harmful without just declaring it so?"

**"Because the harm classes are grounded in prior *empirical* attack work, not my
intuition: forced locomotion / human-joystick (Casey, IEEE TDSC 2021), inception/perceptual
manipulation (arXiv:2403.05721), false-reality sensor attacks (arXiv:2508.08043), and
photosensitive-seizure thresholds are *quantified standards* (WCAG 2.3 / ITU / Harding). I
operationalise each as a measurable cumulative quantity — net displacement, FOV-coverage
fraction, flash rate over a window — with thresholds drawn from those sources, and I
evaluate precision/recall against a labelled set that includes compositional cases. It's
falsifiable: a labelled sequence either crosses the cumulative threshold or it doesn't, and
I report the confusion matrix."**

### H6. "What's the one result that, if it came out wrong, would sink the thesis — and are you confident it won't?"

**"The load-bearing result is the **subgroup false-adult rate** on the voice age gate — a
child systematically passing as an adult in some accent or atypical-speech subgroup would
mean the protective control fails for exactly the children who most need it. I'm *not*
blithely confident it'll be uniform — I expect subgroup gaps, which is why I audit them
explicitly and set thresholds to fail safe for the child, with abstain→escalate near the
boundary. The thesis survives an imperfect number because the design never lets that single
signal be the last line of defence; it would only sink if I'd claimed the model *authorises*
rather than *restricts*, and I've been careful never to claim that."**

### H7. "Give me your honest one-line assessment of where this sits between a real security system and a research prototype."

**"It is a **research/dissertation prototype**, and the repository says exactly that — it's
not a production security boundary and I don't pretend otherwise. Its value is as a
*demonstrated architecture and a set of measured results*: that an attack-grade biometric
inference can be governed into a dual-plane protective control, that compositional immersive
harm is detectable pre-compile, and that an LLM-writes-code XR loop can be hardened to 0
bypass against a growing adaptive corpus. The contribution is the *design and the
evidence*, honestly bounded — not a claim of deployable, production-grade security."**

---

## Quick-reference cheat sheet (glance before you walk in)

| Theme | The one line to land |
|---|---|
| Novelty | "The *coupling* of one age signal to two safety planes is new; age inference is prior art." |
| Compositional harm | "Individually-bounded ops compose into harm; per-action bounds provably miss it." |
| The inversion | "The surveillance attack (Nair, USENIX'23), governed on-device, becomes a defence." |
| Ethics paradox | "On-device, ephemeral, template-free, consent-gated, fail-closed — and I *measure* the residual leakage." |
| Age accuracy | "Child-vs-adult voice ~97% group; motion ~78% → secondary only, never the sole gate." |
| Calibration | "ECE/Brier + temperature scaling + abstain→escalate near the boundary; fail safe for the child." |
| COPPA/FTC | "FTC (Mar 2024) denied facial-age as consent → inference gates experience, never replaces parental consent." |
| EU AI Act | "Art. 5 bans emotion recognition → I output age only, exclude affect entirely." |
| Platform flag | "Meta's flag is authoritative; my model is a shared-headset anomaly safety net." |
| HMAC vs Ed25519 | "Symmetric admission client→backend; asymmetric signing backend→Unity so a leaked client secret can't forge approved code." |
| Replay | "Monotonic per-session sequence + expiry + domain + SHA-256 hash bound in the signed region." |
| Fail-closed | "Missing control = startup error, never silent downgrade; unsafe ops unrepresentable in Mode C." |
| 15%→100% | "0 bypass against a 1000+-vector growing corpus — defence-in-depth, *not* a completeness proof." |
| Biggest limitation | "No child-labelled VR motion dataset → voice load-bearing, gap reported as a finding." |
| Prototype status | "Research prototype; contribution is the governed architecture + measured results, honestly bounded." |

---

### Key citations to have on the tip of your tongue

- **Nair et al.**, *Unique Identification of 50k+ VR Users*, USENIX Sec 2023 — **arXiv:2302.08927** (94.33% ID / 55,541 users)
- **Nair et al.**, *Inferring Private Personal Attributes of VR Users*, VRW 2024 — **arXiv:2305.19198** (age 78.3% per-user)
- **Casey et al.**, *Human Joystick* — IEEE **TDSC 2021** (forced locomotion)
- **Inception Attacks** — **arXiv:2403.05721**; **False Reality** — arXiv:2508.08043
- **Auto-RT** (adaptive red-team) — **arXiv:2501.01830**; guardrail bypass — arXiv:2504.11168
- **LLM-as-judge overconfidence** — **arXiv:2508.06225**
- **Deep Motion Masking** — arXiv:2311.05090 (~96% de-anon reduction); **MetaGuard** — arXiv:2208.05604
- **audEERING wav2vec2 age/gender** — arXiv:2306.16962; **WavLM demographics** — arXiv:2502.12007; layer-wise — arXiv:2508.10332
- **SSF continual IDS** — arXiv:2412.16264; **Devign** — NeurIPS 2019; **Vul-LMGNNs** — arXiv:2404.14719
- **FTC** denial of facial age estimation as consent — **March 2024**; **COPPA** 16 CFR 312 (2025, compliance Apr 2026)
- **EU AI Act Art. 5** (emotion-recognition ban, in force Feb 2025); **UK AADC / ICO Children's Code**; **GDPR Art. 8**
- **ISO/IEC 27566-1:2025**; **IEEE 2089.1-2024**; **Meta Quest** age-group API (Apr 2024, authoritative)
- **BOXRR-23** — arXiv:2310.00430 (adult-skewed, no child labels); position paper — arXiv:2605.17347 (*contested*)

_Prepared as a viva-defence aid. Grounded in `RESEARCH_AND_ML_PLAN.md`. Every accuracy
figure traces to a cited primary source; figures flagged "unverified" in the plan are
deliberately excluded here._
