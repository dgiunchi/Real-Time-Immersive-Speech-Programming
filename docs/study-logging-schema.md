# AgenticXR objective study logging schema

## Scope and evidence boundary

This schema is the executable contract between the paper's Measures subsection,
runtime instrumentation, and analysis export. It covers objective machine-recorded
data only. Questionnaire, interview, demographics, raw audio, and transcript text are
not part of either CSV.

The source-complete pipeline writes study events into the existing append-only
`Server/memory/data/artifact_log.jsonl`. Generated study files are ignored by Git.
The deterministic contract and mock-Ubiq tests exercise the exporter. Unity-side
visibility, consent, validation, execution, mismatch, and rollback fields are wired
but have not yet been observed in a live/on-device run.

All durations and latencies use **milliseconds (ms)**. All event timestamps use UTC
ISO 8601 (`timestampUtc`) and retain a numeric Unix epoch millisecond source (`at`,
`timestamp`, or `loggedAt`) in the JSONL.

## Storage decision

The study retains JSON Lines rather than migrating to SQLite now:

- it extends the existing temporal/version memory rather than adding another store;
- each event remains an append-only, inspectable audit record;
- the offline exporter supplies the query ergonomics the study needs;
- it avoids a native database migration immediately before live integration.

SQLite would become warranted for concurrent multi-user sessions, transactional
cross-table updates, or interactive queries over large longitudinal datasets.
Multiple processes currently perform single-line append operations; sessions should
be archived/exported after each participant and the exporter will fail on any
malformed line. A SQLite migration remains a post-pilot hardening option, not a
prerequisite for capturing the planned single-participant trials.

## Required trial identity

Every `studyEvent: true` record must contain all fields below. `ArtifactLog` validates
them before writing and throws if one is missing, empty, or unsafe.

| Field | Type | Definition |
|---|---|---|
| `participantId` | string | Pseudonymous study code. Never a name, email, or device account. |
| `sessionId` | string | One runtime/study session. |
| `trialId` | string | One participant × task × condition trial. |
| `condition` | string | Experimental condition, e.g. `baseline`, `agenticxr_no_verification`, or `agenticxr_verification`. |
| `taskId` | string | Protocol task code. |
| `interactionMode` | string | `baseline` or `L1`–`L5`. |
| `correlationId` | string | Authoring-turn correlation. A trial may contain several values; the trial CSV lists all of them. |
| `timestampUtc` | UTC string | Event occurrence timestamp, not export time. |
| `studyEvent` | boolean | Must be `true` for inclusion in the objective export. |
| `studySource` | string | Emitter such as `pipeline`, `mcp`, `researcher_cli`, `code_runtime_generator`, or `baseline_runtime`. |

Identifiers allow ASCII letters, digits, `.`, `_`, `:`, and `-`, with a maximum of
128 characters. A missing identifier fails loudly. Null envelope metadata cannot
overwrite the registered trial context.

### Condition semantics and per-trial configuration

The registered `condition` is not only a label — it is the per-session switch for
the H2 arm. When the active trial's condition is `agenticxr_no_verification`, the
bridge deterministically skips Verification Space dry-runs: `simulate_artifact`
returns `status: skipped_no_verification` without a Unity round trip, candidate
ranking accepts that recorded skip as expected evidence (only in this arm), and the
proposal is stamped `verificationState: "unverified"` plus
`verificationBypassed: true` while Shared XR Memory, freshness checks, preview,
consent routing, and `mode_policy` remain identical. Outside a registered trial no
bypass is ever possible. The model cannot trigger or fake the skip — it is derived
from the trial registration in the artifact log.

Trial registration also accepts an optional `candidateTarget` (integer 1–5): the H4
per-trial switch between single-candidate (N=1) and best-of-N generation. It
travels with the study context, reaches the orchestrator turn via
`AGENTICXR_CANDIDATE_COUNT`, and is exported per trial as `candidateTargetCount`
alongside the observed `candidatesGenerated` and the surfaced candidate's rank.

## Event schema

Common optional join/evidence fields are:

