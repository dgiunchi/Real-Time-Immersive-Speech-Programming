# Live XR ↔ Claude setup and acceptance test

The code path is automatic after the Unity scene loads. The remaining steps require
a human because they involve a private API credential, a physical headset, LAN
configuration, and operating-system permissions.

## 1. Private runtime configuration

Open PowerShell in `Server` and set these values for that terminal only:

```powershell
$env:ANTHROPIC_API_KEY="sk-ant-your-real-key"
$env:STT_HTTP_URL="http://your-faster-whisper-host:50101/stt/transcribe"
npm install
$env:AGENTICXR_MODE="claude"
npm run doctor
npm run start:agenticxr
```

Do not put the Anthropic key in `.mcp.json`, `config.json`, Unity assets, or Git.
`npm run start:agenticxr` sets AgenticXR mode itself; setting it before `doctor`
only tells the checker to validate the Claude path instead of the legacy OpenAI
comparison path.

The STT URL must be reachable from the server PC. It receives WAV audio over HTTP;
the Quest does not contact it directly.

## 2. Unity and Quest

1. Open the `Unity` folder with Unity `6000.3.9f1`.
2. Open `Assets/Demos/DynamicCompiler/DynamicCompiler.unity`.
3. Set the Ubiq Room Client/Server address to the server PC's LAN IP and TCP port
   `8009`. Do not use `localhost` in a Quest build.
4. Ensure Windows Firewall allows inbound TCP `8009` for the Node process.
5. Build/deploy to Quest and grant microphone permission when Android asks.
6. Ensure authorable objects use the `game` tag. Stable IDs are generated
   deterministically at runtime.

The AgenticXR bootstrap creates the bridge, cache, scene registry and world-space
Approve/Reject/Undo panel; no manual GameObject/component placement is required.

## 3. Acceptance test

1. Point the ray at a `game` object and keep it there.
2. Hold the left trigger and say: “make this object slowly pulse red.”
3. Release the trigger.
4. Confirm that the server logs a transcript, stable target ID, Claude orchestration
   stages, scene query, validation and proposal.
5. Confirm that the headset shows agent status. For a confirmation-routed change,
   select **Approve**; **Reject** must leave the object unchanged.
6. After applying, select **Undo** and confirm that the generated component is
   removed and the previous generated version, if any, is restored.

Desktop/editor fallbacks are Enter=Approve, Escape=Reject and U=Undo. On Quest, the
existing XR UI input module must drive the world-space buttons. If the buttons render
but cannot be selected, assign the project's tracked-device UI raycaster/input module
to the generated canvas during the device-specific UI pass.

## 4. Claude Code as a direct MCP client

The repository-root `.mcp.json` registers `unity-scene-bridge`. Opening Claude Code
from the repository root makes the tools discoverable. For this direct mode, start
the Ubiq room server and Unity first. The integrated voice path normally uses the
Claude Agent SDK orchestrator and does not require launching a separate bridge.
