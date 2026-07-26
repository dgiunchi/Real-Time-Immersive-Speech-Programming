# AgenticXR Setup Instructions

This guide covers the complete live Quest speech-to-AgenticXR workflow: required
credentials, speech-to-text, Unity, networking, startup order, acceptance testing,
and common failure points.

For the maintained one-page source of truth covering all current prerequisites,
user-supplied information, live acceptance checks, and newly discovered requirements,
use `docs/LIVE_SYSTEM_REQUIREMENTS.md`.

## 1. What you need

### Required credentials

#### Anthropic API key

Create an API key at <https://console.anthropic.com/>. The account must have API
credits or billing enabled.

Never paste the key into source files, `.mcp.json`, Unity assets, `config.json`, Git,
or chat. Set it only in the PowerShell terminal that runs the server:

```powershell
$env:ANTHROPIC_API_KEY="sk-ant-your-real-key"
```

#### Speech-to-text endpoint

The live voice path requires a reachable Faster Whisper-compatible HTTP endpoint:

```text
POST http://HOST:50101/stt/transcribe
```

The application sends multipart form data containing:

- `file`: 16 kHz, mono, 16-bit WAV audio
- `language`: `en`
- `beam_size`: `1`

The endpoint should return the recognized transcript as plain text. Configure it in
the server terminal:

```powershell
$env:STT_HTTP_URL="http://your-stt-host:50101/stt/transcribe"
```

The Node server PC must be able to reach this endpoint. The Quest does not contact
the STT service directly.

This repository currently contains the STT client but not the Faster Whisper HTTP
server. If no compatible endpoint is available, add or deploy one before attempting
the full voice workflow.

### Required hardware and software

- Meta Quest with Developer Mode enabled
- USB debugging approval if deploying over USB
- Quest and server PC on the same LAN
- Unity Hub
- Unity `6000.3.9f1`
- Unity Android Build Support, including SDK, NDK, and OpenJDK
- Node.js and npm
- Windows Firewall access for inbound TCP port `8009`

Unity should add and request Android's `RECORD_AUDIO` permission because the project
uses the Unity `Microphone` API. Approve this permission inside the headset.

### Server PC LAN address

Run:

```powershell
ipconfig
```

Find the IPv4 address for the network adapter connected to the same LAN as the Quest,
for example `192.168.1.42`. This address must be entered in Unity's Ubiq Room Client.

Do not use `localhost` in a Quest build. On Quest, `localhost` refers to the headset.

### Not required for AgenticXR

The AgenticXR/Claude path does not require:

- An OpenAI API key
- An Azure Speech key
- A separate MCP credential
- Claude Code
- The legacy Python virtual environment

An OpenAI key is needed only for the original DreamCodeVR comparison condition.

## 2. Install and verify the server

Open PowerShell:

```powershell
cd "D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming\Server"

npm install

$env:AGENTICXR_MODE="claude"
$env:ANTHROPIC_API_KEY="sk-ant-your-real-key"
$env:STT_HTTP_URL="http://your-stt-host:50101/stt/transcribe"

npm run doctor
npm test
npm run test:integration
```

Expected results:

- `npm run doctor` reports that the Anthropic key and STT URL are available.
- `npm test` passes its deterministic assertions.
- `npm run test:integration` ends with `[mock_integration] PASS`.

### Optional: prepare likely future goals while idle

This feature uses the same Anthropic API key and can increase API usage. It is off
by default. To enable it:

```powershell
$env:AGENTICXR_IDLE_PREDICTION_ENABLED="true"
$env:AGENTICXR_IDLE_PREDICTION_THRESHOLD_MS="60000"
$env:AGENTICXR_IDLE_PREDICTION_COOLDOWN_MS="300000"
```

Idle work only generates, validates, simulates, and stores pinned local drafts. It
cannot propose or commit to Unity. A real request preempts idle work, and any reused
draft must pass all normal validation, freshness, consent, and commit gates.

Speculation is skipped while a study trial is active. Set
`AGENTICXR_STUDY_ALLOW_SPECULATION=true` only when the approved study condition
explicitly includes it. See `docs/goal-loops-and-speculative-futures.md`.

### Optional: proactively assist from continuous activity

