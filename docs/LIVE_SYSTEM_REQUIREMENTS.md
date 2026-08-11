# AgenticXR live-system requirements

Last reviewed: 2026-08-11

This is the living checklist for running the complete AgenticXR system with Unity,
Meta Quest, Ubiq, speech recognition, Claude orchestration, continuous activity
monitoring, bounded goals, and optional future-goal preparation.

Update this file whenever live testing reveals another credential, dependency,
hardware requirement, configuration step, or acceptance check.

## 1. Information required from the user

Record these before live integration:

- [ ] Quest model:
- [ ] Quest OS/version:
- [ ] Server PC operating system:
- [ ] Server PC LAN IPv4 address:
- [ ] Quest and server PC are on the same non-isolated LAN:
- [ ] Unity version is `6000.3.9f1`:
- [ ] Unity Android Build Support is installed:
- [ ] Android SDK, NDK, and OpenJDK are installed through Unity Hub:
- [ ] Faster Whisper STT endpoint:
- [ ] STT health endpoint, if available:
- [ ] First test target: Unity Editor or deployed Quest:
- [ ] Continuous proactive assistance enabled: yes/no
- [ ] Idle future-goal preparation enabled: yes/no
- [ ] Initial experience mode: productivity/training/entertainment/exploration
- [ ] Approved test scenario and expected behavior:

Do not write API keys, participant names, emails, raw transcripts, or other secrets
into this file.

## 2. Required credentials and services

### Anthropic

- [ ] Anthropic API account exists.
- [ ] Billing or API credits are enabled.
- [ ] `ANTHROPIC_API_KEY` is set only in the server PowerShell environment.

```powershell
$env:ANTHROPIC_API_KEY="your-real-key"
```

Never commit the key or place it in Unity assets, JSON configuration, documentation,
Git history, screenshots, or chat.

### Speech-to-text

- [ ] A Faster Whisper-compatible service is reachable from the Node server PC.
- [ ] It accepts `POST /stt/transcribe`.
- [ ] It accepts multipart field `file` containing 16 kHz mono 16-bit WAV.
- [ ] It accepts `language=en` and `beam_size=1`.
- [ ] It returns recognized text as the response body.
- [ ] `STT_HTTP_URL` points to the complete transcription endpoint.

```powershell
$env:STT_HTTP_URL="http://STT-HOST:50101/stt/transcribe"
```

The repository contains the STT client, not the Faster Whisper HTTP server. If no
endpoint exists, deployment requires a target machine, operating system, CPU/GPU
details, network address, and permission to install/run the service.

### Credentials not required

The AgenticXR Claude path does not require:

- OpenAI API key;
- Azure Speech key;
- separate MCP key;
- Claude Code login;
- legacy Python virtual environment.

## 3. Server software

- [ ] Node.js and npm are installed.
- [ ] Repository dependencies are installed from `Server`.
- [ ] Ubiq, Claude Agent SDK, MCP SDK, and Zod pass the setup doctor.
- [ ] TCP port `8009` is available.
- [ ] The runtime and scene bridge use the same Ubiq `roomGuid` and port.

```powershell
cd "D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming\Server"
npm install

$env:AGENTICXR_MODE="claude"
$env:ANTHROPIC_API_KEY="your-real-key"
$env:STT_HTTP_URL="http://STT-HOST:50101/stt/transcribe"

npm run doctor
npm test
npm run test:integration
```

Expected:

- [ ] `npm run doctor` ends with `Setup looks complete.`
- [ ] `npm test` passes.
- [ ] `npm run test:integration` ends with `[mock_integration] PASS`.

## 4. Windows and LAN configuration

- [ ] Server PC and Quest are on the same LAN.
- [ ] Guest/client isolation is disabled.
- [ ] A VPN is not separating the devices.
- [ ] Windows Firewall allows inbound TCP `8009`.
- [ ] Unity uses the server PC LAN address, not `localhost`.

Administrator PowerShell:

```powershell
New-NetFirewallRule `
  -DisplayName "AgenticXR Ubiq TCP 8009" `
  -Direction Inbound `
  -Protocol TCP `
  -LocalPort 8009 `
  -Action Allow