| Field | Type/unit | Meaning |
|---|---|---|
| `eventType` | string | Typed event listed below. |
| `targetObjectId` | string | Stable Unity object identifier. |
| `artifactId` | string | Generated/committed artifact identifier. |
| `candidateId` / `candidateSetId` | string | H4 candidate and group identifiers. |
| `status` | string | Machine outcome, not free-form prose. |
| `reasonCode` | string | Non-identifying categorical reason. Export never includes free-form intent or error text. |
| `durationMs` | number/ms | Duration of the named operation. |
| `verificationDurationMs` | number/ms | Unity Verification Space duration for one candidate. |
| `commitAttachDurationMs` | number/ms | Unity live compilation/attachment duration. |
| `timestampAgeMs` | number/ms | Snapshot/message age when checked/applied. |
| `correlationIdValid` | boolean | Correlation passed format/invalidation checks. |
| `targetObjectValid` | boolean | Stable target existed at the check/application point. |
| `validationState` | string | `accepted`, `rejected`, or unavailable. |
| `riskScore` | number | Normalized proposal risk in `[0,1]`. |
| `failureStage` | string | `compile`, `validation`, or `runtime`. |
| `verificationOutcome` | string | `apply`, `clarify`, `repair`, or `reject`. |
| `unsafeProposal` | boolean | Validator/policy classified the proposal as unsafe. |
| `blockedUnsafeArtifact` | boolean | Capability policy prevented execution. |
| `verificationLiveMismatch` | boolean | Dry-run result disagreed with live result. |
| `verificationBypassed` | boolean | The `agenticxr_no_verification` arm skipped this dry-run/proposal's verification. |
| `verificationState` | string | `unverified` on proposals whose dry-run was bypassed; absent otherwise. |
| `consentRoute` | string | `automatic_low_risk` or `explicit_confirmation`; used for per-route decision counts. |
| `selectedCandidateRank` | integer | One-based deterministic rank. |
| `selectedCandidateScore` | number | Deterministic selection score; not a probability. |
| `goalId` / `goalIteration` | string / integer | Persistent bounded goal and trigger-driven iteration. |
| `verificationLevel` | integer | Goal verifier route, 1 through 5. |
| `goalStatus` | string | Persisted state such as `waiting_trigger`, `completed`, or `awaiting_human`. |
| `boundExhausted` | boolean | Attempt or wall-time bound stopped autonomous work. |
| `resolutionLatencyMs` | number/ms | Delay between queuing and resolving later ground truth. |
| `speculative` | boolean | Event belongs to non-committing future-goal preparation. |

### Emission points and study mapping

