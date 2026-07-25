# Progress Log — DreamCodeVR Agentic XR

**Purpose of this file:** a single chronological, evidence-backed record of what has
been decided, built, and verified in the agentic evolution of DreamCodeVR — written so
another agent (or collaborator) can pick up this project cold, and so the eventual
IEEE VR paper has a factual record to draw on rather than reconstructed memory. Every
claim below is either a link to a committed file, a diagram, or a command that was
actually run (not just planned). Where something is still just a design decision and
not implemented, it's labeled as such explicitly — don't cite unimplemented items as
"done" in the paper.

## How to read this repo (for a new agent/collaborator)

1. `docs/agentic-xr-architecture.md` — full system design: agent roles, the focus+halo
   scene protocol, the artifact validation pipeline, authoring modes, security
   posture, phased rollout.
2. `docs/agent-framework-and-communication.md` — the orchestration framework decision
   (Claude Agent SDK) and its rationale, plus diagram links.
3. `Server/mcp/unity_scene_bridge/README.md` — the one component that is actually
   built and tested; implementation-level detail.
4. This file — chronological status, and the IEEE VR paper-prep section at the bottom.

## Timeline

**2026-07-11 — Baseline audit and design.**
Read the existing DreamCodeVR pipeline (Unity + Ubiq + Ubiq-Genie + RoslynCSharp +
single-shot OpenAI `gpt-5.5` codegen) end to end: `Server/samples/apps/code_runtime_generator/app.js`,
`Unity/Assets/CodeGenerationManager.cs`, `Unity/Assets/Scenes/Scripts/TestRoslyn.cs`,
`Unity/Assets/SceneController.cs` (an existing but *unused* scene-graph model — later
became the intended extension point for the new scene protocol). Authored
`docs/agentic-xr-architecture.md`, proposing: an Embodied Coordinator agent (real-time,
in the Ubiq room) plus a backend agent pool (Scene Analyst, Code Generator,
Validator/Critic, Version/Memory, Conflict Resolver); a token-bounded "focus + halo"
scene-state protocol; a 6-stage artifact pipeline (static validation → semantic
validation → sandbox dry-run → risk-scored routing → commit → rollback); three
authoring modes (automatic / semi-automatic-confirm / semi-automatic-steer, plus a
manual escape hatch); a security posture built on the existing
`RoslynCSharpSecurityAllowance`; and a phased rollout mapped to specific files.