```

## 5. Unity requirements

- [ ] Open the `Unity` directory with Unity `6000.3.9f1`.
- [ ] Open `Assets/Demos/DynamicCompiler/DynamicCompiler.unity`.
- [ ] Configure the Ubiq Room Client with the server PC LAN IPv4 address.
- [ ] Configure TCP port `8009`.
- [ ] Confirm the room GUID matches the server configuration.
- [ ] Authorable objects have the `game` tag.
- [ ] Android is the active build platform.
- [ ] Oculus/Quest XR provider is enabled for Android.
- [ ] Android microphone permission is present.
- [ ] The project compiles without C# errors.

The automatic AgenticXR bootstrap should install the cache exchange manager, scene
publisher, runtime compiler integration, continuous sensor publication, implicit
trigger emitters (region volumes, proximity, head-ray gaze dwell), visible
status, and world-space Approve/Reject/Undo panel.

For the L1/L2 study tasks, author the implicit triggers in the scene:

- [ ] Add an `AgenticRegionVolume` component (with a `regionId`) to each doorway/
  station/target region the protocol references. Membership is polled against the
  head position; no Rigidbody or trigger-collider setup on the XR rig is needed.
- [ ] Confirm proximity enter/exit and gaze dwell events appear in
  `get_activity_stream` when moving toward / looking at a `game`-tagged object.
  Gaze is a head-direction ray with dwell, not eye tracking.

## 6. Quest requirements

- [ ] Meta Quest Developer Mode is enabled.
- [ ] USB debugging is approved.
- [ ] The application is built and installed.
- [ ] `RECORD_AUDIO` permission is approved in the headset.
- [ ] Quest can reach the server PC on TCP `8009`.
- [ ] The Ubiq room connection succeeds.
- [ ] Push-to-talk sends audio.
- [ ] Agent status is visible.
- [ ] Approve, Reject, and Undo can be selected with the XR pointer/controller.

The world-space button/ray interaction is not yet live-validated. If the panel
renders but buttons cannot be selected, inspect the tracked-device UI input module,
event camera, raycaster, and Quest controller bindings.

## 7. Continuous and predictive features

The long-lived activity observer starts by default. Proactive model calls and idle
prediction remain opt-in because they consume API credits.

### Continuous activity assistance

```powershell
$env:AGENTICXR_MONITOR_ENABLED="true"
$env:AGENTICXR_CONTINUOUS_ASSIST_ENABLED="true"
$env:AGENTICXR_ACTIVITY_THRESHOLD="1.1"
$env:AGENTICXR_ACTIVITY_WINDOW_MS="5000"
$env:AGENTICXR_ACTIVITY_COOLDOWN_MS="30000"
$env:AGENTICXR_CONTINUOUS_ASSIST_TIMEOUT_MS="120000"
```

- [ ] Continuous monitor joins Ubiq.
- [ ] Gaze/proximity/locomotion observations appear in the activity stream.
- [ ] Threshold crossing surfaces visible status.
- [ ] Context assistance remains L2.
- [ ] High-risk or unverifiable assistance requires confirmation/dialogue.
- [ ] Push-to-talk preempts continuous assistance.

### Idle future-goal preparation

```powershell
$env:AGENTICXR_IDLE_PREDICTION_ENABLED="true"
$env:AGENTICXR_IDLE_PREDICTION_THRESHOLD_MS="60000"
$env:AGENTICXR_IDLE_PREDICTION_COOLDOWN_MS="300000"
```

- [ ] Idle preparation never proposes or commits directly.
- [ ] Prepared candidates are tied to the current scene snapshot.
- [ ] Stale candidates are rejected.
- [ ] Reused candidates pass the complete normal pipeline.

Both features are suppressed during study trials unless the approved protocol sets:

```powershell
$env:AGENTICXR_STUDY_ALLOW_CONTINUOUS_ASSIST="true"
$env:AGENTICXR_STUDY_ALLOW_SPECULATION="true"
```

## 8. Complete startup

From `Server`, in the PowerShell window containing the environment variables:

```powershell
npm run start:agenticxr
```

This should start:

- Ubiq room server on TCP `8009`;
- Quest audio receiver;
- Faster Whisper STT client;
- Claude Agent SDK orchestration;
- MCP Unity scene bridge for agent turns;
- long-lived continuous activity monitor;
- Shared XR Memory and temporal logging;
- bounded goal and verification routing.

Start the server before launching the Unity Editor client or Quest application.

## 9. Live acceptance sequence

Run in this order:

- [ ] Server doctor, deterministic tests, and mock integration pass.
- [ ] Unity Editor joins the Ubiq room.
- [ ] Scene/sensor observations reach `get_activity_stream`.
- [ ] Walking into an `AgenticRegionVolume` produces a `locomotion` region event;
  approaching an object produces `proximity`; dwelling on it produces `gaze`.
- [ ] A typed/manual Claude request succeeds with mock or Editor Unity.
- [ ] Quest joins the same room.
- [ ] Quest push-to-talk reaches STT.
- [ ] Transcript starts a Claude orchestration turn.
- [ ] Scene grounding returns the selected stable object.
- [ ] Three candidates are generated and validated.
- [ ] Verification Space simulation succeeds.
- [ ] Low-risk automatic policy is enforced correctly.
- [ ] Confirmation-required proposal appears in the headset.
- [ ] Approve commits the generated behavior.
- [ ] Reject prevents application.
- [ ] Undo rolls back the latest behavior.
- [ ] Explicit speech preempts a continuous assistance turn.
- [ ] A bounded goal terminates or escalates rather than spinning.
- [ ] Application restart restores supported checkpoints and reports orphaned targets.

## 10. Evidence to provide when something fails

Provide only non-secret diagnostics:

- exact failed checklist step;
- server terminal output around the failure;
- `npm run doctor` output;
- Unity Console error text;
- Unity Player log or relevant `adb logcat` excerpt;
- Quest screenshot/video of visible status or consent UI;
- server PC IP and Quest network subnet, with sensitive addresses redacted if needed;
- whether the same step works in Unity Editor;
- whether continuous assistance and idle prediction were enabled.

Never include API keys, raw participant audio, identifying participant data, or
unredacted credentials.

## 11. Current verification boundary

Confirmed as of the review date:

- [x] Node deterministic tests pass (`258` assertions on 2026-07-31).
- [x] Local Ubiq + mock Unity integration passes on 2026-07-31.
- [x] Long-lived monitor joins mock Ubiq and observes activity.
- [x] Mock threshold crossing and monitor-only suppression pass.
- [x] Unity `6000.3.9f1` batch compilation succeeds.
- [x] Setup doctor passes in Claude mode when credential/endpoint presence is
  represented; this does not prove that either external service is reachable.

Still requires user/device validation:

- [ ] Real Anthropic request.
- [ ] Real Faster Whisper request.
- [ ] Unity Editor live orchestration.
- [ ] Quest LAN connection.
- [ ] Quest microphone and push-to-talk.
- [ ] Quest world-space consent controls.
- [ ] Real continuous-assistance behavior and preemption.
- [ ] Real generated-code compilation, attachment, watchdog, and undo.
- [ ] Performance, API cost, and latency measurement.
- [ ] Entertainment/productivity/training application-level evaluation.

## 12. Paper-to-code gap ledger

This ledger compares the repository with paper commit `00ef077` dated 2026-07-26.
It distinguishes missing implementation from implemented work that still lacks live
evidence.

### Implemented or scaffolded, but not yet live-evaluated

- [x] Unity implicit-trigger emitters for L1/L2 (authorable region volumes,
  proximity enter/exit, head-ray gaze dwell) compile in batch mode and their wire
  contract is deterministic-tested; the polled events have not been observed in
  Play Mode or on device (2026-08-11).
- [x] The H2 dry-run bypass condition (`agenticxr_no_verification`) and the H4
  per-trial candidate switch (`candidateTarget`, N=1 vs. N>1) are implemented,
  deterministic-tested, and mock-integration-tested end to end (2026-08-11).
- [x] Bounded goal-loop controller, verification levels, delayed evidence, escalation,
  and kill switch have deterministic tests.
- [x] Idle future-goal prediction and speculative candidate storage exist and are
  exercised with mock harnesses.
- [x] Multi-candidate generation metadata, independent simulation calls, ranking, and
  create/edit/remove lifecycle validation exist in the server and mock integration.
- [x] Experience-context inference/override and a consented pseudonymous person-profile
  store exist.
- [x] Checkpoint metadata can be saved and classified as resumable or orphaned.
- [x] Continuous gaze/proximity/locomotion monitoring exists and is mock-integrated.
- [ ] None of the above has been validated through the complete live
  Claude + Unity Play Mode + physical Quest path.

### Partially implemented

- [ ] Full-system checkpoint/resume: metadata is persisted, but attached Unity
  procedures are not automatically reconstructed and resumed after restart.
- [ ] Persistent person memory: opt-in, retention, inspection, and reset exist, but
  privacy behavior, cross-session usefulness, and real consent UI flow are unevaluated.
- [ ] Persistent/provenanced memory: the artifact event log persists decisions and
  failures, but visual state, semantic relations, timelines, and most scene knowledge
  remain bounded in-memory stores rather than a durable compounding knowledge base.
- [ ] Scene graph: `near` and sensor-derived relations exist; hierarchy relations such
  as `on`, `inside`, `attached-to`, and `supports` need richer Unity publication.
  Affordances are static rules, not learned inference.
- [ ] Verification Space: the clone/compiler path exists in Unity source, but has only
  compiled in batch mode and has not executed in Play Mode or on Quest.
- [ ] Conflict resolution: the model can return `proceed`, `queue`, or `redirect`, but
  only `proceed` has an acted-on runtime path; queueing and redirection need deterministic
  orchestration.
- [ ] Orchestration ordering: the six-step agent pipeline is prompt-enforced rather
  than represented as a deterministic state machine.

### Not implemented

- [ ] The paper's future XR "second-brain" knowledge graph: durable concept/object/
  behaviour nodes, typed edges, spatial anchors, contradiction records, and
  attachable/detachable knowledge modules.
- [ ] A human-facing memory inspector/editor for reviewing provenance, correcting or
  removing stored claims, and steering durable knowledge.
- [ ] Shared semantic reuse cache for non-code artifacts such as textures, 3D assets,
  and procedural configurations.
- [ ] Real multi-user roles, ownership, conflicts, and permissions. The current
  person-policy path is a single-owner stub.
- [ ] Fine scene geometry, occlusion-aware visual memory, and learned semantic
  affordances.

### Paper/study work that cannot be claimed complete yet

- [ ] Real API/model run and real Faster Whisper run.
- [ ] Unity-side control-flow execution evidence.
- [ ] Physical Quest execution evidence.
- [ ] Performance, latency, API-cost, safety, and Verification-Space/live mismatch data.
- [ ] Institutional ethics approval before recruiting or recording participants.
- [ ] Human study with the planned tasks, conditions, questionnaires, interviews, and
  approximately 20--24 participants.
- [ ] Replace the paper's planned-results placeholders with observed results.

## 13. Inputs the project team must provide

Required now for the first complete live run:

- [ ] A funded Anthropic API key, supplied privately as `ANTHROPIC_API_KEY`.
- [ ] A deployed Faster Whisper-compatible endpoint, supplied as `STT_HTTP_URL`, or
  authorization and host/GPU details so one can be deployed.
- [ ] The server PC LAN IPv4 address and confirmation that TCP `8009` is reachable from
  the Quest.
- [ ] Quest model, OS version, Developer Mode/USB-debugging status, and microphone
  permission.
- [ ] Confirmation that Unity Hub has Android Build Support, SDK, NDK, and OpenJDK for
  Unity `6000.3.9f1`.
- [ ] The first approved test scene, target object, spoken request, and expected safe
  behavior.
- [ ] A decision on whether continuous assistance and idle speculation should be
  enabled during engineering tests. Leave both disabled for initial study trials unless
  the approved protocol explicitly includes them.
- [ ] A pseudonymous test person ID only if cross-session profile behavior is being
  tested; never provide a participant's real identity.

Required before a human study:

- [ ] Institutional ethics/IRB approval and the approved protocol.
- [ ] Final tasks, conditions, counterbalancing/randomization plan, success criteria,
  and ground-truth rubric.
- [ ] Approved participant information/consent materials and recruitment plan.
- [ ] Final questionnaire instruments and scoring procedures.
- [ ] A secure, institution-approved location and retention/deletion policy for logs,
  audio, transcripts, consent records, and pseudonym mapping.

Not required for the Claude AgenticXR path:

- OpenAI API key;
- Azure Speech key;
- MCP API key;
- a legacy Python virtual environment.

## 14. Related documentation

- `docs/SETUP_INSTRUCTIONS.md`
- `docs/continuous-human-centered-runtime.md`
- `docs/goal-loops-and-speculative-futures.md`
- `docs/study-logging-schema.md`
- `docs/progress-log.md`
