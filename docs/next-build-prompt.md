# Next Build Prompt — Continue AgenticXR, Aligned to the Paper

**Status (2026-07-12): executed.** All of §2.1–§2.7 below are implemented and
verified — see `docs/progress-log.md`'s "Executed `docs/next-build-prompt.md`" entry
for what was built and how it was tested. §2.8 (RoslynCSharp enforcement) remains
open, as planned. This document is kept as-is below for historical/planning
reference — it reads as a plan because it was written and executed in that order.

## 0. How to use this document

This is an executable prompt, not just notes: it resolves the two decisions
`docs/paper-sync-timelines-and-modes.md` §4 deliberately left open, gives an
implementation order, and specifies exact file/shape changes for each item. Read
`docs/progress-log.md` (current status) and `docs/paper-sync-timelines-and-modes.md`
(the gap analysis this prompt executes) first if picking this up cold — this document
does not re-derive that context, it acts on it.

**Nothing in this document has been implemented yet.** It is the plan for the next
build pass, written so it can be executed directly (by me, in a following turn, or by
another agent) without re-litigating the design questions it resolves below.

## 1. Decisions made in this prompt

### 1.1 `interactionMode` is a new field, separate from `authoringMode`

The paper's own mode ladder (`rag/drafts/agenticxr_design_study_sections.md`, carried
into `main.tex`'s `tab:modes`) separates **who initiates** an authoring episode from
**who controls execution**. Those are orthogonal: `authoringMode` already correctly
answers the second question (automatic / semi_auto_confirm / semi_auto_steer ≈ the
paper's "Gate" column: Reversible/Local / Consent/Detail / ongoing-consent-via-dialogue).
L1–L5 answer the first question (Trigger + Agent role), which nothing today captures.

Decision: add `interactionMode: "L1"|"L2"|"L3"|"L4"|"L5"` as a new, optional envelope
field alongside `authoringMode`, not a replacement for it. This is additive — no
existing routing logic changes, no breaking change to `orchestrator/app.js`'s current
validator output.

Mapping the validator should use when it sets both fields:

| interactionMode | Typical authoringMode |
|---|---|
| L1 Proactive | `automatic` |
| L2 Context | `automatic` |
| L3 Clarify | `semi_auto_confirm` (after the missing detail is obtained) |
| L4 Confirm | `semi_auto_confirm` |
| L5 Converse | `semi_auto_steer` |

### 1.2 Region context: smallest viable version

"Locomotion updates region context" (paper §Shared XR Memory) becomes a new
**discrete** sensor type `"locomotion"`, distinct from the existing continuous
`"proximity"` type. A locomotion event fires when the user crosses into/out of a
**named, statically-defined region** (e.g. `"workshop-entrance"`), not on every frame
of movement — this keeps it symmetric with the existing `AFFORDANCE_RULES` pattern
(a static lookup, not learned/continuous tracking), and keeps sensor payloads small.

### 1.3 `speech → intent`: scoped down, dependency flagged honestly

Real speech is not wired into the new pipeline yet — STT still only feeds the legacy
`code_runtime_generator` path (`docs/progress-log.md`), and `orchestrator/app.js`
takes its intent as a CLI string, not a live transcript. Building real speech→intent
routing means bridging the legacy STT service into the new orchestrator, which is a
separate, larger integration task **out of scope for this pass**. What *is* in scope:
build `intent_store.js` so the plumbing and memory shape exist and are testable now,
and have the orchestrator record its own CLI-provided intent into it as a stand-in.
Say this plainly in any status update — this is not real speech-driven intent memory
yet, just the store it will eventually feed.

## 2. Work items, in build order

Order rationale: additive/non-breaking items first (§2.1–2.5), then the two items
that touch existing request/reply logic (§2.6, the trickier of the two; §2.7 the most
invasive). §2.8 is a stretch goal, not required for paper alignment specifically.

### 2.1 Terminology rename (no behavior change)

- `Server/memory/timeline_registry.js` header comment: replace "clock" language with
  "XR Interaction Timeline" / "Agentic Deliberation Timeline" (keep `xr`/
  `deliberation`/`experimental` as the internal map keys — only the prose changes).
- `docs/agentic-xr-architecture.md` §2.4 and `docs/agent-framework-and-communication.md`:
  same rename.
- Rebuild the "Two Clocks" Lucid chart title/lane labels to match. ~~Redeploy to the
  same URL~~ — not possible: the Lucid connector has no in-place "replace content"
  call for a `lucid_create_diagram_from_specification` document, only granular
  per-shape edits. Done as a new document instead: `Two Timelines and Perceived
  Synchronicity` chart, see `docs/progress-log.md` for both URLs (new + superseded).

**Verify:** grep the repo for `"clock"` (case-insensitive) outside of node_modules;
zero remaining hits in prose describing this concept.

### 2.2 `interactionMode` field

- `Server/mcp/unity_scene_bridge/protocol.js`: add
  `INTERACTION_MODES = Object.freeze({ L1: "L1", L2: "L2", L3: "L3", L4: "L4", L5: "L5" })`
  and an `interactionMode` field to `makeEnvelope`'s destructured options (optional,
  default `null`), same pattern as the existing `authoringMode` field.