**2026-07-11 — Framework decision.**
Authored `docs/agent-framework-and-communication.md`: chose the **Claude Agent SDK**
over OpenAI's newer agent-builder tooling for backend orchestration, reasoning
(1) MCP-native — the same connector mechanism used for diagram generation can expose
the XR scene itself as a tool, (2) the orchestrator/subagent/critic pattern is
production-proven (it's the architecture behind Claude Code), (3) recency risk on the
OpenAI side. Decision explicitly does **not** require migrating the existing OpenAI
`gpt-5.5` call — it can remain as the Code Generator subagent's underlying model.
Produced two Lucid diagrams (architecture/topology; sequence diagram for the
confirm-mode authoring flow).

**2026-07-11/12 — Unity Scene Bridge MCP server: built and tested.**
Scaffolded `Server/mcp/unity_scene_bridge/` — `protocol.js` (channel constants +
envelope schema), `scene_bridge_client.js` (joins the Ubiq room, promise/correlationId
request-reply), `server.js` (MCP entrypoint over stdio, 4 tools:
`query_scene`, `propose_artifact`, `get_artifact_status`, `get_bridge_status`),
`mock_unity_peer.js` (stand-in for Unity, since the C# side isn't built yet), and a
README. Added `@modelcontextprotocol/sdk` (v1.29.0) and `zod` to `Server/package.json`,
ran `npm install`. Defined the NetworkId channel scheme: 95 `SceneDelta`, 96
`SceneQuery`, 97 `AgentUtterance`, 99 `ArtifactProposal`, 100 `ArtifactResult`/
`UserDecision`, 101 `AgentPresenceHeartbeat` (94/98 are the pre-existing legacy
channels, untouched). **Verified, not just written:** started the real Ubiq room
server (`code_runtime_generator/app.js`), started `mock_unity_peer.js` as a second
peer, then drove the MCP server through the official `@modelcontextprotocol/inspector`
CLI — `tools/list` returned all 4 tools with correct JSON schemas; `query_scene`
round-tripped over live NetworkIds 96→95 with matching `correlationId` and returned the
mock peer's scene payload. Produced a third Lucid diagram: a consolidated reference
chart (transport-layer diagram + a formatted NetworkId channel table + a formatted
envelope-schema table).

**2026-07-12 — Made the connector reachable.**
Registered `unity-scene-bridge` in a project-root `.mcp.json` so any MCP client
(Claude Code, a future Claude Agent SDK orchestrator) can attach to it. Started the
Ubiq room server and `mock_unity_peer.js` as standing background processes and
re-verified via the inspector: `get_bridge_status` → `{"connectedToRoom": true,
"roomGuid": "6765c52b-3ad6-4fb0-9030-2c9a05dc4731", "pendingRequestCount": 0}`.

**2026-07-12 — Shared XR Memory & Experimental Space design ported from the paper.**
Read (read-only) the IEEE VR 2027 paper workspace at `-2027_IEEEVR-AgenticXR` —
`main.tex` already has a fully drafted "Shared XR Memory and Experimental Space"
section (five memory layers: Visual, Semantic, Script/context, Temporal,
Person/multi-user; an Experimental Space check pipeline; eight named MCP-style access
operations) with its own TikZ figures (`fig:shared-memory`, `fig:experimental-space`)
and table (`tab:memory-layers`). Authored
`docs/shared-memory-and-experimental-space.md` to port that design into this repo:
it restates the layers/operations exactly as named in the paper (naming discipline is
explicit — implementation must not paraphrase), clarifies that the Experimental Space
*is* the existing artifact-pipeline stage [4] "sandbox dry-run" from
`docs/agentic-xr-architecture.md` §4 rather than a new stage, and gives a concrete
module-boundary proposal (`Server/memory/*.js` as additional tools on the existing
`unity_scene_bridge` MCP server, not a second server) plus a phased build order. This
is design-only — none of `Server/memory/` exists yet. Produced a fourth Lucid diagram
(memory-layer architecture + Experimental Space pipeline + both reference tables).

**2026-07-12 — Shared XR Memory implemented, and a real agent orchestrator stood up.**
Scoped via explicit clarification first (three concepts the user introduced -
"timelines and perceived synchronicity" and "sensors" - were not yet in the paper or
any design doc, so were pinned down before writing code, not guessed): timelines
generalize the paper's existing "Two Clocks" model into named lanes (`xr`,
`deliberation`, `experimental`); sensors are Unity-side scene components (proximity,
gaze, collision, hand-tracking) that feed Shared XR Memory, not new hardware. Scope for
this pass: server-side only (memory stores + a real orchestrator process), no
Unity/C# changes.

Built `Server/memory/` (`visual_store.js`, `scene_graph_store.js`, `artifact_log.js`
— JSON-lines append log, `person_policy.js` — static single-owner stub,
`timeline_registry.js`, `sensor_registry.js`, `index.js` aggregator), wired
automatically into `scene_bridge_client.js`'s envelope stream (now emitting outbound
envelopes too, and threading an optional shared `correlationId` through
`querySceneFocus`/`proposeArtifact` so a whole authoring turn can share one timeline).
Extended `Server/mcp/unity_scene_bridge/server.js` with eight Shared XR Memory tools
(`query_visual_memory`, `query_scene_graph`, `query_affordances`, `get_script_context`,
`get_artifact_history`, `get_person_policy`, `simulate_artifact`,
`commit_memory_event` — names matching the paper exactly, per the naming-discipline
rule in `docs/shared-memory-and-experimental-space.md`) plus one extra diagnostic
(`get_timeline_metrics`, not one of the paper's eight, clearly labeled as such).
`simulate_artifact` reuses the existing `ArtifactProposal`/`ArtifactResult`
channels (99/100) distinguished by `payload.mode`, not a new NetworkId. Extended
`mock_unity_peer.js` to emit synthetic sensor events and answer simulate-mode
proposals distinctly from commits.

Built `Server/orchestrator/app.js` — a real Claude Agent SDK (`@anthropic-ai/claude-agent-sdk`
v0.3.207) Task Router with five subagents (`scene_analyst`, `code_generator`,
`validator_critic`, `conflict_resolver`, `version_memory`), each restricted to the
tools it needs and wired to `unity_scene_bridge` as an external stdio MCP server.
Required bumping `zod` to v4 across `Server/package.json` (both SDKs share the peer
range `^3.25 || ^4.0`, but only v4 satisfies the Agent SDK's own `^4.0.0` requirement)
— re-verified the existing bridge tools still worked after the bump before proceeding.

**Verified, not just written:** ran a full round trip through a real MCP client
session (`Server/mcp/unity_scene_bridge/smoketest_client.mjs`, new — a standalone
test client kept in the repo) against the live room server + mock peer:
`query_scene` correctly populated `visual_store`/`scene_graph_store` (including
sensor-derived relations like `near`/`observed-by`), `query_affordances` returned
`["visible"]` from the static lookup, `propose_artifact` auto-logged to
`artifact_log.jsonl` and was retrievable via `get_artifact_history`,
`simulate_artifact` got a distinct `"simulated"` status back from the mock peer
using a fresh correlationId, and `get_timeline_metrics` correctly computed
`timeToValidatedExecutionMs: 1536` matching the mock peer's confirm-mode delay
(`timeToVisibleResponseMs` was `null`, correctly, since nothing sent an
`AgentUtterance` in this test — that only happens inside a real Coordinator/orchestrator
flow). Also verified the orchestrator's failure mode without `ANTHROPIC_API_KEY`:
clean, actionable error message, exit code 1, no raw SDK stack trace. Did not exercise
the orchestrator with a real API key (none available in this environment) — that
verification is documented as the user's next manual step in
`Server/orchestrator/README.md`, not claimed as done here.

**2026-07-12 — Paper re-sync: terminology and taxonomy have moved on.**
Re-read the full paper (read-only) and found it had been substantially revised since
the last sync — new title ("Worlds That Think With Us: Toward Symbiotic AgenticXR"),
rewritten abstract, and renamed core terms: "Two Clocks" is now "Two Timelines and
Perceived Synchronicity" (`XR Interaction Timeline` / `Agentic Deliberation
Timeline`), the sensor definition is now far more precise (six named update paths:
speech→intent, gaze/ray/hand→focus, locomotion→region context, transforms/components→
visual+script/context, scene graph changes→semantic, confirmations/rejections/undo/
results→temporal+person), and the five interaction modes (L1-L5) are now a fully
formalized table (`tab:modes`: Trigger/Agent role/User control/Gate) rather than a
loose sketch. Authored `docs/paper-sync-timelines-and-modes.md`: a precise
concept restatement plus a gap analysis and a prioritized (not yet implemented) code
modification plan - stale-proposal rejection logic, L1-L5 alignment for
`authoringMode` (two options, needs a decision - not made unilaterally), sensor scope
expansion (speech→intent and locomotion→region context are entirely unmodeled today),
`person_policy.js` never actually updates from session events despite the paper now
saying it should, and timeline instrumentation gaps for memory-tool calls and
`simulate_artifact` specifically. Also flagged the reverse drift: the paper's own
Implementation Status section (`sec:implementation`) now **understates** what's
built - it still describes the bridge as not storing Shared XR Memory and lists only
4 MCP tools, both of which were true before the 2026-07-12 memory/orchestrator work
above but are no longer accurate. This is a planning pass only - no code changed.

**2026-07-12 — Executed `docs/next-build-prompt.md`: all 7 items built and verified.**
Terminology renamed to match the paper throughout (`timeline_registry.js`,
`agentic-xr-architecture.md`; redeployed the timeline chart as
https://lucid.app/lucidchart/09ee5cdd-7f30-42c7-8a7c-1615e0ca6412/edit, since the
Lucid connector has no in-place replace and a new document was required — old one
kept for history). Added `interactionMode` (L1-L5) as a field alongside
`authoringMode` throughout the envelope/tools/orchestrator, per the prompt's decision
to keep them separate (who-initiates vs. who-controls-execution). Built
`Server/memory/region_store.js` (discrete `locomotion` sensor type, distinct from
continuous `proximity`) and a `get_region_context` tool. Extended `person_policy.js`
with `recordEvent()`/`priorDecisions`, wired into `propose_artifact`,
`simulate_artifact`, and `commit_memory_event`. Built `Server/memory/intent_store.js`
plus a `record_intent` tool, scoped down exactly as planned — the orchestrator records
its own CLI intent string as a stand-in, explicitly not claiming real speech capture.
Instrumented the 6 remaining memory tools with optional `correlationId` marking
(`memory_retrieval:<toolName>` on the `deliberation` timeline), and gave
`simulate_artifact` its own distinct `experimental`-timeline event
(`SimulateArtifact`) instead of being indistinguishable from a real commit. Built
stale-proposal rejection: `scene_bridge_client.js` now tracks a per-session
`sessionFocus` map (updated on every `query_scene` call with a `sessionId`), tags
every `ArtifactResult` with a `staleness` object before resolving, and emits a
`stale_proposal` event that `server.js` logs to both the artifact log and person
policy. Documented `sessionId` as a required convention (not schema-enforced) in
`Server/orchestrator/README.md`.

**Verified, not just written:** rewrote `smoketest_client.mjs` to exercise every new
behavior in one session against the live room server + mock peer (which now also
emits a synthetic `locomotion` sensor event). Confirmed: `interactionMode: "L4"`
round-trips into `get_artifact_history`; `get_region_context` correctly reports
`workshop-entrance`; `get_person_policy` shows a non-empty `priorDecisions` after a
`propose_artifact` call; `get_timeline_metrics` shows interleaved
`memory_retrieval:*` events on the `deliberation` lane and a distinct `experimental`
lane event (`SimulateArtifact`, 292ms) separate from the real commit
(`ArtifactResult`, 1516ms) for a different correlationId; and the stale-proposal test
(query object A, query object B same session, propose for A) correctly returned
`staleness: { isStale: true, focusObjectIdAtArrival: "obj-stale-b" }` and logged a
distinct `stale_proposal` entry alongside the normal `propose_artifact` entry in
`get_artifact_history`. Hit one real bug during testing (unrelated to the new code):
two leftover Node processes from an earlier session were still holding ports
3000/8009/8010, causing a confusing `EADDRINUSE` crash on the first restart attempt —
resolved by explicitly enumerating and stopping them before retrying; not a defect in
this pass's code.

**2026-07-15 — Cache Exchange Layer implemented (backend) and scaffolded (Unity).**
Checked the paper fresh again before building - found it had moved further since the
2026-07-12 sync: "Experimental Space" is now "Verification Space" throughout, and a
new "Cache Exchange Layer" subsection exists with an exact channel/envelope-field
table matching the request's spec almost verbatim (confirms `rag/prompts/cache_exchange_agenticxr_prompt.md`
as the source prompt). Grounded the whole implementation in that table rather than
inventing channel numbers - every new message type fits onto the 6 channels the
bridge already owns (95-97, 99-101), no new NetworkIds allocated. One documented,
deliberate deviation from the paper's compressed table: `DeltaAck`/`DeltaNack` sent
Server→Unity on 101, not Unity→Server on 100 as the table's shorthand suggests,
because the receiver of a `SceneDelta` is the backend - see
`docs/cache-exchange-layer.md` for the full rationale.

Built `Server/cache/` (`protocol.js`, `event_journal.js`, `agent_working_cache.js`,
`cache_reconciler.js`, `proposal_gate.js`, `index.js`) and wired it into
`scene_bridge_client.js` (new outbound methods, channel 101's listener now routes
everything through `#handleInbound` instead of only heartbeats, `#awaitReply` accepts
an array of acceptable reply types for the two-outcome `CommitRequest`) and
`server.js` (8 new MCP tools). Found and fixed a real correctness bug before it
shipped: an initial high-water-mark-only duplicate check would have wrongly discarded
a legitimately-recovered backfilled delta once a later live delta had already
advanced past it - fixed with a per-session `seenSet` plus a monotonic-safety-net
merge in `agent_working_cache.js` so a late backfill can't regress newer state either.

**Verified, not just written**: extended `mock_unity_peer.js` to simulate Unity's
authoritative side (snapshot, four deltas with `deltaSeq` 3 deliberately dropped on
the wire but kept in its own history, and its own freshness check on
`CommitRequest`/`RollbackRequest`), and ran the full flow end to end
(`Server/mcp/unity_scene_bridge/cache_test_flow.mjs`, new). Confirmed: live deltas 1
and 2 accepted in real time; delta 4's arrival correctly detected the gap
(`fromSeq:3,toSeq:3`) and the reconciler *automatically* requested backfill without
any manual trigger; the recovered delta 3 was accepted while delta 4 (already seen)
was correctly ignored as a duplicate within the same backfill batch; final state
correctly reflects revision 5 (the newer live delta), not regressed by the
later-arriving older backfilled one; a second, fully manual re-backfill from
`lastSeenSeq=0` showed all four as duplicates (idempotence proven); a commit against
the current revision was accepted by the mock peer's own authoritative check; a
commit against a deliberately stale revision was rejected by the *backend's*
pre-flight `ProposalGate` before ever reaching Unity; a commit with a stale
`snapshotTakenAt` under `automatic` mode's tight budget was separately rejected for
that reason. Hit and fixed one process-hygiene issue along the way, unrelated to the
cache logic: two `node app.js` room-server processes (one three days old) were never
caught by earlier cleanup passes because their WMI command line was the bare relative
invocation `node app.js` with no `dcvr_agentic` substring in it, which every prior
`-match 'dcvr_agentic'` cleanup filter silently missed - found instead by checking
which PID actually owned port 3000. Worth remembering for future cleanups: filter by
port ownership when a command-line substring match comes up empty but the port is
still busy.

Scaffolded the Unity C# side (`Unity/Assets/AgenticCache/`: `CacheEnvelope.cs`,
`LocalXRCache.cs`, `CachePublisher.cs`, `CacheChannelRelay.cs`,
`CacheExchangeManager.cs` with all 10 required handlers, including the real
compare-and-swap logic in `HandleCommitRequest`) written against the exact patterns
proven in `CodeGenerationManager.cs`/`MicrophoneCapture.cs`/`SelectRay.cs`. **Not
compiled or run** - no Unity Editor available in this environment. Also surfaced (not
fixed) a real, previously-latent issue: Unity's `JsonUtility` cannot deserialize a
nested JSON payload object, which affects the three legacy message types
(`SceneQuery`, `AgentUtterance`, `ArtifactProposal`) whose payloads are not yet
pre-stringified on the Node side - documented in detail, with the fix path, in
`docs/cache-exchange-layer.md`'s "Wire format and the JsonUtility payload problem".

