# Continuous, human-centered AgenticXR runtime

## What is implemented

AgenticXR now starts a long-lived observer alongside the speech runtime. It reuses
the existing Ubiq `SceneDelta` channel, `SceneBridgeClient`, Shared XR Memory,
sensor registry, experience context, bounded goals, and authoring pipeline.

The monitored stream includes:

- gaze/selection;
- proximity and collision;
- hand/gesture observations;
- locomotion and region entry;
- object revision and scene-state changes;
- user approval/rejection/undo decisions.

`ActivityMonitor` combines recent, sufficiently confident signals in a bounded
window. Crossing the configured threshold creates an L2 `context` opportunity.
It does not itself change Unity.

The continuous observer is enabled by default when `npm run start:agenticxr` runs.
Starting an Anthropic assistance turn from a threshold crossing is separately off
by default because it can consume API credits and create experimental variance.

## Human-centered safety invariants

A context opportunity:

1. surfaces visible agent status;
2. enters the normal Claude orchestration pipeline;
3. retains L2/context classification;
4. stops if no useful low-risk assistance exists;
5. passes Validator/Critic, route-by-verifiability, goal bounds, Proposal Gate, and
   Unity Verification Space;
6. may be automatic only when deterministic/rule-verifiable, risk is below `0.3`,
   reversible, and local;
7. otherwise uses the existing world-space confirmation/dialogue route;
8. can be preempted by push-to-talk or another explicit request;
9. remains undoable through the existing Unity panel.

Continuous assistance is skipped during a study trial unless the study-specific
override is explicitly enabled.

## Experience conditioning

The current experience can be `productivity`, `training`, `entertainment`,
`exploration`, or `unspecified`. It affects both candidate generation and
deterministic ranking:

- productivity favors unobtrusive task support;
- training favors guidance and recoverability;
- entertainment allows playful feedback;
- exploration preserves open-ended discovery.

The architecture and state model therefore represent non-authoring experiences.
Dynamic Unity behavior authoring remains the only fully implemented action harness;
the repository does not yet demonstrate complete entertainment or productivity
applications.

## Configuration

Always-on observation starts unless explicitly disabled:

```powershell
$env:AGENTICXR_MONITOR_ENABLED="true"
```

Enable proactive assistance:

```powershell
$env:AGENTICXR_CONTINUOUS_ASSIST_ENABLED="true"
$env:AGENTICXR_ACTIVITY_THRESHOLD="1.1"
$env:AGENTICXR_ACTIVITY_WINDOW_MS="5000"
$env:AGENTICXR_ACTIVITY_COOLDOWN_MS="30000"
$env:AGENTICXR_CONTINUOUS_ASSIST_TIMEOUT_MS="120000"
```

Only if an approved study condition includes continuous assistance:

```powershell
$env:AGENTICXR_STUDY_ALLOW_CONTINUOUS_ASSIST="true"
```

`ANTHROPIC_API_KEY` is required for assistance, but not for the activity scoring
module itself. The standard `start:agenticxr` command currently requires the key
because the complete runtime includes Claude.

## Evidence boundary

- Sensor publication, normalization, threshold/cooldown behavior, experience-aware
  ranking, and policy rejection are source-complete and deterministic/mock tested.
- The Ubiq mock integration observes the normalized activity stream.
- The continuous service has not yet been exercised with a real Anthropic account,
  live Quest sensor behavior, or participants.
- Claims that it sustains entertainment, productivity, or training experiences remain
  architectural/design claims until those applications are implemented and evaluated.
