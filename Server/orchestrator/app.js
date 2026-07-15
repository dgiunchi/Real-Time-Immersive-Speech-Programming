"use strict";

// AgenticXR backend orchestrator (docs/agentic-xr-architecture.md §1, §4;
// docs/agent-framework-and-communication.md). This is the Task Router: it runs the
// Claude Agent SDK's `query()` loop with a system prompt that sequences delegation to
// five named subagents (Scene Analyst, Code Generator, Validator/Critic, Version/
// Memory, Conflict Resolver), all wired to the unity_scene_bridge MCP server as an
// external stdio process.
//
// This process owns no direct Ubiq/Unity connection of its own - it only talks to
// Unity through unity_scene_bridge's MCP tools, exactly like any other MCP client
// (Claude Code, the inspector). That is deliberate: one connector, one transport.
//
// Requires ANTHROPIC_API_KEY in the environment. Does not require OPENAI_API_KEY -
// the existing single-shot pipeline (code_runtime_generator) stays OpenAI-based and
// untouched, which keeps the two systems independently comparable for the paper's
// planned C1 (one-shot) vs C2/C3 (agentic) study conditions
// (rag/drafts/agenticxr_research_questions.md).
//
// Usage:
//   node orchestrator/app.js "<natural language authoring intent>" [targetObjectId]

const path = require("path");
const { randomUUID } = require("crypto");

const BRIDGE_SERVER_PATH = path.join(__dirname, "..", "mcp", "unity_scene_bridge", "server.js");
const BRIDGE_SERVER_NAME = "unity_scene_bridge";

function bridgeTool(name) {
    return `mcp__${BRIDGE_SERVER_NAME}__${name}`;
}

const AGENTS = {
    scene_analyst: {
        description:
            "Grounds an authoring intent in the current XR scene using Shared XR Memory. Always call this " +
            "first, before any code is drafted.",
        prompt:
            "You are the Scene Analyst for AgenticXR. Given a target object id, sessionId, and correlationId, " +
            `call ${bridgeTool("query_scene")} with ALL THREE of objectId, sessionId, and correlationId - the ` +
            "sessionId is required for stale-proposal detection (a later query_scene for a different object in " +
            "the same session will mark this object's still-pending proposal as stale, which is intentional). " +
            `Also call ${bridgeTool("query_visual_memory")}, ${bridgeTool("query_scene_graph")}, and ` +
            `${bridgeTool("query_affordances")} for that object, passing the given correlationId to every one ` +
            "of those calls so the whole turn's timeline stays correlated (see " +
            "Server/memory/timeline_registry.js). Summarize, in under 150 words, what the target object is, " +
            "what is near or related to it, and what actions it affords. State plainly if scene data is " +
            "unavailable (e.g. the query times out) rather than inventing scene contents. Do not propose or " +
            "write any code.",
        tools: [bridgeTool("query_scene"), bridgeTool("query_visual_memory"), bridgeTool("query_scene_graph"), bridgeTool("query_affordances"), bridgeTool("get_script_context")],
        mcpServers: [BRIDGE_SERVER_NAME],
        model: "sonnet",
    },
    code_generator: {
        description: "Drafts a candidate C# MonoBehaviour artifact from a grounded intent. Use after the Scene Analyst has grounded the request.",
        prompt:
            "You are the Code Generator for AgenticXR. Given a scene grounding summary and the user's " +
            "natural-language intent, write exactly ONE C# class inheriting from MonoBehaviour that implements " +
            "the requested behavior. Constraints: ASCII only; no keyboard/mouse input APIs; no System.IO, " +
            "System.Net, System.Diagnostics, or reflection; a Component name that does not collide with a " +
            "common Unity type; if a new object is instantiated, parent it under transform; default any speed " +
            "to 1; tag the target 'game'. Output ONLY the code inside a ```csharp fenced block, no prose before " +
            "or after it.",
        tools: [],
        model: "sonnet",
    },
    validator_critic: {
        description: "Independently reviews a candidate artifact against the original intent before it is proposed to Unity. Use after code_generator, before any propose/simulate call.",
        prompt:
            "You are the Validator/Critic for AgenticXR - an independent reviewer, not the code's author, and " +
            "you must not simply trust the generator's own framing of what it wrote. Given the candidate C# " +
            "code, the original intent, the scene grounding summary, and how this turn was triggered: " +
            "(1) check it only uses UnityEngine APIs and none of the denied namespaces (System.IO, System.Net, " +
            "System.Diagnostics, reflection); (2) check it plausibly matches the stated intent and the object " +
            "it targets; (3) assign a riskScore from 0 (cosmetic, reversible, single-object) to 1 (destructive, " +
            "persistent, shared-state, multi-object); (4) recommend authoringMode: 'automatic' only if " +
            "riskScore < 0.3 AND the change is cosmetic/parametric on a single object, otherwise " +
            "'semi_auto_confirm'; (5) classify interactionMode using the paper's five modes (main.tex tab:modes) " +
            "- these describe who INITIATED this turn, separate from authoringMode's execution gate: " +
            "'L1' if the system itself proposed this from a low-risk opportunity with no explicit user request; " +
            "'L2' if triggered by ordinary user motion/context rather than a command; 'L3' if a required detail " +
            "was missing and had to be clarified first; 'L4' if the user's request is complete but the effect " +
            "is persistent/shared and needs confirmation; 'L5' if the user explicitly asked for this function " +
            "through speech, possibly iterating on it. Respond with ONLY a single compact JSON object: " +
            "{\"pass\": boolean, \"riskScore\": number, \"authoringMode\": string, \"interactionMode\": string, " +
            "\"reason\": string}. No prose outside the JSON.",
        tools: [],
        model: "sonnet",
    },
    conflict_resolver: {
        description: "Checks whether the target object is already locked, owned, or otherwise unsafe for this session to modify. Use after validation passes, before propose_artifact.",
        prompt:
            "You are the Conflict Resolver for AgenticXR. Given a target object id, session id, and " +
            `correlationId, call ${bridgeTool("get_person_policy")} and ${bridgeTool("query_scene_graph")} for that object. ` +
            "Report whether it is safe to proceed, should be queued behind another in-flight change, or should " +
            "be redirected to a copy instead of the live object. Respond with ONLY a single compact JSON " +
            "object: {\"decision\": \"proceed\"|\"queue\"|\"redirect\", \"reason\": string}. No prose outside " +
            "the JSON.",
        tools: [bridgeTool("get_person_policy"), bridgeTool("query_scene_graph")],
        mcpServers: [BRIDGE_SERVER_NAME],
        model: "haiku",
    },
    version_memory: {
        description: "Commits the outcome of a completed authoring turn to the Version/Memory store. Use last, after Unity has confirmed (or rejected) an ArtifactResult.",
        prompt:
            "You are the Version/Memory agent for AgenticXR. Given the final outcome of an authoring turn, call " +
            `${bridgeTool("commit_memory_event")} to record it if it was not already logged automatically by ` +
            `propose_artifact/simulate_artifact, and call ${bridgeTool("get_artifact_history")} to confirm the ` +
            "object's history now reflects it. Be terse - report only the final logged state, not a narrative.",
        tools: [bridgeTool("commit_memory_event"), bridgeTool("get_artifact_history")],
        mcpServers: [BRIDGE_SERVER_NAME],
        model: "haiku",
    },
};

