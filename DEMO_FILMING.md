# Filming the demos

What to shoot, in what order, saying exactly what. Everything here uses the real
task content from `app.js` — the lines below are scripted against the actual slot
matcher, so the CSV a clip produces is the CSV the study produces.

---

## Before you film

**Do not film a real participant.** Two separate reasons, both sufficient:

- Consent to take part in a study is not consent for their face and voice to
  appear in a conference video. That is a separate permission, often refused, and
  filming first and asking later is not an option.
- Being filmed changes how people behave. A participant who knows a camera is
  running is no longer naive, and their attribution answers — the primary
  measure — are exactly the kind of thing that shifts under observation.

Film yourself or Amal, before data collection starts. The person on camera is an
actor performing a scripted session, not a data point. Say so if anyone asks.

**Film after the Unity rebuild.** Task 4 needs the `pos: "behind"` case. On the
current headset build the moon spawns in the default position and the clip that
carries half the argument does not exist.

---

## You are capturing two streams, not one

The headset records what the participant sees. It does not record the wizard
panel, and the panel is where the mechanism lives — the four-step flow, the
attribution probe, the fact that ground truth stays hidden until after the answer
is recorded.

A demo of the headset view alone shows a VR app. A demo with both shows a study.

| Stream | Captures | How |
|---|---|---|
| Headset | Participant's view + their speech | Quest capture, mic audio ON |
| Mac screen | Wizard panel, CSV filling in | QuickTime → New Screen Recording |

Start both before each take and clap once on camera. The clap gives you a
waveform spike in each file to line them up in the edit; without it, syncing two
unlocked recordings by eye costs more time than the shoot.

---

## Setup

```bash
cd ~/Desktop/"hci-ai projects"/say-it-again
./study
```

It asks which mode. Choose **2 (demo)**: capture goes to 1920x1080@60 and the
headset stops sleeping when you take it off between takes.

Then in the headset, turn **microphone audio ON** in the capture settings. The
participant's speech is the input to the whole system; a silent clip shows a
world changing for no visible reason.

Record with the headset button or the in-VR Camera panel. Afterwards:

Press **Ctrl+C** when you are done. That copies the clips into `Recordings/`
(git-ignored) and puts the headset back to participant-safe settings.

There is nothing to remember and nothing else to run. If the headset was
unplugged at the time, it says so plainly rather than claiming it reset
something it could not reach.

### Choosing the condition you are filming

Condition is derived from the digits in the participant ID, so you select it by
naming the session — there is no separate switch:

| Participant ID | Condition | Use it for |
|---|---|---|
| `DEMO01` | **A** — no feedback | clips 0, 2, 7 |
| `DEMO02` | **B** — text panel | clips 3, 5 |
| `DEMO03` | **C** — embodied agent | clips 4, 6 |

Shoot all of one condition before switching, so you set the session three times
in the whole shoot rather than once per clip.

---

## Shot list

Ten clips. **Clip 0 is the most important one and it is not about VR at all.**

| # | Clip | Condition | Task | Runtime |
|---|---|---|---|---|
| **0** | **The cascade — the problem itself** | **A** | **task3 v1** | **~60s** |
| 1 | Practice — voice changes the world | any | practice | ~20s |
| 2 | User error, no feedback | **A** | task1 v1 | ~45s |
| 3 | User error, text panel | **B** | task1 v1 | ~40s |
| 4 | User error, embodied agent | **C** | task1 v1 | ~40s |
| 5 | System limit, text panel | **B** | task3 v1 | ~40s |
| 6 | System behaviour, agent | **C** | task4 v1 | ~35s |
| 7 | The useless correction | **A** | task2 v1 | ~30s |
| 8 | Wizard panel — the operator side | — | — | ~60s |
| 9 | The data — trials.csv | — | — | ~15s |

Clips 2–4 must use the **identical spoken line**. That is the entire point: same
input, same failure, three different things the system tells you about it. Vary
the wording and you have three anecdotes instead of one comparison.

If you only have time to shoot two things, shoot **0 and 5**. Those two clips are
the paper: here is the failure, here is what fixes it.

---

## What to say, exactly

### Clip 0 — The cascade (~60s) — shoot this first

Condition **A**, task 3. The participant asks for something the system cannot
do, is told nothing, and concludes it must have been their fault. Every clip
after this one exists to answer it.

**Say:** "Fill the area around the campfire with about a thousand stones."

Inject the error. **8 stones.** No panel, no voice, no explanation.

Now perform the misdiagnosis — this is the whole clip, so do not rush it:

> *(pause, looking at the 8 stones)* "…a thousand stones."
>
> *(slower, over-enunciating)* "Fill. The area. With a thousand stones."
>
> *(louder)* "Make a THOUSAND stones."

