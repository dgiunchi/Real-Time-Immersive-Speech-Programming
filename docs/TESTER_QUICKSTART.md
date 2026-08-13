# Tester quickstart — first live AgenticXR acceptance run

Written 2026-08-12. Audience: a tester/developer taking this system through its
**first live end-to-end run** (nothing here has ever been observed live — only
deterministic tests, mock integration, and Unity batch compilation have passed).
Your job: run the staged sequence below, fix what breaks, and record what you
observed. Deep references: `docs/SETUP_INSTRUCTIONS.md` (full guide),
`docs/LIVE_SYSTEM_REQUIREMENTS.md` (the authoritative checklist, esp. §9),
`docs/study-logging-schema.md` (study logging/export contract).

## What is already done for you

- Repo: `D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming`,
  branch `agenticxr-live`. `npm install` has been run in `Server\`.
- **Secrets are already configured** in `Server\.env` (gitignored):
  `ANTHROPIC_API_KEY`, `OPENAI_API_KEY` (baseline arm), and `STT_HTTP_URL`.
  Every `npm run ...` entry point loads them automatically — you do NOT need
  `$env:...` exports. (If you do set one in PowerShell, it overrides the file.)
  Never commit, print, or paste these values anywhere.
- STT server is live: `curl.exe http://130.136.2.161:50101/health` returns a
  faster-whisper status JSON. The server PC must be able to reach that host.
- The legacy Python venv for the baseline arm exists (`Server\samples\venv`).
- Passing as of 2026-08-12 — re-run these first; if any fail, stop and fix:

```powershell
cd "D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming\Server"
$env:AGENTICXR_MODE="claude"
npm run doctor            # expect: Setup looks complete.
npm test                  # expect: [cache_contract_test] PASS (306 assertions)
npm run test:integration  # expect: [mock_integration] PASS  (needs port 8009 free)
```

## Stage 1 — first live Claude turn, no Unity (do this before anything else)

Isolates the model/orchestration from mic, STT, Unity, and headset. Three
PowerShell terminals in `Server\`:

```powershell
# Terminal 1 - room server
node node_modules/ubiq/app.js config/default.json

# Terminal 2 - mock Unity peer (stands in for the headset)
node mcp/unity_scene_bridge/mock_unity_peer.js

# Terminal 3 - one real Claude turn (spends real API credit, roughly ~$1)
node orchestrator/app.js "make this sphere slowly pulse red" obj-mock-0001 manual-test-session
```

Expected in Terminal 3, in order: router narration → Scene Analyst grounding →
three generated candidates → per-candidate Validator verdict + `simulate_artifact`
(mock peer answers `simulated`) → `rank_artifact_candidates` → one
`propose_artifact` → mock `committed` → Version/Memory confirmation. A stalled or
cleanly-rejected turn with a plain-language reason is a *correct* outcome; a hang,
crash, or stack trace is a bug.

Common failures: `401/authentication` → key problem in `Server\.env`;
`EADDRINUSE`/connect timeout → a stale `node` process owns port 8009 — find it by
**port** (`netstat -ano | findstr :8009`), not by process name, and kill it.

## Stage 2 — Unity Editor Play Mode (same machine)

1. Open `Unity\` with Unity **6000.3.9f1**, scene
   `Assets/Demos/DynamicCompiler/DynamicCompiler.unity`.
2. Room Client: `localhost`, TCP `8009` is fine in the Editor (LAN IP is only
   needed for Quest). Authorable objects must be tagged `game`.
3. Stop Terminals 1–2 from Stage 1, then start the full server instead:
   `npm run start:agenticxr` (hosts the room itself — don't run a second room).
4. Enter Play Mode. The AgenticXR runtime self-installs (registry, publisher,
   consent panel, implicit trigger sensors). Watch the Unity Console and the
   server terminal.
5. Run the acceptance sequence (details: `SETUP_INSTRUCTIONS.md` §8): select an
   object with the ray, push-to-talk (left trigger; desktop fallbacks:
   Enter=Approve, Escape=Reject, U=Undo), say
   "Make this object slowly pulse red", release, then Approve → verify the
   behavior runs; Undo → verify it reverts; repeat with Reject and a timeout.
6. Also confirm the **implicit sensors** (never live-observed): approach an
   object → `proximity` events; look at one ~1 s → `gaze` dwell; add an
   `AgenticRegionVolume` component (set `regionId`) to any object with a box
   region → walking the camera into it emits `locomotion`. Verify arrivals via
   the server logs / `get_activity_stream`.

This stage completes `LIVE_SYSTEM_REQUIREMENTS.md` §9 items up to the Quest rows
and the "Before collecting participants" list in `study-logging-schema.md`
(approve/reject/timeout/undo/rollback, one success + one failure, one
Verification-Space-vs-live comparison, one interruption/resumption).

## Stage 3 — Quest

Follow `SETUP_INSTRUCTIONS.md` §5–§8: Android build, server **LAN IP** (never
`localhost` on Quest), firewall TCP 8009, mic permission. Known risk (§9 there):
the world-space panel may render but ignore XR pointer clicks — if desktop
fallback keys work, the backend is fine and the fix is wiring the generated
Canvas to the tracked-device UI input module. That fix is expected to land on
you; the backend does not need changing for it.

## Stage 4 — study-trial dry run (no participant)

```powershell
cd "D:\...\Server"
node evaluation/study_trial.js start --participant=PILOT --session=S-PILOT --trial=T00 --condition=agenticxr_verification --task=pilot-task --mode=L4 --correlation=T00-root --candidates=3
# ...perform one full authoring turn in Play Mode...
node evaluation/study_trial.js end --session=S-PILOT --trial=T00 --correlation=T00-root --completed=true --success=true
node evaluation/study_export.js --output-dir=evaluation/data/pilot
```

Expect one condition-stamped row in `trials.csv` with real (non-mock) latencies.
Repeat once with `--condition=agenticxr_no_verification --candidates=1` and
confirm the row shows `verificationBypassedCount > 0`, `candidatesGenerated = 1`.
The exporter failing loudly on missing identity is intended behavior.

## Known open items you may hit

- **Quest panel raycaster** (Stage 3 note above).
- **Baseline attach acknowledgement** (added 2026-08-13, source-complete): in a
  baseline run, a legacy attach should produce a `CodeAttachResult` reply and an
  `artifactresult` study event with `commitAttachDurationMs` — verify this once
  in Play Mode during your baseline check; it has not been observed live yet.
- Continuous assistance and idle prediction are **off by default** and must stay
  off for acceptance runs (they spend API credit; see `SETUP_INSTRUCTIONS.md` §2).

## Recording results (required)

Append what you actually observed to `docs/progress-log.md` using its vocabulary
— *source-complete / mock-tested / live-exercised* — and only promote a claim to
**live-exercised** for things you personally watched happen. If something fails,
capture: the exact failed step, server terminal output around it, Unity Console
text, and (on Quest) an `adb logcat` excerpt — never API keys or participant
data. Runtime evaluation events land in `Server\evaluation\data\*.jsonl`
(gitignored); export a technical report with
`node evaluation/report.js --source=live-model`.
