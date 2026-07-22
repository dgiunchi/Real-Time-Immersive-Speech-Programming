"use strict";

// MCP entrypoint for the Unity Scene Bridge. Exposes the XR scene and the
// artifact-authoring round trip as MCP tools over stdio, so any MCP client
// (Claude Agent SDK, Claude Code, the MCP inspector) can call them the same
// way it would call any other connector. See README.md in this folder.
//
// IMPORTANT: stdio is the MCP transport - all diagnostics must go to stderr
// (console.error), never console.log/stdout, or they will corrupt the
// JSON-RPC stream.

const path = require("path");
const nconf = require("nconf");
const { z } = require("zod");
const { SESSION_ID_PATTERN } = require("./protocol");
const { checkModePolicy } = require("../../orchestrator/mode_policy");
const { rankCandidates, validateLifecycle, LIFECYCLE_OPERATIONS } = require("../../orchestrator/candidate_selector");
const { appendEvaluationEvent } = require("../../evaluation/event_logger");
const { SceneBridgeClient } = require("./scene_bridge_client");
const { SharedMemory } = require("../../memory");
const { CacheExchangeLayer } = require("../../cache");

const sessionIdSchema = z.string().regex(SESSION_ID_PATTERN, "sessionId must be a 1-128 character safe identifier");

