# Shared XR Memory & Experimental Space — Design and Implementation Prompt

## Implementation status (2026-07-12)

**Implemented and tested:** `Server/memory/*.js` (all five layers plus a timeline
registry - see `docs/progress-log.md`), wired into
`Server/mcp/unity_scene_bridge/server.js` as eight tools plus one bonus diagnostic
(`get_timeline_metrics`), auto-populated from the existing Ubiq envelope stream.
**Not implemented:** anything Unity-side (§4.3), and `simulate_artifact`'s real
staging-clone execution (it currently round-trips through the mock peer's canned
response, not an actual Unity dry-run). The rest of this document is the original
design brief and is still accurate for what's *not yet* built; treat the paragraph
above as the up-to-date status override.

## 0. Source of truth

This design is not invented here — it mirrors what is already written into the paper
(read-only source, not edited from this workspace):
`-2027_IEEEVR-AgenticXR/main.tex`, section **"Shared XR Memory and Experimental
Space"** (`tab:memory-layers`, `fig:shared-memory`, `fig:experimental-space`, plus the
artifact lifecycle `fig:artifact`). This doc exists so the paper's claims and the
actual implementation stay in lockstep: build exactly what's named here, and don't let
the paper describe more than what this repo can back up. If a name or behavior needs
to change during implementation, update `docs/progress-log.md` immediately — that file
is the handoff artifact back to the paper side, since this workspace cannot write into
the paper repo.

**Reference chart** (diagram + both tables below, rendered):
https://lucid.app/lucidchart/2067773e-def4-4940-992c-6f9ea55a59d5/edit

## 1. Design prompt: Shared XR Memory

**Purpose.** Give backend agents a compact, layered, inspectable memory of the live XR
scene — not a raw world dump, and not only the flatter focus+halo model already in
`docs/agentic-xr-architecture.md` §3. Shared XR Memory **extends** focus+halo rather
than replacing it: focus+halo is the *transport* (what Unity pushes over
`SceneDelta`/NetworkId 95), Shared XR Memory is the *organizing model* backend agents
reason over once that data lands server-side.

**Design constraints — preserve these when implementing, they're the paper's stated
claims:**
- Compact, low latency, inspectable, action-oriented — explicitly **not** full world
  reconstruction or photorealistic capture.
- Five layers, each with a distinct update source, retrieval purpose, and
  safety/control role. A validation check should query the one layer relevant to it,
  not the whole memory.
- Access is through eight named operations, implementable as MCP tools **while Unity
  remains the authoritative executor** — this framing (memory is queryable server-side,
  but nothing is actually applied except through Unity) is load-bearing for the
  paper's safety argument; don't let an implementation shortcut give a backend agent
  direct scene-mutation power outside the artifact pipeline.

| Layer | Contents | Update source | Agent retrieval use | Safety/control role |
|---|---|---|---|---|
| Visual | Boxes, coarse occupied volumes, anchors, reachability, labels, confidence | Scene deltas, focus/halo observations | Ground target, check collisions, preview placement | Avoid spatial conflicts and raw-scene overexposure |
| Semantic | Scene graph, object relations, states, affordances | Unity metadata, inferred relations, user corrections | Reason about possible uses beyond proximity | Detect misgrounded or semantically odd actions |
| Script/context | Components, public fields, artifacts, permissions, side effects | Runtime reflection, artifact logs, policies | Predict what generated code can affect | Gate risky APIs, persistence, deletion, forces |
| Temporal | Working, episodic, long-term, procedural memories | Events, confirmations, failures, repairs | Retrieve prior intent, failures, successful patterns | Reject stale work; support repair and rollback |
| Person/multi-user | Roles, preferences, focus, ownership, consent policies | Session state and explicit user choices | Select autonomy level and prompt style | Respect permissions, inspectability, revocation |

**Eight access operations** (exact names — see §1.5 on naming discipline):
`query_visual_memory`, `query_scene_graph`, `query_affordances`, `get_script_context`,
`get_artifact_history`, `get_person_policy`, `simulate_artifact`, `commit_memory_event`.

## 2. Design prompt: Experimental Space

**Purpose.** A low-fidelity staging clone where candidate artifacts are tested against
the memory layers before Unity commits them to the live scene. Not a physics-perfect
simulator — it exists to catch common grounding and runtime failures, produce a
previewable outcome summary, and decide the route: automatic, clarify, confirm,
repair, or reject.

