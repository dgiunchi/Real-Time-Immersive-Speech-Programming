# AgenticXR Paper/Implementation Completion Prompt

## Objective

Bring the live Unity/Node implementation into factual alignment with the implementation
claims in `D:\Research_Activities\agenticXR\agenticxr_paper\main.tex`, while preserving
the legacy DreamCodeVR comparison path and the existing Ubiq NetworkId allocation.

The work must produce a real Unity-backed state, recovery, validation, consent, commit,
preview, and rollback path. Passing tests against `mock_unity_peer.js` alone is not
sufficient evidence for a Unity-side claim.

## Constraints

- Keep NetworkIds 94 and 98 unchanged for the legacy generated-code and audio paths.
- Keep AgenticXR traffic on 95, 96, 97, 99, 100, and 101.
- Unity remains authoritative for scene state and live mutation.
- Preserve the selectable legacy OpenAI baseline.
- Continue using stable object IDs and correlation IDs throughout a turn.
- Do not put API keys or credentials in source-controlled files.
- Use payload strings on the Unity boundary where required by `JsonUtility`.
- Make recovery idempotent: repeated or overlapping backfills must not regress state.
- Treat generated code as untrusted and reject unsupported capabilities before attach.

## Required implementation

### 1. Live Unity state publication

Connect `AgenticSceneRegistry` to `CachePublisher` so production Unity automatically:

- emits an initial `CacheSnapshot` after the Ubiq context is ready;
- detects transform/component/tag/hierarchy changes;
- publishes monotonic `SceneDelta` messages with `deltaSeq`, `objectRevision`,
  `sceneEpoch`, `snapshotId`, timestamp, confidence, and TTL;
- publishes selection/focus changes and real ray/gaze observations;
- maintains compact focus-and-halo state without sending the full scene every frame;
- resets the epoch, snapshot, and publisher state when the active scene changes.

Do not leave publishing methods as uncalled scaffolding.

### 2. Unity event journal and recovery

Add a bounded Unity-side journal of published deltas and important control events.
Implement `BackfillRequest` so it returns the requested missing `deltaSeq` range, or a
fresh snapshot if the range is unavailable or crosses a scene epoch. Duplicate requests
must be safe. Record acknowledgements and prune only entries that are no longer needed.

### 3. Authoritative Unity proposal/commit gate

Before preview or commit, verify all applicable invariants:

- active correlation ID and non-invalidated pending proposal;
- target still exists;
- selected/focused target remains compatible with the request;
- matching `sceneEpoch` and compatible `snapshotId`;
- matching `objectRevision`;
- snapshot/timestamp age within the authoring-mode budget;
- accepted validation state and appropriate consent route;
- capability-policy success;
- no newer local user action invalidated the proposal.

Implement `CommitRequest` as a complete request/reply operation that always returns
`CommitAccepted` or `CommitRejected` on NetworkId 100. Do not silently return or time
out. Keep `ArtifactProposal`/`ArtifactResult` compatibility for the orchestrator path.

### 4. Preview, consent, decisions, and rollback telemetry

Represent the Verification Space result as an inspectable preview record containing the
target, intent, validation summary, risk, permissions, expected effects, and staged
artifact handle. Show this evidence in the world-space panel before confirmation.

Publish explicit `UserDecision` events for approve, reject, timeout, revise/cancel, and
undo. Publish `RollbackResult` for both backend-requested and local rollback. Ensure
backend temporal/person memory receives these events automatically.

### 5. Complete artifact metadata

Carry and store at least:

- target object ID and source intent;
- correlation/session IDs;
- scene epoch, snapshot ID, and object revision;
- validation state and summary;
- risk score and required permissions;
- authoring and L1-L5 interaction modes;
- artifact version, artifact ID, and rollback pointer;
- expected side effects and Verification Space evidence.

### 6. Sensors and interaction modes

Connect available Unity signals to memory updates: selection/ray focus, transforms,
components, locomotion/region, and user decisions. Implement concrete runtime routing
for the feasible modes:

- L1/L2 may trigger only local, reversible, low-risk proposals and must remain undoable;
- L3 preserves the same correlation ID while asking for missing detail;
- L4 always presents evidence and requires explicit approval, with timeout rejection;
- L5 retains conversational context for iterative speech revisions before approval.

If a mode cannot be exercised without hardware or a live model, implement its state
machine and deterministic tests without claiming a completed physical interaction.

### 7. Stable identity and lifecycle

Make object IDs unique for duplicate sibling names and persistent where scene assets
already contain an ID. Detect duplicate IDs explicitly. Handle active-scene changes
without retaining a stale registry, compiler, epoch, or snapshot through the
`DontDestroyOnLoad` runtime root.

### 8. Security

Replace the token blacklist as the sole policy with a structured allow/deny capability
check suitable for the available RoslynCSharp version. Retain compiler security checks.
Add deterministic negative tests for file, network, process, reflection, native calls,
unsafe code, application exit, and dynamically constructed bypass variants where
practical. Clearly document remaining limitations; do not claim a formal sandbox.

## Verification and acceptance criteria

- All custom Node `.js`/`.mjs` files pass `node --check`.
- A repeatable test command exists in `Server/package.json` and exits nonzero on failure.
- Mock integration tests still pass.
- Deterministic tests cover Unity-protocol-equivalent snapshot, delta, gap, backfill,
  duplicate, stale proposal, commit acceptance/rejection, decision, and rollback flows.
- Unity 6 batch import exits zero with no C# compiler errors.
- When Unity runtime execution is available, logs demonstrate a real Unity snapshot,
  a real transform delta, a real backfill response, and a commit/rollback reply.
- Documentation distinguishes source-complete, mock-tested, Unity-compiled,
  Editor-exercised, headset-exercised, live-model-exercised, and user-evaluated status.
- Update `docs/progress-log.md` with exact commands and evidence.
- Do not modify the paper repository during this task; instead provide an explicit list
  of manuscript sentences that are now supported, must be softened, or remain future
  work.

