# DreamCodeVR → Agentic XR Prototyping System

**Design brief and implementation prompt.** This document is written to be handed to an
implementer (human or coding agent) as the spec for evolving DreamCodeVR from a single
speech→LLM→code pipeline into a multi-agent system. It is grounded in the current repo
state (branch `agenticXR`), not a generic template — file paths below are real.

## 0. Where we start from

Today's pipeline is a single linear chain, one request at a time:

```
Quest trigger held → MicrophoneCapture.cs → Ubiq NetworkId 98 (audio)
  → Server: SpeechToTextService (faster-whisper HTTP)
  → Server: CodeGenerationService (spawns openai_chatgpt_api.py, one prompt template
    from code_runtime_generator/config.json)
  → Ubiq NetworkId 94 ("CodeGenerated" message)
  → CodeGenerationManager.ProcessMessage → TestRoslyn.RunCode(targetObject)
  → RoslynCSharp compiles + CreateInstance() directly onto the ray-selected object
```

Relevant existing pieces we will reuse rather than replace:

| Concern | Already exists as |
|---|---|
| Networked real-time transport | Ubiq `NetworkScene`/`NetworkId` (`Server/vendor/ubiq`, `Unity/Packages` UPM package) |
| Reusable backend service scaffolding | `Server/components/application.js` (`ApplicationController`), `service.js` (`ServiceController`) |
| Scene graph model (unused today) | `Unity/Assets/SceneController.cs` — parent/child dict, per-object component list, public field names via reflection, `SetParent`/`SetVariable` |
| Runtime compile + attach | `Unity/Assets/Scenes/Scripts/TestRoslyn.cs` (RoslynCSharp `ScriptDomain`) |
| Code capability sandboxing | `Unity/Assets/RoslynCSharp/.../RoslynCSharpSecurityAllowance.cs`, `ScriptSecurityMode` |
| Object targeting | `SelectRay.cs` / `SelectObjectRay.cs` |
| Multiple single-purpose "mini agents" already | `conversational_agent`, `storytelling`, `texture_generation`, `transcription` server apps — these are effectively the first backend agents, just not yet coordinated |

The README's own to-do list (object/component tracking, versioning, conflict handling,
"we can inject what we want in a Quest", collaborative networked scripting) is exactly
the gap this document closes. Every section below maps back to one of those bullets.

## 1. Target architecture

```
┌─────────────────────────── Unity / Quest (Ubiq room) ───────────────────────────┐
│  Embodied Coordinator Agent (avatar, real-time, low-latency LLM)                 │
│   - listens to push-to-talk, holds conversation, gives immediate spoken/visual    │
│     feedback, shows previews, asks for confirmation                              │
│  Scene State Publisher (extends SceneController.cs)                              │
│   - stable object/component IDs, diff-based state, focus+halo summarization      │
│  Artifact Runtime (extends TestRoslyn.cs)                                        │
│   - staged/sandboxed compile+run, then commit-or-rollback attach                 │
└───────────────────────────────────┬───────────────────────────────────┬─────────┘
                                     │ Ubiq NetworkId channels (real-time)│
┌────────────────────────────────────▼───────────────────────────────────▼─────────┐
│                         Server: Agent Orchestrator (Node)                         │
│   Task Router  →  dispatches to backend agents, tracks correlation IDs            │
│   Shared Blackboard  →  latest scene snapshot, active targets, session state      │
├─────────────────────────────────────────────────────────────────────────────────┤
│  Backend agents (each = one ServiceController-style process/module):              │
│   • Scene Analyst      – turns raw scene diffs into a compact semantic summary    │
│   • Code Generator     – produces the C# artifact from intent + scene summary     │
│   • Validator/Critic   – static + semantic + sandbox verdict on the artifact      │
│   • Version/Memory     – append-only artifact log, diffs, rollback, scene restore │
│   • Conflict Resolver  – arbitrates concurrent edits from multiple users/objects  │
└─────────────────────────────────────────────────────────────────────────────────┘
```

