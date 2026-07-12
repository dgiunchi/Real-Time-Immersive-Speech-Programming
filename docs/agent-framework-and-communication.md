# Agent framework choice & communication protocol (execution of open decision #1)

This resolves the first open decision from `docs/agentic-xr-architecture.md` §9
("model split") by picking a concrete orchestration framework, and turns §2 of that
document into two real diagrams instead of ASCII art.

## Diagrams

- **Communication protocol reference chart** — the canonical one to hand someone: a
  compact 3-tier transport diagram plus two precisely formatted tables (the full
  NetworkId channel scheme, and the shared message envelope schema):
  https://lucid.app/lucidchart/a069924a-8d7a-4c8c-af2f-197c9c2a4004/edit
- **Architecture / agent topology** — Unity/Ubiq real-time layer, the backend
  orchestrator, and the MCP connector layer, with the channel each arrow uses labeled:
  https://lucid.app/lucidchart/d726923b-11d8-47c9-a6ba-21215e606157/edit
- **Sequence diagram — semi-automatic (confirm) authoring flow** — full round trip for
  one `correlationId`, from speech to committed artifact, across Ubiq channels and MCP
  tool calls: https://lucid.app/lucidchart/b92b8d55-9c9e-4da9-b22d-9ba2063ad920/edit

(Both are edit links under your Lucid account — private by default; share/view links
can be generated from Lucid if you want to hand them to someone else.)

## Framework decision: Claude Agent SDK, not OpenAI's

**Use the Claude Agent SDK (Anthropic) as the orchestration framework for the backend
agent pool** — Task Router, Scene Analyst, Validator/Critic, Version/Memory, Conflict
Resolver. Reasons, in order of weight:

1. **MCP-native.** Model Context Protocol is Anthropic's connector standard, and it's
   the same mechanism used above to drive Lucid. The natural way to expose your Unity
   scene to backend agents is the same pattern, not a bespoke one: wrap the Ubiq
   `SceneDelta`/`SceneQuery`/`ArtifactProposal` channels behind a small **Unity Scene
   Bridge MCP server** (tools: `query_scene(objectId|filter)`, `propose_artifact(code,
   targetObjectId, intent)`, `get_artifact_status(correlationId)`). Backend agents then
   call one consistent tool-calling interface for both real connectors (Lucid, later
   GitHub/docs) and the XR scene itself — this *is* the "good communication strategy"
   answer for the backend-to-tool half of the system, complementing the Ubiq channel
   scheme for the real-time embodied half.
2. **Proven orchestrator/subagent/verification pattern.** Multi-agent coordination with
   a router, specialized subagents, and a separate critic gating output before it's
   allowed to act — is exactly the architecture running Claude Code itself, in
   production since 2024. Your Validator/Critic role (§4 of the architecture doc) is
   precisely the "narrow judge, strict instruction-following, must not rationalize its
   own output" role Claude is strong at, and it should already be a separate model call
   from the generator.
3. **Recency risk, which you already flagged.** You said it yourself: "ChatGPT work is
   pretty recent." That's accurate — OpenAI's agent-builder tooling is a late-2025
   product, materially newer than Anthropic's Agent SDK lineage. For a system whose job
   is to sandbox and gate arbitrary generated code before it runs on a headset, the
   maturity of the *orchestration and tool-use loop* matters more here than small
   differences in raw model quality.

**What this does not require you to change:** the existing `Server/samples/services/code_generation/openai_chatgpt_api.py`
call (OpenAI, `gpt-5.5`) can stay exactly as-is as the model *inside* the Code
Generator subagent — model choice per agent role is independent of which SDK owns
orchestration. You already have `OPENAI_API_KEY` working; no need to touch it. What you
add is `ANTHROPIC_API_KEY` for the orchestrator process and, ideally, for the
Validator/Critic call specifically — using a different vendor/model than the generator
for the critic reduces the chance both share the same blind spot on a given artifact.

**Action needed from you:** set `ANTHROPIC_API_KEY` in the same terminal that starts
the Node server (same pattern as the existing `OPENAI_API_KEY`/`OPENAI_MODEL` lines in
the README's "Configure API keys" section). Nothing else in your current setup needs to
change to start building the orchestrator.
