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

// Direct `node orchestrator/app.js` runs get the same gitignored Server/.env
// secrets as the start scripts; a spawned turn inherits the parent env anyway.
require("../scripts/load-local-env");

const { appendEvaluationEvent } = require("../evaluation/event_logger");
const { appendTokenActivity } = require("../evaluation/token_activity_logger");

const BRIDGE_SERVER_PATH = path.join(__dirname, "..", "mcp", "unity_scene_bridge", "server.js");
const BRIDGE_SERVER_NAME = "unity_scene_bridge";

// H4 per-trial switch: the runtime sets AGENTICXR_CANDIDATE_COUNT from the
// registered study trial's candidateTarget (N=1 vs. N>1). Defaults to the paper's
// best-of-three outside a trial. This process is spawned once per turn, so reading
// the environment at module load is per-turn configuration, not a global.
const CANDIDATE_COUNT = Math.min(5, Math.max(1, Number(process.env.AGENTICXR_CANDIDATE_COUNT) || 3));
const MODEL_ID = process.env.AGENTICXR_MODEL_ID || "claude-sonnet-4-20250514";
const DEBUG_TRANSCRIPTS = ["1", "true", "yes"].includes(
    String(process.env.STUDY_DEBUG_TRANSCRIPTS || "").toLowerCase());

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
        model: MODEL_ID,
    },
    code_generator: {
        description: `Drafts ${CANDIDATE_COUNT} distinct lifecycle candidate(s) from a grounded intent. Use after scene grounding and history retrieval.`,
        prompt:
            "You are the Code Generator for AgenticXR. Given a scene grounding summary and the user's " +
            "natural-language intent, operation (create/edit/remove), existing artifact history, and experience context, " +
            `produce exactly ${CANDIDATE_COUNT} materially distinct candidate(s). Create/edit candidates contain one C# MonoBehaviour; ` +
            "create attaches a new generated behavior to the selected scene object and leaves existingArtifactId empty; " +
            "edit names the exact active generated artifactId and replaces/refines that generated implementation; " +
            "remove names the exact active generated artifactId and has no code. A pre-authored scene object's targetObjectId " +
            "is never an existingArtifactId. If the target has no active generated artifact, adding a component or behavior is create, " +
            "even when the user describes it as changing an existing object's appearance or behavior. " +
            "Treat experience context as a behavioral constraint, not a label: productivity should reduce distraction, " +
            "training should favor guidance and recoverability, entertainment may favor playful feedback, and exploration " +
            "should preserve open-ended discovery. The architecture may assist non-authoring experiences even though dynamic " +
            "Unity behavior authoring is the currently implemented action harness. " +
            "Constraints: ASCII only; no keyboard/mouse input APIs; no System.IO, " +
            "System.Net, System.Diagnostics, or reflection; a Component name that does not collide with a " +
            "common Unity type; if a new object is instantiated, parent it under transform; default any speed " +
            `to 1. Do not change the target GameObject's tag unless the user's intent explicitly requests a tag change. ` +
            "Every create/edit candidate must be genuinely reversible: capture every pre-existing value it mutates before " +
            "the first mutation, restore those values idempotently in both OnDisable and OnDestroy, and destroy any child " +
            "objects it spawned. Never restore shared state by mutating a shared Material asset. " +
            "For visual changes, do not assume the root Renderer is the visible one: select the first enabled Renderer " +
            "from GetComponentsInChildren<Renderer>(true), because study objects may keep a disabled authored Renderer " +
            "on the root and display an active child visual. Stop safely if no enabled Renderer exists. " +
            `Output ONLY a JSON array of ${CANDIDATE_COUNT} object(s) with candidateId, operation, ` +
            "existingArtifactId, approach, experienceMode, and code (null only for remove).",
        tools: [],
        model: MODEL_ID,
    },
    validator_critic: {
        description: "Independently reviews a candidate artifact against the original intent before it is proposed to Unity. Use after code_generator, before any propose/simulate call.",
        prompt:
            "You are the Validator/Critic for AgenticXR - an independent reviewer, not the code's author. Review ONE " +
            "candidate at a time; every candidate must receive its own verdict and Verification Space dry-run. Given the candidate C# " +
            "code, the original intent, the scene grounding summary, and how this turn was triggered: " +
            "(1) check it only uses UnityEngine APIs and none of the denied namespaces (System.IO, System.Net, " +
            "System.Diagnostics, reflection); (2) check it plausibly matches the stated intent and the object " +
            "it targets; (3) assign a riskScore from 0 (cosmetic, reversible, single-object or one deterministic " +
            "study guidance pair) to 1 (destructive, " +
            "persistent, shared-state, multi-object); (4) recommend authoringMode: 'automatic' only if " +
            "riskScore < 0.3 AND the change is cosmetic/parametric on a single object, or on one explicitly matched " +
            "task-local source/destination guidance pair, AND interactionMode is L1 or L2, otherwise " +
            "'semi_auto_confirm'. L4 and L5 always use 'semi_auto_confirm'; (5) classify interactionMode using the paper's five modes (main.tex tab:modes) " +
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
        model: MODEL_ID,
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
        model: MODEL_ID,
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
2. Retrieve ${bridgeTool("get_artifact_history")}, ${bridgeTool("get_evolution_history")},
   ${bridgeTool("get_person_policy")}, and ${bridgeTool("get_experience_context")}, then
   classify the lifecycle operation as create, edit, or remove. These operations describe
   the GENERATED ARTIFACT lifecycle, not whether the selected scene GameObject already exists:
   - create: add a new generated component/behavior when no generated artifact is currently active
     on the target. This includes changing the color or behavior of a pre-authored scene object.
   - edit: replace/refine a currently active generated artifact. Use its exact artifactId from
     history as existingArtifactId.
   - remove: remove a currently active generated artifact. Use its exact artifactId.
   Never use targetObjectId as existingArtifactId. If history has no successfully applied/active
   generated artifact for the target, the operation MUST be create, not edit or remove.
   Then use code_generator to draft exactly
   ${CANDIDATE_COUNT} candidate(s) sharing one candidateSetId.
3. Call ${bridgeTool("send_agent_status")} with state "validating". For EACH candidate,
   use validator_critic independently and call ${bridgeTool("simulate_artifact")}.
   Never rank an unvalidated or unsimulated candidate. Exception, decided by the
   tool, never by you: if simulate_artifact returns status "skipped_no_verification",
   the registered study condition has bypassed Verification Space dry-runs - carry
   that exact status forward as the candidate's simulationStatus and continue; the
   proposal will be marked unverified. You may not skip a dry-run on your own.
4. Call ${bridgeTool("rank_artifact_candidates")} with every candidate's verdict and
   dry-run outcome and its approach summary (a single-candidate set is valid and still logged).
   Preserve the tool's comparisonSummary and pass it unchanged as selectionReason when
   proposing the selected candidate, so Unity can show all ranked alternatives. Stop if none
   is eligible. Rejected alternatives remain in evolution
   history and are shown only when an L5 user explicitly asks for alternatives.
5. conflict_resolver - check the target object is safe to modify right now.
   If decision is not "proceed", stop and explain why instead of proceeding.
6. Call ${bridgeTool("send_agent_status")} with state "ready_to_preview". Immediately
   before proposing, call ${bridgeTool("query_scene")} again for the same targetObjectId,
   sessionId, and correlationId. This final authoritative refresh is mandatory because
   candidate generation and validation can outlive the proposal freshness window. Stop
   if the target disappeared or its identity/revision conflicts with the grounded target;
   otherwise use sceneEpoch, snapshotId, objectRevision, and response timestamp from THIS
   refreshed query, not the older Scene Analyst report. Then call
   ${bridgeTool("propose_artifact")} yourself (this call belongs to you, the
   router, not a subagent) with the candidate code, targetObjectId, intent,
   authoringMode, interactionMode, validationState="accepted", validationSummary,
   riskScore, requiredPermissions, expectedSideEffects, triggerSource, reversible,
   localOnly, and detailResolved from the validator's verdict,
   and the refreshed sceneEpoch, snapshotId, objectRevision, snapshotTakenAt,
   plus operation, existingArtifactId, candidateId, candidateSetId, candidateCount,
   selectionReason, experienceMode, sessionId and the shared correlationId. Never omit freshness metadata.
   L4/L5, edit, and remove always require confirmation and may never use automatic mode.
7. version_memory - confirm the outcome is logged and query evolution history.

BOUNDED GOALS AND LOOPS:
- If External trigger source is context, this is monitored human activity crossing a
  configured threshold, not an explicit command. Preserve L2/context classification.
  First surface status, and stop without proposing anything when there is no useful,
  reversible, local assistance. Never reinterpret observation as consent.
  The intent text supplies raw environmental context (region, anchor role/components,
  affordances, nearby objects, experience mode) and never names a function - YOU
  derive what fits from that context. Favor a clearly VISIBLE, reversible, local
  effect (light, motion, or a spawned child object parented under the target) over
  silent state changes, so the user can see what the system did and undo it.
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
- For ANY real (non-speculative) turn - explicit requests AND context-triggered
  assistance alike - after scene grounding and before drafting, call
  ${bridgeTool("select_speculative_candidate")} with the exact current sceneEpoch,
  snapshotId, objectRevision, target, and actual objective. Reuse a returned draft only
  as one candidate: independently validate and simulate it again, then follow every
  normal authoring and consent gate. If there is no fresh semantic match, draft normally.

Narrate each step in one short sentence before moving to the next. If any stage fails,
rejects, or times out, stop immediately and explain the reason in plain language
instead of guessing or proceeding anyway - a stalled or rejected turn is a correct,
expected outcome, not a failure of the router.`;

// L1/L2 are low-risk implicit assistance modes. Their old route injected the
// complete general-purpose authoring prompt and called four specialist agents,
// even for a single visual cue. This bounded route preserves grounding,
// independent validation, Verification Space, freshness, ranking and the Unity
// proposal gate while removing unrelated exploration and candidate generation.
const FAST_IMPLICIT_SYSTEM_PROMPT = `You are the bounded AgenticXR router for one low-risk implicit L1/L2 turn.
Treat the supplied target as an observed focus, which may be either a manipulable source or a compatible destination.
Work only on the one source/destination pair grounded from that focus. The assistance behavior is already decided:
create exactly one continuous, high-contrast cyan-to-magenta color pulse on BOTH the source object and
its matching destination, combined with a subtle 1.00-to-1.08 scale pulse. This two-object pair is one local,
reversible guidance cue. The destination must be chosen from the grounded scene data, never from a fixed naming
suffix, index, or prewritten object pair. Do not choose or invent another behavior.

Required sequence:
1. Call ${bridgeTool("record_intent")} with the supplied intent, sessionId and correlationId.
2. Call ${bridgeTool("query_scene")} for only the supplied observed focus using the same sessionId and correlationId.
   Classify that focus from its semantic name and component types. If it is manipulable, use it as the source and
   choose one compatible destination/receptacle/drop location from the halo. If it is a destination, use it as the
   destination and choose one compatible manipulable source from the halo. Use task affordance and spatial proximity;
   prefer the nearest compatible counterpart when several are equivalent. Preserve the exact selected source and
   destination ids and names. Stop if either side or authoritative scene metadata is unavailable or genuinely ambiguous.
   Never derive either side from a numeric suffix or hard-coded source/destination mapping.
3. Draft exactly one minimal ASCII-only C# MonoBehaviour implementing that fixed paired pulse with UnityEngine
   Renderer and Transform APIs. The generated component will be attached to the selected SOURCE, not necessarily the
   originally observed focus. Embed the exact destination name selected from grounding and find only that exact active
   GameObject at runtime; do not derive it from the source name or from a prewritten naming convention.
   The study roots can have disabled Renderers while their visible geometry is an active child: on BOTH source and
   destination use GetComponentsInChildren<Renderer>(true) and select the first enabled Renderer; never use an
   inactive root Renderer. Use unscaled Time, Color.Lerp(Color.cyan, Color.magenta, ping-pong), and scale both from
   their captured original scales to originalScale * 1.08. Use separate MaterialPropertyBlocks so no shared Material
   is changed. Capture both original property blocks and scales, then restore both idempotently in OnDisable and
   OnDestroy. Do not spawn objects, inspect unrelated objects beyond that exact paired destination, use input APIs,
   networking, files, or reflection. Use operation=create. If either visible Renderer or the exact destination is
   absent, stop safely; do not substitute another object or behavior.
4. Call validator_critic once for an independent compact verdict. Treat the exact source/destination study pair as
   one bounded local cue. Stop unless it passes, riskScore is below 0.3, and it confirms reversible=true and
   localOnly=true. Do not repair or generate a second candidate.
5. Call ${bridgeTool("simulate_artifact")} once against the selected SOURCE with a derived simulation correlationId, then call
   ${bridgeTool("rank_artifact_candidates")} with exactly that one candidate and the main correlationId.
   A registered no-verification study condition may return skipped_no_verification; preserve that status.
6. Call ${bridgeTool("query_scene")} once more for the selected SOURCE and main correlationId. Use metadata from this
   refresh in ${bridgeTool("propose_artifact")} and set targetObjectId to that selected source. Pass candidateCount=1,
   the frozen L1/L2 mode, trigger source,
   validation fields, authoringMode=automatic, operation=create, reversible=true, localOnly=true, and the selected
   candidate. Never propose if the target changed or any preceding step failed.

No broad scene exploration, behavioral choice, history/profile/speculation lookup, clarification, alternatives,
goals, repair loops, or narrative. Use short status text only. One candidate, one validation, one simulation,
one proposal maximum.`;

function isFastImplicitMode(env = process.env) {
    if (String(env.AGENTICXR_FAST_IMPLICIT_PROMPT || "true").toLowerCase() === "false") return false;
    if (String(env.AGENTICXR_SPECULATIVE_ONLY || "false").toLowerCase() === "true") return false;
    const mode = String(env.AGENTICXR_INTERACTION_MODE || "").toUpperCase();
    const trigger = String(env.AGENTICXR_TRIGGER_SOURCE || "").toLowerCase();
    return ["L1", "L2"].includes(mode) && ["system_opportunity", "context"].includes(trigger);
}

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
    if (process.env.AGENTICXR_MODEL_VERSION !== MODEL_ID) {
        console.error(`[orchestrator] live model version must be reported as pinned '${MODEL_ID}' before startup`);
        process.exit(1);
    }

    const intent = process.argv[2];
    const targetObjectId = process.argv[3] || "obj-test-42";
    if (!intent) {
        console.error('Usage: node orchestrator/app.js "<natural language authoring intent>" [targetObjectId] <sessionId> [correlationId]');
        process.exit(1);
    }

    const correlationId = process.argv[5] || randomUUID();
    const sessionId = process.argv[4];
    if (!sessionId) {
        console.error("[orchestrator] sessionId is required as the fourth CLI argument.");
        process.exit(1);
    }

    console.log(`[orchestrator] correlationId=${correlationId} target=${targetObjectId}`);
    if (DEBUG_TRANSCRIPTS) console.log(`[orchestrator] intent: "${intent}"`);
    else console.log(`[orchestrator] intent received characters=${intent.length}`);

    const fastImplicit = isFastImplicitMode();
    const runtimeCandidateCount = fastImplicit ? 1 : CANDIDATE_COUNT;
    const directFastImplicit = fastImplicit &&
        String(process.env.AGENTICXR_FAST_IMPLICIT_DIRECT || "true").toLowerCase() !== "false";
    console.log(`[orchestrator] route=${directFastImplicit ? "direct-fast-implicit" : fastImplicit ? "fast-implicit" : "full"} candidates=${runtimeCandidateCount}`);
    if (directFastImplicit) {
        const { runFastImplicitPipeline } = await import("./fast_implicit_pipeline.mjs");
        await runFastImplicitPipeline({
            intent,
            targetObjectId,
            sessionId,
            correlationId,
            model: MODEL_ID,
        });
        return;
    }

    const { query } = await import("@anthropic-ai/claude-agent-sdk");
    const options = {
        systemPrompt: fastImplicit ? FAST_IMPLICIT_SYSTEM_PROMPT : SYSTEM_PROMPT,
        agents: fastImplicit ? { validator_critic: AGENTS.validator_critic } : AGENTS,
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
        model: MODEL_ID,
        cwd: __dirname,
        ...(fastImplicit ? { maxTurns: Math.max(8, Number(process.env.AGENTICXR_FAST_IMPLICIT_MAX_TURNS) || 14) } : {}),
    };

    const prompt =
        `Authoring intent: "${intent}"\n` +
        `Target object id: ${targetObjectId}\n` +
        `Session id: ${sessionId}\n` +
        `Cross-session profile consent: ${String(process.env.AGENTICXR_PROFILE_CONSENT || "false").toLowerCase() === "true"}\n` +
        `Pseudonymous person id: ${process.env.AGENTICXR_PERSON_ID || "not-provided"}\n` +
        `Speculative idle preparation: ${String(process.env.AGENTICXR_SPECULATIVE_ONLY || "false").toLowerCase() === "true"}\n` +
        `External trigger source: ${process.env.AGENTICXR_TRIGGER_SOURCE || "explicit_request"}\n` +
        `Frozen interaction mode: ${process.env.AGENTICXR_INTERACTION_MODE || "infer-from-request"}\n` +
        `Interaction action: ${process.env.AGENTICXR_INTERACTION_ACTION || "initial"}\n` +
        `Clarification detail resolved: ${process.env.AGENTICXR_DETAIL_RESOLVED || "not-applicable"}\n` +
        `Revision count in this chain: ${process.env.AGENTICXR_REVISION_COUNT || "0"}\n` +
        `Experience mode supplied by the continuous runtime: ${process.env.AGENTICXR_EXPERIENCE_MODE || "not-provided"}\n` +
        `correlationId to reuse for every subagent and tool call in this turn: ${correlationId}`;

    const maxAttempts = Math.max(1, Number(process.env.AGENTICXR_ANTHROPIC_MAX_ATTEMPTS) || 3);
    const baseBackoffMs = Math.max(100, Number(process.env.AGENTICXR_ANTHROPIC_RETRY_BASE_MS) || 2000);
    let sawMutatingToolCall = false;

    for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
        const attemptStartedAt = Date.now();
        appendEvaluationEvent({ eventType: "orchestrator_attempt_started", sessionId, correlationId, targetObjectId, attempt });
        try {
            for await (const message of query({ prompt, options })) {
                const reportedModel = message.model || (message.message && message.message.model);
                // The Agent SDK emits synthetic routing/status messages with the
                // sentinel model name <synthetic>. They are not model responses
                // and must not be treated as evidence of live model drift.
                if (reportedModel && reportedModel !== "<synthetic>" && reportedModel !== MODEL_ID) {
                    throw new Error(`live model drift: reported '${reportedModel}', pinned '${MODEL_ID}'`);
                }
                if (message.type === "assistant") {
                    for (const block of message.message.content || []) {
                        if (block.type === "tool_use" && /(?:propose_artifact|request_commit)$/.test(block.name || "")) {
                            sawMutatingToolCall = true;
                        }
                        if (block.type === "text" && block.text.trim()) {
                            if (DEBUG_TRANSCRIPTS) console.log(`[router] ${block.text.trim()}`);
                            else console.log(`[router] model output received characters=${block.text.trim().length}`);
                        }
                    }
                } else if (message.type === "result") {
                    const latencyMs = Date.now() - attemptStartedAt;
                    console.log(`[orchestrator] finished (subtype=${message.subtype})`);
                    const resultEvent = {
                        eventType: "orchestrator_result",
                        sessionId,
                        correlationId,
                        targetObjectId,
                        attempt,
                        subtype: message.subtype || null,
                        usage: message.usage || null,
                        totalCostUsd: message.total_cost_usd ?? null,
                        latencyMs,
                        model: MODEL_ID,
                        interactionMode: process.env.AGENTICXR_INTERACTION_MODE || "infer-from-request",
                        triggerSource: process.env.AGENTICXR_TRIGGER_SOURCE || "explicit_request",
                        experienceMode: process.env.AGENTICXR_EXPERIENCE_MODE || "not-provided",
                        candidateCount: runtimeCandidateCount,
                        orchestratorRoute: fastImplicit ? "fast-implicit" : "full",
                    };
                    appendEvaluationEvent(resultEvent);
                    // The CSV is intentionally a second, analysis-oriented view
                    // of this same terminal SDK result, not a separate source of truth.
                    appendTokenActivity({
                        ...resultEvent,
                        activity: fastImplicit ? "fast_implicit_orchestrator_turn" : "agentic_orchestrator_turn",
                        resultSubtype: resultEvent.subtype,
                        outcome: resultEvent.subtype,
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

if (require.main === module) {
    main().catch((err) => {
        console.error("[orchestrator] fatal error:", err);
        process.exit(1);
    });
}

module.exports = {
    SYSTEM_PROMPT, FAST_IMPLICIT_SYSTEM_PROMPT, MODEL_ID, CANDIDATE_COUNT,
    isFastImplicitMode, main, isTransientAnthropicError,
};