Two clocks, one transport. The **Coordinator** must feel real-time (sub-second
acknowledgement, streaming speech/text) because it's embodied in front of the user.
The **backend agents** are allowed to take seconds because their output is always
gated through validation before it touches the scene. Both sides talk over the same
Ubiq `NetworkScene`, but never block on the same message.

## 2. Communication strategy (the part you asked me to just decide)

**Recommendation: keep Ubiq as the single transport, but replace the two hardcoded
`NetworkId`s (94/98) with a small structured protocol on top of it.** Do not introduce
a second transport (separate WebSocket/REST server) between Unity and the backend —
Ubiq already gives you a working, NAT-traversed, room-scoped, binary-capable channel
to every client including the Quest, and `Server/vendor/ubiq` already speaks it. Adding
a second channel would only create two sources of truth for session/room state. Reserve
a second transport only for backend-internal, non-realtime traffic if the agent pool
later grows past a single Node process (see §2.3) — Unity never needs to see it.

### 2.1 Channel scheme

Replace magic numbers with a documented `NetworkId` range, one id per message *class*,
not per object (object identity travels inside the payload):

| NetworkId | Direction | Purpose | Replaces |
|---|---|---|---|
| 98 | Unity → Server | audio (STT input) | existing, unchanged |
| 94 | Server → Unity | legacy `CodeGenerated` (kept for back-compat during migration) | existing |
| 95 | Unity → Server | `SceneDelta` (scene state pushes) | new |
| 96 | Server → Unity | `SceneQuery` (backend asks for on-demand detail) | new |
| 97 | Server → Unity | `AgentUtterance` (coordinator speech/text/emote, streamed) | new |
| 99 | Server → Unity | `ArtifactProposal` / `ArtifactResult` (see §4) | new |
| 100 | Unity → Server | `UserDecision` (confirm/reject/steer) | new |
| 101 | bidirectional | `AgentPresenceHeartbeat` (which agents are alive, for the embodied avatar's status indicator) | new |

### 2.2 Message envelope

Every message on every channel above shares one envelope so the orchestrator and
Unity can route/correlate without per-channel special-casing:

```json
{
  "schemaVersion": "1.0",
  "type": "ArtifactProposal",
  "sessionId": "room-<ubiq-room-uuid>",
  "correlationId": "req-3f9a",
  "originAgent": "code_generator",
  "targetObjectId": "obj-8b21-...",
  "authoringMode": "semi_auto_confirm",
  "priority": "normal",
  "timestamp": 1752230000,
  "payload": { }
}
```

`correlationId` is what lets a slow backend round-trip (STT → codegen → validate,
possibly 2-6s) resolve against the right in-flight UI state in Unity, even if the user
has since selected a different object or spoken again. `targetObjectId` is a **stable
GUID assigned by Unity's Scene State Publisher**, never a Unity `InstanceID` or ray-hit
transform reference — this is what makes "replace object (versioning)" and "positioning
in the hierarchy" from the README to-do list tractable later.

### 2.3 Backend-internal bus

Inside the Node process, agents don't need a broker — `Server/components/application.js`
already gives you an `EventEmitter`-style pattern (`ServiceController.on("response", ...)`).
Extend that: the orchestrator is one more `ApplicationController` subclass that owns a
`Map<correlationId, TaskState>` and fans messages out to the Scene Analyst / Code
Generator / Validator as child `ServiceController`s, exactly like `code_runtime_generator/app.js`
already chains `transcriptionService → codeGenerationService`. Don't reach for
Redis/RabbitMQ/etc. until you actually split agents across processes or machines —
premature here, and the existing in-process pattern is proven in this codebase.

### 2.4 Real-time vs. batch split

- Coordinator's spoken acknowledgement ("okay, making the sphere red") must go out on
  `AgentUtterance` **immediately**, before validation finishes — it's conversational
  filler, not a claim the change happened.
- The actual `ArtifactResult` (attached / rejected / needs-confirmation) follows once
  the Validator agent finishes, correlated back via `correlationId`.
- This decouples "feels responsive" from "is verified," which is the actual tension in
  the README to-do "how to deal with code that does not work... but can build."

## 3. Scene state protocol (token-bounded, real-time)

Extend `SceneController.cs`, which already builds the parent/child + component + public
field maps — it just needs (a) stable IDs and (b) a bounded serialization strategy.

**Focus + halo model**, sent on `SceneDelta` (95):

- **Focus** (full detail): the currently ray-selected/targeted object — all components,
  all public fields with current values, full transform.
- **Halo** (summary only): objects within N hops in the hierarchy or within a spatial
  radius of the focus object — name, tag, type, id only, no field values.
- **Everything else**: omitted by default. If the Scene Analyst agent needs more, it
  sends a `SceneQuery` (96) with an object id or a filter (`tag:"game"`,
  `componentType:"Light"`); Unity answers on `SceneDelta` scoped to just that query.
- **Deltas, not snapshots**: after the first full push per session, only changed
  objects/components/fields are sent, keyed by their stable id — directly implements
  the README to-dos "track all the objects in the scene" / "track all components in
  the scene" as an ongoing diff rather than a one-shot dump.

Example payload:

```json
{
  "focus": {
    "id": "obj-8b21-...",
    "name": "Sphere_Red",
    "tag": "game",
    "transform": { "pos": [0.1, 1.2, -0.4], "rot": [0,0,0,1], "scale": [1,1,1] },
    "components": [
      { "type": "MeshRenderer", "fields": { "material.color": "#FF0000" } },
      { "type": "GeneratedBehaviour_a91f", "artifactId": "art-102", "fields": { "speed": 1.0 } }
    ]
  },
  "halo": [
    { "id": "obj-5c11-...", "name": "Table", "tag": "game", "type": "static" },
    { "id": "obj-77aa-...", "name": "Light_Main", "tag": "env", "type": "Light" }
  ],
  "changedSince": "delta-cursor-118"
}
```

This is what the Scene Analyst agent turns into the short natural-language scene
summary that actually goes into the Code Generator's prompt — the LLM never sees raw
Unity reflection dumps, which is where most of your token budget would otherwise go.

## 4. Code artifact lifecycle and validation

Everything the system wants to attach to the scene is an **artifact**: a proposed C#
`MonoBehaviour`, plus metadata, that must pass a fixed pipeline before `TestRoslyn`-style
`CreateInstance()` ever runs on the *live* object.

```
Intent (speech transcript, or system-initiated trigger)
   │
   ▼
[1] Code Generator agent → draft artifact (source, targetObjectId, intent text)
   │
   ▼
[2] Static validation
    - RoslynCSharp compile (existing domain.CompileAndLoadMainSource)
    - Capability whitelist via RoslynCSharpSecurityAllowance / ScriptSecurityMode
      (no filesystem, no reflection escape, no networking APIs, single class,
      must inherit the known base type — config.json's prompt_suffix already
      states these constraints; now they're *enforced*, not just requested)
    - Size/complexity bound (reject pathological generations)
   │  fail → back to [1] with compiler errors as feedback (bounded retries)
   ▼
[3] Semantic validation (Validator/Critic agent, separate LLM call from the generator)
    - does the artifact plausibly satisfy the transcribed intent given the scene focus?
    - known-anti-pattern check (busy-loops in Update, teleporting other users'
      avatars, deleting objects, touching objects outside targetObjectId)
    - produces a riskScore (0-1) and a rationale string
   │  fail → reject, tell Coordinator agent to relay a spoken explanation
   ▼
[4] Sandbox dry-run
    - CreateInstance() on a hidden staging clone (not the live object) for a fixed
      window (e.g. 3-5 seconds of simulated frames)
    - watch for: exceptions, NaN/Inf transform values, runaway frame time, infinite
      spawn loops
   │  fail → reject with the captured exception, same feedback loop as [2]
   ▼
[5] Routing decision (uses riskScore + change class, see §5) →
    automatic-apply | propose-and-confirm | propose-and-steer
   │
   ▼
[6] Commit: attach to the live object, write an entry to the Version/Memory store
    (artifactId, targetObjectId, source, intent, riskScore, authorAgent|user,
    timestamp, previousArtifactId for that object) → this is what makes "replace
    object/component (versioning)" and scene restore possible later, and it's what
    a rollback (§4.1) reads.
```

### 4.1 Rollback

Because every commit records `previousArtifactId`, "undo" is: destroy the current
component instance, recompile+reattach the previous artifact's source (or literally
remove the component if `previousArtifactId` is null). Expose this as a Coordinator
voice command ("undo that") and as the automatic response when a *post-attach* watchdog
(same exception/NaN/frame-time checks as [4], now running on the live object) trips —
this closes the other half of the README's "code that does not work... which visual
feedback? how to edit?" question: revert immediately, then let the user re-prompt.