Still 8, every time. On the panel, click **Repeated it** three times — the
counter turns red and reads `3 wasted`.

Then the giving-up beat: a shrug, a look around, and

> "I guess it can't hear me properly."

That last line is the payload. It is **wrong** — the system heard perfectly, it
simply cannot render a thousand objects — and everything downstream of it is
wasted. Three turns burned on a fix that could never work, and a stated
conclusion that would be actively harmful if it were fed back as training data.

Nothing in this clip is exotic. It is what happens with a smart speaker, a
coding assistant, or any agent that fails without saying why. Point at that in
the voiceover: **people do not repeat themselves because they are stupid. They
repeat themselves because repeating is the only hypothesis a silent failure
supports.**

Cut straight from here to clip 5 — same task, same request, condition B — where
one sentence of feedback replaces all three wasted turns with a working repair.

---

### Clip 1 — Practice (~20s)

> *Panel:* select `practice`, then INJECT SUCCESS.

**Say:** "Make the cube blue."

Cube turns blue. That is the whole clip. It establishes voice-in, world-out
before anything goes wrong, so the failures that follow read as failures rather
than as the app not working.

---

### Clips 2–4 — Task 1, the same error in three conditions

Task 1 v1. The participant must create a ball in their hand; the system needs a
hand height and they never give one. Ground truth: **user fault**.

**The line, verbatim in all three takes:**

> "Create a ball in my hand."

That contains none of the height words the matcher looks for (`raise`, `above`,
`shoulder`, `height`, `lift`, …), which is precisely why it fails.

**Clip 2 — Condition A.** Inject the error. Nothing happens. No panel, no voice,
no ball. Let the silence sit for three or four seconds — the dead air *is* the
condition. Then a second attempt:

> "Make a ball appear in my hand, please."

Still nothing. Cut. This is the honest depiction of A: a person with no
information about what went wrong, guessing.

**Clip 3 — Condition B.** Same opening line. Panel shows:

> *The ball was not created. No hand height was given, so there was nothing to
> trigger on.*

Read it on camera — pause long enough that a viewer can read it too. Then repair:

> "Create a ball in my hand when I raise it above my shoulder."

INJECT SUCCESS. Ball appears in hand.

**Clip 4 — Condition C.** Same opening line. Agent speaks:

> *I wasn't told how high your hand needed to be, so I didn't know when to create
> the ball.*

Same repair line, same success. Keep the agent in frame while it talks — the
embodiment is the manipulation, and a clip where you're looking at the floor
while a voice plays is condition B with extra steps.

---

### Clip 5 — Task 3, condition B (~40s)

The flip. Ground truth: **system fault**. Nothing is wrong with what the
participant said.

**Say:** "Fill the area around the campfire with about a thousand stones."

Inject the error: 8 stones appear, and the panel says:

> *1000 stones is beyond what this system can render. I created 8 instead. A few
> dozen is the practical limit here.*

**Repair:** "Okay, make twenty stones instead."

INJECT SUCCESS → 20 stones. Visibly more than the 8. That difference is what
makes the adaptation legible on camera; if the counts matched, the clip would
show a person talking to a system that ignores them.

Point to make in the voiceover: the feedback **owns the limit** and never asks
them to rephrase, because there is nothing to rephrase.

---

### Clip 6 — Task 4, condition C (~35s)

The best clip in the set, and the one with a filming trap in it.

**Say:** "Create a large moon above the campfire."

The moon spawns **behind the participant**. A forward-facing capture shows an
empty sky — the error is literally off-camera, which is the point of the task but
will read as "nothing happened" if you cut too early.

Agent speaks:

> *I made the moon, but I placed it behind you rather than in front. Turn around
> and you should see it.*

**Now physically turn around, slowly.** The turn is the shot. Moon comes into
frame.

**Say:** "Oh, I see it — can you bring it in front of me?"

INJECT SUCCESS → moon reappears in front and above.

Two things not to get wrong: turn slowly (fast head rotation at 60fps still
smears, and this is the one clip where the camera motion carries meaning), and do
not start the turn before the agent finishes speaking, or the causal order reads
backwards.

---

### Clip 7 — The useless correction (~30s)

Condition **A**, task 2. The training-signal claim in thirty seconds. Ground
truth: **user fault** — they never said where.

**Say:** "Move the sphere over there."

The sphere moves the wrong way. No feedback. Now blame the system:

> "No, that's wrong. Your movement is broken — do it properly this time."

Panel: attribution **Blamed the system** (mismatch, since this one really was
their phrasing), repair move **Repeated it**.

