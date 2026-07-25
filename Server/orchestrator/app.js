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
//   node orchestrator/app.js "<natural language authoring intent>" [targetObjectId] <sessionId> [correlationId]

const path = require("path");
const { randomUUID } = require("crypto");
const { appendEvaluationEvent } = require("../evaluation/event_logger");

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
            "what is near or related to it, and what actions it affords. Preserve the exact sceneEpoch, " +
            "snapshotId, objectRevision, and response timestamp from query_scene in your summary so the " +
            "router can perform Unity's authoritative freshness check. State plainly if scene data is " +
            "unavailable (e.g. the query times out) rather than inventing scene contents. Do not propose or " +
            "write any code.",
        tools: [bridgeTool("query_scene"), bridgeTool("query_visual_memory"), bridgeTool("query_scene_graph"), bridgeTool("query_affordances"), bridgeTool("get_script_context"), bridgeTool("get_evolution_history"), bridgeTool("get_experience_context")],
        mcpServers: [BRIDGE_SERVER_NAME],
        model: "sonnet",
    },
    code_generator: {
        description: "Drafts three distinct lifecycle candidates from a grounded intent. Use after scene grounding and history retrieval.",
        prompt:
            "You are the Code Generator for AgenticXR. Given a scene grounding summary and the user's " +
            "natural-language intent, operation (create/edit/remove), existing artifact history, and experience context, " +
            "produce exactly THREE materially distinct candidates. Create/edit candidates contain one C# MonoBehaviour; " +
            "edit names existingArtifactId and refines the current implementation; remove names existingArtifactId and has no code. " +
            "Constraints: ASCII only; no keyboard/mouse input APIs; no System.IO, " +
            "System.Net, System.Diagnostics, or reflection; a Component name that does not collide with a " +
            "common Unity type; if a new object is instantiated, parent it under transform; default any speed " +
            "to 1; tag the target 'game'. Output ONLY a JSON array of three objects with candidateId, operation, " +
            "existingArtifactId, approach, experienceMode, and code (null only for remove).",
        tools: [],
        model: "sonnet",
    },
    validator_critic: {
        description: "Independently reviews a candidate artifact against the original intent before it is proposed to Unity. Use after code_generator, before any propose/simulate call.",
        prompt:
            "You are the Validator/Critic for AgenticXR - an independent reviewer, not the code's author. Review ONE " +
            "candidate at a time; every candidate must receive its own verdict and Verification Space dry-run. Given the candidate C# " +
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
            "\"reason\": string, \"requiredPermissions\": string[], \"expectedSideEffects\": string, " +
            "\"triggerSource\": \"system_opportunity\"|\"context\"|\"clarification\"|\"explicit_request\", " +
            "\"reversible\": boolean, \"localOnly\": boolean, \"detailResolved\": boolean}. " +
            "No prose outside the JSON.",
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
            `propose_artifact/simulate_artifact, and call ${bridgeTool("get_artifact_history")} plus ` +
            `${bridgeTool("get_evolution_history")} to confirm the object's version lineage reflects it. ` +
            "Be terse - report only the final logged state, not a narrative.",
        tools: [bridgeTool("commit_memory_event"), bridgeTool("get_artifact_history"), bridgeTool("get_evolution_history")],
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
   If and only if the prompt says cross-session profile consent is true and supplies a
   pseudonymous personId, call ${bridgeTool("set_person_profile_consent")} before reading
   person policy. Never infer consent. Then call ${bridgeTool("send_agent_status")} with
   state "thinking" and a short detail. If the user explicitly asks to forget/reset
   learning, call ${bridgeTool("reset_person_profile")} with confirmReset=true.
1. Call ${bridgeTool("send_agent_status")} with state "querying_memory", then use
   scene_analyst to ground the request in the current scene.
2. Classify the lifecycle operation as create, edit, or remove. Retrieve
   ${bridgeTool("get_evolution_history")}, ${bridgeTool("get_person_policy")}, and
   ${bridgeTool("get_experience_context")}, then use code_generator to draft exactly
   three candidates sharing one candidateSetId.
3. Call ${bridgeTool("send_agent_status")} with state "validating". For EACH candidate,
   use validator_critic independently and call ${bridgeTool("simulate_artifact")}.
   Never rank an unvalidated or unsimulated candidate.
4. Call ${bridgeTool("rank_artifact_candidates")} with all three verdicts and dry-run
   outcomes. Stop if none is eligible. Rejected alternatives remain in evolution
   history and are shown only when an L5 user explicitly asks for alternatives.
5. conflict_resolver - check the target object is safe to modify right now.
   If decision is not "proceed", stop and explain why instead of proceeding.
6. Call ${bridgeTool("send_agent_status")} with state "ready_to_preview", then call
   ${bridgeTool("propose_artifact")} yourself (this call belongs to you, the
   router, not a subagent) with the candidate code, targetObjectId, intent,
   authoringMode, interactionMode, validationState="accepted", validationSummary,
   riskScore, requiredPermissions, expectedSideEffects, triggerSource, reversible,
   localOnly, and detailResolved from the validator's verdict,
   and sceneEpoch, snapshotId, objectRevision, snapshotTakenAt from the Scene Analyst,
   plus operation, existingArtifactId, candidateId, candidateSetId, candidateCount,
   selectionReason, experienceMode, sessionId and the shared correlationId. Never omit freshness metadata.
   Edit and remove always require confirmation and may never use automatic mode.
7. version_memory - confirm the outcome is logged and query evolution history.

BOUNDED GOALS AND LOOPS:
- If the intent asks for an ongoing objective ("until", "keep", "maintain", scheduled,
  or context-triggered work), call ${bridgeTool("create_bounded_goal")} before execution.
  Choose one explicit verification level (1 deterministic, 2 rule threshold, 3 delayed
  ground truth, 4 Validator/Critic judgment, 5 human checkpoint), a concrete predicate,
  and finite attempt/wall-time bounds. The model may make bounds stricter, never wider
  than the tool's global caps.
- After the normal execute/verify/persist stages, call ${bridgeTool("advance_goal_loop")}.
  A waiting-trigger result persists context for the next event/schedule. A delayed
  result must wait for ${bridgeTool("resolve_delayed_goal")}. An escalation or exhausted
  bound stops work until ${bridgeTool("continue_goal_after_human")} records an explicit
  decision. Approval must come from a later world-space panel decision or a new
  explicit follow-up user turn with its own decisionCorrelationId; the current model
  turn cannot approve its own continuation. Never reinterpret an escalation as permission.
- Verification levels 1/2 may be automatic only when the existing L1/L2 risk,
  reversibility, locality, and freshness rules also pass. Level 4 must use
  validator_critic and ${bridgeTool("record_goal_validator_judgment")}. Levels 3/5 and
  exhausted bounds require the normal L4/L5 human route.
- If the runtime prompt says speculative idle preparation is true, DO NOT call
  propose_artifact or request_commit. Create a speculative bounded goal, generate and
  independently simulate candidates against the pinned scene tuple, then call
  ${bridgeTool("register_speculative_candidate")} for eligible drafts and stop. A later
  real request may call ${bridgeTool("select_speculative_candidate")}, but the selected
  draft must still pass the complete normal pipeline and consent gates.
- For a real request, after scene grounding and before drafting, call
  ${bridgeTool("select_speculative_candidate")} with the exact current sceneEpoch,
  snapshotId, objectRevision, target, and actual objective. Reuse a returned draft only
  as one candidate: independently validate and simulate it again, then follow every
  normal authoring and consent gate. If there is no fresh semantic match, draft normally.

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
        console.error('Usage: node orchestrator/app.js "<natural language authoring intent>" [targetObjectId] <sessionId> [correlationId]');
        process.exit(1);
    }

    const { query } = await import("@anthropic-ai/claude-agent-sdk");

    const correlationId = process.argv[5] || randomUUID();
    const sessionId = process.argv[4];
    if (!sessionId) {
        console.error("[orchestrator] sessionId is required as the fourth CLI argument.");
        process.exit(1);
    }

    console.log(`[orchestrator] correlationId=${correlationId} target=${targetObjectId}`);
    console.log(`[orchestrator] intent: "${intent}"`);

    const options = {
        systemPrompt: SYSTEM_PROMPT,
        agents: AGENTS,
        mcpServers: {
            [BRIDGE_SERVER_NAME]: {
                type: "stdio",
                command: "node",
                args: [BRIDGE_SERVER_PATH],
                env: Object.fromEntries(
                    ["AGENTICXR_EVALUATION_SOURCE", "AGENTICXR_EVALUATION_LOG", "AGENTICXR_ARTIFACT_LOG"]
                        .filter((name) => process.env[name])
                        .map((name) => [name, process.env[name]])
                ),
            },
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
        `Cross-session profile consent: ${String(process.env.AGENTICXR_PROFILE_CONSENT || "false").toLowerCase() === "true"}\n` +
        `Pseudonymous person id: ${process.env.AGENTICXR_PERSON_ID || "not-provided"}\n` +
        `Speculative idle preparation: ${String(process.env.AGENTICXR_SPECULATIVE_ONLY || "false").toLowerCase() === "true"}\n` +
        `correlationId to reuse for every subagent and tool call in this turn: ${correlationId}`;

    const maxAttempts = Math.max(1, Number(process.env.AGENTICXR_ANTHROPIC_MAX_ATTEMPTS) || 3);
    const baseBackoffMs = Math.max(100, Number(process.env.AGENTICXR_ANTHROPIC_RETRY_BASE_MS) || 2000);
    let sawMutatingToolCall = false;

    for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
        appendEvaluationEvent({ eventType: "orchestrator_attempt_started", sessionId, correlationId, targetObjectId, attempt });
        try {
            for await (const message of query({ prompt, options })) {
                if (message.type === "assistant") {
                    for (const block of message.message.content || []) {
                        if (block.type === "tool_use" && /(?:propose_artifact|request_commit)$/.test(block.name || "")) {
                            sawMutatingToolCall = true;
                        }
                        if (block.type === "text" && block.text.trim()) {
                            console.log(`[router] ${block.text.trim()}`);
                        }
                    }
                } else if (message.type === "result") {
                    console.log(`[orchestrator] finished (subtype=${message.subtype})`);
                    appendEvaluationEvent({
                        eventType: "orchestrator_result",
                        sessionId,
                        correlationId,
                        targetObjectId,
                        attempt,
                        subtype: message.subtype || null,
                        usage: message.usage || null,
                        totalCostUsd: message.total_cost_usd ?? null,
                    });
                }
            }
            return;
        } catch (error) {
            const transient = isTransientAnthropicError(error);
            appendEvaluationEvent({
                eventType: "orchestrator_attempt_failed",
                sessionId,
                correlationId,
                targetObjectId,
                attempt,
                transient,
                mutatingToolCallSeen: sawMutatingToolCall,
                error: error.message,
            });
            if (!transient || sawMutatingToolCall || attempt >= maxAttempts) throw error;
            const delayMs = baseBackoffMs * Math.pow(2, attempt - 1);
            console.error(`[orchestrator] transient API failure on attempt ${attempt}/${maxAttempts}; retrying in ${delayMs}ms: ${error.message}`);
            await new Promise((resolve) => setTimeout(resolve, delayMs));
        }
    }
}

function isTransientAnthropicError(error) {
    const status = error && (error.status || error.statusCode);
    if (status === 408 || status === 409 || status === 429 || (status >= 500 && status <= 599)) return true;
    const text = `${error && error.code ? error.code : ""} ${error && error.message ? error.message : ""}`.toLowerCase();
    return ["econnreset", "etimedout", "eai_again", "socket hang up", "rate limit", "overloaded", "temporarily unavailable"].some((token) => text.includes(token));
}

main().catch((err) => {
    console.error("[orchestrator] fatal error:", err);
    process.exit(1);
});