**Check pipeline** (from the paper's `fig:experimental-space`):

```
candidate artifact + memory snapshot
  -> spatial checks (volume, reachability, unintended movement)
  -> semantic/script checks (intent fit, affordances, APIs, side effects)
  -> temporal/person checks (stale state, ownership, consent policy)
  -> route: automatic | clarify | confirm | repair | reject
```

**Relationship to the existing artifact pipeline.** The Experimental Space *is* stage
[4] "sandbox dry-run" from `docs/agentic-xr-architecture.md` §4 — this is the fuller
specification of that stage, not a new one. That doc currently only says dry-run
should "watch for exceptions, NaN/Inf transforms, runaway frame time"; the paper adds
the spatial/semantic/script/temporal/person-policy checks grounded in the memory
layers above. **Action:** treat stage [4] in `agentic-xr-architecture.md` as superseded
by this document rather than duplicating it — the two should not drift.

## 3. Charts

- **This design** (memory-layer architecture + table, Experimental Space pipeline +
  MCP operations table): https://lucid.app/lucidchart/2067773e-def4-4940-992c-6f9ea55a59d5/edit
- Still valid, from earlier work: communication protocol reference chart
  (https://lucid.app/lucidchart/a069924a-8d7a-4c8c-af2f-197c9c2a4004/edit), agent
  topology (https://lucid.app/lucidchart/d726923b-11d8-47c9-a6ba-21215e606157/edit),
  confirm-mode sequence diagram (https://lucid.app/lucidchart/b92b8d55-9c9e-4da9-b22d-9ba2063ad920/edit).

## 4. Implementation prompt: porting the design into dcvr_agentic

Written as direct instructions for whoever implements this next (human or agent).
Assumes the state in `docs/progress-log.md`: the Unity Scene Bridge MCP server
(`Server/mcp/unity_scene_bridge/`) is built and tested against a mock peer; Unity-side
channel handlers are not implemented; no orchestrator agent code exists yet.

### 4.1 Don't build a second system

The Visual layer's raw material already has a transport — `Server/mcp/unity_scene_bridge/`
delivers `SceneDelta`/focus+halo today. The Visual layer is a server-side cache/summary
built on top of what the bridge already receives, not a new channel. The existing
`query_scene` MCP tool is the low-level primitive `query_visual_memory` and
`query_scene_graph` get built on, server-side — don't reimplement scene transport.

### 4.2 Module boundaries (concrete proposal)

Add these as **additional tools on the existing `unity_scene_bridge` MCP server**
(`Server/mcp/unity_scene_bridge/server.js`), not a second MCP server process — this
keeps the "one connector" principle already established rather than fragmenting it:

- `Server/memory/visual_store.js` — cache of last-known focus+halo per object, keyed
  by `targetObjectId`. Backs `query_visual_memory`.
- `Server/memory/scene_graph_store.js` — relations (`on`/`inside`/`near`/`attached-to`/
  `supports`/`reachable-from`/`controlled-by`) derived from Unity hierarchy and tags.
  Backs `query_scene_graph`, `query_affordances`. Start affordances as a static
  tag→affordance lookup (e.g. component `Grabbable` → affordance `usable`), not
  learned reasoning — keep the paper's implementation-status language honest about
  this; it is not semantic inference yet.
- `Server/memory/artifact_log.js` — this **is** the Version/Memory store already
  planned in `docs/agentic-xr-architecture.md` §4.1/§8 phase 5, now scoped concretely
  by `get_artifact_history` and `commit_memory_event`.
- `Server/memory/person_policy.js` — session-scoped roles/permissions/consent. New
  concept, not previously scoped in `agentic-xr-architecture.md`. Start as a static
  per-room single-owner config before any real multi-user policy engine exists; only
  needs to grow once the paper's L4/collaborative study tasks are actually built.

`simulate_artifact` is different from the rest — it needs a real Unity round-trip
(compile + run on a hidden clone), not a server-side store. Concretely: add a
`simulateArtifact()` sibling to `proposeArtifact()` in `scene_bridge_client.js` that
sends on the existing `ARTIFACT_CHANNEL` (99) with `payload.mode: "simulate"` instead
of `"commit"`; Unity's future handler runs it against a staging clone instead of the
live object and replies with an outcome summary rather than a final `ArtifactResult`.
Document this as an addendum to the channel table in `protocol.js` — reuse 99/100,
distinguish by `payload.mode`, don't allocate a new NetworkId for it.

### 4.3 Unity-side notes

- Visual-layer publishing extends `SceneController.cs`'s existing (currently unused)
  scene graph — already flagged as not-implemented in `docs/progress-log.md`.
- Where the staging clone for `simulate_artifact` actually executes is the same open
  decision already logged in `docs/agentic-xr-architecture.md` §9 (hidden clone inside
  the live Unity/Quest process vs. a headless Unity batch instance on the server). The
  paper's Experimental Space section specifies *what* to check, not *where* it runs —
  that decision is still open and should be resolved before this gets built.

### 4.4 Phased order (inserts into `docs/agentic-xr-architecture.md` §8)

1. `scene_graph_store.js` + `visual_store.js`, server-side only — buildable today
   against `mock_unity_peer.js`, no new Unity work required.
2. `artifact_log.js` — this is roadmap phase 5, now scoped by the paper's
   `get_artifact_history`/`commit_memory_event` operation names.
3. `person_policy.js` — static single-owner stub first.
4. `simulate_artifact` + Unity staging-clone execution — hardest piece, depends on
   resolving the open sandbox-location decision; build last.

### 4.5 Naming discipline

Every name used in the paper — `query_visual_memory`, `query_scene_graph`,
`query_affordances`, `get_script_context`, `get_artifact_history`,
`get_person_policy`, `simulate_artifact`, `commit_memory_event` — must be the literal
MCP tool name in the implementation, not a paraphrase. A reviewer or anyone
cross-checking the paper against the code should find identical vocabulary. If a name
changes during implementation, record it in `docs/progress-log.md` immediately so the
paper side can be told at the next sync.

## Pointers

- Paper source (read-only): `-2027_IEEEVR-AgenticXR/main.tex`, §"Shared XR Memory and
  Experimental Space"; also `rag/notes/claude_agenticxr_architecture_sync.md` for how
  the paper side has been tracking this repo.
- Existing design: `docs/agentic-xr-architecture.md`
- Existing implementation: `Server/mcp/unity_scene_bridge/`
- Progress log: `docs/progress-log.md`