- `scene_bridge_client.js`: `proposeArtifact()` accepts an optional `interactionMode`
  param, threads it into the envelope.
- `server.js`: `propose_artifact` and `simulate_artifact` tool `inputSchema` gain
  `interactionMode: z.enum(["L1","L2","L3","L4","L5"]).optional()`; pass through.
  `propose_artifact`'s auto-log to `artifactLog.append(...)` includes it.
- `orchestrator/app.js`: update `validator_critic`'s prompt to output the mapping
  table from §1.1 as part of its JSON verdict — add `"interactionMode"` to the
  required JSON shape: `{"pass": bool, "riskScore": number, "authoringMode": string,
  "interactionMode": string, "reason": string}`. Update the router's system prompt
  step 5 to pass both fields to `propose_artifact`.

**Verify:** rerun `smoketest_client.mjs` (add `interactionMode: "L4"` to its
`propose_artifact` call) and confirm it round-trips into `get_artifact_history`.

### 2.3 Region context

New file `Server/memory/region_store.js`:

```js
"use strict";

// Static named-region lookup - "locomotion updates region context" per the paper.
// Discrete: fires on region entry/exit, not continuous per-frame proximity (that's
// what the existing "proximity" sensor type already covers).
const REGION_RULES = [
    // { regionId, anchorObjectTag } - extend as real scenes define more regions.
];

class RegionStore {
    constructor() {
        this.currentRegionBySession = new Map(); // sessionId -> { regionId, enteredAt }
        this.history = []; // bounded ring buffer of transitions
    }

    ingestLocomotionEvent(event) {
        // event.value: { regionId, entering: boolean }
        if (!event.value || !event.value.regionId) return;
        const sessionId = event.sourceObjectId || "default";
        if (event.value.entering) {
            this.currentRegionBySession.set(sessionId, { regionId: event.value.regionId, enteredAt: event.timestamp });
        } else if (this.currentRegionBySession.get(sessionId)?.regionId === event.value.regionId) {
            this.currentRegionBySession.delete(sessionId);
        }
        this.history.push({ ...event, sessionId });
        if (this.history.length > 200) this.history.shift();
    }

    getCurrentRegion(sessionId = "default") {
        return this.currentRegionBySession.get(sessionId) || null;
    }
}

module.exports = { RegionStore, REGION_RULES };
```

- `sensor_registry.js`: add `"locomotion"` to `KNOWN_SENSOR_TYPES`; constructor takes
  an optional `regionStore`; in `_ingestOne`, route `sensorType === "locomotion"`
  events to `this.regionStore.ingestLocomotionEvent(event)` instead of the existing
  relation-derivation path (locomotion events describe region membership, not an
  object-to-object relation).
- `memory/index.js`: instantiate `RegionStore`, pass it into `SensorRegistry`, expose
  as `this.region`.
