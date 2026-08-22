# AgenticXR paper-study operator runbook

This runbook implements the planned study in section 7 of *Worlds That Think
With Us*. The machine-readable source is `Server/study/protocol.v1.json`.
Repository comments and commit messages are implementation evidence, not
authority for changing H1-H4, L1-L5, condition pairings, measures, or safety.

## Stop gates

Do not run a human participant until all of the following are true:

1. `Server/study/approvals.local.json` exists and every gate copied from
   `approvals.example.json` is documented as approved.
2. Exact approved item wording/anchors are inserted in
   `questionnaires.v1.json`; do not paraphrase the validated scales or invent
   paper-specific items.
3. Every task in `rubrics.v1.json` has an approved, versioned, condition-blind
   success/quality rubric.
4. The physical Quest-over-Link acceptance matrix below passes on the actual
   study PC, headset, controllers, network, STT endpoint, and model accounts.
5. A full two-condition pilot exports ten accepted trials and zero rejected
   trials with no participant-identifying text.

Use `--mode=researcher-dry-run` with reserved `P900`-`P999` IDs for bench tests.
Use `--mode=human-session` for participants. A mode is mandatory and there is no
approval override.

## Supported runtime

- Unity `6000.3.9f1`.
- Windows x64 PC-VR player, OpenXR, Quest-class headset through Quest Link or
  Air Link.
- Standalone Quest/Android is not supported: the study depends on Roslyn runtime
  compilation in the Windows Mono player.
- Raw audio is held only long enough for STT. Verbatim transcript persistence is
  off unless `STUDY_DEBUG_TRANSCRIPTS=1`; that flag must be off in preflight.

## One-time setup

From `Server`:

```powershell
npm install
npm test
npm run test:integration
```