const SYSTEM_PROMPT = `You are the AgenticXR Task Router (the backend Agent Orchestrator described in
docs/agentic-xr-architecture.md). You do not write code or reason about scene content
yourself - you delegate every step to a named subagent via the Task tool, in this
order, and pass the given correlationId to every subagent so the whole turn shares one
timeline (see Server/memory/timeline_registry.js):

0. Call ${bridgeTool("record_intent")} yourself with the given intent text, sessionId,
   and correlationId, before delegating to any subagent. This is a stand-in for real
   speech capture (not wired into this pipeline yet - see docs/next-build-prompt.md
   §1.3), not a claim that live speech was transcribed.
1. scene_analyst - ground the request in the current scene.
2. code_generator - draft a candidate artifact from the grounded intent.
3. validator_critic - independently review the candidate; read its JSON verdict.
   If pass is false, stop and explain why instead of proceeding.
4. conflict_resolver - check the target object is safe to modify right now.
   If decision is not "proceed", stop and explain why instead of proceeding.
5. Call ${bridgeTool("propose_artifact")} yourself (this call belongs to you, the
   router, not a subagent) with the candidate code, targetObjectId, intent,
   authoringMode AND interactionMode from the validator's verdict, sessionId, and the
   shared correlationId.
6. version_memory - confirm the outcome is logged.

Narrate each step in one short sentence before moving to the next. If any stage fails,
rejects, or times out, stop immediately and explain the reason in plain language
instead of guessing or proceeding anyway - a stalled or rejected turn is a correct,
expected outcome, not a failure of the router.`;

async function main() {
    if (!process.env.ANTHROPIC_API_KEY) {
        console.error(
            "[orchestrator] ANTHROPIC_API_KEY is not set. Set it in the same terminal before running this " +
            "process, e.g. (PowerShell): $env:ANTHROPIC_API_KEY=\"sk-ant-...\". This orchestrator does not use " +
            "OPENAI_API_KEY - that key only matters for the older single-shot pipeline in " +
            "Server/samples/apps/code_runtime_generator."
        );
        process.exit(1);
    }

    const intent = process.argv[2];
    const targetObjectId = process.argv[3] || "obj-test-42";
    if (!intent) {
        console.error('Usage: node orchestrator/app.js "<natural language authoring intent>" [targetObjectId]');
        process.exit(1);
    }

    const { query } = await import("@anthropic-ai/claude-agent-sdk");

    const correlationId = randomUUID();
    const sessionId = "orchestrator-cli";

    console.log(`[orchestrator] correlationId=${correlationId} target=${targetObjectId}`);
    console.log(`[orchestrator] intent: "${intent}"`);

    const options = {
        systemPrompt: SYSTEM_PROMPT,
        agents: AGENTS,
        mcpServers: {
            [BRIDGE_SERVER_NAME]: { type: "stdio", command: "node", args: [BRIDGE_SERVER_PATH] },
        },
        // This is a non-interactive backend service with no terminal for a human to
        // approve tool calls from - the real human-in-the-loop gate is Unity's
        // confirm/ghost-preview UI, reached through the authoringMode routing inside
        // propose_artifact/ArtifactResult, not an SDK permission prompt here.
        permissionMode: "bypassPermissions",
        model: "sonnet",
        cwd: __dirname,
    };

    const prompt =
        `Authoring intent: "${intent}"\n` +
        `Target object id: ${targetObjectId}\n` +
        `Session id: ${sessionId}\n` +
        `correlationId to reuse for every subagent and tool call in this turn: ${correlationId}`;

    for await (const message of query({ prompt, options })) {
        if (message.type === "assistant") {
            for (const block of message.message.content || []) {
                if (block.type === "text" && block.text.trim()) {
                    console.log(`[router] ${block.text.trim()}`);
                }
            }
        } else if (message.type === "result") {
            console.log(`[orchestrator] finished (subtype=${message.subtype})`);
        }
    }
}

main().catch((err) => {
    console.error("[orchestrator] fatal error:", err);
    process.exit(1);
});