## Current implementation status

| Component | Status | Evidence |
|---|---|---|
| Baseline DreamCodeVR pipeline (speech → single-shot codegen → attach) | Pre-existing, unchanged | `Server/samples/apps/code_runtime_generator/`, `Unity/Assets/CodeGenerationManager.cs` |
| Multi-agent architecture design | Designed, documented | `docs/agentic-xr-architecture.md` |
| Orchestration framework decision | Decided, documented | `docs/agent-framework-and-communication.md` |
| Focus+halo scene protocol | Designed (schema only) | `docs/agentic-xr-architecture.md` §3 |
| Envelope/channel scheme | Designed **and implemented** on the Node side | `Server/mcp/unity_scene_bridge/protocol.js` |
| Unity Scene Bridge MCP server | **Implemented and tested** (against a mock peer) | `Server/mcp/unity_scene_bridge/`, this log's 2026-07-11/12 entries |
| MCP connectivity for a client (Claude Code etc.) | **Wired up** | `.mcp.json` |
| Unity-side channel handlers (`SceneController.cs`, `CodeGenerationManager.cs`) | **Not implemented** | — |
| Stable per-object GUIDs in Unity | **Not implemented** | `SceneController.cs` has an unused scene graph, no GUIDs yet |
| Backend orchestrator (Task Router + 5 subagents: Scene Analyst, Code Generator, Validator/Critic, Conflict Resolver, Version/Memory) | **Implemented, structurally tested** (no real ANTHROPIC_API_KEY run yet) | `Server/orchestrator/app.js` |
| Artifact validation pipeline (static/semantic/sandbox/commit/rollback) | Delegation structure implemented (validator_critic subagent); static-compile/RoslynCSharp enforcement still design only | `Server/orchestrator/app.js`, `docs/agentic-xr-architecture.md` §4 |
| Authoring-mode routing + confirm/steer UX | Routing decision implemented server-side (validator's JSON verdict); Unity-side confirm/ghost-preview UI **not implemented** | `Server/orchestrator/app.js`, `docs/agentic-xr-architecture.md` §5 |
| Version/Memory persistence store | **Implemented and tested** (flat JSON-lines; SQLite migration still open) | `Server/memory/artifact_log.js` |
| Conflict Resolver (multi-user) | Subagent implemented against the static single-owner policy stub; real multi-user policy **not implemented** | `Server/orchestrator/app.js`, `Server/memory/person_policy.js` |
| Shared XR Memory (5 layers, ported from paper) | **Implemented and tested** server-side; Unity-side sensor publishing **not implemented** (mock peer emits synthetic events) | `Server/memory/*.js`, `docs/shared-memory-and-experimental-space.md` |
| Timelines & perceived synchronicity | **Implemented and tested** (`xr`/`deliberation`/`experimental` lanes; `timeToValidatedExecutionMs` verified against mock peer) | `Server/memory/timeline_registry.js` |
| Experimental Space real staging-clone dry-run | `simulate_artifact` tool + channel plumbing implemented; actual Unity clone execution **not implemented** | `Server/mcp/unity_scene_bridge/server.js`, `docs/shared-memory-and-experimental-space.md` §2 |
| `interactionMode` (L1-L5, per paper `tab:modes`) | **Implemented and tested**, additive alongside `authoringMode` | `protocol.js`, `orchestrator/app.js` validator_critic |
| Region context (`locomotion` sensor → named regions) | **Implemented and tested**, static region lookup, empty rule set by default | `Server/memory/region_store.js` |
| Intent memory (`speech → intent`) | Store + tool **implemented and tested**; real speech input **not wired** (CLI stand-in only) | `Server/memory/intent_store.js` |
| Person policy mutation from session events | **Implemented and tested** (`priorDecisions` updates from propose/simulate/commit/stale events) | `Server/memory/person_policy.js` |
| Memory-tool timeline instrumentation | **Implemented and tested** (`memory_retrieval:*` marks; `simulate_artifact` gets its own `experimental`-lane event) | `Server/mcp/unity_scene_bridge/server.js` |
| Stale-proposal rejection | **Implemented and tested** (session-focus tracking + `staleness` tagging + logging); requires callers to pass `sessionId` consistently (convention, not enforced) | `Server/mcp/unity_scene_bridge/scene_bridge_client.js` |
| Cache Exchange Layer - backend (journal, working cache, reconciler, proposal gate) | **Implemented and tested** (gap detection, automatic backfill, idempotent dedup, monotonic safety net, two-stage commit gate) | `Server/cache/*.js` |
| Cache Exchange Layer - Unity (local cache, publisher, exchange manager, all 10 handlers) | **Scaffolded, not compiled or run** - no Unity Editor available | `Unity/Assets/AgenticCache/*.cs` |
| Unity JsonUtility nested-payload parsing (SceneQuery/AgentUtterance/ArtifactProposal) | **Known gap, documented, not fixed** | `docs/cache-exchange-layer.md` |
| Quantitative evaluation / user study | **Not started** | — |

## Explicit gaps (say these plainly in any status update — don't let "designed" read as "done")

- Nothing from a real headset can reach the pipeline yet — the Unity-side channel
  handlers are the hard blocker, independent of everything else. Everything verified
  so far has been against `mock_unity_peer.js`, whose replies are canned, not computed
  from a real scene.
- The orchestrator has not been run with a real `ANTHROPIC_API_KEY` in this
  environment — only its structure (subagent wiring, MCP config, graceful failure
  without a key) has been verified. The actual quality of its scene-grounding,
  code generation, and validation has not been observed.
- `simulate_artifact`'s "Experimental Space" is channel plumbing only — it does not
  yet run real code against a Unity staging clone; the mock peer's dry-run response
  is canned, not computed.
- No safety enforcement (`RoslynCSharpSecurityAllowance`) is wired into the new
  pipeline yet — `get_script_context`'s `capabilityPolicy` is informational only, not
  enforced anywhere.
- `query_scene_graph`'s relations and `query_affordances`' affordances are naive
  (halo-membership + a static lookup table), explicitly not learned semantic
  reasoning — say so if this is described in the paper.
- No formal evaluation data has been collected — the timeline/synchronicity
  instrumentation now exists and works, but no real usage session has generated data
  yet, let alone a user study.

## For the IEEE VR paper

Being direct about one thing first: no amount of documentation gives a submission a
guaranteed acceptance — reviewer variance is real even for strong papers. What *is*
within control is contribution framing, related-work depth, and evaluation rigor, so
that's what this section focuses on.

### Likely contribution framing

DreamCodeVR itself (single-shot speech→LLM→code) is the prior baseline. The novel
contribution being built now is reframing XR live-authoring as a **multi-agent systems
problem**, specifically: (a) a token-bounded, real-time scene-state protocol for
XR↔LLM communication (focus+halo, diff-based), (b) a staged artifact
validation/authoring-mode pipeline that lets the system decide autonomously when to
act vs. when to require confirmation vs. when to accept steering, and (c) an empirical
evaluation of both. A pure architecture proposal without (c) is a weak IEEE VR
technical-papers/TVCG submission — the venue expects either a strong systems
evaluation, a user study, or both.

### Related work to review and cite (not yet done — flagging so it isn't forgotten)

- End-user/live programming in VR/AR and prior DreamCodeVR publications from this group.
- LLM code-generation safety and sandboxing (Copilot/Codex-adjacent execution-safety literature).
- Multi-agent LLM orchestration and tool-use/critic patterns.
- Embodied conversational agents and presence in VR.
- Mixed-initiative interaction — Horvitz's "Principles of Mixed-Initiative User
  Interfaces" is a natural anchor for the automatic/confirm/steer authoring-mode design.

### What reviewers will expect that isn't built yet

- **Quantitative system evaluation**: per-stage latency (STT → scene query → codegen →
  validation → sandbox → commit), and actual measured token counts per turn (the
  "limiting tokens" design goal needs numbers, not just the claim).
- **A user study**: N participants, defined authoring tasks, comparing authoring modes
  and/or against the single-shot baseline. Standard instruments: SUS (usability),
  NASA-TLX (workload), a presence questionnaire (e.g. IPQ), plus qualitative interviews.
- **Correctness/safety metrics**: rejection rate per validation stage, rollback rate,
  false-positive/negative rate of the Validator/Critic agent.
- **Ethics approval** for any user study — flag this early given the UCL affiliation;
  IRB/ethics timelines are often the actual critical path to a submission deadline.
- **Limitations section**: current single-room scope, no multi-headset conflict
  testing yet, LLM cost/latency ceiling, sandbox fidelity vs. real device behavior.

### Data to start logging now, so it exists before the deadline

Every envelope already carries a `correlationId` and `timestamp` (see
`Server/mcp/unity_scene_bridge/protocol.js`) — persist these (append to a file or
SQLite) rather than only logging to console, and this evaluation data accrues for free
as the system is used:

- Full per-`correlationId` pipeline trace (timestamp at each stage).
- Input/output token counts per LLM call, once the Code Generator/Validator agents exist.
- Rejection reason and stage for every artifact that doesn't get committed.

### Suggested paper skeleton

1. Introduction & motivation
2. Related work
3. System design (condense `docs/agentic-xr-architecture.md`)
4. Implementation (condense `Server/mcp/unity_scene_bridge/README.md` + the Unity work,
   once it exists)
5. Evaluation — system benchmarks + user study
6. Discussion & limitations
7. Conclusion & future work

## Pointers

- Design: `docs/agentic-xr-architecture.md`
- Framework decision + diagrams: `docs/agent-framework-and-communication.md`
- Shared XR Memory & Experimental Space (ported from the paper): `docs/shared-memory-and-experimental-space.md`
- Paper re-sync + code modification plan (2026-07-12, not yet implemented): `docs/paper-sync-timelines-and-modes.md`
- Next build prompt (2026-07-12, resolves the two open decisions, ready to execute): `docs/next-build-prompt.md`
- Cache Exchange Layer (2026-07-15, backend implemented+tested, Unity scaffolded): `docs/cache-exchange-layer.md`
- Reference chart (channels + envelope, formatted): https://lucid.app/lucidchart/a069924a-8d7a-4c8c-af2f-197c9c2a4004/edit
- Architecture/topology diagram: https://lucid.app/lucidchart/d726923b-11d8-47c9-a6ba-21215e606157/edit
- Sequence diagram (confirm-mode flow): https://lucid.app/lucidchart/b92b8d55-9c9e-4da9-b22d-9ba2063ad920/edit
- Shared XR Memory & Experimental Space chart: https://lucid.app/lucidchart/2067773e-def4-4940-992c-6f9ea55a59d5/edit
- Two Timelines chart (interaction/deliberation/experimental, with a real measured trace, terminology aligned to the current paper): https://lucid.app/lucidchart/09ee5cdd-7f30-42c7-8a7c-1615e0ca6412/edit — supersedes the earlier "Two Clocks" version (https://lucid.app/lucidchart/a8ff07bd-4189-4795-bb51-5286db5bab64/edit, kept for history only; the Lucid connector has no in-place "replace content" call, only granular edits, so terminology updates create a new document rather than updating the old one)
- Implementation (transport + memory): `Server/mcp/unity_scene_bridge/README.md`
- Implementation (orchestrator, how to test, API keys needed): `Server/orchestrator/README.md`
- MCP client registration: `.mcp.json`
- Paper source (read-only): `-2027_IEEEVR-AgenticXR/main.tex`, §"Shared XR Memory and Experimental Space"

## 2026-07-18 — Live XR ↔ Claude integration pass

Implemented the real Unity side of the AgenticXR contract and the live speech
handoff to the Claude Agent SDK. `AgenticXRBootstrap` installs the runtime without
scene-YAML edits; `AgenticSceneRegistry` assigns deterministic IDs and serializes
focus/halo state; the cache exchange now answers queries/snapshots, emits presence,
stages artifacts on inactive clones, gates confirmation, commits through Roslyn,
reports `ArtifactResult`, and supports generated-component rollback. Microphone
recording-start messages now include the selected stable object ID, and
`start:agenticxr` routes completed STT transcripts to one Claude orchestration turn
per peer while leaving the OpenAI baseline selectable.

Also unified nested-payload wire encoding, removed the committed Azure credential,
made MCP test paths repository-relative, and prevented focus/halo `SceneDelta`
replies from entering the compact cache reconciler.

Verified: Node syntax checks; mode-aware setup doctor; full MCP smoke test (scene,
memory, proposal, simulation, person policy, stale detection); deterministic cache
snapshot/backfill/commit/stale-gate test; and two Unity `6000.3.9f1` batch imports,
the final one exiting 0 with no C# errors. Not yet verified: a real Anthropic turn
(no `ANTHROPIC_API_KEY` was available) or physical Quest microphone/LAN/XR-button
interaction. Human steps are in `docs/live-xr-claude-setup.md`.

## 2026-07-19 — Paper/implementation completion pass

Audited the current paper implementation claims against source and found that the
backend/mock cache path was ahead of the live Unity state/recovery path. Wrote the
executable specification in `docs/paper-implementation-completion-prompt.md`, then
implemented its source-complete portions without modifying the paper repository.

Unity now publishes an initial real-scene `CacheSnapshot`, scans stable objects for
monotonic revision changes, emits state and selected-ray/gaze deltas, emits a coarse
active-scene locomotion/region event, keeps a bounded delta journal, serves real
backfill ranges, consumes delta acknowledgements, and resets its epoch/snapshot state
across active-scene changes. Stable hierarchy IDs include sibling indices and duplicate
IDs are detected and collision-resolved at runtime.

The authoritative proposal path now checks validation state, scene epoch, snapshot ID,
object revision, snapshot age, consent route, automatic-mode risk, current selection,
and local invalidation before attach. `CommitRequest` always returns
`CommitAccepted`/`CommitRejected`; confirmation timeout defaults to rejection. The
world-space panel shows validation evidence, risk, permissions, and expected effects.
Approve/reject/timeout/undo and rollback results are returned to backend temporal and
person memory. Artifact envelopes/logs carry freshness, validation, risk, permission,
side-effect, version, and rollback metadata.

Added `Server/orchestrator/mode_policy.js` so L1/L2 automatic execution is limited to
low-risk, reversible, local proposals with the correct trigger; unresolved L3 cannot
continue; and L4/L5 cannot bypass confirmation. Strengthened runtime compilation from
a small token blacklist to a normalized namespace/capability guard in front of
RoslynCSharp `UseSettings`. This remains defense in depth, not a formal sandbox.

Verification performed:

- `cd Server; npm test` — PASS, 87 assertions. This command also runs `node --check`
  across all custom JS/MJS and deterministically covers wire encoding, gaps, backfill,
  duplicate handling, monotonic state, freshness rejection, mode policy, and required
  Unity contract/security markers.
- `cd Server; npm run test:integration` — PASS. A checked-in harness starts and cleans
  up a real Ubiq room server plus `mock_unity_peer.js`, then runs the full MCP smoke
  and cache flows, including automatic gap recovery, idempotent backfill, fresh commit,
  stale-revision rejection, stale-snapshot rejection, memory, sensors, and staleness.
- Unity `6000.3.9f1 -batchmode -nographics -quit` — exit 0; no C# compiler errors;
  `unity-agenticxr-completion-compile.log` ends with “Exiting batchmode successfully”.

Status boundary: the source is implemented, mock-integrated, and Unity-compiled. A
real Anthropic turn, physical Quest microphone/LAN/XR-button run, real Unity runtime
capture of snapshot/delta/backfill/commit/rollback logs, multi-user conflict behavior,
and the user study remain unverified and must not be described as completed.

## 2026-07-22 — Code acceptance and evaluation hardening

Executed the paper-side code-acceptance prompt against the current implementation.
Added a three-minute authoring-turn watchdog with Unity failure status, bounded
Anthropic retries with exponential backoff and a no-retry-after-mutation guard, and a
coarse Unity generated-behaviour frame/allocation watchdog. Removed the hardcoded STT
server fallback. `sessionId` is now validated by the wire protocol and required by all
bridge tools that thread a session. Confirmed the earlier JsonUtility payload fix and
added explicit regression assertions for the legacy message types.

Added JSONL runtime instrumentation and a provenance-aware technical report exporter
for turn outcomes/latency, staleness, verification/attach timing, cache gaps/backfill,
and model usage. Generated evaluation data is gitignored. Mock child MCP processes now
inherit the evaluation provenance and output path.

Verification performed:

- `cd Server; npm test` — PASS, 103 assertions, including syntax checks.
- `cd Server; npm run test:integration` — PASS with a real local Ubiq room and mock
  Unity peer. The run detected one intentionally dropped delta, accepted its automatic
  backfill, suppressed one duplicate, detected one stale proposal, and completed the
  proposal/simulation/commit/preflight flows.
- Mock report — four turns; validated-result median 309 ms (mean 909.75 ms); 13 cache
  reconciliation events, 12 accepted, one duplicate, one detected gap, five accepted
  backfilled entries. These are mock timings, not live Unity/model/headset evidence.
- Unity `6000.3.9f1 -batchmode -nographics -quit` — successful import/compile; no C#
  errors; log ended `Exiting batchmode successfully now!`.

Current acceptance boundary: source, deterministic tests, mock end-to-end transport,
and Unity compilation pass. No `ANTHROPIC_API_KEY` or `STT_HTTP_URL` was available in
the execution environment, so a live Claude turn, real speech, Unity Play Mode
proposal execution, Quest interaction, and user-study measurements remain pending.

## 2026-07-22 — Five paper-design extensions

Executed `code-implement-paper-design-2026-07-22.md`. The prerequisite paper-
strengthening prompt had not been executed: `main.tex` still described single-
candidate verification, H1--H3 only, and missing person persistence/revocation. The
detailed five-capability engineering prompt was therefore used as the contract.

Extended the existing architecture with: explicit-opt-in pseudonymous cross-session
profiles and reset/retention; best-of-three candidate generation with independent
validation/dry-run eligibility and deterministic risk/preference/context ranking;
typed create/edit/remove lifecycle operations; ordered evolution lineage in the
existing artifact log; inspectable/overridable experience context; and backend plus
Unity checkpoint/resume with explicit orphan/failure events. Learned preferences can
only restrict automatic execution, never loosen L1--L5, Proposal Gate, or Unity gates.

Study sequencing prioritizes multi-candidate selection because it can be measured in
one session as first-proposal acceptance without revision. Cross-session learning
requires longitudinal study; checkpoint/resume is an engineering test; evolution and
experience continuity are better transparency/consistency probes. This scope does not
supersede the still-open live Anthropic, Unity Play Mode, Quest, and real-metrics work.

Verification performed:

- `cd Server; npm test` — PASS, 121 assertions, including JS syntax checks and
  deterministic coverage of lifecycle invariants, candidate eligibility/ranking,
  profile opt-in/persistence/reset, experience override, evolution lineage, and
  checkpoint orphan classification.
- `cd Server; npm run test:integration` — PASS with a real local Ubiq room and mock
  Unity peer. The run independently simulated three candidates, ranked and committed
  the selected candidate, then simulated/committed edit and remove operations. It also
  detected one deliberate cache gap, accepted its backfill, and suppressed duplicates.
- Mock report — 11 turns; validated-result median 314 ms (mean 880.55 ms); three
  generated/eligible candidates; one selected candidate validated and committed
  without revision; 13 cache reconciliation events, one gap, one backfill, and five
  duplicates. These are mock technical timings, not live model/Unity/headset evidence.
- Unity `6000.3.9f1 -batchmode -nographics -quit` — successful compile; Tundra reported
  build success and the log ended `Exiting batchmode successfully now!`; no C# compiler
  errors were found.

Acceptance boundary: the backend behaviors are deterministic-tested, the extended
transport/lifecycle is mock-integrated, and Unity source compiles. Cross-session use by
real participants, a live Claude turn, Unity Play Mode restart/resume, Quest speech and
interaction, and study outcomes remain unverified.

## 2026-07-25 — Objective study logging and export

Executed `code-objective-logging-spec-2026-07-25.md`. The referenced companion
study-coherence audit was not present, so the paper's H1--H4 Measures subsection and
the prompt's explicit objective-measure list were used as the contract.

Extended the existing append-only temporal artifact log with validated pseudonymous
participant/session/trial/condition/task/mode/correlation context and UTC timestamps.
Missing study identifiers fail before write. Null transport metadata cannot erase
trial identifiers. Trial start/end records capture task completion, success, structured
quality-rubric signals, and total task time without adding audio or transcript content
to the objective export.

Wired study events at intent capture, receiver-confirmed status visibility, proposal
and candidate selection, Verification Space results, Proposal Gate checks, Unity
artifact/consent/rollback results, memory reads, stale results, policy/capability
failures, and overlapping-speech interruptions. Added a structured API/CLI for the
repair, clarification, interruption/resumption, grounding, unsafe, and mismatch events
that cannot always be inferred. Baseline generation records intent and generated-code
delivery, but validated baseline application still needs a Unity legacy attach
acknowledgement.

Added the offline CSV exporter:

- `trials.csv`: one analysis-ready row per participant task-trial with 67 stable
  columns spanning performance, separate latency timestamps/deltas, failures,
  verification, grounding, safety, repair/consent, interruption, H4 candidates, and
  status visibility.
- `events.csv`: a whitelisted long-format event table. It excludes intent text,
  generated code, raw audio, free-form summaries, and free-form errors.

Kept JSONL rather than migrating to SQLite because the existing append-only log plus
offline export meets the immediate single-participant trial requirement without a
storage rewrite. SQLite remains warranted if concurrent multi-user writes or
interactive longitudinal querying become study requirements.

Verification performed:

- `cd Server; npm test` — PASS, 245 assertions. Deterministic coverage checks strict
  identifiers, joined trial export, separate acknowledgement/execution timestamps,
  task/rubric outcomes, candidate selection, memory timing, interruption duration,
  all 67 trial columns, all long-format columns, and stable CSV headers.
- `cd Server; npm run test:integration` — PASS with a real local Ubiq room and mock
  Unity peer. The scripted session opened/closed a study trial, exported exactly one
  joined participant/task row, observed receiver-side status visibility, three
  candidates, validation and committed execution, memory timing, and the existing
  lifecycle/staleness flows. The deliberate cache gap was detected, backfilled, and
  duplicate backfill entries were suppressed.
- Direct exporter invocation produced one 67-column trial row and whitelisted
  long-format event rows from the mock session.
- Unity `6000.3.9f1 -batchmode -nographics -quit` — Tundra build success, no C#
  compiler errors, and `Exiting batchmode successfully now!`.

Status boundary: objective logging is source-complete and mock-tested. Unity status,
consent, Verification Space, live execution, mismatch, undo/rollback, and safety
fields are wired but not live/on-device observed. Controller/gaze interruption and a
baseline direct-attach acknowledgement remain partially wired. No study data has been
collected.

## 2026-07-25 — Bounded goal loops and speculative future preparation

Executed `code-loop-goal-2026-07-25.md` and incorporated the requested idle-time
future-goal strategy.

Implemented:

- Persistent goal schema and trigger-driven `trigger -> execute -> verify -> persist`
  controller over the existing temporal `ArtifactLog`.
- Deterministic, rule/constraint, delayed-ground-truth, Validator/Critic, and human
  checkpoint verifiers with code-enforced route-by-verifiability.
- Hard global attempt/wall-time caps, escalation on exhaustion, explicit human
  continuation, and a persistent global kill switch.
- MCP tools for goal creation, advancement, delayed resolution, validator judgment,
  human continuation, state inspection, and kill-switch control.
- Opt-in idle scheduler and future-goal predictor. It prepares at most three
  independently validated/simulated candidates pinned to an exact scene tuple,
  never commits, is preempted by real user work, and requires the complete normal
  pipeline when a prediction is later selected.
- Study export fields for loop iterations, verifier level, escalations, exhaustion,
  delayed resolution latency, idle predictions, and speculative preparation/adoption.
- Architecture/setup/evidence documentation in
  `docs/goal-loops-and-speculative-futures.md`.

Verification:

- `node tests/goal_loop_test.js`: PASS (30 assertions).
- `npm test`: PASS (245 assertions).
- `npm run test:integration`: PASS, including bounded goal creation, a mock Unity
  artifact commit, goal termination, and the 67-column study export.

Evidence boundary:

- Goal logic and bridge flow are deterministic/mock tested, not live Quest validated.
- Idle scheduling is source-complete but has not been run against a real Anthropic
  account or measured for latency/cost.
- The paper describes related speculative-futures and Verification Space concepts,
  but its exact bounded-loop and idle-selection protocol still needs reconciliation
  before these additions are presented as evaluated paper functionality.

## 2026-07-25 — Continuous, human-centered, experience-agnostic alignment

Executed `code-continuous-human-centered-2026-07-25.md` as an assess-and-align task.

Assessment:

- Experience context was already representable as productivity, training,
  entertainment, exploration, or unspecified; it already affected candidate ranking
  and was supplied to generation.
- Unity already continuously published selection/gaze, locomotion, and scene revision
  updates over the existing `SceneDelta` path. Shared XR Memory already normalized
  sensors, region context, scene relations, decisions, and learned preferences.
- L1–L5 mode policy, Proposal Gate, Verification Space, bounded goals, visible status,
  interruption, confirmation, and undo already supplied the human-centered guards.
- The genuine gap was a long-lived thresholded connection from monitored activity to
  a context assistance opportunity outside an explicit speech turn.

New alignment:

- Added `ActivityMonitor`, which combines confidence-weighted observations inside a
  bounded window, applies cooldown, and emits an L2/context opportunity only when the
  configured assist-worthy threshold is crossed.
- Added `continuous_monitor.js`, a long-lived service started by
  `npm run start:agenticxr`. It reuses `SceneBridgeClient` and `SharedMemory`; it does
  not introduce a parallel transport or memory system.
- Monitoring is on by default; proactive Anthropic turns are opt-in. A triggered turn
  surfaces status, preserves L2/context classification, uses the current experience
  mode, and enters the normal orchestrator and consent pipeline.
- Push-to-talk and explicit requests persist a preemption request that terminates a
  running continuous turn. Continuous assistance is suppressed during study trials
  unless explicitly enabled for that condition.
- Strengthened the generator instruction so experience context changes intended
  behavior rather than acting as an output label. Non-authoring experience modes are
  representable, while Unity behavior authoring remains the only complete action
  harness.
- Added `get_activity_stream` to inspect recent normalized observations.

Evidence boundary:

- Threshold/cooldown, context routing, high-risk/delayed-verification rejection,
  experience-aware ranking, status/preemption source contracts, and syntax are
  deterministic-tested.
- `cd Server; npm test` — PASS, 258 assertions.
- `npm run doctor` — PASS with placeholder credential/endpoint presence, including
  agentic dependencies and matching Ubiq room/port configuration. This does not test
  real credential validity or STT reachability.
- `cd Server; npm run test:integration` — PASS with local Ubiq and mock Unity; the
  long-lived monitor joined the room, crossed the activity threshold, and correctly
  suppressed proactive work in monitor-only mode. The `get_activity_stream` MCP call
  returned normalized proximity, gaze, and locomotion observations before the
  existing validation/commit/cache flows passed.
- Unity `6000.3.9f1 -batchmode -nographics -quit` — exit code 0 and Tundra build
  success; no C# compiler errors were found.
- Proactive assistance has not been run with a live Anthropic account or Quest.
- Entertainment/productivity/training support beyond authoring remains architectural,
  not demonstrated application evidence.