async function main() {
    const configPath = process.argv[2] || path.join(__dirname, "config.json");
    nconf.file(configPath);
    const config = nconf.get();

    if (!config || !config.roomGuid || !config.roomserver) {
        throw new Error(`Invalid or missing config at ${configPath} - expected { roomGuid, host, roomserver: { tcp: { port } } }`);
    }

    const bridge = new SceneBridgeClient(config);
    const memory = new SharedMemory();
    memory.attach(bridge);
    const cacheExchange = new CacheExchangeLayer();
    cacheExchange.attach(bridge);
    const persistContinuityCheckpoint = () => {
        try {
            memory.checkpoints.save({
                artifactLog: memory.artifactLog,
                personPolicy: memory.personPolicy,
                experienceContext: memory.experienceContext,
                sceneEpoch: cacheExchange.workingCache.getSceneEpoch(),
            });
        } catch (error) { console.error(`[checkpoint] persistence failed: ${error.message}`); }
    };
    setInterval(persistContinuityCheckpoint, 30000).unref();
    process.once("exit", persistContinuityCheckpoint);

    // Stale-proposal rejection (docs/next-build-prompt.md §2.7): the ArtifactResult
    // still resolves to whoever asked for it either way (see
    // SceneBridgeClient#annotateStaleness) - this listener only logs the event so
    // RQ4's "stale applications" measure has something to count.
    bridge.on("stale_proposal", (envelope) => {
        memory.artifactLog.append({
            eventType: "stale_proposal",
            targetObjectId: envelope.targetObjectId,
            correlationId: envelope.correlationId,
            staleness: envelope.staleness,
        });
        memory.personPolicy.recordEvent({
            sessionId: envelope.sessionId,
            eventType: "stale_proposal",
            targetObjectId: envelope.targetObjectId,
            at: envelope.timestamp,
        });
        console.error(`[unity_scene_bridge] stale proposal detected: correlationId=${envelope.correlationId} target=${envelope.targetObjectId} focusWas=${envelope.staleness.focusObjectIdAtArrival}`);
    });

    // Memory-tool calls are synchronous/in-process (near-zero real latency by
    // construction) so there is no meaningful "duration" to instrument - what
    // matters for the paper's "memory retrieval latency" measure is marking WHEN
    // each retrieval happens within a turn's deliberation timeline. Only marks if
    // the caller supplied a correlationId (docs/next-build-prompt.md §2.6).
    function markMemoryRetrieval(correlationId, toolName) {
        if (correlationId) memory.timeline.mark(correlationId, "deliberation", `memory_retrieval:${toolName}`);
    }

    // @modelcontextprotocol/sdk ships ESM-only; this package is CommonJS, so
    // it is loaded via dynamic import rather than require().
    const { McpServer } = await import("@modelcontextprotocol/sdk/server/mcp.js");
    const { StdioServerTransport } = await import("@modelcontextprotocol/sdk/server/stdio.js");

    const server = new McpServer({ name: "unity-scene-bridge", version: "0.1.0" });

    server.registerTool(
        "query_scene",
        {
            title: "Query XR scene state",
            description:
                "Requests the current focus+halo scene summary from the Unity/Ubiq XR client, either for a " +
                "specific stable object id or a filter (e.g. 'tag:game', 'componentType:Light'). Real-time over " +
                "Ubiq NetworkId 96 (request) / 95 (reply). Requires the Unity-side SceneQuery/SceneDelta " +
                "handlers (roadmap phase 1) - will time out against a real headset until those land; use " +
                "mock_unity_peer.js to test the bridge itself in the meantime.",
            inputSchema: {
                objectId: z.string().optional().describe("Stable scene object id to focus on"),
                filter: z.string().optional().describe("Filter expression, e.g. tag:game or componentType:Light"),
                sessionId: sessionIdSchema.describe("Required for stale-proposal detection and cross-call correlation"),
                correlationId: z.string().optional().describe("Reuse an existing correlationId to thread this call into the same authoring-turn timeline as prior/later calls"),
                timeoutMs: z.number().int().positive().optional(),
            },
        },
        async ({ objectId, filter, sessionId, correlationId, timeoutMs }) => {
            try {
                const result = await bridge.querySceneFocus({ objectId, filter, sessionId, correlationId, timeoutMs });
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (err) {
                return { content: [{ type: "text", text: `query_scene failed: ${err.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "propose_artifact",
        {
            title: "Propose a code artifact to the XR scene",
            description:
                "Sends a C# MonoBehaviour artifact to Unity for attachment to targetObjectId. This tool assumes " +
                "the artifact already passed static/semantic/sandbox validation upstream - it is the delivery " +
                "step, not the validator. authoringMode='automatic' tells Unity to apply without a confirm UI " +
                "(only for low-risk, single-object, cosmetic changes already cleared); any other mode shows a " +
                "ghost-preview/confirm UI to the user. Always resolves with the final ArtifactResult " +
                "(status: committed|rejected|error) once Unity responds on NetworkId 100.",
            inputSchema: {
                code: z.string().optional().describe("Full C# source; omitted only for operation='remove'"),
                targetObjectId: z.string().describe("Stable scene object id this artifact attaches to"),
                intent: z.string().optional().describe("Original natural-language intent, shown in the confirmation UI"),
                authoringMode: z.enum(["automatic", "semi_auto_confirm", "semi_auto_steer"]).optional(),
                interactionMode: z.enum(["L1", "L2", "L3", "L4", "L5"]).optional().describe("Which of the paper's five interaction modes initiated this turn (main.tex tab:modes) - separate from authoringMode, which controls the execution gate"),
                sceneEpoch: z.string().optional(),
                snapshotId: z.string().optional(),
                objectRevision: z.number().optional(),
                snapshotTakenAt: z.number().optional(),
                validationState: z.enum(["accepted", "rejected"]).optional(),
                validationSummary: z.string().optional(),
                riskScore: z.number().min(0).max(1).optional(),
                consentRoute: z.string().optional(),
                requiredPermissions: z.array(z.string()).optional(),
                expectedSideEffects: z.string().optional(),
                artifactVersion: z.string().optional(),
                triggerSource: z.enum(["system_opportunity", "context", "clarification", "explicit_request"]).optional(),
                reversible: z.boolean().optional(),
                localOnly: z.boolean().optional(),
                detailResolved: z.boolean().optional(),
                operation: z.enum(LIFECYCLE_OPERATIONS).optional(),
                existingArtifactId: z.string().optional(),
                candidateId: z.string().optional(),
                candidateSetId: z.string().optional(),
                candidateCount: z.number().int().min(1).optional(),
                selectionReason: z.string().optional(),
                experienceMode: z.string().optional(),
                sessionId: sessionIdSchema,
                correlationId: z.string().optional().describe("Reuse an existing correlationId to thread this call into the same authoring-turn timeline as prior/later calls"),
                timeoutMs: z.number().int().positive().optional(),
            },
        },
        async (args) => {
            const { code, targetObjectId, intent, authoringMode, interactionMode, sessionId, correlationId } = args;
            try {
                const lifecycle = validateLifecycle(args);
                if (!lifecycle.accepted) return { content: [{ type: "text", text: `propose_artifact lifecycle rejected: ${lifecycle.reasons.join("; ")}` }], isError: true };
                if (["edit", "remove"].includes(lifecycle.operation) && authoringMode === "automatic") {
                    return { content: [{ type: "text", text: `${lifecycle.operation} requires explicit confirmation` }], isError: true };
                }
                if (interactionMode) {
                    const policy = checkModePolicy({ ...args, userPreference: memory.personPolicy.getPolicy({ sessionId }) });
                    if (!policy.accepted) {
                        return { content: [{ type: "text", text: `propose_artifact rejected by interaction-mode policy: ${policy.reasons.join("; ")}` }], isError: true };
                    }
                }
                const result = await bridge.proposeArtifact(args);
                memory.artifactLog.append({
                    eventType: "propose_artifact",
                    targetObjectId,
                    correlationId: result.correlationId,
                    intent: intent || null,
                    authoringMode: authoringMode || null,
                    interactionMode: interactionMode || null,
                    sceneEpoch: args.sceneEpoch || null,
                    snapshotId: args.snapshotId || null,
                    objectRevision: args.objectRevision ?? null,
                    validationState: args.validationState || "accepted",
                    validationSummary: args.validationSummary || null,
                    riskScore: args.riskScore ?? null,
                    requiredPermissions: args.requiredPermissions || [args.operation === "remove" ? "detach_component" : "attach_component"],
                    expectedSideEffects: args.expectedSideEffects || null,
                    artifactVersion: args.artifactVersion || "1",
                    operation: args.operation || "create",
                    supersedesArtifactId: args.existingArtifactId || null,
                    candidateId: args.candidateId || null,
                    candidateSetId: args.candidateSetId || null,
                    candidateCount: args.candidateCount || 1,
                    selectionReason: args.selectionReason || null,
                    status: result.payload && result.payload.status,
                    artifactId: result.payload && result.payload.artifactId,
                });
                memory.personPolicy.recordEvent({ sessionId, eventType: `propose_artifact:${result.payload && result.payload.status}`, targetObjectId, at: Date.now() });
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (err) {
                return { content: [{ type: "text", text: `propose_artifact failed: ${err.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "rank_artifact_candidates",
        {
            title: "Rank independently verified artifact candidates",
            description: "Selects the best eligible candidate after every candidate has its own Validator/Critic and Verification Space result. Ranking never bypasses mode or proposal gates.",
            inputSchema: {
                sessionId: sessionIdSchema,
                correlationId: z.string(),
                targetObjectId: z.string(),
                candidateSetId: z.string(),
                candidates: z.array(z.object({
                    candidateId: z.string(), operation: z.enum(LIFECYCLE_OPERATIONS), code: z.string().optional(),
                    existingArtifactId: z.string().optional(), validationState: z.string(), simulationStatus: z.string(),
                    riskScore: z.number().min(0).max(1), authoringMode: z.string().optional(), experienceMode: z.string().optional(),
                })).min(2),
            },
        },
        async ({ sessionId, correlationId, targetObjectId, candidateSetId, candidates }) => {
            const result = rankCandidates(candidates, {
                profile: memory.personPolicy.getPolicy({ sessionId }),
                experienceContext: memory.experienceContext.get(sessionId),
            });
            for (const candidate of result.ranked) {
                memory.artifactLog.append({
                    eventType: candidate === result.selected ? "candidate_selected" : "candidate_rejected",
                    operation: candidate.operation, targetObjectId, correlationId, candidateSetId,
                    candidateId: candidate.candidateId, riskScore: candidate.riskScore,
                    status: candidate.ranking.eligible ? (candidate === result.selected ? "selected" : "not_selected") : "ineligible",
                    selectionReason: candidate === result.selected ? `highest deterministic score ${candidate.ranking.score}` : `score ${candidate.ranking.score}`,
                });
            }
            appendEvaluationEvent({
                eventType: "candidate_selection", sessionId, correlationId, targetObjectId, candidateSetId,
                candidateCount: candidates.length, eligibleCount: result.ranked.filter((item) => item.ranking.eligible).length,
                selectedCandidateId: result.selected && result.selected.candidateId,
                selectedScore: result.selected && result.selected.ranking.score,
            });
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }], isError: !result.selected };
        }
    );

    server.registerTool(
        "simulate_artifact",
        {
            title: "Dry-run a candidate artifact in the Experimental Space",
            description:
                "Tests a candidate C# MonoBehaviour artifact against the staging clone described in " +
                "docs/shared-memory-and-experimental-space.md, without ever touching the live object. Reuses " +
                "the same ArtifactProposal/ArtifactResult channels (99/100) as propose_artifact, distinguished " +
                "by payload.mode='simulate'. Use this before propose_artifact for anything above automatic-mode " +
                "risk. Unity compiles and attaches the candidate to an inactive staging clone; " +
                "mock_unity_peer.js provides a deterministic stand-in for headless integration tests.",
            inputSchema: {
                code: z.string().optional().describe("Full C# source; omitted only for operation='remove'"),
                targetObjectId: z.string().describe("Stable scene object id the artifact would attach to"),
                intent: z.string().optional(),
                interactionMode: z.enum(["L1", "L2", "L3", "L4", "L5"]).optional(),
                operation: z.enum(LIFECYCLE_OPERATIONS).optional(),
                existingArtifactId: z.string().optional(),
                candidateId: z.string().optional(),
                candidateSetId: z.string().optional(),
                sessionId: sessionIdSchema,
                correlationId: z.string().optional(),
                timeoutMs: z.number().int().positive().optional(),
            },
        },
        async (args) => {
            const { code, targetObjectId, intent, interactionMode, sessionId, correlationId, timeoutMs } = args;
            try {
                const lifecycle = validateLifecycle(args);
                if (!lifecycle.accepted) return { content: [{ type: "text", text: `simulate_artifact lifecycle rejected: ${lifecycle.reasons.join("; ")}` }], isError: true };
                const result = await bridge.proposeArtifact({ ...args, simulate: true });
                memory.artifactLog.append({
                    eventType: "simulate_artifact",
                    targetObjectId,
                    correlationId: result.correlationId,
                    intent: intent || null,
                    interactionMode: interactionMode || null,
                    operation: lifecycle.operation,
                    supersedesArtifactId: args.existingArtifactId || null,
                    candidateId: args.candidateId || null,
                    candidateSetId: args.candidateSetId || null,
                    status: result.payload && result.payload.status,
                });
                memory.personPolicy.recordEvent({ sessionId, eventType: `simulate_artifact:${result.payload && result.payload.status}`, targetObjectId, at: Date.now() });
                if (result.payload && result.payload.status === "simulated") {
                    memory.timeline.mark(result.correlationId, "experimental", "SimulateArtifact", result.timestamp);
                }
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (err) {
                return { content: [{ type: "text", text: `simulate_artifact failed: ${err.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "get_artifact_status",
        {
            title: "Get the last known status of a correlationId",
            description:
                "Non-blocking lookup for a correlationId returned by an earlier query_scene/propose_artifact " +
                "call. Does not wait again; returns 'pending', 'resolved' (with the envelope), or 'unknown'.",
            inputSchema: { correlationId: z.string() },
        },
        async ({ correlationId }) => {
            const result = bridge.getArtifactStatus(correlationId);
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
        }
    );

    server.registerTool(
        "get_bridge_status",
        {
            title: "Get Unity Scene Bridge connection status",
            description:
                "Reports whether this MCP server is currently connected to the Ubiq room and when Unity's " +
                "presence heartbeat (NetworkId 101) was last seen. Use this first when diagnosing timeouts.",
            inputSchema: {},
        },
        async () => {
            return { content: [{ type: "text", text: JSON.stringify(bridge.getStatus(), null, 2) }] };
        }
    );

    // --- Shared XR Memory tools (docs/shared-memory-and-experimental-space.md) ---
    // Names match the paper (main.tex, "Shared XR Memory and Experimental Space")
    // exactly - naming discipline is deliberate, see that doc §4.5.

    server.registerTool(
        "query_visual_memory",
        {
            title: "Query the Visual memory layer",
            description:
                "Retrieve coarse boxes/volumes/transforms/confidence for a target object id, or all cached " +
                "objects matching a text filter. Backed by cached SceneDelta data plus recent sensor events - " +
                "see docs/shared-memory-and-experimental-space.md tab:memory-layers.",
            inputSchema: {
                objectId: z.string().optional(),
                filter: z.string().optional(),
                correlationId: z.string().optional().describe("Marks this retrieval on the shared turn timeline; see get_timeline_metrics"),
            },
        },
        async ({ objectId, filter, correlationId }) => {
            markMemoryRetrieval(correlationId, "query_visual_memory");
            return { content: [{ type: "text", text: JSON.stringify(memory.visual.query({ objectId, filter }), null, 2) }] };
        }
    );

    server.registerTool(
        "query_scene_graph",
        {
            title: "Query the Semantic memory layer's relation graph",
            description:
                "Retrieve relations for an object (or the whole cached graph): 'near' from halo co-membership, " +
                "plus sensor-derived relations (touching, observed-by, reachable-from). Real relations like " +
                "on/inside/attached-to/supports require Unity to publish hierarchy data, not implemented yet - " +
                "this is a naive approximation, documented as such per docs/shared-memory-and-experimental-space.md §4.2.",
            inputSchema: { objectId: z.string().optional(), correlationId: z.string().optional() },
        },
        async ({ objectId, correlationId }) => {
            markMemoryRetrieval(correlationId, "query_scene_graph");
            return { content: [{ type: "text", text: JSON.stringify(memory.sceneGraph.queryGraph({ objectId }), null, 2) }] };
        }
    );

    server.registerTool(
        "query_affordances",
        {
            title: "Query inferred affordances for an object",
            description:
                "Retrieve inferred usable actions for an object beyond simple proximity, e.g. 'usable' for a " +
                "Grabbable component. This is a static tag/component lookup table, NOT learned semantic " +
                "reasoning - do not describe it as inference in the paper without changing the implementation.",
            inputSchema: { objectId: z.string(), correlationId: z.string().optional() },
        },
        async ({ objectId, correlationId }) => {
            markMemoryRetrieval(correlationId, "query_affordances");
            return { content: [{ type: "text", text: JSON.stringify(memory.sceneGraph.queryAffordances({ objectId }), null, 2) }] };
        }
    );

    server.registerTool(
        "get_script_context",
        {
            title: "Query the Script/context memory layer",
            description:
                "Retrieve components, recent artifact history, and the static capability policy (allowed/denied " +
                "namespaces) for an object - what generated code touching this object can and cannot do.",
            inputSchema: { objectId: z.string(), correlationId: z.string().optional() },
        },
        async ({ objectId, correlationId }) => {
            markMemoryRetrieval(correlationId, "get_script_context");
            const visualEntry = memory.visual.byObjectId.get(objectId);
            const result = {
                objectId,
                components: (visualEntry && visualEntry.focus && visualEntry.focus.components) || [],
                recentArtifacts: memory.artifactLog.history({ objectId, limit: 5 }),
                capabilityPolicy: {
                    allowedNamespaces: ["UnityEngine"],
                    deniedNamespaces: ["System.IO", "System.Net", "System.Diagnostics", "System.Reflection"],
                    note: "Unity enforces a structured namespace/capability guard plus RoslynCSharp UseSettings security checks; this is defense in depth, not a formal process sandbox.",
                },
            };
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
        }
    );

    server.registerTool(
        "get_artifact_history",
        {
            title: "Query the Temporal memory layer's artifact history",
            description: "Retrieve prior versions, proposals, simulations, and outcomes logged for an object (or the most recent across all objects).",
            inputSchema: {
                objectId: z.string().optional(),
                limit: z.number().int().positive().optional(),
                correlationId: z.string().optional(),
            },
        },
        async ({ objectId, limit, correlationId }) => {
            markMemoryRetrieval(correlationId, "get_artifact_history");
            return { content: [{ type: "text", text: JSON.stringify(memory.artifactLog.history({ objectId, limit }), null, 2) }] };
        }
    );

    server.registerTool(
        "get_person_policy",
        {
            title: "Query the Person/multi-user memory layer",
            description:
                "Retrieve roles, permissions, consent policy, and priorDecisions for a session. Role/permissions " +
                "are a static single-owner stub (docs/shared-memory-and-experimental-space.md §4.2); " +
                "priorDecisions now updates from real session events (docs/next-build-prompt.md §2.4).",
            inputSchema: { sessionId: sessionIdSchema, correlationId: z.string().optional() },
        },
        async ({ sessionId, correlationId }) => {
            markMemoryRetrieval(correlationId, "get_person_policy");
            return { content: [{ type: "text", text: JSON.stringify(memory.personPolicy.getPolicy({ sessionId }), null, 2) }] };
        }
    );

    server.registerTool(
        "set_person_profile_consent",
        {
            title: "Opt in or revoke pseudonymous cross-session learning",
            description: "Binds this session to a pseudonymous personId only after explicit consent. Revocation deletes the persisted profile.",
            inputSchema: { sessionId: sessionIdSchema, personId: z.string().min(1).max(128), consent: z.boolean(), retentionDays: z.number().int().min(1).max(365).optional() },
        },
        async (args) => ({ content: [{ type: "text", text: JSON.stringify(memory.personPolicy.setPersistenceConsent(args), null, 2) }] })
    );

    server.registerTool(
        "reset_person_profile",
        {
            title: "Reset a learned person profile",
            description: "Deletes learned cross-session preferences and revokes bindings. This is irreversible and must follow an explicit user request.",
            inputSchema: { sessionId: sessionIdSchema, personId: z.string().min(1).max(128).optional(), confirmReset: z.literal(true) },
        },
        async (args) => ({ content: [{ type: "text", text: JSON.stringify(memory.personPolicy.resetProfile(args), null, 2) }] })
    );

    server.registerTool(
        "get_experience_context",
        {
            title: "Inspect the current activity/experience context",
            inputSchema: { sessionId: sessionIdSchema },
        },
        async ({ sessionId }) => ({ content: [{ type: "text", text: JSON.stringify(memory.experienceContext.get(sessionId), null, 2) }] })
    );

    server.registerTool(
        "set_experience_context",
        {
            title: "Explicitly override the current activity/experience context",
            inputSchema: { sessionId: sessionIdSchema, mode: z.enum(["productivity", "training", "entertainment", "exploration", "unspecified"]) },
        },
        async ({ sessionId, mode }) => ({ content: [{ type: "text", text: JSON.stringify(memory.experienceContext.set({ sessionId, mode }), null, 2) }] })
    );

    server.registerTool(
        "get_region_context",
        {
            title: "Query current region context (locomotion sensor)",
            description:
                "Not one of the paper's eight named memory operations - reports which named region a session " +
                "is currently inside, derived from discrete 'locomotion' sensor events (region entry/exit), " +
                "per the paper's 'locomotion updates region context' sentence " +
                "(docs/paper-sync-timelines-and-modes.md §1.3, §2.3).",
            inputSchema: { sessionId: sessionIdSchema },
        },
        async ({ sessionId }) => ({ content: [{ type: "text", text: JSON.stringify(memory.region.getCurrentRegion(sessionId), null, 2) }] })
    );

    server.registerTool(
        "commit_memory_event",
        {
            title: "Record a memory event",
            description:
                "Writes an arbitrary result, rollback, or decision to BOTH the Temporal memory layer " +
                "(artifact log) and the Person memory layer (priorDecisions) - the paper's own sentence is " +
                "'confirmations, rejections, undo events, and agent results update temporal AND person memory' " +
                "(main.tex, Shared XR Memory). Outside the automatic logging propose_artifact/simulate_artifact " +
                "already do. Use for events like an undo, a rejected clarification, or a conflict-resolution " +
                "decision.",
            inputSchema: {
                targetObjectId: z.string(),
                eventType: z.string().describe("e.g. 'rollback', 'clarification_rejected', 'conflict_decision'"),
                sessionId: sessionIdSchema,
                correlationId: z.string().optional(),
                data: z.record(z.string(), z.unknown()).optional(),
            },
        },
        async ({ targetObjectId, eventType, sessionId, correlationId, data }) => {
            const record = memory.artifactLog.append({ targetObjectId, eventType, correlationId, ...(data || {}) });
            memory.personPolicy.recordEvent({ sessionId, eventType, targetObjectId, at: record.loggedAt });
            return { content: [{ type: "text", text: JSON.stringify(record, null, 2) }] };
        }
    );

    server.registerTool(
        "record_intent",
        {
            title: "Record a speech/text intent",
            description:
                "Not one of the paper's eight named memory operations - records an utterance into the intent " +
                "memory concept from the paper's sensor sentence ('speech updates intent'). Not object-scoped " +
                "(intent capture happens before scene grounding), so this is separate from commit_memory_event " +
                "rather than reusing it. IMPORTANT: real speech-to-text is not wired into this pipeline yet - " +
                "today this is fed by whatever text the caller provides (e.g. the orchestrator's CLI argument), " +
                "not a live transcript. See docs/next-build-prompt.md §1.3.",
            inputSchema: {
                text: z.string(),
                sessionId: sessionIdSchema,
                correlationId: z.string().optional(),
            },
        },
        async ({ text, sessionId, correlationId }) => {
            const entry = memory.intent.record({ sessionId, text, correlationId });
            const experienceContext = memory.experienceContext.observeIntent({ sessionId, text });
            return { content: [{ type: "text", text: JSON.stringify({ intent: entry, experienceContext }, null, 2) }] };
        }
    );

    server.registerTool(
        "get_evolution_history",
        {
            title: "Inspect procedure evolution for an object or scene",
            description: "Returns ordered create/edit/remove/candidate/rollback lineage from the existing temporal artifact log.",
            inputSchema: { objectId: z.string().optional(), limit: z.number().int().positive().max(500).optional(), correlationId: z.string().optional() },
        },
        async ({ objectId, limit, correlationId }) => {
            markMemoryRetrieval(correlationId, "get_evolution_history");
            return { content: [{ type: "text", text: JSON.stringify(memory.artifactLog.evolution({ objectId, limit }), null, 2) }] };
        }
    );

    server.registerTool(
        "create_system_checkpoint",
        {
            title: "Persist the backend continuity checkpoint",
            description: "Persists active artifact references, consented profiles, and experience contexts. Live code attachment is independently checkpointed by Unity.",
            inputSchema: {},
        },
        async () => ({ content: [{ type: "text", text: JSON.stringify(memory.checkpoints.save({ artifactLog: memory.artifactLog, personPolicy: memory.personPolicy, experienceContext: memory.experienceContext, sceneEpoch: cacheExchange.workingCache.getSceneEpoch() }), null, 2) }] })
    );

    server.registerTool(
        "resume_system_checkpoint",
        {
            title: "Classify checkpoint references against the current scene",
            description: "Never silently restores missing targets; absent stable IDs are returned and logged as orphaned.",
            inputSchema: { currentObjectIds: z.array(z.string()).min(1) },
        },
        async ({ currentObjectIds }) => {
            const result = memory.checkpoints.load({ currentObjectIds });
            for (const orphan of result.orphaned || []) memory.artifactLog.append({ eventType: "checkpoint_orphaned", ...orphan });
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
        }
    );

    server.registerTool(
        "get_timeline_metrics",
        {
            title: "Get timeline / perceived-synchronicity metrics for a correlationId",
            description:
                "Not one of the paper's eight named memory operations - an additional diagnostic that " +
                "operationalizes 'perceived synchronicity': the gap between the first visible agent response " +
                "and the final validated/committed result for one authoring turn, matching the paper's " +
                "dependent variables 'time to visible response' and 'time to validated execution' " +
                "(rag/drafts/agenticxr_design_study_sections.md).",
            inputSchema: { correlationId: z.string() },
        },
        async ({ correlationId }) => ({ content: [{ type: "text", text: JSON.stringify(memory.timeline.synchronicity(correlationId), null, 2) }] })
    );

    // --- Cache Exchange Layer tools (docs/cache-exchange-layer.md) ---
    // Backend agents may READ the Agent Working Cache and issue backfill/snapshot/
    // commit/rollback REQUESTS - they never mutate live XR state directly. Unity's
    // (unimplemented) handler is the authoritative gate on commit/rollback; these
    // tools only reach the pre-flight ProposalGate check and the wire send.

    server.registerTool(
        "query_agent_cache",
        {
            title: "Query the Agent Working Cache",
            description:
                "Read the backend's mirror of recently ACCEPTED scene deltas - NOT authoritative for live " +
                "commits (docs/cache-exchange-layer.md). Look up by exactly one of stableObjectId, " +
                "correlationId, label, region, or artifactId.",
            inputSchema: {
                stableObjectId: z.string().optional(),
                correlationId: z.string().optional(),
                label: z.string().optional(),
                region: z.string().optional(),
                artifactId: z.string().optional(),
            },
        },
        async ({ stableObjectId, correlationId, label, region, artifactId }) => {
            let result;
            if (stableObjectId) result = cacheExchange.workingCache.getByObjectId(stableObjectId);
            else if (correlationId) result = cacheExchange.workingCache.getByCorrelationId(correlationId);
            else if (label) result = cacheExchange.workingCache.queryByLabel(label);
            else if (region) result = cacheExchange.workingCache.queryByRegion(region);
            else if (artifactId) result = cacheExchange.workingCache.getByArtifactId(artifactId);
            else return { content: [{ type: "text", text: "query_agent_cache requires one of: stableObjectId, correlationId, label, region, artifactId" }], isError: true };
            return { content: [{ type: "text", text: JSON.stringify({ sceneEpoch: cacheExchange.workingCache.getSceneEpoch(), result }, null, 2) }] };
        }
    );

    server.registerTool(
        "request_backfill",
        {
            title: "Request missing deltas from Unity's event history",
            description: "Recovers a deltaSeq range after a detected gap, reconnect, or restart. lastSeenSeq=0 means 'send everything you have'.",
            inputSchema: { sessionId: sessionIdSchema, lastSeenSeq: z.number().int().min(0).optional(), correlationId: z.string().optional(), timeoutMs: z.number().int().positive().optional() },
        },
        async ({ sessionId, lastSeenSeq, correlationId, timeoutMs }) => {
            try {
                const result = await bridge.requestBackfill({ sessionId, lastSeenSeq, correlationId, timeoutMs });
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (err) {
                return { content: [{ type: "text", text: `request_backfill failed: ${err.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "request_snapshot",
        {
            title: "Request a fresh CacheSnapshot from Unity",
            description: "Used when the working cache is stale, epoch-conflicted, or missing an object entirely - asks Unity to restate full state rather than patch it.",
            inputSchema: { sessionId: sessionIdSchema, correlationId: z.string().optional(), timeoutMs: z.number().int().positive().optional() },
        },
        async ({ sessionId, correlationId, timeoutMs }) => {
            try {
                const result = await bridge.requestSnapshot({ sessionId, correlationId, timeoutMs });
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (err) {
                return { content: [{ type: "text", text: `request_snapshot failed: ${err.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "check_proposal_gate",
        {
            title: "Pre-flight compare-and-swap freshness check (advisory)",
            description:
                "Runs the backend's ProposalGate BEFORE sending a CommitRequest to Unity - catches obviously " +
                "stale proposals fast, with structured reasons. This is advisory only: Unity's own handler is " +
                "the authoritative gate and re-checks at commit time regardless of this result.",
            inputSchema: {
                correlationId: z.string(),
                targetObjectId: z.string(),
                sceneEpoch: z.string().optional(),
                objectRevision: z.number().optional(),
                snapshotId: z.string().optional(),
                snapshotTakenAt: z.number().optional(),
                authoringMode: z.enum(["automatic", "semi_auto_confirm", "semi_auto_steer"]).optional(),
                consentRoute: z.string().optional(),
                validationState: z.string().optional(),
            },
        },
        async (args) => ({ content: [{ type: "text", text: JSON.stringify(cacheExchange.proposalGate.checkProposal(args), null, 2) }] })
    );

    server.registerTool(
        "request_commit",
        {
            title: "Phase 2: ask Unity to authoritatively commit a proposal",
            description:
                "Runs check_proposal_gate first (rejects fast, without a round trip, if already stale), then " +
                "sends CommitRequest to Unity on NetworkId 99 and awaits CommitAccepted/CommitRejected on 100. " +
                "Unity's decision is authoritative regardless of the pre-flight result.",
            inputSchema: {
                correlationId: z.string(),
                targetObjectId: z.string(),
                snapshotId: z.string(),
                objectRevision: z.number().optional(),
                sceneEpoch: z.string().optional(),
                snapshotTakenAt: z.number().optional(),
                authoringMode: z.enum(["automatic", "semi_auto_confirm", "semi_auto_steer"]).optional(),
                consentRoute: z.string().optional(),
                validationState: z.string().optional(),
                sessionId: sessionIdSchema,
                timeoutMs: z.number().int().positive().optional(),
            },
        },
        async (args) => {
            const preflight = cacheExchange.proposalGate.checkProposal(args);
            cacheExchange.journal.append(args.sessionId, "validation", { correlationId: args.correlationId, preflight });
            if (!preflight.accepted) {
                return { content: [{ type: "text", text: JSON.stringify({ committed: false, stage: "preflight", ...preflight }, null, 2) }] };
            }
            cacheExchange.reconciler.markProposalPending(args.correlationId, args.sessionId);
            try {
                const result = await bridge.sendCommitRequest(args);
                cacheExchange.reconciler.resolvePending(args.correlationId);
                cacheExchange.journal.append(args.sessionId, "commit", result);
                if (result.type === "CommitAccepted") {
                    cacheExchange.workingCache.recordArtifactOutcome(result.payload && result.payload.artifactId, { correlationId: args.correlationId, targetObjectId: args.targetObjectId, status: "committed" });
                }
                return { content: [{ type: "text", text: JSON.stringify({ committed: result.type === "CommitAccepted", stage: "unity", envelope: result }, null, 2) }] };
            } catch (err) {
                cacheExchange.reconciler.resolvePending(args.correlationId);
                return { content: [{ type: "text", text: `request_commit failed: ${err.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "request_rollback",
        {
            title: "Ask Unity to roll back a previously committed artifact",
            inputSchema: {
                correlationId: z.string(),
                targetObjectId: z.string(),
                artifactId: z.string().optional(),
                sessionId: sessionIdSchema,
                timeoutMs: z.number().int().positive().optional(),
            },
        },
        async ({ correlationId, targetObjectId, artifactId, sessionId, timeoutMs }) => {
            try {
                const result = await bridge.sendRollbackRequest({ correlationId, targetObjectId, artifactId, sessionId, timeoutMs });
                cacheExchange.journal.append(sessionId, "rollback", result);
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (err) {
                return { content: [{ type: "text", text: `request_rollback failed: ${err.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "send_cache_invalidation",
        {
            title: "Tell Unity a cached object/region/proposal is stale",
            inputSchema: { sessionId: sessionIdSchema, correlationId: z.string().optional(), targetObjectId: z.string().optional(), reason: z.string() },
        },
        async ({ sessionId, correlationId, targetObjectId, reason }) => {
            if (correlationId) cacheExchange.reconciler.invalidateProposal(correlationId, reason);
            const sentCorrelationId = bridge.sendCacheInvalidation({ sessionId, correlationId, targetObjectId, reason });
            return { content: [{ type: "text", text: JSON.stringify({ sent: true, correlationId: sentCorrelationId }, null, 2) }] };
        }
    );

    server.registerTool(
        "send_agent_status",
        {
            title: "Push a structured agent status update",
            description: "state is one of: thinking, querying_memory, validating, repairing, waiting_for_user, ready_to_preview, failed.",
            inputSchema: { sessionId: sessionIdSchema, correlationId: z.string().optional(), state: z.string(), detail: z.string().optional() },
        },
        async ({ sessionId, correlationId, state, detail }) => {
            const sentCorrelationId = bridge.sendAgentStatus({ sessionId, correlationId, state, detail });
            return { content: [{ type: "text", text: JSON.stringify({ sent: true, correlationId: sentCorrelationId }, null, 2) }] };
        }
    );

    await bridge.connect();
    console.error(
        `[unity_scene_bridge] connected to Ubiq room ${config.roomGuid} at ${config.host || "localhost"}:${config.roomserver.tcp.port}`
    );

    const transport = new StdioServerTransport();
    await server.connect(transport);
    console.error("[unity_scene_bridge] MCP server ready on stdio");
}

main().catch((err) => {
    console.error("[unity_scene_bridge] fatal error:", err);
    process.exit(1);
});