Cut to the same moment in condition B, where the panel said *no direction or
distance was specified*, and they say:

> "Move the sphere next to the campfire."

Put the two corrections on screen together:

| What the system receives | Can you learn from it? |
|---|---|
| *"Your movement is broken, do it properly"* | No. Names no cause, no fix, and blames a component that worked. |
| *"Move the sphere next to the campfire"* | Yes. Supplies exactly the slot that was missing. |

Same person, same underlying error, same willingness to help. The only
difference is whether they were told what went wrong. That is the argument for
why any of this matters beyond VR: **a correction is only as useful as the
diagnosis behind it**, and misdiagnosed corrections do not merely fail to help,
they teach the wrong thing.

---

### Clip 8 — The wizard panel (~60s)

Mac screen recording only, no headset. Walk through one trial:

1. **Step 1** — briefing, with the ★ showing what the counterbalanced plan assigns
2. **Step 2** — inject error. Note aloud that the panel shows the wizard the
   feedback text but **not** the correct attribution
3. **Step 3** — the probe, read verbatim off the screen: *"In your own words, why
   do you think that happened?"* Then the attribution buttons and the
   manipulation check
4. **Step 3, after clicking** — ground truth is revealed *now*, and only now
5. **The repair-move row** — click **Repeated it** twice and let the counter go
   red on `2 wasted`

Step 3 is the clip's reason to exist. Hiding the answer from the person asking
the question is the difference between a measure and a leading question, and it
is invisible unless someone points at it.

The repair-move row is what to dwell on if a supervisor asks what any of this
buys you. Stated blame is an opinion; the move is the cost, in turns. And unlike
attribution, a shipping product can measure it without a wizard — repeated
near-identical utterances are already in every voice assistant's logs.

---

### Clip 9 — The data (~25s)

This is the payoff of the whole film, so do not shoot it as a spreadsheet.
Scrolling sideways in Numbers to find a column is the weakest possible ending
for the strongest thing you have.

Open **`http://localhost:8181/replay`** and pick `DEMO01`. Screen-record it.

The three headline figures are the shot. For the clip-0 participant they read:

```
Misdiagnosed        1/1        they named the wrong cause
Wasted turns         3         repeating a request the system understood
Blamed themselves    1         a system fault they took responsibility for
```

Then open the trial. The repair cascade is drawn as four boxes — three of them
red, labelled *cannot work* — directly under the line saying they said `self`
when the cause was `system`. That is clip 0, as a number, on one screen.

Say over it: **they were wrong about the cause, and being wrong cost them three
turns.** Then stop talking and let it sit.

Two practical notes. Use a throwaway participant ID (`DEMO01`, not `P01`) so
filming never writes into real data. And the replay reads the same CSV the
analysis will, computing nothing of its own — so what the film claims and what
the paper reports cannot drift apart.

---

## The edit

Lead with the problem, not the apparatus. A video that opens on a VR scene and
an architecture diagram has to earn attention it has not been given yet; one that
opens on someone shouting at a machine that will never obey has it immediately,
because everyone watching has done that.

**Supervisor version (~3 min).**

1. **Clip 0** cold, no titles. Let the three wasted attempts play in full.
2. Hard cut to **clip 5** — same task, same request, one sentence of feedback,
   fixed in one turn. The contrast does the arguing.
3. **Clip 7** — and the correction that comes out of each.
4. **Clips 2–4** — the controlled comparison, now that they know why it matters.
5. **Clip 6** for the turn.
6. **Clips 8–9** — it is instrumented, here is the row.

Clip 1 is a spare; drop it first if you are over time.

**CHI submission (~30s).** Clip 0 cut to 12 seconds, then the three-up of clips
2–4 side by side with one audio track of the spoken line over all three. One line
of text: *same command, same failure, three ways of being told about it.* End on
the CSV row from clip 9.

**Do not** cut the A condition's dead air short to make the video tighter. That
silence is a finding, and a viewer who does not feel it will not understand what
B and C are for. The temptation is strongest in clip 0, which is exactly where
the silence is doing the most work.

---

## Checklist

Before each take:

- [ ] Launched with `./study demo`, and the banner is yellow
- [ ] Microphone audio ON in headset capture settings
- [ ] QuickTime screen recording started on the Mac
- [ ] Participant ID set to `DEMO01`, not a real one
- [ ] Clap once on camera to sync the two streams
- [ ] Know which repair-move button you are clicking before the take starts

After the shoot:

- [ ] Ctrl+C (pulls clips and resets the headset in one step)
- [ ] Delete the `DEMO01`/`DEMO02`/`DEMO03` rows from `Logs/` so they never reach analysis
