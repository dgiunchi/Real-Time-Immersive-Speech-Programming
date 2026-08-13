# AgenticXR user study — experimenter briefing

Shareable briefing for the person running the study sessions. Written 2026-08-12.
Deep references live in the repo: `docs/TESTER_QUICKSTART.md` (staged first-run
validation), `docs/SETUP_INSTRUCTIONS.md` (full setup), `docs/study-logging-schema.md`
(logging/export contract), `docs/LIVE_SYSTEM_REQUIREMENTS.md` (authoritative checklist).

---

Hi! You'll be running the AgenticXR user study. The system lets a person in VR
select an object, speak a request, and have an AI agent generate, verify, and —
after the person approves — attach live behaviour to that object. Below is
everything you need: what's already set up, what you must do once, and the exact
routine for each participant session.

## 1. What is already set up

- Study PC: `D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming`
  (branch `agenticxr-live`). Node dependencies installed; all automated tests pass.
- API keys (Anthropic + OpenAI) and the speech-to-text endpoint are configured in
  `Server\.env` on that PC. You never need to touch or export them. Never commit,
  copy, or screenshot that file.
- Speech-to-text server is running at `http://130.136.2.161:50101` (health check:
  `curl.exe http://130.136.2.161:50101/health`). The study PC must reach that host.

## 2. One-time setup you must complete (before any participant)

1. **Validate the pipeline in stages** — follow `docs/TESTER_QUICKSTART.md` top to
   bottom: sanity tests → one live agent turn without Unity → Unity Editor Play
   Mode acceptance (approve/reject/undo, implicit sensors) → Quest build.
   Nothing has been observed live yet; this validation is part of your job.
2. **Quest + network**: Quest in developer mode, same non-isolated LAN as the PC,
   Windows firewall open on inbound TCP 8009, Unity Room Client set to the PC's
   LAN IPv4 (not localhost), microphone permission approved in the headset.
   Details: `docs/SETUP_INSTRUCTIONS.md` §5–§6.
3. **Study scenes**: authorable target objects must be tagged `game`; for the
   implicit-trigger tasks (L1/L2), place `AgenticRegionVolume` components (each
   with a `regionId`) on the doorways/stations the protocol references.
4. **Ethics**: no real participant may be recruited or recorded before the
   ethics/IRB approval and consent materials are in place. Participants are
   identified ONLY by pseudonymous codes (P001, ...), never names or emails.

## 3. Study design in one table

Each participant does five task types; each task runs under two conditions:

| Tasks | Comparison | Condition strings to register |
|---|---|---|
| L1–L2 (implicit triggers) | full AgenticXR vs. verification bypassed (H2) | `agenticxr_verification` vs. `agenticxr_no_verification` |
| L3–L5 (explicit speech) | full AgenticXR vs. DreamCodeVR baseline (H1) | `agenticxr_verification` vs. `baseline` |
| L4–L5 (nested, H4) | one candidate vs. best-of-several | add `--candidates=1` vs. `--candidates=3` |

The registered condition does the work for you: `agenticxr_no_verification`
automatically skips the verification dry-runs (and marks proposals unverified);
`--candidates` sets how many solutions the agent drafts. You never toggle these
in code.

## 4. Per-session routine

**Start the server** (PowerShell, in `...\Server`):

- AgenticXR conditions: `npm run start:agenticxr`
- Baseline condition trials: stop it and run `npm run start:code-runtime-generator`
  instead (the original single-shot pipeline). Switch modes between trials as the
  counterbalancing order requires; start the server BEFORE launching the headset app.

**For each trial** (PowerShell, in `...\Server`):

```powershell
# 1. Register the trial BEFORE the participant starts the task
node evaluation/study_trial.js start --participant=P001 --session=S001 --trial=T01 `
  --condition=agenticxr_verification --task=<taskId> --mode=L4 --correlation=T01-root --candidates=3

# 2. Participant performs the task (select object -> hold left trigger -> speak -> release -> approve/reject/undo in headset)

# 3. Record events the system cannot infer, when they happen (optional, repeatable)
node evaluation/study_trial.js event --session=S001 --correlation=T01-root --type=interruption

# 4. Close the trial with your rubric judgment
node evaluation/study_trial.js end --session=S001 --trial=T01 --correlation=T01-root `
  --completed=true --success=true --quality-score=4 --quality-signals-json='{"rubricVersion":"v1","behaviorMatched":true}'
```

Registration is mandatory: study events are refused without a registered trial,
and the exporter fails loudly on incomplete identity. One session = one
participant; one trial = one participant x task x condition.

**After each participant** — export and archive:

```powershell
node evaluation/study_export.js --output-dir=evaluation/data/study-P001
```

This writes `trials.csv` (one analysis row per trial) and `events.csv`. Copy the
export folder AND `Server\memory\data\artifact_log.jsonl` to the secure study
storage, then archive/rotate the log before the next participant.

## 5. Rules

- Pseudonymous IDs only; no names, emails, audio files, or transcripts in any log.
- Continuous assistance and idle prediction stay OFF (they are off by default and
  automatically suppressed during trials — do not enable their env switches
  unless the approved protocol explicitly includes them).
- If anything fails: note the exact step, save the server terminal output and
  Unity Console text (never the `.env` contents), and check
  `docs/SETUP_INSTRUCTIONS.md` §10 troubleshooting order.
- Record what you validated in `docs/progress-log.md` using its vocabulary
  (source-complete / mock-tested / live-exercised) — claim only what you saw.

Known open item you may encounter: the Quest world-space Approve/Reject/Undo
panel may need its Canvas wired to the tracked-device input module if buttons
render but ignore the pointer (desktop fallback keys: Enter=Approve,
Esc=Reject, U=Undo).

Questions / anything broken: contact Daniele.
