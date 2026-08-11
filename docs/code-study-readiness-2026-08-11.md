# Code prompt (for Codex) — close the study-blocking gaps for the AgenticXR user study

**This prompt targets `D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming`.**
It is written for the coding agent (Codex) that holds write access there; the session
that authored this prompt has read-only access. Its job: make the system able to *run
the study* described in the paper's Study Design — five tasks (L1--L5), each under two
conditions, with the measures exportable per trial. Extend the existing mechanisms
(sensor registry, cache publisher, `mode_policy.js`, Verification Space path, artifact
log/exporter), **not building parallel systems beside them**. Read
`docs/progress-log.md` first for current state, and treat
`docs/study-logging-schema.md` as the executable contract with the paper's Measures
subsection — it already names the three conditions (`baseline`,
`agenticxr_no_verification`, `agenticxr_verification`). Report honestly in the
established source-complete / mock-tested / live-exercised vocabulary.

## Why these items (study mapping)

The study pairs conditions per task: L1--L2 run full AgenticXR vs. AgenticXR with
dry-run verification bypassed (H2); L3--L5 run full AgenticXR vs. the DreamCodeVR
baseline (H1); L4/L5 trials nest a single-candidate vs. selected-best-of-several
proposal (H4). Today three of the four condition mechanics have gaps.

## 1. Implicit-trigger sensing for L1/L2 (highest priority — the implicit tasks cannot trigger today)

Current state as read from source: `Server/memory/sensor_registry.js` ingests
`proximity, collision, gaze, handTracking, gesture, locomotion` and maps them to
semantic relations, but `Unity/Assets/AgenticCache/CachePublisher.cs` emits only (a) a
`locomotion` event at scene entry and (b) a `gaze` event when the *selected object*
changes. L2's trigger is "motion, gaze, proximity" toward doorways/stations/regions;
L1 needs an implicit opportunity signal. Build the Unity-side emitters:

- **Region/trigger volumes**: authorable in-scene volumes (doorway, station, target
  region) emitting `locomotion` events with `regionId` and `entering`/`exiting` on
  the existing sensor-event path, using stable object IDs.
- **Proximity**: user-to-object proximity events (enter/exit within a configurable
  radius) emitting `proximity` with `sourceObjectId`/`targetObjectId`.
- **Head-ray gaze** (decision point): either a real head-ray focus emitter (dwell
  threshold, emitting `gaze` with the focused object), or — if that is out of scope
  before the study — say so explicitly in the progress log so the paper can descope
  "gaze" from L2's trigger list. Do not leave the mismatch silent.
- Wire these through to the L1/L2 trigger path end-to-end so a context change can
  start an authoring iteration gated by `mode_policy` exactly as an explicit request
  would be.

Out of scope, deliberately (privacy/IRB): continuous position-trajectory logging,
eye tracking, and any camera/passthrough image capture. Discrete events only.

## 2. Dry-run bypass toggle (the H2 condition arm)

A per-session configuration flag (server-side, surfaced in logs as
`condition: agenticxr_no_verification`) that skips Verification Space dry-runs while
**keeping everything else identical**: Shared XR Memory, Cache Exchange freshness
checks, preview surface, and consent routing all stay on, so the contrast isolates
verification only. Proposals in this arm still carry a validation-state field marking
them unverified. The `mode_policy` guard must remain authoritative — the bypass may
not widen autonomy. Acceptance: a contract test showing the same intent produces a
proposal in both arms, differing only in dry-run evidence and validation state, with
the condition string correctly stamped on every study event.

## 3. Multi-candidate generation and ranking (the H4 mechanism)

The paper specifies it (background multi-candidate extension): the Artifact/Code
Generator produces N candidates per intent, each independently dry-run in the
Verification Space; a ranking step (dry-run success, Validator/Critic risk) selects
one for the unchanged consent gate; rejected candidates are logged into the artifact
history. For the study, N=1 vs. N>1 must be switchable per trial and logged
(candidate count, rank of the surfaced candidate). Acceptance: mock-tested pipeline
showing N candidates dry-run, one surfaced, others journaled and retrievable via
`get_artifact_history`.

## 4. Per-trial export completeness

Against `docs/study-logging-schema.md`: verify every load-bearing log-derived measure
in the paper's Measures table is actually derivable from the export — acknowledgement
/ proposal / verification / preview-to-commit latencies from envelope pairs; grounding
errors, stale applications, unsafe/blocked artifacts, validation failures, repair
attempts, rollbacks; per-route accept/reject/undo decisions — one row per trial with
`participantId`/`sessionId`/`trialId`/`condition`/`taskId`/`interactionMode` stamped.
Add whatever researcher-CLI trial registration is missing to set that identity before
each trial. Acceptance: an end-to-end mock session producing a trial CSV whose columns
cover the paper's H1--H4 log-derived measures, with the exporter failing loudly on
missing identity.

## 5. The overarching gate: first live end-to-end run

Everything above is unverifiable until the Unity-side control flow has been observed
executing once in Play Mode against a live model (then on device): select → speak →
generate → dry-run → preview → approve → commit → rollback. Follow
`docs/LIVE_SYSTEM_REQUIREMENTS.md`. When it runs, update `docs/progress-log.md` and
flip the affected source-complete claims to live-exercised — the paper's
Implementation Status section mirrors that vocabulary and is updated from it.

## Constraints

- Extend existing modules; no parallel systems. `mode_policy.js` remains the
  deterministic autonomy authority in all conditions.
- Single-user scope is acceptable for the study; the owner-permissions stub stays
  disclosed, not fixed, unless trivial.
- No secrets in files or logs; pseudonymous `participantId` only, per the logging
  schema's identity rules.
- Honest reporting: every claim in `docs/progress-log.md` labeled source-complete /
  mock-tested / live-exercised as evidenced, never above.