| Event type | Emitter | Key fields | Objective measure / hypothesis | Evidence |
|---|---|---|---|---|
| `study_trial_started` | `ArtifactLog.startStudyTrial` / CLI / MCP | all identity fields | Trial join and total task start | Implemented and tested |
| `study_trial_ended` | `ArtifactLog.endStudyTrial` / CLI / MCP | completion, success, quality rubric | Task completion, success/quality, total time; H1 | Implemented and tested; rubric values are researcher supplied |
| `intent_captured` | orchestrator record-intent or code runtime | timestamp only; no transcript | Start timestamp for both H1 latencies | Implemented and mock-tested |
| `agent_status_sent` | status sender | status | Delivery diagnostics, not visibility | Implemented and mock-tested |
| `agent_status_surfaced` | Unity `AgentStatusVisible` acknowledgement | status | Immediate acknowledgement and status visibility; H1 | Source-complete and mock-tested; not live-observed |
| `agent_acknowledgement_surfaced` | visible agent utterance path | timestamp | Immediate acknowledgement; H1 | Wired; no live observation |
| `propose_artifact` | bridge/baseline runtime | candidate, risk, operation, outcome | Generated-artifact count, unsafe proposal, preview start; H1/H2/H4 | Agentic path mock-tested; baseline only records code sent |
| `proposal_preview_surfaced` | outbound typed proposal | candidate/version | Preview-to-commit start | Mock-tested; live panel not observed |
| `simulate_artifact` / `verification_outcome` | Verification Space | outcome, duration, candidate | Apply/clarify/repair/reject and verification time; H2/H4 | Apply/reject mock-tested; clarify/repair use structured event API until live flow emits them |
| `simulate_artifact` with `status: skipped_no_verification` | condition-gated bridge bypass | `verificationBypassed`, `verificationOutcome: bypassed` | H2 no-verification arm: dry-run skipped, proposal marked unverified | Implemented, deterministic- and mock-integration-tested |
| `candidate_selected` / `candidate_rejected` | deterministic ranker | rank, score, candidate IDs | Candidate count/selection/rank/score; H4 | Implemented and mock-tested |
| `candidate_selection` | deterministic ranker | selected candidate and set size | H4 grouping | Implemented and mock-tested |
| `proposal_gate_checked` | backend Proposal Gate | validity, age, stale flag | Correlation/target validity, timestamp age, stale proposal; H2 | Implemented and deterministic-tested |
| `stale_proposal` | bridge result/focus comparison | staleness evidence | Stale proposal and grounding error; H2 | Mock-tested |
| `stale_application` or `staleApplication: true` | live result classification | object/result status | Stale application; H2 | Classification wired and mock-observable; no live observation |
| `artifactresult` / `commitaccepted` / `commitrejected` | Unity result channel | result, failure, timing, validity | Validated execution, compile/validation/runtime failure, preview-to-commit; H1/H2 | Mock-tested; Unity source not live-observed |
| `verification_live_mismatch` | explicit runtime event or exporter comparison by candidate | candidate, outcomes | Verification-Space/live mismatch; H2 | Derivation tested; needs real Unity execution for empirical values |
| `memory_retrieval` | timed memory wrapper | operation, duration | Memory-retrieval latency; H2 | Implemented and mock-tested |
| `validation_failure` / `artifact_pipeline_failure` | lifecycle, mode, transport, capability gates | stage, reason code | Failure and unsafe/block counts; H2 | Deterministic/mock-tested; capability rejection not live-observed |
| `user_decision:approved` | Unity consent panel | status | Confirmation and H4 first acceptance | Mock-tested; real panel not live-observed |
| `user_decision:rejected` / `user_decision:timeout` | Unity consent panel | status/reason | Rejection/timeout | Wired; real panel not live-observed |
| `user_decision:undo` | Unity undo control | artifact | Undo | Wired; real control not live-observed |
| `rollbackresult` | Unity rollback | status/artifact | Rollback | Wired and deterministic contract-tested; not live-observed |
| `repair_attempt` / `clarification_turn` / `revision_requested` | structured study event API | timestamp, candidate/target | Repair effort, clarification, H4 revision | Partially wired; orchestration must emit when these branches occur |
| `interruption` / `resumption` | overlapping speech detector or structured runtime event | timestamp | Interruption count/duration | Overlapping speech wired; controller/gaze interruption requires live runtime event |
| `grounding_error` | structured runtime event or invalid join/target inference | validity flags | Grounding error; H2 | Invalid/stale inference tested; semantic rubric error remains researcher/runtime supplied |
| `unsafe_proposal` | validator/policy structured event | risk/reason code | Unsafe proposal; H2 | Wired; requires live unsafe test cases |
| `goal_created` / `goal_state` / `goal_triggered` | bounded goal controller | goal, iteration, verifier, trigger | Persistent loop reconstruction | Implemented and deterministic/mock-tested |
| `goal_iteration_executed` / `goal_verification_outcome` | bounded goal controller | iteration and verifier outcome | Iterations and verification route | Implemented and deterministic/mock-tested |
| `goal_escalated` / `goal_bound_exhausted` | bounded goal controller | reason and exhausted flag | Escalation and bounded autonomy | Implemented and deterministic-tested |
| `goal_delayed_evaluation_pending` / `goal_delayed_evaluation_resolved` | goal memory | signal and resolution latency | Delayed ground-truth resolution | Implemented and deterministic-tested |
| `goal_terminated` / `goal_killed` | bounded goal controller | iterations, wall time, status | Completion/termination | Implemented and deterministic/mock-tested |
| `idle_prediction_triggered` / `idle_prediction_finished` / `idle_prediction_preempted` | code runtime | speculative flag and status | Prediction frequency and preemption | Source-complete; deterministic predictor tested, real idle timer not live-observed |
| `speculative_candidate_prepared` / `speculative_candidate_adopted` | future-goal predictor | pinned scene tuple and candidate | Prepared/reused prediction counts | Implemented and deterministic-tested |
| `activity_assist_triggered` / `activity_assist_suppressed` | continuous activity monitor | threshold, structured signal types, L2 route | Continuous-assistance opportunities | Source-complete and deterministic-tested; not live-observed |
| `continuous_assist_started` / `continuous_assist_finished` / `continuous_assist_preempted` | continuous monitor | context trigger and bounded process status | Continuous assistance and interruption | Source-complete; mock stream tested, live agent/device unobserved |

## Per-trial CSV

`trials.csv` contains one row per
`participantId × sessionId × trialId × condition × taskId`. It has these groups:

- Identity: the six trial identity fields plus `correlationIds`.
- Task performance: `taskCompletion`, `taskSuccess`, `taskQualityScore`,
  `taskQualitySignalsJson`, `totalTaskTimeMs`.
- Latency timestamps and deltas: `intentCapturedAtUtc`,
  `firstAcknowledgementAtUtc`, `firstProposalAtUtc`, `validatedExecutionAtUtc`,
  `immediateAcknowledgementLatencyMs`, `proposalLatencyMs`,
  `validatedExecutionLatencyMs`.
- Artifacts/failures: `generatedArtifactCount`, compile/validation/runtime failure
  counts, four verification-outcome counts,
  `verificationCandidateDurationsMsJson`, `verificationTimeTotalMs`,
  `verificationBypassedCount` (H2 no-verification arm),
  `previewToCommitTimeMs`, `verificationLiveMismatchCount`.
- Grounding/safety: grounding/stale/invalid-ID/invalid-target counts,
  `timestampAgeAtApplicationMsJson`, memory latency list/mean,
  unsafe and blocked-unsafe counts.
- Repair/consent: repair, clarification, confirmation, rejection, undo, and rollback
  counts, plus `decisionRouteBreakdownJson` — per-consent-route (falling back to
  authoring mode) approved/rejected/timeout/undo counts.
- Interruption: interruption/resumption counts and `interruptionTotalTimeMs`.
- H4: `candidateTargetCount` (registered N), `candidatesGenerated` (observed),
  selected ID/rank/score, and `firstProposalAcceptedWithoutRevision`.
- Visibility: status count and `firstAgentStatusAtUtc`.
- Goal loops: goal/iteration counts, iterations to completion, verifier levels,
  escalation/bound-exhaustion counts, and delayed-resolution latencies.
- Speculation: idle prediction, prepared-candidate, and adopted-candidate counts.

JSON-list columns preserve per-candidate/per-application raw durations while scalar
columns support immediate analysis. A zero count is a real observed zero; an empty
cell means the pipeline had no applicable timestamp/value.

## Long-format CSV

`events.csv` contains one row per study event and only whitelisted structured fields.
It intentionally omits `intent`, generated code, transcript text, raw audio,
free-form validation summaries, and free-form errors. Its columns are the required
identity fields plus correlation/timestamp/event, object/artifact/candidate IDs,
machine status/reason code, durations, validity flags, rank/score, source, and the
machine-enum route/validation fields `authoringMode`, `consentRoute`,
`validationState`, and `verificationBypassed`.
Goal-loop rows additionally retain only structured goal/iteration/verifier/status,
bound, delayed-latency, and speculative fields. Objective text and prepared code are
not exported.

## Researcher workflow

From `Server`:

```powershell
node evaluation/study_trial.js start --participant=P001 --session=S001 --trial=T01 --condition=agenticxr_verification --task=door_guidance --mode=L4 --correlation=T01-root --candidates=3
```

`--condition=agenticxr_no_verification` activates the H2 dry-run bypass for the
trial (see "Condition semantics" above). `--candidates=1` selects the H4
single-candidate arm; omit it for the runtime default of three.

Run the task. Record non-inferable events when required:

```powershell
node evaluation/study_trial.js event --session=S001 --correlation=turn-123 --type=interruption
node evaluation/study_trial.js event --session=S001 --correlation=turn-123 --type=resumption
```

Close the trial using only protocol-defined structured rubric signals:

```powershell
node evaluation/study_trial.js end --session=S001 --trial=T01 --correlation=turn-123 --completed=true --success=true --quality-score=4 --quality-signals-json='{"rubricVersion":"v1","behaviorMatched":true}'
```

Export after the session:

```powershell
node evaluation/study_export.js --input=memory/data/artifact_log.jsonl --output-dir=evaluation/data/study-P001
```

This writes `trials.csv` and `events.csv`. Export fails rather than silently emitting
unjoinable data if a study event lacks a required identifier.

## Before collecting participants

Run `npm test` and `npm run test:integration`, then complete and retain evidence from:

1. Unity Play Mode status-visible acknowledgement.
2. One approve, reject, timeout, undo, and rollback from the actual consent panel.
3. One successful and one compile/capability/runtime failure.
4. One real Verification-Space/live execution comparison.
5. One controller/speech interruption and resumption during deliberation.
6. One baseline task with a Unity-side direct-attach result acknowledgement.

Until those checks pass, the fields are captured in the pipeline and mock-tested, not
live/on-device validated.
