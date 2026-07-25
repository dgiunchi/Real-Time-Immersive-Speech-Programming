# Bounded goal loops and speculative future preparation

## Status and evidence boundary

The server now has a persistent, trigger-driven goal controller over the existing
append-only `ArtifactLog`. The controller and MCP tools are source-complete and
deterministic/mock tested. They have not yet been evaluated with a live Quest,
real participant, or real delayed outcome.

The idle-time prediction path is deliberately **off by default**. It prepares local
drafts; it never proposes or commits them to Unity. A later real request may select
a fresh matching draft, but must run the ordinary scene, Validator/Critic,
Verification Space, mode-policy, consent, and commit gates again.

The paper currently discusses Verification Space and speculative futures at a
conceptual level, but does not yet state this exact bounded loop, verifier routing,
or idle-preparation protocol. Treat these additions as an implementation extension
to reconcile in the paper before claiming them as part of the evaluated design.

## Persistent loop

Each goal stores its objective, verification level, concrete termination predicate,
attempt/wall-time bounds, current iteration, last trigger, status, study joins,
target, artifact version/rollback pointer, speculative origin, and pinned scene tuple
when applicable.

One iteration is:

```text
trigger -> execute through the existing harness -> verify -> persist -> wait/terminate
```

| Level | Verifier | Automatic eligibility |
|---|---|---|
| 1 | deterministic field/all-true predicate | L1/L2 only, if ordinary safety rules pass |
| 2 | numeric rule/constraint threshold | L1/L2 only, if ordinary safety rules pass |
| 3 | delayed ground-truth signal | wait persistently, then route through L4/L5 |
| 4 | independent Validator/Critic score | validator required; no automatic execution |
| 5 | explicit human checkpoint | human required; no automatic execution |

Model output cannot widen these routes. The controller enforces a global maximum of
10 attempts and 15 minutes per goal. Exhaustion stops at `awaiting_human`. A
persistent global kill switch stops new iterations and needs explicit human approval
to clear.

An exhausted goal cannot accept its own model-generated `approved: true`. Continuation
must be backed by a later world-space panel approval or a new explicit follow-up user
turn with a different correlation ID.

Goal state, triggers, iterations, verification outcomes, escalations, exhaustion,
delayed-resolution latency, termination, and kill-switch changes use the same
temporal artifact log as the rest of AgenticXR.

## MCP surface

- `create_bounded_goal`
- `advance_goal_loop`
- `resolve_delayed_goal`
- `record_goal_validator_judgment`
- `continue_goal_after_human`
- `set_goal_loop_kill_switch`
- `get_goal_loop_state`
- `register_speculative_candidate`
- `select_speculative_candidate`

`advance_goal_loop` does not introduce a second execution engine. It derives
evidence from scene, validation, simulation, consent, and artifact-result events
already produced by the existing harness.

## Idle-time future preparation

An idle prediction run is allowed only when the feature was explicitly enabled, an
Anthropic key is present, a stable target is selected, no other turn is running,
idle/cooldown limits have elapsed, and no study trial is active unless separately
opted in.

The predictor keeps at most three predictions per idle window. A candidate is stored
only after validation reports `accepted` and Verification Space reports `simulated`.
It stays tied to the exact `sceneEpoch`, `snapshotId`, `objectRevision`, and target.

An actual request preempts a running speculative process. Selection requires an
exact scene-tuple match and semantic overlap with the actual objective. Stale or
unrelated candidates are rejected. A selected draft is only an input candidate to
the normal pipeline and has `mayCommitAutomatically: false`.

This can reduce generation latency when a prediction is correct, but can increase
API use, storage, and experimental variance. It is therefore opt-in and disabled
during studies by default.

## Configuration

From `Server`, in the PowerShell process used to start AgenticXR:

```powershell
$env:AGENTICXR_IDLE_PREDICTION_ENABLED="true"
$env:AGENTICXR_IDLE_PREDICTION_THRESHOLD_MS="60000"
$env:AGENTICXR_IDLE_PREDICTION_COOLDOWN_MS="300000"
```

Only when a study protocol explicitly includes speculative preparation:

```powershell
$env:AGENTICXR_STUDY_ALLOW_SPECULATION="true"
```

No additional API credential is needed beyond `ANTHROPIC_API_KEY`. The live voice
path separately needs `STT_HTTP_URL`.

## Verification

```powershell
cd Server
node tests/goal_loop_test.js
npm test
npm run test:integration
```

The focused suite covers deterministic/rule verification, attempt exhaustion,
autonomy-widening rejection, delayed later resolution and latency, persistent kill
switch behavior, restart persistence, non-commit speculation, normal-gate reuse,
and stale-snapshot rejection. Mock integration exercises goal creation, an existing
artifact commit, goal termination, and the 67-column study export.
