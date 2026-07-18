# Unity Scene Bridge — MCP server

Wraps the XR communication protocol from `docs/agentic-xr-architecture.md` §2-3 as
MCP tools, so the Claude Agent SDK orchestrator (or any MCP client) talks to the
Unity/Ubiq scene the same way it talks to any other connector — no bespoke
event-bus code in the orchestrator.

This folder is **transport plus Shared XR Memory** (`docs/shared-memory-and-experimental-space.md`).
It does not call any LLM and does not decide authoring modes — those are the
orchestrator's subagents in `Server/orchestrator/`. This bridge's job is turning Ubiq
`NetworkScene` messages into promises keyed by `correlationId`, and caching/deriving
memory (visual, semantic/scene-graph, script/context, temporal/artifact-log,
person/policy) from that same message stream as it flows through.

## Status

The Node/MCP transport, memory stores and Claude orchestrator are implemented and
tested against the mock peer. The Unity side is now implemented under
`Unity/Assets/AgenticCache`: it installs automatically, publishes stable-ID
focus/halo state, answers scene queries, stages Roslyn artifacts, requests consent,
commits or rejects them, reports `ArtifactResult`, and supports undo. The complete
project imports and compiles with Unity `6000.3.9f1`. A physical Quest run remains a
human/device verification step because microphone permission, LAN routing and XR UI
input cannot be exercised in headless batch mode.

## Files

| File | Purpose |
|---|---|
| `protocol.js` | Channel/NetworkId constants and the shared message envelope — single source of truth, matches `docs/agentic-xr-architecture.md` §2.1-2.2 |
| `scene_bridge_client.js` | Joins the Ubiq room as a peer, sends/receives on the new channels, resolves promises by `correlationId`; emits every envelope (inbound and outbound) for `../../memory` to observe |
| `server.js` | MCP entrypoint (stdio transport) — registers the transport tools (`query_scene`, `propose_artifact`, `simulate_artifact`, `get_artifact_status`, `get_bridge_status`) and the Shared XR Memory tools (`query_visual_memory`, `query_scene_graph`, `query_affordances`, `get_script_context`, `get_artifact_history`, `get_person_policy`, `commit_memory_event`, `get_timeline_metrics`) |
| `mock_unity_peer.js` | Dev-only stand-in for Unity — answers `SceneQuery`/`ArtifactProposal` (including simulate mode) the way Unity eventually will, and emits synthetic sensor events, so you can test everything without touching C# |
| `smoketest_client.mjs` | Standalone MCP client (no LLM) exercising every tool in one session — the fastest way to check the memory stores in isolation |
| `config.json` | Room connection info — `roomGuid`/`roomserver.tcp.port` must match whatever server is currently hosting the room (today: `code_runtime_generator/config.json`) |
| `../../memory/*.js` | The five Shared XR Memory stores (`docs/shared-memory-and-experimental-space.md`), documented there, not duplicated here |

Formatted reference chart (same tables, plus the transport-layer diagram):
https://lucid.app/lucidchart/a069924a-8d7a-4c8c-af2f-197c9c2a4004/edit

## Channel scheme (owned by this bridge)

| NetworkId | Direction | Type | Purpose |
|---|---|---|---|
| 95 | Unity → Server | `SceneDelta` | Reply to a scene query, or an unsolicited push |
| 96 | Server → Unity | `SceneQuery` | Request scene detail for an object id or filter |
| 97 | Server → Unity | `AgentUtterance` | Coordinator speech/text filler (fire-and-forget) |
| 99 | Server → Unity | `ArtifactProposal` | Deliver a validated code artifact for attachment |
| 100 | Unity → Server | `ArtifactResult` / `UserDecision` | Final commit outcome (always) + optional confirm/reject telemetry |
| 101 | both | `AgentPresenceHeartbeat` | Liveness, surfaced via `get_bridge_status` |

Channels 94/98 (the existing `CodeGenerated` / audio pipeline) are untouched.

## Running it

Requires `npm install` at `Server/` (already run — adds `@modelcontextprotocol/sdk`
and `zod`). No API keys needed; this process makes no LLM calls.

**1. Start (or already have running) the Ubiq room server** — currently that's
   `code_runtime_generator`, which also hosts the TCP room on port 8009:

   ```powershell
   cd Server/samples/apps/code_runtime_generator
   node app.js
   ```

**2a. Smoke-test without Unity** — in a second terminal, run the mock peer, then
   drive the bridge's tools with the official MCP inspector CLI:

   ```powershell
   cd Server
   node mcp/unity_scene_bridge/mock_unity_peer.js
   ```

   ```powershell
   # third terminal
   cd Server
   npx @modelcontextprotocol/inspector --cli node mcp/unity_scene_bridge/server.js --method tools/list
   npx @modelcontextprotocol/inspector --cli node mcp/unity_scene_bridge/server.js --method tools/call --tool-name query_scene --tool-arg objectId=obj-test-42
   ```

   `query_scene` should return a `SceneDelta` envelope with the mock peer's canned
   focus/halo payload, correlated back to your request. This has been verified.

**2b. Run for real** — use `npm run start:agenticxr` for the integrated
speech-to-Claude path. The orchestrator spawns the MCP server itself. Use
`npm run start:unity-scene-bridge` only when a separate MCP client will drive the
tools directly.

**2c. Wire into an MCP client** (e.g. Claude Agent SDK or a `.mcp.json`):

   ```json
   {
     "mcpServers": {
       "unity-scene-bridge": {
         "command": "node",
         "args": ["Server/mcp/unity_scene_bridge/server.js"]
       }
     }
   }
   ```

## Running the full agent pipeline

The backend orchestrator (Task Router + five subagents) lives in `Server/orchestrator/`
and spawns this server automatically as its MCP connection — see
`Server/orchestrator/README.md` for setup (one API key: `ANTHROPIC_API_KEY`) and how
to run a full authoring turn against the mock peer.

## Next steps

1. Run the physical Quest acceptance test in `docs/live-xr-claude-setup.md` and tune
   the world-space consent panel for the chosen XR input module.
2. Expand the current gaze/selection sensor summary with collision, locomotion-region
   and hand proximity publishers required by L1/L2 study tasks.
3. Migrate `Server/memory/artifact_log.js` off flat JSON-lines to SQLite once the
   history needs querying beyond per-object, append/read (open decision,
   `docs/agentic-xr-architecture.md` §9).
