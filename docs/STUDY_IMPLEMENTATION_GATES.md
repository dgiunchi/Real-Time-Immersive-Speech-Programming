# AgenticXR study implementation gates

Status at branch commit `318628d` (audit performed before the next implementation
wave). The paper remains authoritative for H1--H4, L1--L5, pairings, measures,
and safety. `AgenticXR_complete_study_method.tex` is a proposed operationalisation
pending investigator review.

Evidence labels used here are deliberately narrow:

- **implemented**: executable source exists;
- **deterministic-tested**: local tests exercise the relevant contract;
- **mock-integrated**: the Node/Ubiq/mock-Unity path completed;
- **Unity-compiled**: Unity 6000.3.9f1 compiled the source;
- **physically exercised**: observed on the study PC and Quest hardware.

Passing a lower evidence level never implies a higher one.

## Audited baseline

The branch has implemented and tested generic study identity, counterbalancing,
per-participant storage, telemetry/export, transcript privacy, condition switches,
candidate accounting, preview acknowledgement, approve/reject/undo, state-isolated
reset, and withdrawal. The Node suite passes 575 assertions; the mock integration
passes both verification arms; and the Unity source compiles in 6000.3.9f1.

That evidence does **not** establish the five participant tasks:

| Gate | Required behaviour | Audited state | Gate closes when |
|---|---|---|---|
| G-TASK-ID | Every trial carries a frozen Method version and A/B task variant | Missing | plan, event, export, preflight, and reset tests preserve both fields |
| G-L1 | A genuine `system_opportunity` can start one low-risk local reversible automatic turn | Missing | deterministic trigger/policy tests, mock visible apply/undo, then physical task pass |
| G-L2-SCENE | Authored region and inert-anchor instances exist for both station variants | Component source only; zero tracked scene/prefab instances | scene validator plus physical region/proximity/dwell task pass |
| G-L3 | A clarification is surfaced, answered, cancelled/timed out, and resumed on one correlation chain | Policy rejection exists; interaction state machine missing | multi-process tests and live two-utterance task pass |
| G-L4-REVISE | A visible proposal can enter a revision turn without being approved | Approve/reject/undo/timeout exist; Revise missing | Unity/server revision tests and physical revise pass |
| G-L5 | Three speech turns preserve conversational and artifact identity and apply edits rather than accidental creates | Missing | state/restart/timeout tests plus physical two-revision task pass |
| G-BASELINE-L5 | Baseline replacement-versus-accumulation semantics are frozen and logged | Missing | documented deterministic rule and task test |
| G-SCENE | Dedicated matched A/B task scenes and separate training objects exist | Missing | manifest validation and two-researcher walkthrough pass |
| G-RUBRIC | Versioned task criteria and two-rater capture are approved | Placeholder schema blocks collection | approved schema, duplicate/range tests, rater rehearsal |
| G-QUESTION | Exact approved questionnaire forms and timing exist | Placeholder schema blocks collection | approval record and full operator capture rehearsal |
| G-HARDWARE | Every mode/condition/failure/reset path works on Quest over Link | Not established | signed physical acceptance matrix |

## Source findings behind the missing-mode gates

### L1

`Server/samples/apps/code_runtime_generator/app.js` starts idle prediction only in
`AGENTICXR_SPECULATIVE_ONLY` mode. Speculative candidates cannot commit. The active
continuous path in `Server/orchestrator/continuous_monitor.js` exports
`AGENTICXR_TRIGGER_SOURCE=context`; the mode policy therefore admits L2, not the
paper's distinct L1 `system_opportunity`. No executable L1 trigger was found.

### L2 scene

`Unity/Assets/AgenticCache/ImplicitTriggerSensors.cs` implements region,
proximity, and head-direction dwell transitions, and
`AgenticInertAnchor.cs` publishes neutral anchor context. A GUID search of tracked
`.unity` and `.prefab` files found no authored instances of either study script.

### L3

`Server/orchestrator/mode_policy.js` rejects an L3 artifact when
`detailResolved !== true`, but no typed clarification request/response, pending
clarification store, answer timeout, or resume path exists. Each push-to-talk start
currently creates a new correlation ID; the first agent turn cannot wait for and
continue from the second utterance as the paper requires.

### L4 revise

`AgenticXRConsentPanel.cs` exposes Approve, Cancel/Reject, Undo, and Reset Trial.
`CacheExchangeManager.cs` implements approval, rejection, timeout rejection,
rollback, and proposal visibility. It exposes no Revise action or revision state.

### L5

The artifact lifecycle supports create/edit/remove, but the speech runtime deletes a
turn correlation when its orchestrator process exits. The next push-to-talk start
creates a new correlation, and no persistent in-memory conversational thread is
provided to the new orchestrator. Artifact history is not equivalent to the paper's
multi-turn conversational state.

## Implementation order

1. Close G-TASK-ID without changing participant-facing content.
2. Add executable contract tests for G-L1, G-L3, G-L4-REVISE, G-L5, and
   G-BASELINE-L5 before implementing their state machines.
3. Implement the generic interaction state layer and protocol messages.
4. Add the task manifest and scene-readiness validator before authoring final scenes.
5. Freeze investigator-approved wording/rubrics and then author the A/B scenes.
6. Run deterministic, mock, Unity compile, researcher-only physical, and participant
   gates in that order.

Every logical change is a local commit on `codex/paper-study-implementation`. No
remote update is incorporated automatically, and no local implementation commit is
evidence of participant readiness until the higher gates pass.

## Acceptance-contract checkpoint

`Server/study/interaction_contract.v1.json` freezes the engineering acceptance
states for L1, L3, L4, L5 and the baseline L5 replacement rule. Its presence closes
only the contract-definition part of step 2. A gate remains open until executable
state transitions, integration evidence, and the physical pass named above exist.
