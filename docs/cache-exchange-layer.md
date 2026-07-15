# Cache Exchange Layer

Implements the paper's "Cache Exchange Layer" (`main.tex`, subsection of that name,
part of `sec:system`) — the client-agent synchronization substrate between the Unity
XR client and the Node/agentic backend, grounded in
`rag/prompts/cache_exchange_agenticxr_prompt.md`. This is **not** simple pub/sub: it
combines a fast local cache (Unity), a mirrored working cache (backend), an
append-only recovery journal, pub/sub for live deltas, request/reply for missing
detail, and a compare-and-swap proposal gate before anything touches the live scene.

## Architectural principle (unchanged, now enforced in code)

Unity is authoritative for live scene mutation. Backend agents read the Agent Working
Cache and Shared XR Memory, but only ever **propose** artifacts — `Server/cache/`
never mutates a live object, and `ProposalGate` is explicitly advisory
(`Server/cache/proposal_gate.js`'s own header comment). The authoritative check is
`CacheExchangeManager.HandleCommitRequest` on the Unity side
(`Unity/Assets/AgenticCache/CacheExchangeManager.cs`) — scaffolded, not yet compiled
or run (see status table below).

## Three-plane protocol, mapped onto the existing 6 channels

The paper's channel table (`tab:channels`) fits every new message type onto the
channels the bridge already owns — **no new NetworkIds were allocated**. 94/98
(legacy) are untouched.

| Channel | Direction | Messages |
|---|---|---|
| 95 | Unity → Server | `SceneDelta`, `CacheSnapshot`, `BackfillResponse` |
| 96 | Server → Unity | `SceneQuery`(legacy)/`DetailRequest`, `BackfillRequest` |
| 97 | Server → Unity | `AgentUtterance`(legacy), `AgentStatus` |
| 99 | Server → Unity | `ArtifactProposal`(legacy), `CommitRequest`, `RollbackRequest` |
| 100 | Unity → Server | `ArtifactResult`/`UserDecision`(legacy), `CommitAccepted`, `CommitRejected`, `RollbackResult` |
| 101 | Bidirectional | `AgentPresenceHeartbeat`(legacy), `CacheInvalidation`, `DeltaAck`, `DeltaNack` |

**One documented deviation from the paper's compressed table**: it lists "delta
acks" under the Unity-to-Server row (100). This implementation sends
`DeltaAck`/`DeltaNack` Server→Unity instead, on channel 101, because the *receiver*
of a `SceneDelta` is the backend (deltas flow Unity→Server on 95) — acknowledging
what you received is the only direction that makes distributed-systems sense.
Rationale lives in `Server/cache/protocol.js`'s header comment.

Full field-level chart of channels + envelope, still accurate and worth reading
alongside this doc: https://lucid.app/lucidchart/a069924a-8d7a-4c8c-af2f-197c9c2a4004/edit

## Envelope

Extends the existing shared envelope (`Server/mcp/unity_scene_bridge/protocol.js`)
with the Cache Exchange Layer's freshness/idempotence fields
(`Server/cache/protocol.js`'s `makeCacheEnvelope`): `sceneEpoch`, `snapshotId`,
`deltaSeq`, `objectRevision`, `source`, `confidence`, `ttlMs`, plus `stableObjectId`
(an alias for `targetObjectId` at the wire level — same value, paper's preferred name
for this layer).

## Wire format and the JsonUtility payload problem

Unity's `JsonUtility` cannot deserialize an arbitrary nested JSON object into a
field. The Node side's `payload` is normally a nested object (e.g.
`{code, intent, mode}`), which breaks a naive `JsonUtility.FromJson<Envelope>()` call
on the Unity side. This is a **pre-existing, previously undiscovered issue** — every
prior "Unity side not implemented yet" message type (`SceneQuery`, `ArtifactProposal`,
`SceneDelta`, `ArtifactResult`, `AgentUtterance`, `AgentPresenceHeartbeat`) was only
ever exercised against `mock_unity_peer.js` (plain Node `JSON.parse`, which handles
nesting fine), never against a real `JsonUtility` parser.

**Fix applied for the new Cache Exchange types**: `Server/cache/protocol.js` defines
`STRINGIFY_PAYLOAD_FOR_UNITY` — for the 14 new message types, `payload` is
JSON-stringified before being sent (`toWireFormat()`), and `Unity/Assets/AgenticCache/CacheEnvelope.cs`
declares `payload` as `string` accordingly. `fromWireFormat()` reverses this on the
Node side (used by `mock_unity_peer.js` and `cache/index.js` when unpacking
`CacheSnapshot`/`BackfillResponse`'s multi-object payloads).

**Known gap, not fixed in this pass**: the three legacy types Unity must also
receive — `SceneQuery`, `AgentUtterance`, `ArtifactProposal` — are *not*
pre-stringified yet. `scene_bridge_client.js`'s `querySceneFocus`/`proposeArtifact`/
`sendAgentUtterance` send nested-object payloads directly, bypassing the new
`#sendCache`/`toWireFormat` path deliberately — extending this would touch already-
tested, verified Node-side code this pass intentionally left alone. Unity's handlers
for those three types are scaffolded but will receive an empty/malformed `payload`
until a follow-up either extends `STRINGIFY_PAYLOAD_FOR_UNITY` to cover them (and
routes those three send methods through it), or Unity-side parsing is swapped to
something that handles nested objects (the codebase already links `Ubiq.Logging.Utf8Json`
— `CacheChannelRelay.cs` uses its `data.FromJson<T>()` extension, matching
`CodeGenerationManager.cs`'s proven pattern exactly, but its nested-object capability
was not verified against source in this environment).

## Modules

### Backend (`Server/cache/`) — implemented and tested

| File | Role |
|---|---|
| `protocol.js` | Message type constants, `makeCacheEnvelope`, wire (de)serialization, channel mapping |
| `event_journal.js` | Append-only log; `backfill(sessionId, lastSeenSeq)`; storage behind a swappable adapter interface (starts in-memory) |
| `agent_working_cache.js` | Mirrors accepted deltas; indexed by `stableObjectId`/`correlationId`/label/region/`artifactId`; monotonic-revision safety net; explicitly NOT authoritative |
| `cache_reconciler.js` | Detects duplicates (per-session `seenSet`, not just a high-water mark — a backfilled delta must still apply even after a later one arrived), gaps, staleness (TTL), epoch changes; recommends backfill/snapshot/invalidate |
| `proposal_gate.js` | Advisory pre-flight compare-and-swap check (correlationId active, object exists, epoch/revision match, snapshot age vs. authoringMode threshold, consent route, validation state) |
| `index.js` | `CacheExchangeLayer` aggregator; `attach(bridge)` wires the reconciler into the bridge's envelope stream and auto-triggers backfill/snapshot requests |

Wired into `Server/mcp/unity_scene_bridge/server.js` as 8 new MCP tools:
`query_agent_cache`, `request_backfill`, `request_snapshot`, `check_proposal_gate`,
`request_commit`, `request_rollback`, `send_cache_invalidation`, `send_agent_status`.

### Bridge extensions (`Server/mcp/unity_scene_bridge/`)

- `scene_bridge_client.js`: new methods `requestSnapshot`, `requestBackfill`,
  `sendCommitRequest` (awaits `CommitAccepted` *or* `CommitRejected` — `#awaitReply`
  now accepts an array of acceptable reply types), `sendRollbackRequest`,
  `sendCacheInvalidation`, `sendAgentStatus`, `sendDeltaAck`/`sendDeltaNack`. Channel
  101's listener now routes every message through the same `#handleInbound` path
  (previously heartbeat-only).
- `mock_unity_peer.js`: plays Unity's role for testing — sends a `CacheSnapshot` and
  four `SceneDelta`s on join (deliberately dropping `deltaSeq` 3 on the wire to
  simulate packet loss while still recording it in its own history), answers
  `BackfillRequest`/`CommitRequest`/`RollbackRequest` with its own authoritative
  freshness check against its tracked `objectRevisions`/`sceneEpoch`/`snapshotId`.

### Unity (`Unity/Assets/AgenticCache/`) — scaffolded, **not compiled or run**

No Unity Editor is available in this environment, so nothing below has been verified
by an actual compiler — only carefully pattern-matched against the three confirmed-
working networked scripts already in this repo (`CodeGenerationManager.cs`,
`MicrophoneCapture.cs`, `SelectRay.cs`: `NetworkScene.Register(this, networkId)` →
`NetworkContext`, `data.FromJson<T>()` to receive, `ReferenceCountedSceneGraphMessage.Rent()`
+ `context.Send()` to send).

| File | Role |
|---|---|
| `CacheEnvelope.cs` | Envelope class + message-type string constants, matching the Node schema field-for-field |
| `LocalXRCache.cs` | Plain C# state: focus/halo/selected object, per-object revision + TTL, pending proposals (keyed by correlationId), previews, agent status, rollback pointers |
| `CachePublisher.cs` | Registers on NetworkId 95; coalesces transform deltas (time-windowed), sends semantic/state deltas only on revision advance, builds `CacheSnapshot` |
| `CacheChannelRelay.cs` | One small relay component per additional channel (96/97/99/101) — Ubiq's registration model is one NetworkId per component, so `CacheExchangeManager` can't register on four channels itself |
| `CacheExchangeManager.cs` | The authoritative gate: all 10 handlers (`CacheSnapshot`-trigger, `DeltaAck`, `DeltaNack`, `BackfillRequest`, `CacheInvalidation`, `AgentStatus`, `DetailRequest`, `ArtifactProposal`, `CommitRequest`, `RollbackRequest`) |

`CacheExchangeManager.HandleCommitRequest` is the real content: it independently
re-checks correlationId validity, object existence, `sceneEpoch`, `objectRevision`,
and snapshot age against `maxSnapshotAgeMsBy­Mode` — the backend's `ProposalGate`
result is never trusted, matching the architectural principle.

**Not implemented even as scaffolding**: the actual Roslyn compile+attach on accept
(flagged with a `TODO` at the exact line in `HandleCommitRequest`), real scene data
sourcing for `DetailRequest`/snapshot content (would need `SceneController.cs` to
grow stable IDs first, per `docs/agentic-xr-architecture.md` phase 1), and real
component rollback in `HandleRollbackRequest`.

## Running the mock/test flow

Three terminals, from `Server/`:

```powershell
# 1
cd Server\samples\apps\code_runtime_generator
node app.js

# 2 - start this, then move to terminal 3 within a few seconds (see below)
cd Server
node mcp\unity_scene_bridge\mock_unity_peer.js

# 3 - run within ~10s of starting terminal 2
cd Server
node mcp\unity_scene_bridge\cache_test_flow.mjs
```

`mock_unity_peer.js` waits 12 seconds after joining before starting its sequence, so
terminal 3's `cache_test_flow.mjs` (which spawns its own `server.js` subprocess and
connects to the room) has time to connect first — **Ubiq does not replay missed
messages to late joiners**, so if you wait too long between terminals 2 and 3 you'll
only see the recovery-via-explicit-backfill path, not the live-plus-automatic-gap-
backfill path. Either way demonstrates the reconciler correctly; the automatic path
is more interesting to watch.

`cache_test_flow.mjs` polls `query_agent_cache` every 2 seconds so you can watch the
sequence unfold, then runs:
1. A pre-flight `check_proposal_gate` + `request_commit` against the **current**
   (fresh) revision → expect `CommitAccepted`.
2. `request_commit` against a **deliberately stale** `objectRevision` (2, from before
   the deltas applied) → expect a pre-flight rejection (`stage: "preflight"`,
   `objectRevision mismatch`), never reaching Unity.
3. `request_commit` with a stale `snapshotTakenAt` under `automatic` mode's tight
   2-second freshness budget → expect a pre-flight rejection for that reason instead.

**Verified in this repo** (2026-07-15, see `docs/progress-log.md`): every one of
these steps, including the trickiest part — the reconciler correctly distinguishing
a genuinely-missing `deltaSeq` (gap, triggers backfill) from an already-seen one
(duplicate, ignored) even when the backfilled delta arrives *after* a newer live
delta already advanced the object's revision (monotonic safety net in
`agent_working_cache.js` prevents regression without rejecting the backfill outright).

