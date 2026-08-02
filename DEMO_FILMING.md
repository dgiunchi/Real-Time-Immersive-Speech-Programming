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
cd Server
npm run demo:setup      # 1920x1080@60, 15 Mbps, headset stays awake off-head
npm run study           # server + IP handoff, as normal
```

Then in the headset, turn **microphone audio ON** in the capture settings. The
participant's speech is the input to the whole system; a silent clip shows a
world changing for no visible reason.

Record with the headset button or the in-VR Camera panel. Afterwards:

```bash
npm run demo:pull       # clips land in Recordings/ (git-ignored)
npm run demo:reset      # BEFORE any real participant
adb shell setprop debug.oculus.guardian_pause 0
```

`demo:reset` matters. The setup step holds the proximity sensor open so the
headset does not sleep when you take it off to check a take. Left on during a
real session, the headset never sleeps on the desk between participants.

---

## Shot list

Eight clips. The first three are the study's argument; skip any of the rest
before you skip those.

| # | Clip | Condition | Task | Runtime |
|---|---|---|---|---|
| 1 | Practice — voice changes the world | any | practice | ~20s |
| 2 | User error, no feedback | **A** | task1 v1 | ~45s |
| 3 | User error, text panel | **B** | task1 v1 | ~40s |
| 4 | User error, embodied agent | **C** | task1 v1 | ~40s |
| 5 | System limit, text panel | **B** | task3 v1 | ~40s |
| 6 | System behaviour, agent | **C** | task4 v1 | ~35s |
| 7 | Wizard panel — the operator side | — | — | ~60s |
| 8 | The data — trials.csv | — | — | ~15s |

Clips 2–4 must use the **identical spoken line**. That is the entire point: same
input, same failure, three different things the system tells you about it. Vary
the wording and you have three anecdotes instead of one comparison.

---

## What to say, exactly

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

### Clip 7 — The wizard panel (~60s)

Mac screen recording only, no headset. Walk through one trial:

1. **Step 1** — briefing, with the ★ showing what the counterbalanced plan assigns
2. **Step 2** — inject error. Note aloud that the panel shows the wizard the
   feedback text but **not** the correct attribution
3. **Step 3** — the probe, read verbatim off the screen: *"In your own words, why
   do you think that happened?"* Then the attribution buttons and the
   manipulation check
4. **Step 3, after clicking** — ground truth is revealed *now*, and only now

Step 3 is the clip's reason to exist. Hiding the answer from the person asking
the question is the difference between a measure and a leading question, and it
is invisible unless someone points at it.

---

### Clip 8 — The data (~15s)

Screen recording of `Logs/trials.csv` gaining a row as you end a trial. Show the
`attribution`, `correctAttribution` and `attributionCorrect` columns landing.

Use a throwaway participant ID (`DEMO01`, not `P01`) so filming does not write
into the real data files.

---

## The edit

**Supervisor version (~3 min).** Clips in listed order. The A/B/C block is the
spine — cut 2, 3, 4 back to back with the spoken line audible in each so the
repetition lands. Then 5 and 6 to show the fault type flipping. Then 7 and 8 to
show it is instrumented.

**CHI submission (~30s).** Only the three-up: clip 2, 3, 4, side by side, same
audio track of the spoken line playing once over all three. One line of text on
screen: *same command, same failure, three ways of being told about it.* End on
the task 4 turn from clip 6 if you have the seconds.

**Do not** cut the A condition's dead air short to make the video tighter. That
silence is a finding, and a viewer who does not feel it will not understand what
B and C are for.

---

## Checklist

Before each take:

- [ ] `npm run demo:setup` has run since the last headset reboot
- [ ] Microphone audio ON in headset capture settings
- [ ] QuickTime screen recording started on the Mac
- [ ] Participant ID set to `DEMO01`, not a real one
- [ ] Clap once on camera to sync the two streams

After the shoot:

- [ ] `npm run demo:pull`
- [ ] `npm run demo:reset`
- [ ] `adb shell setprop debug.oculus.guardian_pause 0`
- [ ] Delete the `DEMO01` rows from `Logs/` so they never reach analysis
