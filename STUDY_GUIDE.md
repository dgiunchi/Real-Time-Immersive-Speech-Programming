# DreamCodeVR — Error-Feedback Study Guide

Everything you need to run the Wizard-of-Oz study comparing three feedback
conditions for AI/speech-pipeline errors in VR authoring.

- **Condition A** — No feedback (participant sees only the scene result)
- **Condition B** — Text panel **only** (no agent)
- **Condition C** — Embodied agent **only** (agent speaks it; no text panel)

B and C are deliberately exclusive: each carries the explanation exactly once, so
the comparison isolates *modality* rather than *amount* of information. The
transcript strip ("what the system heard") stays visible in both — it is not an
explanation, and holding it constant keeps B and C differing in one thing only.

A hidden researcher triggers pre-scripted *success* or *error* outcomes, so
every participant gets the identical experience — no live LLM.

Design summary for supervisors: [STUDY_DESIGN.md](STUDY_DESIGN.md).
For the demo films — shot list, scripted lines, capture setup — see
[DEMO_FILMING.md](DEMO_FILMING.md). Film with a stand-in before data collection
begins, never with a real participant.

## Design (agreed with Daniele, July 2026)

### Attribution (the governing principle)

Every failure has a **ground truth about whose fault it is**, and the feedback
wording must agree with it.

- On tasks 1, 2 and 5 the cause really is something the participant left out or
  phrased ambiguously. Feedback names the missing detail.
- On tasks 3, 4 and 6 the participant did nothing wrong. Feedback owns the limit,
  explains where the object went, or says the capability does not exist. It must
  **never** suggest they rephrase, because there is nothing to rephrase.

Getting this wrong invalidates the results. If feedback says "please speak more
clearly" when the ground truth is a system fault, a participant who believes it
is scored as mis-attributing, and conditions B and C are penalised for carrying
a message the study itself wrote.

**The participant is never told what to say.** They get a briefing describing
the scene and the goal, and use their own words. The scripted failure is
injected regardless of what they actually say.

### Structure

**Between-subjects, 30 participants, 10 per condition.** Each participant
experiences ONE feedback condition and completes all six measured tasks.

Each task is a different failure scenario, and only half are the participant's
doing:

| Task | Scenario | Whose fault | What happens |
|---|---|---|---|
| 1 | Create object in hand | **User** | A required detail (hand height) was never given |
| 2 | Move object to target | **User** | Phrasing allowed more than one reading |
| 5 | Create object that stands out | **User** | Intention stated, but no colour given |
| 3 | Create 1000+ objects | **System** | Valid request, beyond what the system can render |
| 4 | Create object above fire | **System** | Executed correctly, but lands out of view |
| 6 | Make an object move itself | **System** | Animation is not a capability that exists |

Three of each fault type. Each user-fault task turns on a different omission (a
trigger condition, a spatial reference, a parameter), and each system-fault task
on a different kind of impossibility (a ceiling, a surprise, a missing
capability).

**That split is the study.** Tasks 3, 4 and 6 are the important half: a memory
ceiling is nobody's fault and *cannot* be fixed by rephrasing. A participant who
blames themselves there will burn attempts rewording a request that can never
succeed. So the feedback on tasks 3, 4 and 6 must never imply user fault, or it
manufactures the exact mis-attribution being measured.

**A correct outcome is silent** in every condition. Only failures are ever
explained, so the feedback channel never doubles as a success signal.

### Counterbalancing

- **Condition** = `CONDITIONS[(p-1) mod 3]`, interleaved A,B,C,A,B,C... so that
  drift over the recruitment period spreads across all three groups rather than
  loading onto whichever ran first. Gives exactly 10 per condition over P01-P30.
- **Task order** = balanced (Williams) Latin square, row
  `floor((p-1)/3) mod 6`. Not a plain rotation: it ensures each task appears in
  each position equally often *and* each task precedes every other equally
  often. That matters because the system-fault tasks teach participants the
  system has limits, which would otherwise colour how they read the user-fault
  ones.
  The index divides by 3 first. Using `(p-1)` for both condition and order would
  tie them together, so condition A would only ever see two of the six orders.
  With n=30 over 3 conditions x 6 orders, coverage is near-balanced rather than
  exact; n=36 would be exact.