- `server.js`: add one more extra tool (like `get_timeline_metrics`, clearly labeled
  as not one of the paper's 8 named operations): `get_region_context` →
  `memory.region.getCurrentRegion(sessionId)`.
- `mock_unity_peer.js`: occasionally emit a synthetic `locomotion` sensor event
  (e.g. entering `"workshop-entrance"`) alongside the existing `proximity`/`gaze`
  events in its `SceneDelta` reply, so this is testable immediately.

**Verify:** `smoketest_client.mjs` calls `get_region_context` after `query_scene` and
gets back the synthetic region.

### 2.4 `person_policy.js` actually mutates from session events

Per the paper: "confirmations, rejections, undo events, and agent results update
temporal *and* person memory" — today only `artifact_log.js` (temporal) is updated;
`person_policy.js` never changes.

- `person_policy.js`: extend the policy record with `priorDecisions: []`; add
  `recordEvent({ sessionId, eventType, targetObjectId, at })` that pushes to it
  (bounded, e.g. keep last 50).
- `server.js`: in `propose_artifact`, `simulate_artifact`, and `commit_memory_event`
  handlers, after the existing `artifactLog.append(...)` call, also call
  `memory.personPolicy.recordEvent({ sessionId, eventType: <derived>, targetObjectId,
  at: Date.now() })` — every one of these calls updates *both* stores, not
  conditionally, matching the paper's "and" (not "or").

**Verify:** `get_person_policy` after a `propose_artifact` call shows a non-empty
`priorDecisions` array.

### 2.5 `intent_store.js` (scoped per §1.3)

New file `Server/memory/intent_store.js`: a small ring-buffer store,
`record({ sessionId, text, correlationId })` / `recent({ sessionId, limit })`, last
20 entries per session. Wire into `memory/index.js` as `this.intent`.
`orchestrator/app.js` calls `commit_memory_event` (or a direct future
`record_intent` tool, your call at implementation time) with the CLI-provided intent
string at the start of a run, tagged `eventType: "speech_intent"` — and the
router's narration should say plainly that this stands in for real speech until the
STT bridge exists, not claim it *is* speech input.

### 2.6 Timeline instrumentation for memory tools and `simulate_artifact`

Two distinct fixes, don't conflate them:

- **Memory tools** (`query_visual_memory` etc.): these are synchronous in-process
  calls with near-zero real latency by construction — instrumenting a "duration" for
  them would be meaningless. What's actually useful is marking *when* each retrieval
  happens within a turn's timeline, so a trace shows the deliberation sequence. Add
  one `memory.timeline.mark(correlationId, "deliberation", "memory_retrieval:" +
  toolName)` call per tool handler in `server.js` (only when the caller passes a
  `correlationId` — make it an optional param on each of the 8 memory tool schemas,
  same as `query_scene` already has).
- **`simulate_artifact`**: currently indistinguishable from a real commit in
  `get_timeline_metrics` because both resolve as envelope `type: "ArtifactResult"`.
  After `bridge.proposeArtifact({ ..., simulate: true })` resolves in `server.js`,
  check `result.payload.status === "simulated"` and mark a distinct label
  (`memory.timeline.mark(result.correlationId, "experimental", "SimulateArtifact",
  result.timestamp)`) so `synchronicity()` can report sandbox validation time
  separately from `timeToValidatedExecutionMs`. This also finally gives the
  `experimental` timeline lane real data instead of staying empty.

**Verify:** rerun the `get_timeline_metrics` check from the earlier smoketest; the
`simulate_artifact` correlationId's trace now includes an `experimental`-lane event.

### 2.7 Stale-proposal rejection (most invasive — do last, most carefully)

Server-side half only (Unity doesn't exist yet, so "current selection" means
"server's last-known focus for a session," not literally what's in the headset):

- `scene_bridge_client.js` or `memory/index.js`: track
  `sessionFocus: Map<sessionId, { objectId, at }>`, updated whenever a `SceneQuery`
  is sent or a `SceneDelta` with a `payload.focus.id` is received for a given
  `sessionId`.
- On `ArtifactResult` inbound: before resolving the pending promise, compare
  `envelope.targetObjectId` against `sessionFocus.get(envelope.sessionId)?.objectId`.
  If they differ, still resolve the promise (the caller asked for this specific
  object, that hasn't changed), but additionally tag it and log a distinct
  `commit_memory_event` with `eventType: "stale_proposal"` so RQ4's "stale
  applications" measure has something to count.
- **Precondition, not optional:** this only works if callers pass `sessionId`
  consistently. Today `smoketest_client.mjs` and ad-hoc tool calls often omit it. Fix
  the test client and document in `Server/orchestrator/README.md` that `sessionId`
  is now a required convention for any multi-call authoring flow, not just a nice-to-have.

**Verify:** in a smoketest, call `query_scene` for object A, then `query_scene` for
object B (same `sessionId`), then `propose_artifact` targeting object A with the
same `sessionId` — confirm a `stale_proposal` event is logged.

### 2.8 (Stretch, lower priority) RoslynCSharp capability enforcement

Already logged in `docs/progress-log.md` as a known gap; restated here because it's
what the paper's artifact lifecycle ("checked against capability policy") describes
as already happening. Not required to align with the *timelines/modes* concepts this
prompt is scoped around — pick up separately.

## 3. Explicit non-goals for this pass

- No Unity/C# changes — everything above is server-side, tested against
  `mock_unity_peer.js`, same as all prior work.
- No real speech-to-text wiring into the orchestrator (§1.3).
- No change to the existing 3-value `authoringMode` routing semantics — only additive.
- Not running the orchestrator with a real `ANTHROPIC_API_KEY` — still your manual
  step per `Server/orchestrator/README.md`.

## 4. After this pass

Update `docs/progress-log.md` with what actually got built vs. what's still open
(some of §2.1–2.8 may reasonably be deferred), update the status table row for
"Timelines & perceived synchronicity" and add rows for region context / person
policy mutation / stale-proposal rejection, and mark
`docs/paper-sync-timelines-and-modes.md` §3's items as done/partial/not-done rather
than leaving them all as "planned."
