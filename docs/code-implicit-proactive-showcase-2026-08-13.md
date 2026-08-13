# Code prompt — implicit/proactive showcase for L1–L2: visible, context-derived, precomputed

**This prompt targets `D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming`.**
It is written for the coding agent with write access there. Read
`docs/progress-log.md` (entry 2026-08-11 onward) first for current state. Extend
the existing mechanisms — `ImplicitTriggerSensors`, `ActivityMonitor`,
`continuous_monitor.js`, `future_goal_predictor.js`, `mode_policy.js`, the
speculative-candidate MCP tools, and the study logging schema — **never build a
parallel system beside them**. Report honestly in the established
source-complete / mock-tested / live-exercised vocabulary.

## Why (study motivation)

The study's L1/L2 tasks currently prove that an implicit trigger *fires*, but
not that the outcome is worth watching. The experiment needs a demonstrator in
which **the participant gives no instruction at all**: they merely move, look,
or approach an environmental item, and the agentic system decides — from the
environment itself — what function to add, then adds it. The payoff must be
(a) **visible** — unmistakable in-headset and on a session recording;
(b) **impossible for the baseline** — DreamCodeVR answers only explicit speech,
so nothing in its arm can ever act unprompted; this is the point to make
undeniable, not merely claim;
(c) **variable, not scripted** — a developed/pre-scripted system could fake one
hard-coded trigger→effect pair. Here the *agents* must evaluate the scene and
choose/parameterize the function from context (region, nearby objects,
affordances, recent activity, experience mode), so two runs in different
contexts produce visibly different, contextually sensible results;
(d) **anticipated when possible** — the system may understand *before* the
trigger that the user is likely to go somewhere or engage with something, and
precompute candidates that are then applied (through every normal gate) at
trigger time. This exercises the paper's speculative-futures mechanism for real
and turns deliberation latency into a measurable near-zero at the moment of
contact.

## What already exists (ground your work in it)

- Unity: `ImplicitTriggerSensors.cs` emits discrete `locomotion` (region
  volumes), `proximity`, and `gaze`-dwell events; `AgenticRegionVolume` is the
  authorable trigger volume. Generated MonoBehaviours may instantiate new
  objects **parented under the target's transform** (code-generator constraint)
  — that is the sanctioned way to make "an object that was not present appear".
- Server: `ActivityMonitor` (weighted window/threshold/cooldown) →
  `continuous_monitor.js` spawns an L2 orchestrator turn with
  `AGENTICXR_TRIGGER_SOURCE=context`; `mode_policy.js` restricts automatic
  execution to low-risk/reversible/local and is authoritative in all arms.
- Speculation: `future_goal_predictor.js` + `create_bounded_goal(speculative)` +
  `register_speculative_candidate` / `select_speculative_candidate` pin prepared
  candidates to an exact scene tuple; adoption re-runs the full normal pipeline.
  Today this path is **idle-time-based** — it fires when the user does nothing —
  not anticipation-based.
- Study logging already has `activity_assist_*`, `continuous_assist_*`,
  `idle_prediction_*`, `speculative_candidate_prepared/adopted` events, and the
  trial export counts them. Continuous assist and speculation are suppressed
  during trials unless `AGENTICXR_STUDY_ALLOW_CONTINUOUS_ASSIST` /
  `AGENTICXR_STUDY_ALLOW_SPECULATION` are set — the L1/L2 study conditions that
  need this behavior must set them; say so in the study docs you touch.

## Build items

### 1. Anticipation: predict the destination/engagement before the trigger

Extend `future_goal_predictor.js` (and, as needed, `ActivityMonitor`) so
speculation is driven by **directed activity**, not only idleness: a sustained
pattern of gaze toward / movement toward / heading alignment with a region or
anchor object (derived from the existing discrete sensor stream — no continuous
trajectory logging; privacy constraints stand) raises a *predicted-engagement*
signal for that specific target BEFORE any threshold crossing. That signal
starts a speculative goal for the predicted target: candidates generated,
validated, dry-run, and registered pinned to the current scene tuple, never
committed. When the real L1/L2 trigger later fires for that target,
`select_speculative_candidate` must be consulted first (this consultation
already exists in the orchestrator prompt — verify it actually happens on the
context path and fix if not), the reused draft re-validated, and every consent/
mode gate applied unchanged. Log preparation→adoption lead time as a
first-class measure (extend the schema/export with e.g.
`speculativePreparationLeadTimeMs` if not derivable already).

### 2. Context-derived function choice (the variability requirement)

The L2 turn's objective currently says "assess whether the current activity
would benefit from assistance." Strengthen the context handed to the
orchestrator: include the region id, the anchor object's tag/components/
affordances, nearby-object summary, and experience mode, and require the
generated candidates to *derive the function from that context* rather than
receive it. Acceptance is behavioral, not prompt-cosmetic: with a mock model
harness (or live key when available), the same trigger on two differently
configured anchors must yield materially different, contextually apt candidate
sets — journal both runs as evidence. Do not hard-code any trigger→function
table anywhere in code; if you find one creeping in, that is the failure mode
this prompt exists to prevent.

### 3. The visible payoff in Unity

Make the applied result unmistakable: the study scene needs 2–3 authored
*inert anchors* (e.g. an unlit lamp, an empty pedestal, a dormant guide marker
near a doorway `AgenticRegionVolume`) whose stable IDs exist from scene load but
which visibly do nothing until the agent acts. The generated behavior (or its
spawned child object) must produce an obvious change — light, motion, an
appearing object on the pedestal, an animated guide — plus the existing status
surface announcing what was noticed and what was prepared ("I noticed you keep
returning to the bench — I've added X"). Keep everything inside the automatic
gate's limits (local, reversible, non-persistent) or route to consent when it
is not; never widen `mode_policy` to make the demo flashier.

### 4. Study wiring and docs

Verify the trial export derives: implicit-trigger count, assist opportunities
suppressed vs. acted, speculative prepared/adopted counts, the new lead-time
measure, and time-from-trigger-to-visible-change (this is the perceived-latency
star of the L1/L2 arms; add an envelope-pair derivation if missing). Update
`docs/study-logging-schema.md`, `docs/STUDY_SESSION_SCRIPT.md` (replace the
DRAFT L1/L2 scenarios with the authored ones), `docs/LIVE_SYSTEM_REQUIREMENTS.md`
(env flags the L1/L2 conditions must set), and `docs/progress-log.md`.

## Constraints

- `mode_policy.js` stays the deterministic autonomy authority; speculation
  never commits; adoption always re-runs the full pipeline and consent gates.
- Discrete sensor events only — no continuous position logging, no eye
  tracking, no camera capture (privacy/IRB, unchanged).
- Extend existing modules; no parallel systems, no new NetworkIds.
- Mock-tested acceptance for every deterministic piece; a live-model run of the
  full anticipate→trigger→adopt→visible-change chain requires the study PC's
  configured keys and Unity Play Mode — label evidence honestly, and do not
  claim live-exercised without it.