- **Variant** = `((p-1) + position) mod 3`, for content variety.

### Measures and analysis plan

Fix this before collecting data. Deciding what counts as a result after seeing
the numbers is the single easiest way to lose a paper.

**Primary — attribution accuracy.** Per trial, did the participant's stated
cause match the ground truth (`attributionCorrect`)? Analysed as a mixed-effects
logistic regression: `correct ~ condition * scenarioType + (1|participant)`.

The **main effect of `scenarioType` is the headline**: accuracy should fall on
system-caused failures, where people reach for a self-blaming explanation the
evidence does not support. That term is within-participant, three observations
per cell, and is the claim the design can actually support.

The `condition x scenarioType` interaction is the interesting-but-underpowered
term: feedback may help on user-fault tasks while doing nothing, or actively
harming, on system-fault ones. Pre-register it as **exploratory**.

Six tasks rather than four exists for this model. At four, the random intercept
is estimated from two binary observations per cell, which is too thin to
identify and prone to convergence trouble.

**Independent check on the repair coding.** `wastedRepairs` is co-primary and is
coded live by the wizard while they are also running the session: one rater, in
the moment, no second opinion. Every utterance pair in a trial is therefore also
scored automatically for lexical overlap (`maxUtteranceSimilarity`,
`utteranceSimilarities`, with word counts in the event log).

Report the agreement between the two. Overlap alone cannot classify - "create a
ball in my hand" sits inside "create a ball in my hand when I raise it above my
shoulder", which is a good repair and not a repetition - so read it with the
word counts: high overlap at similar length is a repeat, high overlap with
growth is added detail. The raw scores are logged rather than a threshold, so
where that cut sits stays an analysis decision.

**`msToFirstRepair`** is the finer-grained noticing measure. The manipulation
check is a yes/no, and someone who answers "yes I saw it" after twenty seconds
of silence did not notice it the way someone who reacted in two did.

**Co-primary — what the misattribution costs** (`firstRepairStrategy`,
`repairSequence`, `wastedRepairs`). Attribution on its own is a stated belief,
and a reviewer can fairly ask who cares. The repair move is the belief with a
price on it, and it is the only part of this a shipping product could act on.

Coded live, once per attempt: `detail` (added the missing information),
`verbatim` (said it again, louder or slower), `scope` (asked for less),
`question` (asked the system what went wrong), `gaveup`.

`verbatim` is the measure that carries the practical claim. It cannot fix
anything — not a user fault, because the missing information is still missing,
and not a system limit, because the limit does not care how clearly you speak.
It is what misattribution looks like from the outside, and unlike attribution it
is observable without a wizard: any product can count repeated near-identical
utterances. The prediction is that `wastedRepairs` is highest in condition A on
system-fault tasks, where nothing tells the participant that rephrasing is
futile.

Model: `wastedRepairs ~ condition * scenarioType + (1|participant)`, Poisson.

**Secondary — repair quality** (`repairContainsSlot`), **attempts to recovery**
(`attempts`), **time to first repair**, **completion** (`completionStatus`).

`repairContainsSlot` is the training-signal measure and deserves more weight
than its name suggests. A correction produced under a wrong diagnosis does not
address the real cause, so it is worthless as feedback to learn from — worse
than worthless, since it teaches the wrong lesson. This column is where that
claim becomes a number.

**Manipulation check** (`noticedFeedback`). Report it. A null result means
nothing if participants never registered the feedback.

**Power.** n=10 per cell detects only large between-group effects (d≈1.3 at
80%). Treat the modality main effect as **exploratory** and say so in the paper.
The within-participant contrast — user-fault vs system-fault scenarios, where
every participant is their own control — is far better powered and is where the
real claim lives.

