# DreamCodeVR — Error-Feedback Study Guide

Everything you need to run the Wizard-of-Oz study comparing three feedback
conditions for AI/speech-pipeline errors in VR authoring.

- **Condition A** — No feedback (participant sees only the scene result)
- **Condition B** — Text panel (transcript + action + plain-language error)
- **Condition C** — Embodied agent (agent speaks before/after; panel also visible)

A hidden researcher triggers pre-scripted *success* or *error* outcomes, so
every participant in a condition gets the identical experience — no live LLM.

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
- Enter the **Participant ID** (e.g. `P01`) and choose the **Condition**.
- Click **Start / Update Session**. From now on everything is logged to CSV
  under that participant.

> Make sure the condition on the control panel matches the condition set on the
> `StudyUIBootstrapper` in Unity.

### 4. Run each task
For each of the 4 tasks:
1. Click the task chip (1–4) on the control panel.
2. Read the task aloud to the participant.
3. The participant gives a spoken instruction (left trigger in the headset, or
   hold **Space** in the Unity Editor, while speaking).
4. Their transcript appears live on the control panel (and in VR for B/C).
5. **You click SUCCESS or one of the ERROR buttons** — this is the Wizard-of-Oz
   step. After a short "thinking" pause, the pre-scripted outcome runs in VR.
6. Use the **Quick note** box to log any observation.
7. **Clear scene / Reset** before the next participant (and to go from free-play
   into the real tasks) — it removes everything created and clears the feedback.

> **There is no live AI.** You (the researcher) decide every outcome by clicking
> the buttons. If you click nothing, nothing happens — that is by design, and is
> what keeps every participant's experience identical within a condition.

**What the participant sees per condition (e.g. you inject ERR 2 on task 1):**
- **A – No feedback:** a cube appears. Nothing else — no transcript, no text,
  no agent. They must interpret and recover alone. (This is the control.)
- **B – Text panel:** cube appears **and** a panel shows the action, a ✗, and a
  plain-language explanation ("a cube was created instead of a sphere…"), plus
  their transcript.
- **C – Embodied agent:** everything in B **plus** a visible assistant that
  acknowledges the request beforehand and comments on the result afterwards
  ("Hmm — that came out as a cube. Did you mean a sphere?").

### 4a. Optional — agent voice for condition C
The agent speaks via on-screen subtitles by default. To give it a real voice,
drop `.wav` clips into `Unity/Assets/Resources/AgentVoice/` named by line
(`t1_preSuccess.wav`, `t1_postError2.wav`, … one per task/stage/response) — they
load automatically, no wiring. Without clips, subtitles are used.

### 5. Questionnaire
After the session, open `http://localhost:8181/questionnaire` (link is in the
control panel header) on a laptop/tablet for the participant. It contains:
- **SUS** (10 standard items)
- **Presence & experience** (4 items)
- **System feedback & support** (engagement, confidence, supportiveness,
  error clarity, recovery, trust)

You can pre-fill it: `…/questionnaire?pid=P01&cond=B`.

---

## Where the data goes

All files are written to a `Logs/` folder at the top of the project:
```
Real-Time-Immersive-Speech-Programming-…/Logs/
```
- `sessions.csv` — one row per session start (participant, condition)
- `<PID>_events.csv` — every transcript, inject, task change, note, and (if
  `StudySessionLogger` is used) speech attempts and task timing
- `<PID>_questionnaire.csv` — questionnaire answers

These CSVs are git-ignored so participant data never gets committed.

---

## The 4 tasks and their error types

Each task has a correct outcome plus 4 error types. The errors are designed so
the scene *looks fine at first* and the problem emerges through interaction
(the "root-cause attribution" principle from the supervisor meeting).

| Task | Correct outcome | Error types |
|------|-----------------|-------------|
| 1. Create a ball | Ball at hand, interactable | wrong position · cube not sphere · falls through floor · squashed |
| 2. Colour green | Ball turns green | all objects green · teal not green · reverts after 2s · new object |
| 3. Orbit the cube | Ball orbits cube | orbits origin · wrong axis · crashes into cube · too fast |
| 4. Solar system | Star + orbiting planet | only star · squashed planet · planet drifts off · 50 planets |

Edit the code strings in
[`Server/samples/apps/wizard_of_oz/app.js`](Server/samples/apps/wizard_of_oz/app.js)
(the `SCRIPTS` object) to change what each button injects.

---

## Voice transcription note

Speech-to-text calls the lab's Whisper server
(`http://130.136.2.161:50101`), usually only reachable on the university
network. If you're testing at home, the transcript won't appear — but you can
still inject responses from the control panel and run the whole flow. To point
at a different STT server, set `STT_HTTP_URL` before launching, or ask Daniele
for the current endpoint/key.

---

## Piloting order (from the supervisor)

1. Pilot **one task** with the embodied-agent interaction (1–2 questions max,
   no infinite loops).
2. Expand to 2–3 pilot participants; check the interaction feels right.
3. Add the remaining tasks with the same narrative.
4. Full run: ~10 participants, between-subjects (each sees only A, B, or C),
   3–4 tasks, ~45 min per session.

## Keyboard shortcuts
- **Space** (Unity Editor / standalone) — hold to record voice
- **F12** — toggle the in-Unity Wizard-of-Oz panel (alternative to the web panel)
