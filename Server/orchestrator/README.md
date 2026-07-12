# AgenticXR Orchestrator

The Task Router: a Claude Agent SDK process that delegates an authoring intent to five
named subagents (Scene Analyst, Code Generator, Validator/Critic, Conflict Resolver,
Version/Memory), all reaching Unity only through the `unity_scene_bridge` MCP server's
tools. See `docs/agentic-xr-architecture.md` §1/§4 and
`docs/agent-framework-and-communication.md` for the design this implements.

This process holds no direct Ubiq/Unity connection of its own — every scene read and
every artifact proposal goes through `unity_scene_bridge`, which it spawns as a child
MCP server automatically. You do not need to start that server separately.

## What you need to test this

**One API key: `ANTHROPIC_API_KEY`.** Get one at https://console.anthropic.com if you
don't have one. Nothing else — this orchestrator does not call OpenAI. (The older
single-shot pipeline in `Server/samples/apps/code_runtime_generator` still uses
`OPENAI_API_KEY`/`OPENAI_MODEL` and is untouched; the two are independent on purpose,
so they stay comparable for the paper's planned one-shot-vs-agentic study conditions.)

**Something answering on the Unity side.** Real Unity/C# handlers for
`SceneQuery`/`SceneDelta`/`ArtifactProposal`/`ArtifactResult` are not implemented yet
(`docs/progress-log.md`), so for now that "something" is `mock_unity_peer.js`.

## Steps

Three terminals, all from the `Server/` directory:

**1. Start the Ubiq room server** (hosts the room; the mock peer, the bridge, and
   eventually real Unity all join it):

```powershell
cd Server\samples\apps\code_runtime_generator
node app.js
```

**2. Start the mock Unity peer** (stands in for a real headset until the Unity-side
   handlers exist):

```powershell
cd Server
node mcp\unity_scene_bridge\mock_unity_peer.js
```

**3. Set your key and run the orchestrator** with a natural-language authoring intent:

```powershell
cd Server
$env:ANTHROPIC_API_KEY="sk-ant-your-real-key"
node orchestrator\app.js "make this sphere pulse red when I touch it" obj-test-42
```

You should see the router narrate each stage (`scene_analyst` grounding the object,
`code_generator` drafting C#, `validator_critic`'s verdict, `conflict_resolver`'s
decision, the `propose_artifact` call, then `version_memory` confirming it's logged).
The mock peer's terminal will show it receiving and answering each message; after
~0.2–1.5s (simulating Unity/user latency) it replies with a committed `ArtifactResult`.

If `ANTHROPIC_API_KEY` is missing, the orchestrator exits immediately with a clear
message instead of a raw SDK error — that's the expected failure mode, not a bug.

## Inspecting what actually happened

`Server/mcp/unity_scene_bridge/smoketest_client.mjs` is a standalone MCP client
(no LLM involved) that exercises every memory tool in one session — useful for
checking `Server/memory/*` in isolation, independent of whether the orchestrator or a
real LLM is involved:

```powershell
cd Server
node mcp\unity_scene_bridge\smoketest_client.mjs
```

The artifact history persists across runs at `Server/memory/data/artifact_log.jsonl`
(gitignored — it's runtime data, not source).

## What this does and doesn't prove yet

Running this end-to-end with a real `ANTHROPIC_API_KEY` and the mock peer proves the
agent-to-agent delegation, the MCP tool wiring, and the memory stores all work
together. It does **not** prove anything about a real headset, because Unity doesn't
implement its side of the protocol yet — the mock peer's replies are canned, not
computed from a real scene. See `docs/progress-log.md` for exactly what's implemented
vs. designed-only.