**Pre-register** the above before participant 1. It costs an hour and converts
every later choice from a judgement call into a plan.

### Threats handled, and the ones remaining

| Threat | Handling |
|---|---|
| Experimenter bias in the probe | Ground truth hidden from the wizard until the answer is recorded; prompt is scripted verbatim |
| Learning the interface on task 1 | Practice trial first, always succeeds, not analysed |
| Order effects | Balanced Williams square |
| Drift over recruitment | Conditions interleaved A,B,C rather than blocked |
| "Feedback didn't help" vs "never saw it" | Manipulation check per trial |
| Success acting as a second feedback channel | Correct outcomes are silent in all conditions |
| Feedback contradicting ground truth | Tasks 3-4 never imply user fault |
| Repair measure inflated by loose matching | Word-boundary synonym sets; agreement tokens excluded |
| Random intercept unidentifiable | Six measured trials, three per fault type |
| Order confounded with condition | Order index advances per A/B/C cycle, not per participant |
| Scripted error contradicting what they said | Flagged per trial as `preInjectHadSlot`, excludable |

**Still open, disclose in the limitations:** asking "why do you think that
happened?" after every error may itself train participants to look for causes,
and six trials makes that more acute than four; no ordering fixes it, since it
is probe repetition rather than task order. Trial number is in the data, so a
practice effect across positions is at least visible.

Also: the wizard is not blind to
condition (unavoidable in WoZ, since they operate the feedback); coding of vague
attribution answers is a judgement call, so record audio and have a second coder
rate ~20% for inter-rater reliability; scripted failures are not naturalistic
errors.

### Trial protocol

One trial per task, six measured tasks per participant.

1. Confirm the **task** and **variant** on the panel (★ = assigned by the plan).
2. Click **▶ Start trial** — clears the scene and starts the clock.
3. Read the **briefing**. Do not tell them what to say.
4. They speak → click **INJECT ERROR**.
5. Feedback appears per condition (A none / B panel / C agent).
6. Ask **"Why do you think that happened?"** and click their answer in Step 3.
   Ask it the same way every time and do not react to whether they are right.
7. They try again:
   - adapted → **INJECT SUCCESS** (silent), then **✓ End trial & load next**
   - same mistake → **INJECT ERROR again**; do not hand them the answer
8. Repeat. Ending a trial loads the next task by itself — there is no separate
   "next task" step. After the sixth, the panel asks for the post-condition
   questionnaire, and submitting it ends the session.

Every trial starts from an identical scene: trial objects removed, environment
colours restored, sphere, cube and campfire rebuilt, agent returned to idle.
Objects that belong to the scene rather than to a trial are never destroyed.

---

## Ethics, consent and debriefing

This is a deception study. Participants are told they are speaking to a working
AI system; every outcome is in fact triggered by a person in the room. That is
authorised deception, which is routine for Wizard-of-Oz work but is only
defensible with a consent process and a full debrief attached to it. Neither is
optional, and a reviewer or an ethics panel will ask for both.

### Before the session: consent

Consent is written, and covers three things explicitly:

1. **VR use**, with the right to stop at any point for any reason, including
   simulator sickness, with no consequence.
2. **Audio recording** of the whole session, what it will be used for, how long
   it is kept, and that it is stored non-identifiably.
3. **Data retention**, including the right to withdraw afterwards.

The consent form says the study is about "how people interact with a
speech-driven system and respond when it does not do what they expected". That
is true and it is incomplete: naming the wizard would destroy the measure, since
a participant who knows a person is choosing the failures has no reason to
diagnose anything. Incompleteness is what the debrief exists to repair.

### Record audio for the whole session

Not optional, and easy to skip because the system already logs transcripts.

The transcript log only captures what the participant says **to the system**
through push-to-talk. The attribution answer is spoken **to the researcher**, off
mic, and is therefore nowhere in the data. Without audio, the second coder has
nothing to code and the inter-rater reliability plan is a sentence rather than a
number.

### After the questionnaire: debrief

Read it out, do not hand it over and leave. Cover, in this order:

1. **The reveal.** No AI decided anything. A researcher triggered every outcome,
   including all the failures, from a script prepared in advance.
2. **Why.** Every participant had to meet exactly the same failures for the
   comparison to mean anything, and a real system cannot be made to fail on cue.
3. **The reassurance, and say it plainly.** The failures were scripted and would
   have happened whatever they said. Nothing that went wrong reflects on them.
   People routinely leave a WoZ session believing they were bad at it, and on a
   study whose whole subject is self-blame, sending someone away with that is not
   an acceptable outcome.
4. **Withdrawal.** They may have their data destroyed now that they know what it
   was. Give a contact route and honour it without asking why.
5. **Ask what they thought was happening.** Record the answer. Anyone who
   suspected a wizard is excluded by the criterion below, and you can only apply
   that criterion if you asked.

### Exclusion criteria, fixed in advance

Written down before participant 1, so that no exclusion decision is ever made
with the results visible.

**Exclude the participant** if any of:

- fewer than 6 measured trials completed
- a technical failure (STT down, headset disconnect, server drop) affecting more
  than one trial
- at debrief, they report having suspected the outcomes were human-controlled
- they withdraw, for any reason including simulator sickness

**Exclude the trial only** (keep the participant) if:

- `preInjectHadSlot` is true, so the scripted error contradicted what they
  actually said and the feedback was false for them
- a technical failure affected that single trial

Report every exclusion with its reason. A study that reports none is less
believable than one that reports several.

---

## The one command you run

```bash
cd ~/Desktop/Real-Time-Immersive-Speech-Programming-Visualisation-DreamCodeVR-feedback-loop/Server && npm run study
```

That single command:
1. installs dependencies the first time,
2. copies the local TLS certs into the study app,
3. starts the study server,
4. opens the **researcher control panel** in your browser automatically.

Leave that terminal running for the whole session. Press `Ctrl+C` to stop.

---

## Running a session

### 1. Start the server
Run the command above. Your browser opens `http://localhost:8181` — the
researcher control panel.

### 2. Open Unity and deploy to the Quest
- In Unity Hub, open the `Unity/` folder of this project.
- Open the scene **`Assets/Demos/DynamicCompiler/DynamicCompiler`** (the campfire
  scene — NOT `Scenes/SampleScene`, which is an empty placeholder).
- If a "TMP Importer" window pops up, click **Import TMP Essentials**.
- **One-time scene setup:** create an empty GameObject (`GameObject → Create Empty`),
  name it `StudyManager`, and add the **`StudyUIBootstrapper`** component to it.
  Pick the condition (A/B/C) in its Inspector. That's the only wiring you need —
  it builds every panel, the embodied agent, discovery, and the outcome runner,
  and connects all the scripts automatically at runtime. (Optionally also add
  `StudySessionLogger` for speech-attempt and task-timing logs.) Save the scene.

**Running on the Meta Quest (this is a Mac — Quest Link is not available):**
- The study runs as a **standalone Android build on the headset**, which talks to
  the Mac's server over Wi-Fi.
- One-time: put the Quest in **Developer Mode** (Meta Quest phone app → your
  headset → Developer Mode), connect it by USB-C, and accept the "Allow USB
  debugging" prompt in the headset.
- One-time Unity settings: **Build target = Android** (File → Build Profiles →
  Switch Platform); **Player → Other Settings → Scripting Backend = IL2CPP**,
  **Target Architectures = ARM64**; **XR Plug-in Management → Android → Oculus**.
- Each build: **File → Build Profiles → Build And Run**. Unity builds the APK,
  installs it, and launches it on the headset.
- You only need to **rebuild when the C# changes** — never for a new location,
  network or participant. Between participants just relaunch the app.

**Networking — plug the cable in before `npm run study`, then unplug.**

That one habit is all you need anywhere in the world. On startup the launcher
finds the Mac's current IP and writes it straight into the app's storage over
USB, so the headset knows where to connect before it even starts looking. Once
connected the session runs over Wi-Fi and **the cable can come out**.