## Status: implemented / mocked / TODO

| Area | Status |
|---|---|
| Message schema, channel mapping, wire (de)serialization | **Implemented, tested** |
| `EventJournal`, `AgentWorkingCache`, `CacheReconciler`, `ProposalGate` | **Implemented, tested** |
| Bridge outbound methods (snapshot/backfill/commit/rollback/invalidation/status requests) | **Implemented, tested** |
| Automatic gap-detection → backfill / stale → snapshot request | **Implemented, tested** |
| 8 new MCP tools | **Implemented, tested** |
| Mock Unity peer (snapshot, deltas incl. simulated packet loss, commit/rollback authoritative check) | **Implemented, tested** — this is a mock, not real Unity |
| `LocalXRCache`, `CachePublisher`, `CacheExchangeManager`, `CacheChannelRelay`, `CacheEnvelope` (Unity C#) | **Scaffolded, NOT compiled or run** — no Unity Editor available in this environment |
| Unity JsonUtility payload parsing for `SceneQuery`/`AgentUtterance`/`ArtifactProposal` | **Known gap** — payload will be malformed until the wire-format fix described above lands |
| Real Roslyn compile+attach on `CommitAccepted` | **Not implemented** — `TODO` marker in `HandleCommitRequest` |
| Real scene data for `DetailRequest`/Unity-originated `CacheSnapshot` content | **Not implemented** — depends on `SceneController.cs` growing stable object IDs (existing roadmap phase 1) |
| Real component rollback | **Not implemented** — `TODO` marker in `HandleRollbackRequest` |

## Pointers

- Paper source (read-only): `-2027_IEEEVR-AgenticXR/main.tex`, subsection "Cache
  Exchange Layer"; `rag/prompts/cache_exchange_agenticxr_prompt.md`
- Prior layer this builds on: `docs/shared-memory-and-experimental-space.md`,
  `docs/paper-sync-timelines-and-modes.md`
- Full record: `docs/progress-log.md`
