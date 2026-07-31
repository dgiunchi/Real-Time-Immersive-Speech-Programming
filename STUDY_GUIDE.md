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

## Design (agreed with Daniele, July 2026)

### Attribution (the governing principle)

Every failure has a **ground truth about whose fault it is**, and the feedback
wording must agree with it.

- On tasks 1 and 2 the cause really is something the participant left out or
  phrased ambiguously. Feedback names the missing detail.
- On tasks 3 and 4 the participant did nothing wrong. Feedback owns the limit
  or explains where the object went. It must **never** suggest they rephrase,
  because there is nothing to rephrase.

Getting this wrong invalidates the results. If feedback says "please speak more
clearly" when the ground truth is a system fault, a participant who believes it
is scored as mis-attributing, and conditions B and C are penalised for carrying
a message the study itself wrote.

**The participant is never told what to say.** They get a briefing describing
the scene and the goal, and use their own words. The scripted failure is
injected regardless of what they actually say.

### Structure

**Between-subjects, 30 participants, 10 per condition.** Each participant
experiences ONE feedback condition and completes all four tasks.

Each task is a different failure scenario, and only half are the participant's
doing:

| Task | Scenario | Whose fault | What happens |
|---|---|---|---|
| 1 | Create object in hand | **User** | A required detail (hand height) was never given |
| 2 | Move object to target | **User** | Phrasing allowed more than one reading |
| 3 | Create 1000+ objects | **System** | Valid request, beyond what the system can render |
| 4 | Create object above fire | **System** | Executed correctly, but lands out of view |

**That split is the study.** Tasks 3 and 4 are the important half: a memory
ceiling is nobody's fault and *cannot* be fixed by rephrasing. A participant who
blames themselves there will burn attempts rewording a request that can never
succeed. So the feedback on tasks 3 and 4 must never imply user fault, or it
manufactures the exact mis-attribution being measured.

**A correct outcome is silent** in every condition. Only failures are ever
explained, so the feedback channel never doubles as a success signal.

### Counterbalancing

- **Condition** = `CONDITIONS[(p-1) mod 3]`, interleaved A,B,C,A,B,C... so that
  drift over the recruitment period spreads across all three groups rather than
  loading onto whichever ran first. Gives exactly 10 per condition over P01-P30.
- **Task order** = balanced (Williams) Latin square, row `(p-1) mod 4`. Not a
  plain rotation: it ensures each task appears in each position equally often
  *and* each task precedes every other equally often. That matters because
  tasks 3 and 4 teach participants the system has limits, which would otherwise
  colour how they read tasks 1 and 2.
- **Variant** = `((p-1) + position) mod 3`, for content variety.

### Measures and analysis plan

Fix this before collecting data. Deciding what counts as a result after seeing
the numbers is the single easiest way to lose a paper.

**Primary — attribution accuracy.** Per trial, did the participant's stated
cause match the ground truth (`attributionCorrect`)? Analysed as a mixed-effects
logistic regression: `correct ~ condition * scenarioType + (1|participant)`.
The interaction is the interesting term: feedback may help on user-fault tasks
while doing nothing, or actively harming, on system-fault ones.

**Secondary — repair quality** (`repairContainsSlot`), **attempts to recovery**
(`attempts`), **time to first repair**, **completion** (`completionStatus`).

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

**Still open, disclose in the limitations:** the wizard is not blind to
condition (unavoidable in WoZ, since they operate the feedback); coding of vague
attribution answers is a judgement call, so record audio and have a second coder
rate ~20% for inter-rater reliability; scripted failures are not naturalistic
errors.

### Trial protocol

One trial per task, four tasks per participant.

1. Confirm the **task** and **variant** on the panel (★ = assigned by the plan).
2. Click **▶ Start trial** — clears the scene and starts the clock.
3. Read the **briefing**. Do not tell them what to say.
4. They speak → click **INJECT ERROR**.
5. Feedback appears per condition (A none / B panel / C agent).
6. Ask **"Why do you think that happened?"** and click their answer in Step 3.
   Ask it the same way every time and do not react to whether they are right.
7. They try again:
   - adapted → **INJECT SUCCESS** (silent), then **✓ End trial**
   - same mistake → **INJECT ERROR again**; do not hand them the answer
8. Click **⏭ Next task** and repeat. Four tasks, then the questionnaire.

Every trial starts from an identical scene: trial objects removed, environment
colours restored, sphere and cube rebuilt, agent returned to idle.

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

## Where the data goes

All files are written to a `Logs/` folder at the top of the project:
```
Real-Time-Immersive-Speech-Programming-…/Logs/
```
- **`trials.csv` — the primary analysis file.** One row per trial:
  `participantId, conditionOrder, block, condition, trial, task, variant,
  errorType, startTime, endTime, durationMs, completionStatus, attempts, injects`.
  This is everything the design calls for in one place — no joining needed for
  per-condition comparisons.
- `sessions.csv` — one row per block start, with the participant's condition and
  full assigned plan (task, variant and error pair)
- `<PID>_events.csv` — the fine-grained trace. One row per event with
  `timestamp, participantId, condition, block, trial, task, variant, errorType,
  attempt, eventType, msSinceTrialStart, detail`. Event types include
  `transcript` (what they said), `inject` (what you triggered),
  `feedback-shown` (what they were actually told), `trial-start`, `trial-end`,
  `block-advance`, `note`.
- `<PID>_background.csv` — pre-session demographics (one row)
- `<PID>_condition.csv` — post-condition questionnaires (three rows, one per
  condition)

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