Set `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, and `STT_HTTP_URL` in the operator's
private environment. Never paste them into Git, plans, logs, screenshots, or
participant notes.

Create the participant's deterministic counterbalanced plan:

```powershell
npm run study:operator -- plan --participant=P001
```

The plan contains ten trials: L1 then L2 in fixed order (full vs no-dry-run),
then a counterbalanced permutation of L3-L5 (full vs baseline). The full L4/L5
trials receive one N=1 and one N=3 assignment, order balanced by participant.
Condition labels are never announced to the participant.

## Before every trial

Ask the operator for the required runtime mode:

```powershell
npm run study:operator -- runtime --participant=P001 --trial=T01
```

Set the exact `AGENTICXR_ARTIFACT_LOG`, `AGENTICXR_MODE`, and
`STUDY_DEBUG_TRANSCRIPTS=0` values it prints. Restart the runtime whenever the
mode changes:

- `claude`: `npm run start:agenticxr`
- `legacy`: `npm run start:code-runtime-generator`

Run preflight in a second terminal carrying the same environment:

```powershell
npm run study:operator -- preflight --mode=human-session --participant=P001
```

Preflight checks protocol identity/count, Unity version, both model credentials,
STT, transcript privacy, participant-specific log routing, no open trial, and all
human approval gates. Do not override a failed human preflight.

Start only the planned trial:

```powershell
npm run study:operator -- start --mode=human-session --participant=P001 --trial=T01
```

The command refuses overlapping trials, wrong runtime mode, incorrect log path,
or artifacts left active from the previous condition.

## During and after a trial

Use the neutral script in `STUDY_SESSION_SCRIPT.md`. If an interruption cannot be
inferred automatically:

```powershell
npm run study:operator -- event --participant=P001 --session=P001-S01 --correlation=TURN-ID --type=interruption
npm run study:operator -- event --participant=P001 --session=P001-S01 --correlation=TURN-ID --type=resumption
```

For an unrecoverable technical failure:

```powershell
npm run study:operator -- abort --participant=P001 --trial=T01 --reason-code=stt_unavailable
```

For a normal end, record only whether the interaction trial completed:

```powershell
npm run study:operator -- end --participant=P001 --trial=T01 --completed=true
```

After ending a trial, trigger the Unity `TrialReset` and verify that generated
objects/behaviours, transforms, active state, and target-local material instances
are restored. The next start fails while any active artifact remains.

Task success and quality are entered later through the condition-blind `rubric`
command after the approved coding process; ad-hoc rubric JSON is rejected at trial
close. Questionnaire collection is fail-closed: the operator rejects any item without
approved verbatim wording and anchors. H4 perceived latency is collected
immediately after the full L4/L5 proposal, not at the end of the block.

## Export, recovery, and withdrawal

```powershell
npm run study:operator -- status --participant=P001
npm run study:operator -- export --participant=P001
```

Each participant is isolated under
`Server/evaluation/data/participants/P001/`. Export writes `trials.csv`,
`events.csv`, `questionnaire_responses.csv`, `rubric_ratings.csv`,
`rejected_trials.json`, and `export_manifest.json`. A bad trial is
quarantined while valid rows are preserved, but the process exits non-zero when
anything is rejected. Never analyse a manifest with a non-zero rejected count or
fewer accepted trials than expected without a documented protocol decision.

Withdrawal is exact-ID and destructive by design. Verify the participant code
against the approved withdrawal request, then run:

```powershell
npm run study:operator -- withdraw --participant=P001 --confirm=P001 --yes-delete=true
```

This deletes only that participant's isolated directory. Confirm any separately
approved recording store is also handled under the ethics protocol; the code
cannot delete storage it does not own.

## Physical acceptance matrix

Technical preflight now proves that the generated scene references the canonical Ubiq XR player,
its tracked/controller interaction prefabs, a Teleport-tagged floor, the study-safe runtime compiler,
the expected graspable task objects, and both controller-usable L3 buttons. This is Gate 7:
physical executability. It closes the earlier review gap where declaration consistency could pass
even though a participant could not inhabit or act in the scene. It does not replace headset rehearsal.

Interpret the tasks conservatively. L4 uses a trial-local, non-persistent practice door and two
scripted non-colliding proxies so the baseline remains safe; consequently its consent judgement has
lower real shared-resource stakes than deployment consent. L5 is a standardized two-step sequential
revision task, not unrestricted open-ended co-authoring; `priorRequirementRestatementCount` measures
the cost of retaining the first requirement through the second revision.

For H2, the paper and locked plan define grounding-error counts per trial. The export also derives
candidate, dry-run, visible-proposal, application-attempt, commit, error-opportunity, and task-clock
exposures from the append-only journal. Whether the confirmatory model should retain errors per trial
or use attempted applications as an offset remains an investigator/preregistration decision; do not
silently change the locked analysis-plan hash.

The paper defines L1/L2 triggers and measures but no separate participant-facing primary activity, so
none has been invented in the build. If the investigator wants a stronger intrusion/opportunity context,
approve and preregister one condition-independent option before piloting: (a) a visual workshop-inspection
checklist, noting that directed gaze may contaminate L2 gaze triggers; (b) a manual sorting/assembly task,
noting added motor workload and competition with agent-authored object changes; or (c) anomaly monitoring,
noting added artificial cognitive load. Each option requires revised task cards, trigger validation, timing,
and detector/logging review rather than an informal experimenter instruction.

Retain dated evidence for each item before piloting:

- headset fit, tracking, controller selection, push-to-talk start/stop;
- tracked head and both hands move correctly after entering Play Mode;
- joystick and teleport movement can safely reach both L2 and L4 authored regions;
- every L1 tool, L2 part, and L3 marker can be grasped, moved, released, and reset;
- both L3 done buttons respond once to a controller use and never fire from mere proximity;
- ray/UI interaction remains legible at the authored 2.6-4.0 m task distances;
- correct trial identity on the first cross-process live event;
- visible listening/transcribing/thinking/heard status and acknowledgement;
- L1 automatic local reversible behavior and undo;
- L2 discrete region/proximity/head-ray-dwell trigger and undo;
- L3 clarification/answer/cancel under one correlation chain;
- L4 approve, reject, revise, timeout-to-reject, and undo;
- L5 multi-turn revision and final approval;
- full vs no-dry-run behavior with memory/cache/preview otherwise unchanged;
- baseline direct attach plus Unity `CodeAttachResult`;
- N=1 and N=3 generation with observed candidate count matching assignment;
- compile, capability, validation, runtime-watchdog, STT, and network failures;
- Verification-Space/live mismatch classification;
- trial reset with no transform, active-state, material, or generated-child leak;
- recovery after server restart and cache backfill;
- ten accepted exported trials, zero unjoined events, zero rejected trials;
- no transcript text, generated code, free-form intent, names, emails, raw audio,
  continuous trajectories, eye tracking, or camera data in the exported files.