## 5. Authoring modes

| Mode | Trigger | Human role | Applies when |
|---|---|---|---|
| **Automatic** | System-initiated (e.g. the Coordinator or a backend agent notices a cleanup/consistency opportunity, or a low-risk parameter tweak that's a strict refinement of the last artifact on that object) | None — happens, then is announced | `riskScore` below threshold **and** scope is a single object **and** change class is cosmetic/parametric (color, scale, simple numeric field) **and** it passed [2]-[4] cleanly |
| **Semi-automatic — confirm** | User's spoken instruction, default path | Approve/reject a preview before commit | Any behavior-affecting change (new script logic, physics, movement), or `riskScore` mid-range |
| **Semi-automatic — steer** | User keeps talking mid-generation or after seeing a preview ("no, make it faster instead") | Redirects generation before/instead of confirming | Iterative prototyping conversations; Planner agent treats it as a new intent chained to the same `correlationId`/artifact lineage rather than a fresh unrelated request |
| **Manual** | User opens the existing code panel (`TestRoslyn`'s `text`/canvas UI) and hand-edits | Full control | Escape hatch; hand-edited code still must pass [2]-[4] before it's allowed to attach |

Confirmation UX should stay embodied and low-friction: the Coordinator shows the
pending artifact as a **ghost/preview state** on the target object (e.g. translucent
material swap, or the change genuinely applied to a *duplicate* ghost object next to
the original) and accepts a short voice "yes/apply" or "no/cancel", with a visible
timeout that defaults to *not* applying (silence = reject, not accept — never auto-commit
a behavior change the user didn't clearly approve).

Anything touching **multiple objects, deleting an object, or shared/networked state**
(relevant because Ubiq rooms are collaborative) is never automatic, regardless of
riskScore — route straight to confirm, and if more than one user is present, to the
Conflict Resolver first (§6).

## 6. Collaboration & conflict handling

Since Ubiq rooms are multi-user by design, two peers can target the same object or
issue conflicting instructions concurrently — the README already flags this
("clashing between valid instructions, conflict handler"). Minimum viable policy:

- **Per-object soft lock**: while an artifact for `targetObjectId` is anywhere in
  stages [1]-[5], the Scene State Publisher marks it locked in the shared blackboard;
  a second concurrent instruction targeting the same object is queued and the second
  user's Coordinator avatar says so, rather than silently racing.
- **Networked objects only**: per the README's own note, any object that can be
  mutated by generated code must be a Ubiq networked object so the resulting field
  changes replicate — this is a Unity-side prerequisite, not an agent concern, but the
  Validator's static stage [2] should reject artifacts that mutate a `targetObjectId`
  known not to be networked, rather than producing a change only the author sees.
- **Ownership**, not merging: don't attempt CRDT-style merges of two people's C#
  proposals for the same object — first valid artifact wins the object lock, the
  second is offered as "apply after" or "apply to a copy instead."

## 7. Security posture

The README already names the real risk plainly: "we can inject what we want in a
Quest." Treat every artifact as untrusted code regardless of authoring mode:

- Compile and dry-run (stage [4]) in a domain that never has the changes committed
  until [5]/[6] — RoslynCSharp's `ScriptDomain` already gives process-level isolation
  for the compiled assembly; use `RoslynCSharpSecurityAllowance` to enforce an explicit
  allowed-API surface (UnityEngine transform/render/audio/physics namespaces) and deny
  everything else (`System.IO`, `System.Net`, `System.Diagnostics.Process`, reflection
  escape hatches) at the security-verification stage, not just via prompt instructions
  in `config.json`.
- Never trust the LLM's own claim that code is safe — that's exactly why stage [3] is
  a *separate* model call from stage [1]'s generator, ideally with a different/cheaper
  model, so it isn't rationalizing its own output.
- Log every rejected artifact (reason, riskScore, offending API if applicable) to the
  Version/Memory store — this becomes your evidence trail if you ever need to explain
  why the sandbox refused something, and lets you tighten `config.json`'s
  `prompt_suffix` over time based on real rejection patterns instead of guessing.

## 8. Phased rollout mapped to this repo

1. **Stable IDs + scene diffing** — extend `SceneController.cs` to assign/persist a
   GUID per tracked object, serialize focus+halo JSON, wire it onto new `NetworkId`
   95/96. No agent changes yet; verify the payload in isolation.
2. **Envelope + channel migration** — introduce the shared envelope, add 97/99/100/101,
   keep 94/98 working so `code_runtime_generator` doesn't regress mid-migration.
   ✅ Node/MCP side scaffolded and tested end-to-end (against a mock peer) at
   `Server/mcp/unity_scene_bridge/` — see its README. Unity-side handlers for
   `SceneQuery`/`SceneDelta`/`ArtifactProposal`/`ArtifactResult` are the
   remaining piece of phases 1-2 and are not implemented yet.
3. **Split the orchestrator out of `app.js`** — turn `CodeGeneration` in
   `code_runtime_generator/app.js` into the Task Router; keep
   `SpeechToTextService`/`CodeGenerationService` as-is but make them `Scene Analyst`
   and `Code Generator` roles addressed by correlation id instead of being hardwired
   in sequence.
4. **Validator agent + sandbox dry-run** — new `ServiceController`, stages [2]-[4] from
   §4; wire `TestRoslyn.RunCode` to a staging clone before touching the live object.
5. **Version/Memory store** — append-only log (SQLite is enough at this scale) keyed by
   `artifactId`/`targetObjectId`; implement undo.
6. **Authoring-mode routing + confirm/steer UX** — Coordinator-side ghost preview,
   `UserDecision` channel, riskScore thresholds from §5.
7. **Conflict Resolver** — per-object soft lock once you're regularly testing with
   two headsets in one room.

## 9. Open decisions before implementation starts

- ~~Model split~~ — resolved in [`docs/agent-framework-and-communication.md`](agent-framework-and-communication.md):
  backend orchestration runs on the Claude Agent SDK with MCP connectors (including a
  Unity Scene Bridge MCP server wrapping the Ubiq channels below); the existing OpenAI
  `gpt-5.5` call can stay as the Code Generator's model. That doc also has the two
  architecture/sequence diagrams for this design.
- Where does sandbox dry-run [4] execute — inside the live Unity/Quest process (hidden
  clone) or a headless Unity batch instance on the server? The former is simpler given
  today's single-project setup; the latter avoids any risk to the live session but adds
  infrastructure.
- Store choice for the Version/Memory log — flat JSON append log vs. SQLite; SQLite
  recommended once "scene restore" needs to reconstruct a full prior state, not just
  the latest artifact per object.
