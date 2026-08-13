# Paper prompt — describe the implicit/proactive showcase (anticipation, context-derived function choice, inert anchors)

**This prompt targets the paper workspace** (`agenticxr_paper`, `main.tex`). It
describes a mechanism set implemented in the code repo on 2026-08-13
(`Real-Time-Immersive-Speech-Programming`, progress-log entry "2026-08-12/13")
and asks the paper to describe it accurately — at the evidence level it actually
has. Ground every claim in the code repo's `docs/progress-log.md` vocabulary:
source-complete / mock-tested (here: deterministic-tested) / live-exercised.
Nothing below is live-exercised yet.

## What was built (the facts to describe)

1. **Implicit sensing → L2 turns (existing, now with a visible payoff).** Unity
   emits discrete implicit-trigger events (authorable region volumes →
   `locomotion` entry/exit; proximity enter/exit with hysteresis; head-ray gaze
   dwell — a head-direction ray with angular tolerance, NOT eye tracking; say
   so wherever L2's trigger list uses "gaze"). A weighted activity window
   turns sustained activity into a context trigger that starts a normal L2
   authoring turn gated by the unchanged mode policy.

2. **Inert anchors (new).** Study scenes contain authored `AgenticInertAnchor`
   objects — e.g. an unlit lamp, an empty pedestal, a dormant guide marker —
   that exist with stable IDs from scene load but visibly do nothing. Their
   `anchorRole`/`description` fields are published into Shared XR Memory as
   part of scene state. They describe what the object IS, never what function
   to add. Generated behaviors may instantiate child objects parented under an
   anchor, which is how "an object that was not present appears".

3. **Context-derived function choice (new, the variability claim).** When an
   implicit trigger fires, the turn's objective carries only RAW environmental
   context assembled from Shared XR Memory (region, anchor role/components,
   derived affordances, nearby objects, experience mode). The agents derive
   WHAT function fits; no trigger→function table exists anywhere in code (this
   is deterministic-tested: differently configured anchors provably produce
   different contexts). This is the capability neither the explicit-only
   DreamCodeVR baseline (it cannot act unprompted) nor a pre-scripted trigger
   system (it cannot vary with context) can reproduce — the paper may make
   that contrast explicitly, as a design claim, with behavioral variability
   across contexts pending live evidence.

4. **Anticipation / speculative pre-computation (new).** Sustained directed
   attention toward a specific target (≥2 gaze/proximity/hand observations
   crossing a sub-assist threshold) emits a `predicted_engagement` signal
   BEFORE the assist trigger. It starts a speculative-only preparation run:
   candidates generated, validated, dry-run in the Verification Space, and
   registered pinned to the exact scene tuple — never committed. When the real
   trigger later fires, the normal turn consults the speculative store first;
   an adopted candidate still re-runs validation and every consent/mode gate.
   This extends the paper's existing idle-time speculative-futures mechanism
   from "prepare when the user is idle" to "prepare for where the user is
   heading". Explicit user activity preempts preparation; study trials
   suppress it unless the protocol's flags allow it.

5. **New measures (in the 76-column per-trial export).**
   - `predictedEngagementCount` — anticipations per trial;
   - `speculativePreparationLeadTimeMs` — how far ahead of the real trigger an
     adopted candidate was prepared (stamped on the adoption event);
   - `implicitTriggerToVisibleChangeMs` — context trigger → first committed
     visible change, from envelope pairs sharing the trigger's correlation ID;
   - plus the existing prepared/adopted counts and implicit-trigger counts.

## What the paper should do with this

- **Interaction modes / L1–L2**: describe the implicit arm's payoff concretely
  (inert anchors coming alive, objects appearing) and correct any wording that
  implies eye tracking; gaze is head-ray dwell.
- **Speculative futures / Verification Space section**: extend the existing
  idle-preparation description with attention-driven anticipation and the
  preparation→adoption lead-time measure; keep the invariant prominent —
  speculation never commits, adoption re-runs the full pipeline.
- **Study Design**: the L1/L2 task descriptions can now name the anchor
  scenario (lamp/pedestal/guide marker) and add the three new log-derived
  measures to the Measures subsection (they are descriptive/characterizing for
  H2's arms; `implicitTriggerToVisibleChangeMs` is the perceived-latency
  companion for the implicit tasks; the anticipation lead time quantifies the
  "system prepared before I arrived" experience).
- **Implementation Status**: list anticipation logic, context assembly,
  lead-time stamping, and export derivations as deterministic-tested; the
  speculative spawn path and the Unity anchor component as source-complete
  (batch-compiled); the full anticipate→trigger→adopt→visible-change chain as
  not yet observed live. Do not present the showcase as evaluated.
- **Privacy/ethics**: discrete transition events only — no continuous
  trajectories, no eye tracking, no camera capture; anticipation consumes the
  same discrete stream.
