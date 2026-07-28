# DreamCodeVR — Error-Feedback Study Guide

Everything you need to run the Wizard-of-Oz study comparing three feedback
conditions for AI/speech-pipeline errors in VR authoring.

- **Condition A** — No feedback (participant sees only the scene result)
- **Condition B** — Text panel **only** (no agent)
- **Condition C** — Embodied agent **only** (agent speaks the explanation; no text panel)

B and C are deliberately exclusive: each carries the explanation exactly once, so
the comparison isolates *modality* rather than *amount* of information. The
transcript strip ("what the system heard") stays visible in both — it is not an
explanation, and holding it constant keeps B and C differing in one thing only.

A hidden researcher triggers pre-scripted *success* or *error* outcomes, so
every participant gets the identical experience — no live LLM.

## Design (agreed with Daniele, July 2026)

**Within-subjects:** every participant does **all three conditions** in three
blocks, and a **different task in each**. No task is ever repeated within a
participant, so a correction learned in one condition cannot carry into the next.

**Counterbalancing.** Six condition orders (A-B-C, A-C-B, B-A-C, B-C-A, C-A-B,
C-B-A) are assigned by participant number. The task rotation advances
independently — every 6 participants rather than every participant — because
deriving both from the same number would lock each condition to the same tasks
forever. Across 18 participants all nine (condition × task) cells are used
equally.

Type the participant ID into the panel and it shows the assigned plan.

**3 tasks × 4 AI-pipeline error categories.** Any task can be made to fail at any
stage of the pipeline the participant believes is running:

| Category | What appears to have gone wrong |
|---|---|
| **Speech recognition** | the words were mis-heard ("wall" for "ball") |
| **Intent interpretation** | words right, wrong action inferred (five balls, not one) |
| **Missing parameter** | action right, a needed detail absent (no position given) |
| **Execution** | understood correctly, performed badly (falls through the floor) |

The Wizard picks the category before starting each trial.

**Trial protocol:**
1. Select the **task** (★ = the one the plan assigns) and the **error category**.
2. Click **▶ Start trial** — this clears the scene and starts the clock.
3. Read the prompt shown on the panel to the participant.
4. They speak → click **INJECT ERROR**.
5. Feedback appears per condition (A none / B panel / C agent).
6. They try again:
   - problem repaired → **INJECT SUCCESS**, then **✓ End trial**
   - same mistake repeated → **INJECT ERROR again** (do not hand them the
     answer), and prompt them to check the feedback
7. Click **⏭ Next condition** — ends the trial, switches the headset to the next
   assigned condition, loads its task and clears the scene in one press.

Every trial starts from an identical scene: generated objects removed,
environment colours restored, agent returned to idle.

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
  installs it, and launches it on the headset. The headset finds the Mac
  automatically (LAN auto-discovery) — no IP setup.
- You only need to **rebuild** when the code changes or you switch to a
  different Wi-Fi network. Between participants, just relaunch the app on the
  headset — no rebuild.

**Testing in the Editor (no headset):** you can also press **Play** in the Editor
to rehearse the whole flow on the desktop. Everything works there too (the
outcome runner is plain C#), so you can pilot the researcher workflow without
the Quest.

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

### 4. Run the block's task
Each block is **one task** in **one condition**.

1. Check the **Task** card — ★ marks the task the plan assigns. Pick the **error
   category** you are injecting.
2. Click **▶ Start trial**. The scene clears and the clock starts.
3. Read the prompt shown on the panel to the participant.
4. They speak their instruction (hold the controller trigger — either hand — or
   **Space** in the Unity Editor). The transcript appears on the panel.
5. Click **INJECT ERROR**. After a short "thinking" pause the outcome appears
   and the condition's feedback fires.
6. They try again:
   - problem repaired → **INJECT SUCCESS**
   - same mistake repeated → **INJECT ERROR again**, and say *"have another
     look at the feedback and try once more"*
7. Click **✓ End trial**, then **⏭ Next condition** to move on.
8. Use **Quick note** for observations at any point.

> **There is no live AI.** You decide every outcome by clicking. If you click
> nothing, nothing happens — that is the method, and it is what keeps the
> experience identical for everyone in a condition.

**What the participant sees (task 1, missing-parameter error):**
- **A – No feedback:** the ball appears in the wrong place. Nothing else. They
  must work it out alone. (Control condition.)
- **B – Text panel:** the ball appears and the panel explains: *"A ball was
  created, but at the centre of the room rather than near you — no position was
  specified."* No agent.
- **C – Embodied agent:** the ball appears and a visible assistant acknowledges
  beforehand (*"Okay, I'll create a ball for you."*) then explains afterwards
  (*"…I wasn't told where to put it. Where would you like it?"*). No text panel.

### 4a. Editing the tasks (no rebuild needed)
Task prompts, error wording and agent dialogue live in the `TASKS` object in
[`Server/samples/apps/wizard_of_oz/app.js`](Server/samples/apps/wizard_of_oz/app.js)
— three tasks, each with all four error categories and a success outcome.
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
  `participantId, conditionOrder, block, condition, trial, task, errorCategory,
  startTime, endTime, durationMs, completionStatus, attempts, injects`.
  This is everything the design calls for in one place — no joining needed for
  per-condition comparisons.
- `sessions.csv` — one row per block start, with the participant's condition
  order and task plan
- `<PID>_events.csv` — the fine-grained trace. One row per event with
  `timestamp, participantId, condition, block, trial, task, errorCategory,
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