The long-lived activity observer starts automatically. Proactive Claude turns remain
off until explicitly enabled because they consume API credits:

```powershell
$env:AGENTICXR_CONTINUOUS_ASSIST_ENABLED="true"
$env:AGENTICXR_ACTIVITY_THRESHOLD="1.1"
$env:AGENTICXR_ACTIVITY_WINDOW_MS="5000"
$env:AGENTICXR_ACTIVITY_COOLDOWN_MS="30000"
```

Continuous opportunities retain L2/context classification and still pass the normal
verification, risk, Proposal Gate, and Unity consent checks. Push-to-talk preempts a
continuous turn. During studies, assistance is suppressed unless
`AGENTICXR_STUDY_ALLOW_CONTINUOUS_ASSIST=true` is explicitly set by the protocol.
See `docs/continuous-human-centered-runtime.md`.

## 3. Test the Anthropic API without Quest

This test isolates Claude orchestration from the microphone, STT, Unity, and Quest.

### Terminal 1: room server

```powershell
cd "D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming\Server"
node node_modules/ubiq/app.js config/default.json
```

### Terminal 2: mock Unity peer

```powershell
cd "D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming\Server"
node mcp/unity_scene_bridge/mock_unity_peer.js
```

### Terminal 3: real Claude orchestration

```powershell
cd "D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming\Server"

$env:ANTHROPIC_API_KEY="sk-ant-your-real-key"

node orchestrator/app.js "make this sphere slowly pulse red" obj-mock-0001 manual-test-session
```

The output should show these stages:

1. Scene Analyst
2. Code Generator
3. Validator/Critic
4. Conflict Resolver
5. Artifact proposal
6. Version/Memory logging

Complete this test before introducing STT and headset variables.

## 4. Verify speech-to-text

If the STT service exposes a health endpoint, test it from the Node server PC:

```powershell
curl.exe http://your-stt-host:50101/health
```

The configured transcription endpoint must accept the multipart WAV request described
above and return plain transcript text.

## 5. Configure Unity

1. Open this folder through Unity Hub:

   ```text
   D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming\Unity
   ```

2. Use Unity `6000.3.9f1`.
3. Open `Assets/Demos/DynamicCompiler/DynamicCompiler.unity`.
4. Find the Ubiq `Room Client` configuration.
5. Replace `localhost` with the server PC's LAN IPv4 address.
6. Confirm that the TCP port is `8009`.
7. Ensure authorable objects have the `game` tag.
8. Switch the build platform to Android.
9. Build and deploy the application to Quest.

The AgenticXR runtime, cache, scene registry, bridge handlers, and world-space
Approve/Reject/Undo panel install automatically when the scene loads.

## 6. Configure Windows networking

From an Administrator PowerShell terminal, allow inbound Ubiq TCP traffic:

```powershell
New-NetFirewallRule `
  -DisplayName "AgenticXR Ubiq TCP 8009" `
  -Direction Inbound `
  -Protocol TCP `
  -LocalPort 8009 `
  -Action Allow
```

Also verify:

- The PC and Quest are on the same Wi-Fi/LAN.
- The network is not a guest network with client isolation.
- A VPN is not separating the PC and Quest routes.
- The Unity Room Client uses the PC address rather than `localhost`.

## 7. Start the complete AgenticXR server

From the `Server` directory:

```powershell
$env:ANTHROPIC_API_KEY="sk-ant-your-real-key"
$env:STT_HTTP_URL="http://your-stt-host:50101/stt/transcribe"

