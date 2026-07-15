# Paper Sync: Two Timelines, Perceived Synchronicity, Shared XR Memory, Interaction Modes

**Status (2026-07-12): the gaps in §2 and the plan in §3 were executed** via
`docs/next-build-prompt.md` (see `docs/progress-log.md` for what was built and
verified). Only §3's P5/P8-equivalent item (RoslynCSharp capability enforcement)
remains open. This document's analysis below is kept as the historical record of what
the gaps were and why each decision was made.

## 0. Why this doc exists

The paper (read-only source: `-2027_IEEEVR-AgenticXR/main.tex`) has been substantially
revised since the last sync (`docs/shared-memory-and-experimental-space.md`,
`docs/progress-log.md` 2026-07-12 entries) — new title ("Worlds That Think With Us:
Toward Symbiotic AgenticXR"), rewritten abstract, and renamed core terminology. This
document re-reads the current paper precisely, then plans what needs to change in
`dcvr_agentic` to stay aligned. It is a **plan**, not an implementation — nothing in
§3 has been built yet; this pass is check-and-plan only, per your request.

Also flagged: the drift runs **both directions**. The paper's own Implementation
Status section is now behind the actual code (it undersells what's built), while the
code's terminology and mode taxonomy are behind the paper's current language. §5
covers the paper-side half, which I cannot fix myself (read-only).

## 1. The concept, precisely, as the paper defines it now

### 1.1 Two Timelines and Perceived Synchronicity
(`main.tex`, subsection "Two Timelines and Perceived Synchronicity", part of
`sec:system`)

- **XR Interaction Timeline** — "the user's lived timeline: speech, gaze or ray
  focus, locomotion, selection, manipulation, rendering, preview, confirmation, and
  undo."
- **Agentic Deliberation Timeline** — "the backend's slower and potentially parallel
  timeline: intent interpretation, scene querying, memory retrieval, planning, code
  generation, critique, validation, simulation, repair, routing, and logging."
- **Perceived synchronicity** is the design goal, not a single metric: "the user
  receives immediate acknowledgement and visible agent status while slower agents
  reason over time-stamped shared memory and return proposals that can be previewed,
  confirmed, repaired, rejected, or rolled back." Explicit non-goal: agents are **not**
  forced to be frame-synchronous with XR.

**Naming note:** the paper no longer says "clock" anywhere in this section (it did in
an earlier draft I worked from). `Server/memory/timeline_registry.js` and the "Two
Clocks" Lucid chart both use the old "clock" language — see §3.1.

### 1.2 Temporal Contract
(`main.tex` §Communication Contract) — "The communication protocol is a temporal
contract, not only a serialization format." Correlation IDs bind a request, scene
query, generated artifact, preview, user decision, validation result, and commit-or-
rollback event **across both timelines**. Target object IDs prevent proposals applying
to a no-longer-selected object. Timestamps and memory snapshots support **freshness
checks and stale-result rejection** — this is a specific, testable behavior the paper
now claims, not just a nice-to-have (see §1.7, §3.3).

Channel table (`tab:channels`) purposes are now terser than what I originally ported,
but map 1:1 onto the implemented NetworkIds — no channel numbers changed:

| ID | Direction | Paper's phrasing | Matches implemented type |
|---|---|---|---|
| 98 | Unity→Server | Speech audio | (unchanged, legacy) |
| 94 | Server→Unity | Legacy generated code | `CodeGenerated` (unchanged, legacy) |
| 95 | Unity→Server | Focus-and-halo deltas | `SceneDelta` |
| 96 | Server→Unity | Detail requests | `SceneQuery` |
| 97 | Server→Unity | Agent feedback | `AgentUtterance` |
| 99 | Server→Unity | Validated proposals | `ArtifactProposal` |
| 100 | Unity→Server | Decisions and results | `ArtifactResult` / `UserDecision` |
| 101 | Bidirectional | Presence heartbeat | `AgentPresenceHeartbeat` |

No code change needed here — `protocol.js` already matches.

### 1.3 Shared XR Memory + sensors-as-update-sources
(`main.tex` §Shared XR Memory and Experimental Space, `tab:memory-layers`)

The five layers are unchanged in substance from what's implemented, but the paper's
column headers are now terser (`Layer | Contents | Source | Agent use | Control role`
vs. the fuller headers I originally ported) — cosmetic, no action needed.

**What's new and matters:** the paper now has an explicit sentence defining sensors
as memory update sources, more precisely and more broadly than what
`Server/memory/sensor_registry.js` currently implements:

> "Sensors are treated as memory update sources: speech updates intent, gaze/ray/hand
> input updates focus, locomotion updates region context, object transforms and
> components update visual and script/context memory, scene graph changes update
> semantic relations, and confirmations, rejections, undo events, and agent results
> update temporal and person memory."

Six distinct update paths named here; current code covers roughly two and a half of
them. See §2 gap table and §3.3.

### 1.4 Experimental Space
(`main.tex`, same section, `fig:experimental-space`) — "lets the Agentic Deliberation
Timeline run ahead hypothetically by asking *what would happen if this artifact ran?*
before Unity changes the live XR Interaction Timeline." The eight named operations are
**unchanged** from what I already implemented — `query_visual_memory`,
`query_scene_graph`, `query_affordances`, `get_script_context`,
`get_artifact_history`, `get_person_policy`, `simulate_artifact`,
`commit_memory_event`. No renaming needed here; naming discipline held.

### 1.5 Artifact Lifecycle
(`fig:artifact`) — Intent → Draft Artifact → Validate → Dry Run → Route
(`apply | clarify | confirm | repair | reject`) → Commit/Rollback, with a repair loop
from Dry Run back to Draft. Matches the existing pipeline description in
`docs/agentic-xr-architecture.md` §4 closely enough — no action needed.

### 1.6 Five Interaction Modes — the paper's central autonomy framework
(`sec:modes`, `tab:modes`) — this is now a fully formalized taxonomy, not a loose
sketch, and it is **not yet reflected in the code's `authoringMode` enum**, which only
has three values (`automatic`, `semi_auto_confirm`, `semi_auto_steer`).

| Mode | Trigger | Agent role | User control | Gate |
|---|---|---|---|---|
| L1 Proactive | Low-risk opportunity | Apply affordance | Status, reject, undo | Reversible |
| L2 Context | Motion, gaze, proximity | Activate guidance | Ignore, redirect, undo | Local |
| L3 Clarify | Missing detail | Ask, then continue | Answer, retarget, cancel | Detail |
| L4 Confirm | Persistent/shared effect | Preview proposal | Accept, reject, revise | Consent |
| L5 Converse | User asks function | Plan, revise, execute | Speech and approval | Consent |

The paper distinguishes **implicit context-driven authoring** (L1, L2 — no explicit
command, no in-the-moment consent) from **explicit authoring** (L3, L4, L5 — user
asked, confirmed, or is iterating). That implicit/explicit split is a load-bearing
distinction for the study design (RQ1, RQ2) and isn't represented in the code's
`authoringMode` field at all right now.

### 1.7 Study measures that are now code-relevant
(`sec:study`, Measures) — several named measures aren't separately instrumented yet:
**memory retrieval latency** and **sandbox validation time** are named alongside the
already-instrumented immediate/validated-execution latency, but
`Server/memory/timeline_registry.js` only observes envelopes that cross the Ubiq
bridge — the eight memory tools (`query_visual_memory` etc.) are pure in-process calls
and never touch the bridge, so they currently generate **zero** timeline data. Also
new: **stale proposal events** and **temporal intelligibility** (whether the user
understands what's happening across both timelines) as named constructs — the former
needs actual staleness-rejection logic (§1.2) to have anything to measure; the latter
is a study/UX construct, not a code gap.

## 2. Gap analysis: paper text vs. actual code

| Paper concept | Current code state | Gap |
|---|---|---|
| XR Interaction Timeline / Agentic Deliberation Timeline naming | `timeline_registry.js` uses `xr`/`deliberation`/`experimental` (fine as internal ids) but comments/docs say "clock" | Cosmetic rename in comments + the "Two Clocks" Lucid chart |
| Stale-result rejection via timestamps/memory snapshots | Only orphaned-reply drop (no waiter) + a fixed timeout; no check against current selection/session freshness | Real gap — needs new logic |
| Sensors: speech→intent | Not modeled — no "intent" memory concept exists | New |
| Sensors: gaze/ray/hand→focus | Partially covered (`gaze`, `handTracking` sensor types feed relations) but no distinct "focus" memory concept | Partial |
| Sensors: locomotion→region context | Not modeled — no "region" concept anywhere | New |
| Sensors: transforms/components→visual+script/context | Covered (SceneDelta already feeds both via `visual_store`/`get_script_context`) | None |
| Sensors: scene graph changes→semantic relations | Covered, though naive (halo-membership + sensor events only) | None (already documented as naive) |
| Sensors: confirmations/rejections/undo/results→temporal+person memory | `propose_artifact` auto-logs to `artifact_log` (temporal); `person_policy` **never** updates from any event | Partial — person side missing entirely |
| L1–L5 interaction-mode taxonomy | `authoringMode` has 3 values, no L1–L5 mapping, no implicit/explicit split | Real gap — needs a decision, see §4 |
| Memory retrieval latency (study measure) | Not instrumented — memory tools bypass the bridge's envelope stream entirely | Real gap |
| Sandbox validation time (study measure) | `simulate_artifact` timing is implicitly in `get_timeline_metrics` today only because it happens to route through the bridge, but isn't labeled/reported as its own measure | Partial |
| 8 Experimental Space operation names | Implemented verbatim | None |
| Channel scheme / envelope fields | Implemented verbatim | None |
| Artifact lifecycle stages | Implemented (validator subagent + propose/simulate) | None structurally; RoslynCSharp capability enforcement still not wired (already known gap, `docs/progress-log.md`) |

## 3. Planned code modifications, prioritized

None of this is implemented yet — flagging what I'd do and why, in order.

### P0 — Terminology consistency (low risk, do first)
Rename in comments/docs only, no behavior change:
- `Server/memory/timeline_registry.js` header comment: "clock" → "XR Interaction
  Timeline" / "Agentic Deliberation Timeline", keep the short internal ids (`xr`,
  `deliberation`, `experimental`) as-is since they're just map keys.
- Rebuild or relabel the "Two Clocks" Lucid chart
  (https://lucid.app/lucidchart/a8ff07bd-4189-4795-bb51-5286db5bab64/edit) title/lane
  labels to match §1.1's exact terms.
- `docs/agent-framework-and-communication.md` and `docs/agentic-xr-architecture.md`
  §2.4 ("Two Clocks") — same rename.

### P1 — Stale-proposal rejection (real logic, medium effort)
Add a freshness check in `scene_bridge_client.js`'s `#handleInbound`: when an
`ArtifactResult` resolves, compare its `timestamp` (or the originating proposal's) to
the *current* known selection/session state for that `targetObjectId` (would need a
"current focus" concept — see P3) and mark/reject results that arrive after the user
has moved on. This is the concrete mechanism behind the paper's "stale-result
rejection" and RQ4's "stale applications" measure — currently there is nothing to
measure here.

### P2 — L1–L5 interaction-mode alignment (needs your decision first, see §4)
Two ways to do this, not deciding without you:
- (a) Replace `authoringMode`'s 3 values with the 5 paper modes directly in
  `protocol.js`/`scene_bridge_client.js`/`server.js` — cleaner mapping to the paper,
  but is a breaking change to the existing enum and to `orchestrator/app.js`'s
  `validator_critic` prompt (which currently only reasons about "automatic" vs.
  "semi_auto_confirm").
- (b) Keep `authoringMode` as the coarse routing signal (already correctly maps to
  the paper's "Gate" column: automatic↔Reversible, semi_auto_confirm↔Consent/Detail)
  and add a separate `interactionMode` field (`L1`..`L5`) purely for study
  logging/analysis, decoupled from the runtime routing decision. Lower risk, keeps
  the existing routing logic stable.

### P3 — Sensor scope expansion (`sensor_registry.js`)
Add the missing update paths named in §1.3:
- `speech` sensor type → a new lightweight "intent" memory concept (could live in
  `visual_store` or a new `intent_store.js` — small enough to fold into an existing
  store rather than add a sixth top-level layer, since the paper's layer table still
  only names five).
- `locomotion` sensor type → "region context" — needs a notion of named/inferred
  regions, which doesn't exist anywhere yet; smallest version is a static
  region-bounding-box lookup similar to the existing affordance rules.
- Wire `commit_memory_event` (or a new explicit `confirmation`/`rejection`/`undo`
  event type) to also update `person_policy.js`, not just `artifact_log.js` — right
  now `person_policy` is 100% static and never changes regardless of what happens in
  a session, which contradicts the paper's own sentence.

### P4 — Timeline instrumentation for memory tools and simulate_artifact
Add explicit `memory.timeline.mark(correlationId, "deliberation", "memory_retrieval:" + toolName)`
calls at the start/end of each of the 8 memory-tool handlers in `server.js` (they
don't need Ubiq envelopes for this — `mark()` doesn't require one). Add a distinct
label (not just reusing `"ArtifactResult"`) for `simulate_artifact` completions so
`get_timeline_metrics` can report "sandbox validation time" as its own number instead
of conflating it with real commit latency.

### P5 — RoslynCSharp capability enforcement wiring
Already a known, previously-logged gap (`docs/progress-log.md`), restated here
because §1.5's artifact lifecycle ("checked against capability policy") is exactly
what's missing: `get_script_context`'s `capabilityPolicy` is informational only today,
not enforced anywhere in the pipeline.

## 4. What I'm not deciding for you

- **P2's (a) vs (b)** — whether `authoringMode` becomes the 5 paper modes directly, or
  a separate `interactionMode` field sits alongside the existing 3-value routing
  signal. This changes both the runtime behavior and the orchestrator's prompts;
  it's a real design choice, not a mechanical rename.
- **P3's "region context"** — how coarse a first version should be (static bounding
  boxes vs. anything smarter) and whether it deserves its own memory layer name in
  code even though the paper's table still lists only five layers.
- Whether to spend effort on P1 (staleness) before or after P2 (modes) — P1 is more
  clearly load-bearing for the paper's RQ4, but P2 is more visible/central to the
  paper's whole framing (the L1–L5 table is arguably the paper's main deliverable).

## 5. Paper-side item (flagging, cannot fix — read-only)

`main.tex` §Implementation Status (`sec:implementation`) currently **understates**
what's built: it says the bridge is "intentionally transport-only: it does not call
an LLM, decide autonomy, store Shared XR Memory, or run Experimental Space
validation," and lists only four MCP tools (`query_scene`, `propose_artifact`,
`get_artifact_status`, `get_bridge_status`). As of the 2026-07-12 entries in
`docs/progress-log.md`, this is no longer accurate: `Server/memory/*` implements
Shared XR Memory, `Server/orchestrator/app.js` calls an LLM and decides autonomy via
five subagents, and thirteen MCP tools exist (tested end-to-end, though not yet
against a real Unity client or a real `ANTHROPIC_API_KEY` run). The facts needed to
update that section are already written up in `docs/progress-log.md`'s 2026-07-12
entries and this document's §2 — should be straightforward to paraphrase into paper
prose whenever you're ready to edit `main.tex` yourself.