It also opens a USB tunnel as a backup, which works even on networks that block
everything else — but that one needs the cable to stay in.

<details>
<summary>Why the cable, when there's auto-discovery?</summary>

Discovery works by UDP broadcast, and broadcasts do not cross subnets. Labs
routinely put the headset on a local AP and the Mac on the institutional
network — different subnets, so the beacon never arrives. The app then falls
back to `localhost`, which on the headset is *itself*: **"connection lost."**

Ordinary TCP between the two is fine (verified across exactly that split); only
the *discovery* step was broken. The cable supplies the address once, and Wi-Fi
does the rest.
</details>

**Testing in the Editor (no headset):** you can also press **Play** in the Editor
to rehearse the whole flow on the desktop. Everything works there too (the
outcome runner is plain C#), so you can pilot the researcher workflow without
the Quest.

### Black screen, frozen app, or "connection lost"

Almost always one of three things, in this order:

**1. The headset is off your head.** The Quest sleeps the moment you take it off
and Unity suspends with it — black screen, no response, no logs. Put it on. To
check: `adb shell dumpsys power | grep mWakefulness` (`Asleep` = this is it).

**2. The server isn't running.** The app connects but nothing responds to your
clicks. `npm run study` must stay running the whole session.

**3. No guardian boundary set.** The app gains focus then immediately loses it,
so you see a partial view or the grey boundary screen instead of the scene.
Set the boundary in the headset.

**Desk testing without wearing it** — these two make the headset behave as if
worn, which is handy while you rehearse:

```bash
adb shell am broadcast -a com.oculus.vrpowermanager.prox_close  # stay awake off-head
adb shell setprop debug.oculus.guardian_pause 1                 # run with no boundary
```

⚠ **Turn the guardian back on before any participant wears it** — it is the
boundary that stops them walking into things:

```bash
adb shell setprop debug.oculus.guardian_pause 0
adb shell am broadcast -a com.oculus.vrpowermanager.automation_disable
```

### 3. Set up the participant on the control panel
- Enter the **Participant ID** (e.g. `P01`). The panel immediately shows that
  participant's **counterbalanced plan** — which condition and which task to run
  in each of the three blocks.
- Leave **Condition** on *"Follow counterbalanced plan"* and pick the **Block**
  you're about to run, then click **Start / Update Session**.
- The headset switches to that condition automatically — no rebuild, and no need
  to match anything by hand in Unity.

Before the first block, have the participant complete the **Background form**
(link in the panel header) — once per participant.

### 4. Run each task
Each participant does all three tasks, with two error types per task.

1. Check the **Task & variant** card — ★ marks what the master table assigns.
   Pick the **error type** (★ marks the two assigned to this participant + task).
2. Click **▶ Start trial**. The scene clears and the clock starts.
3. Read the **briefing** shown on the panel. **Do not tell them what to say** —
   they must phrase the instruction themselves.
4. They speak their instruction (hold the controller trigger — either hand — or
   **Space** in the Unity Editor). The transcript appears on the panel.
5. Click **INJECT ERROR**. After a short "thinking" pause the outcome appears
   and the condition's feedback fires. The panel shows you the exact panel text
   and agent line, so you can read the agent line aloud if a voice clip is missing.
6. They try again:
   - problem repaired → **INJECT SUCCESS** (deliberately silent — no feedback)
   - same mistake repeated → **INJECT ERROR again**, and say *"have another
     look at the feedback and try once more"*
7. Click **✓ End trial**. Repeat from step 1 with the task's **second** assigned
   error type, then **⏭ Next condition** for the next task.
8. Use **Quick note** for observations at any point.

> **There is no live AI.** You decide every outcome by clicking. If you click
> nothing, nothing happens — that is the method, and it is what keeps the
> experience identical for everyone in a condition.

**What the participant sees (task 1, "Happened Differently"):**
- **A – No feedback:** the ball appears on the floor instead of their hand.
  Nothing else. They must work it out alone. (Control condition.)
- **B – Text panel:** the ball appears on the floor and the panel reads *"The
  ball appeared, but not in your hand. Please specify the location."* No agent.
- **C – Embodied agent:** the ball appears on the floor and the assistant says
  *"The ball was created, but I wasn't sure exactly where you wanted it."* No panel.

In every case the cause traces back to the participant's own phrasing — they
never said where the ball should appear.

### 4a. Editing the tasks (no rebuild needed)
Briefings, panel wording and agent dialogue live in
[`Server/samples/apps/wizard_of_oz/app.js`](Server/samples/apps/wizard_of_oz/app.js).
Variants are generated from three builder functions (`buildTask1/2/3`) rather
than written out 36 times, so editing one line of wording updates that error
across all three variants and keeps them parallel — which is what makes variants
interchangeable in the first place. To change only one variant, edit its entry in
the `TASKS` table below the builders.

The headset executes these as data, so **edits take effect on the next server
restart — you do not need to rebuild or reinstall the APK.**

### 4b. Optional — agent voice for condition C
The agent speaks via on-screen subtitles by default. To give it a real voice,
drop `.wav` clips into `Unity/Assets/Resources/AgentVoice/` — they are picked up
automatically. Without clips, subtitles are used.

### 5. Questionnaires
- **Once per participant, before the first block:** `http://localhost:8181/background`
  — age range, VR experience, technical background, assistant use.
- **After each block (3× per participant):** `http://localhost:8181/questionnaire`
  — **SUS** (10 items), **UES-SF** (12 items, randomised order; reverse-score the
  three perceived-usability items), and **Perceived Support** (4 items + overall).

Pre-fill either with `?pid=P01`. Both refuse to submit until every item is
answered, highlighting anything missed.

Then run the **semi-structured interview** (5–10 min) from the study materials —
that part is on paper/audio, not in this system.

---

## The event log

The `recordType=event` rows of `Logs/<participant>.csv`. One row per state
change, written from the moment the session is created and before the
participant has done anything. Two worked examples are committed:

- [docs/example_events.csv](docs/example_events.csv) - condition C, task 4. Head
  pose shows the participant turning round after the agent tells them where the
  object went.
- [docs/example_events_conditionA.csv](docs/example_events_conditionA.csv) -
  condition A, task 3. The misdiagnosis cascade the study exists to measure,
  visible as data: three near-identical retries, then giving up, then blaming
  themselves for a ceiling that was never theirs.

| Column | Notes |
|---|---|
| `seq` | Monotonic counter. Two events share a millisecond often enough (an inject and the feedback it causes) that file order is otherwise the only evidence of sequence, and that does not survive a sort |
| `timestampIso`, `epochMs` | Absolute, for lining the log up against video and audio |
| `msSinceSessionStart` | Position in the session, for fatigue and drift |
| `msSinceTrialStart` | Position in the trial, the analysis unit |
| `participantId` … `attempt` | Full context repeated on every row, so any row reads on its own |
| `source` | `wizard` / `participant` / `system`. Without it, the researcher pausing and the participant pausing are the same silence |
| `category` | `session` `ui` `scene` `trial` `speech` `outcome` `feedback` `measure` `pose` `warning` |
| `eventType`, `detail`, `value`, `target` | What happened |
| `posX/Y/Z`, `yaw` | Head pose, 10Hz, movement-gated |

Every row carries its full context even though that is redundant, because a
reconstruction mistake found after the last participant has gone home cannot be
fixed, and disk is free.

**Head pose exists for task 4.** The object spawns behind the participant, so
without pose "never noticed it" and "turned, saw it, said nothing" are the same
absence of rows. With yaw the turn is an event with a latency:

```
16   6894  system       feedback  feedback-shown     <- "it is behind you"
18  10230  participant  pose      head-pose    41.6
19  10730  participant  pose      head-pose    98.2
20  11230  participant  pose      head-pose   163.7
21  11730  participant  pose      head-pose   179.2  <- turned
22  13610  participant  speech    transcript         <- "Oh, I see it"
```

Sampling is movement-gated, so a participant standing still produces no rows.
That keeps the file readable by eye and means a row is always a change.

Pose needs `StudyTelemetry.cs`, which is in the pending rebuild.

## Where the data goes

All files are written to a `Logs/` folder at the top of the project:
```
Real-Time-Immersive-Speech-Programming-…/Logs/
```
**One file per participant.** `Logs/P01.csv` is everything P01 did, in the order
they did it. Nothing about a participant lives anywhere else.

The `recordType` column says what each row is:

- `session-start` — the assigned plan: condition, task order, variant per task.
- `event` — the fine-grained trace, one row per state change. `eventType`
  distinguishes them: `transcript` (what they said), `inject` (what you
  triggered), `feedback-shown` (what they were actually told), `trial-start`,
  `trial-end`, `attribution`, `repair-strategy`, `head-pose`, `note`, and the
  `stt-silent` / `stt-error` warnings.
- `trial-summary` — **the primary analysis rows.** One per completed trial,
  carrying `taskOrder, condition, task, variant, scenario, durationMs,
  completionStatus, attempts, injects, attribution, correctAttribution,
  attributionCorrect, noticedFeedback, firstRepairStrategy, repairSequence,
  wastedRepairs, msToFirstRepair, maxUtteranceSimilarity`. Filter to these and
  you have the analysis table — no joining.
- `questionnaire` — one row per submitted form. Items are held as JSON in the
  `answers` column, so background and post-condition forms share the schema
  regardless of which items each asks.

Every row carries its own full context (participant, condition, block, trial,
task, variant, attempt, three clocks), so no analysis step depends on carrying
state forward from earlier rows. The schema is wide and most columns are blank
in most rows; that is the price of one file per participant, and it is worth it.

Superseded files — anything written before a column change, and the older
multi-file layout — are moved to `Logs/archive/` rather than left beside the
live ones.

These CSVs are git-ignored so participant data never gets committed.

**What you can measure from this:** attempts to recovery per condition; trial
duration; completion status; first-vs-recovery transcript comparison — grouped
by condition and by error category, which is what the hypotheses need.

> **`attempts` counts participant utterances**, not your button presses (those
> are `injects`). It is a measure of how much the participant had to try, which
> is the recovery measure the study is about.

If the column layout ever changes (a new field, an edited task), the old CSV is
set aside as `<name>_pre-<timestamp>.csv` and a fresh one started, rather than
new rows being appended under a header that no longer matches. Nothing is
overwritten and no file ever mixes two layouts.

---

## Voice / microphone

**Push-to-talk:** hold the trigger on **either** controller (grip also works)
while speaking, then release. The transcript appears 1–2 s after you release.

**Check the mic before every session.** The control panel has a live
**Microphone** card showing whether the headset's mic is capturing and a level
meter that moves when it hears sound. If it says *"No report from headset"*, the
app isn't running or isn't connected. If it says *"NOT capturing"*, check
microphone permission: headset → *Settings → Apps → [the app] → Permissions →
Microphone*.

**If the trigger fails mid-session**, use **Hold to record** on the panel — it
drives recording remotely so a session isn't lost.

Speech-to-text calls the lab's Whisper server (`http://130.136.2.161:50101`).
`npm run study` pings it at startup and prints a green/yellow banner so you know
before a session whether live transcripts will work. To use a different
endpoint, set `STT_HTTP_URL` before launching.

---

## Piloting order (from the supervisor)

1. Pilot **one task** with the embodied-agent interaction (1–2 questions max,
   no infinite loops).
2. Expand to 2–3 pilot participants; check the interaction feels right.
3. Then run all 3 tasks with the same narrative.
4. Full run: ~10 participants, **within-subjects** (each does all of A, B and C
   in counterbalanced order), 3 tasks per condition, ~45 min per session.

## Keyboard shortcuts
- **Space** (Unity Editor) — hold to record voice
- **F12** — toggle the in-Unity Wizard-of-Oz panel (alternative to the web panel)