npm run start:agenticxr
```

This command:

- Hosts the Ubiq room on TCP `8009`.
- Receives Quest audio on NetworkId `98`.
- Sends completed recordings to STT.
- Starts a Claude Agent SDK orchestration turn for each transcript.
- Spawns the MCP bridge automatically.
- Returns scene queries, status, and artifact proposals to Unity.

Do not start a separate MCP bridge for this integrated voice workflow.

## 8. Headset acceptance test

1. Start the Node server before launching the Quest application.
2. Launch the application on Quest.
3. Approve microphone permission.
4. Point the ray at an object tagged `game`.
5. Hold the left trigger.
6. Say: `Make this object slowly pulse red.`
7. Release the trigger.
8. Confirm that the server logs:
   - Recording start
   - Selected stable object ID
   - Audio reception
   - STT transcript
   - Claude orchestration stages
   - Scene query
   - Validation
   - Artifact proposal
9. Confirm that the headset shows agent status and proposal evidence.
10. Select **Approve** and confirm that the behavior becomes active.
11. Select **Undo** and confirm that the generated behavior is removed or the previous
    generated version is restored.
12. Repeat with **Reject** and confirm that the object remains unchanged.

Desktop/Editor fallback controls:

- Enter: Approve
- Escape: Reject
- U: Undo

## 9. Likely Quest-specific issue

The world-space panel is generated automatically. It may render without accepting XR
pointer clicks if its Canvas is not connected to the project's tracked-device UI
raycaster/input module.

If desktop Enter/Escape/U works and the Quest buttons render but cannot be selected,
the backend is probably operational. Complete a Quest UI input integration pass by
connecting the generated Canvas to the active tracked-device UI input module.

## 10. Troubleshooting order

Diagnose the pipeline from the bottom upward:

1. `npm test`
2. `npm run test:integration`
3. Real Anthropic orchestration with the mock peer
4. STT health and transcription request
5. Unity Editor connection to the Node server
6. Quest connection over LAN
7. Quest microphone permission and push-to-talk
8. Proposal panel XR interaction
9. Generated artifact approval and rollback

Common symptoms:

- **Doctor reports missing Anthropic key:** set it in the same terminal before running
  `npm run doctor` or `npm run start:agenticxr`.
- **STT returns nothing:** verify the endpoint, microphone permission, and that the
  left trigger remains held for the whole utterance.
- **No selected stable object:** point at and select an object tagged `game` before
  starting the recording.
- **Quest cannot connect:** replace `localhost`, check TCP `8009`, firewall rules,
  Wi-Fi client isolation, and VPN routing.
- **Proposal buttons do not respond:** test desktop fallback controls, then connect
  the generated Canvas to the Quest tracked-device UI input module.
- **Proposal rejected as stale:** keep the ray on the same target while Claude is
  reasoning, or issue the request again against the newly selected object.

## 11. Information needed for further setup assistance

Keep credentials private. Only provide non-secret setup information:

- Whether the Anthropic API account has active credits
- Whether a Faster Whisper HTTP endpoint already exists
- Whether testing starts in Unity Editor or directly on Quest
- Quest model
- Server PC LAN topology and whether the Quest is on the same network

## 12. Reliability and evaluation controls

Optional runtime controls (the defaults are suitable for an initial acceptance run):

```powershell
$env:AGENTICXR_TURN_TIMEOUT_MS="180000"
$env:AGENTICXR_ANTHROPIC_MAX_ATTEMPTS="3"
$env:AGENTICXR_ANTHROPIC_RETRY_BASE_MS="2000"
$env:AGENTICXR_EVALUATION_SOURCE="live-model"
```

Cross-session preference learning is disabled by default. For a participant who has
explicitly consented, supply a pseudonymous study identifier (never a name or email):

```powershell
$env:AGENTICXR_PROFILE_CONSENT="true"
$env:AGENTICXR_PERSON_ID="participant-007"
```

Unset both variables for session-only memory. A user can explicitly request profile
reset/revocation through the agent; this calls `reset_person_profile` and deletes the
persisted learned profile. Default retention is 90 days and can be shortened through
the consent tool. Generated Unity procedures are checkpointed locally in
`Application.persistentDataPath`; treat that application data as potentially
sensitive source code.

The Anthropic retry loop only retries transient failures and stops retrying after a
mutating proposal/commit tool call, avoiding duplicate scene changes. Unity disables
generated behaviours after repeated global frame/allocation budget violations. This
is a coarse recovery mechanism: Unity cannot pre-empt truly infinite code on its main
thread, and unrelated work can contribute to the observed global budget.

Runtime evaluation events are written to
`Server/evaluation/data/runtime-events.jsonl` and are gitignored. Export a report with:

```powershell
cd "D:\Research_Activities\agenticXR\Real-Time-Immersive-Speech-Programming\Server"
node evaluation/report.js --source=live-model
```

Never cite the mock integration report as live-model, Unity-runtime, headset, or user
study evidence. Verification-space and commit-attach fields remain empty until a real
Unity proposal executes.
