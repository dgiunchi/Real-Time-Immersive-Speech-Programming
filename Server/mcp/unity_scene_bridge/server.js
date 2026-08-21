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
const { checkModePolicy, isVerificationBypassed, SIMULATION_SKIPPED_STATUS } = require("../../orchestrator/mode_policy");
const { rankCandidates, validateLifecycle, LIFECYCLE_OPERATIONS } = require("../../orchestrator/candidate_selector");
const { VERIFICATION_LEVELS } = require("../../orchestrator/goal_schema");
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
    const memory = new SharedMemory({ artifactLogPath: process.env.AGENTICXR_ARTIFACT_LOG });
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
            sessionId: envelope.sessionId,
            targetObjectId: envelope.targetObjectId,
            correlationId: envelope.correlationId,
            staleness: envelope.staleness,
            stale: true,
            groundingError: true,
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
    function measureMemoryRetrieval(correlationId, toolName, retrieve) {
        const started = process.hrtime.bigint();
        const result = retrieve();
        const durationMs = Number(process.hrtime.bigint() - started) / 1e6;
        if (correlationId) {
            memory.timeline.mark(correlationId, "deliberation", `memory_retrieval:${toolName}`);
            if (memory.artifactLog.getStudyContext({ correlationId })) {
                memory.artifactLog.appendStudyEvent({
                    correlationId,
                    eventType: "memory_retrieval",
                    memoryOperation: toolName,
                    durationMs,
                });
            }
        }
        return result;
    }

    function appendStudyIfActive({ sessionId, correlationId } = {}, event) {
        if (!memory.artifactLog.getStudyContext({ sessionId, correlationId })) return null;
        return memory.artifactLog.appendStudyEvent({
            ...(sessionId ? { sessionId } : {}),
            correlationId,
            ...event,
        });
    }

    // The per-session study condition is whatever the registered trial says - the
    // researcher CLI / start_study_trial is the single switch. Outside a trial there
    // is no condition and therefore never a verification bypass.
    function activeStudyCondition({ sessionId, correlationId } = {}) {
        const context = memory.artifactLog.getStudyContext({ sessionId, correlationId });
        return context ? context.condition || null : null;
    }

    function recordProposalGate(args, result) {
        const reasons = result.reasons || [];
        const timestampAgeMs = Number.isFinite(args.snapshotTakenAt)
            ? Math.max(0, result.checkedAt - args.snapshotTakenAt) : null;
        return appendStudyIfActive(args, {
            eventType: "proposal_gate_checked",
            targetObjectId: args.targetObjectId || null,
            status: result.accepted ? "accepted" : "rejected",
            correlationIdValid: !reasons.some((reason) => /correlationId/i.test(reason)),
            targetObjectValid: !reasons.some((reason) => /target object/i.test(reason)),
            timestampAgeMs,
            stale: reasons.some((reason) => /epoch|snapshot|revision|too old|stale/i.test(reason)),
            validationState: args.validationState || null,
            reasonCode: !result.accepted ? "proposal_gate_rejected" : null,
        });
    }

    function deriveGoalEvidence(goal) {
        const records = memory.artifactLog.history({ limit: Number.MAX_SAFE_INTEGER })
            .filter((entry) =>
                entry.correlationId === goal.correlationId &&
                (entry.loggedAt || entry.at || 0) >= goal.createdAt
            );
        const statuses = records.map((entry) => String(entry.status || "").toLowerCase());
        const latestNumeric = (field) => {
            const found = records.find((entry) => Number.isFinite(entry[field]));
            return found ? found[field] : null;
        };
        const validator = records.find((entry) => entry.eventType === "goal_validator_judgment");
        return {
            artifactCommitted: statuses.includes("committed") || statuses.includes("removed"),
            validationAccepted: records.some((entry) => entry.validationState === "accepted"),
            simulationSucceeded: statuses.includes("simulated"),
            noRuntimeError: !records.some((entry) =>
                entry.failureStage === "runtime" || entry.status === "watchdog_disabled"),
            riskScore: latestNumeric("riskScore"),
            verificationDurationMs: latestNumeric("verificationDurationMs"),
            commitAttachDurationMs: latestNumeric("commitAttachDurationMs"),
            currentIteration: goal.currentIteration + 1,
            validatorJudgment: validator ? {
                accepted: validator.accepted === true,
                score: validator.score,
                rationaleCode: validator.rationaleCode || null,
            } : null,
            humanApproved: records.some((entry) =>
                entry.eventType === "user_decision:approved" || entry.status === "approved"),
        };
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
                if (!lifecycle.accepted) {
                    appendStudyIfActive(args, {
                        eventType: "validation_failure",
                        targetObjectId,
                        failureStage: "validation",
                        reasonCode: "lifecycle_invariant",
                    });
                    return { content: [{ type: "text", text: `propose_artifact lifecycle rejected: ${lifecycle.reasons.join("; ")}` }], isError: true };
                }
                if (["edit", "remove"].includes(lifecycle.operation) && authoringMode === "automatic") {
                    appendStudyIfActive(args, {
                        eventType: "validation_failure",
                        targetObjectId,
                        failureStage: "validation",
                        reasonCode: "confirmation_required",
                    });
                    return { content: [{ type: "text", text: `${lifecycle.operation} requires explicit confirmation` }], isError: true };
                }
                if (interactionMode) {
                    const policy = checkModePolicy({ ...args, userPreference: memory.personPolicy.getPolicy({ sessionId }) });
                    if (!policy.accepted) {
                        appendStudyIfActive(args, {
                            eventType: "validation_failure",
                            targetObjectId,
                            failureStage: "validation",
                            reasonCode: "interaction_mode_policy",
                            unsafeProposal: Number(args.riskScore) >= 0.7,
                        });
                        return { content: [{ type: "text", text: `propose_artifact rejected by interaction-mode policy: ${policy.reasons.join("; ")}` }], isError: true };
                    }
                }
                // In the agenticxr_no_verification arm the proposal proceeds through
                // the identical gate/consent path but is explicitly marked unverified
                // (no dry-run evidence exists). checkModePolicy above already ran
                // unchanged - the bypass never widens autonomy.
                const proposeCondition = activeStudyCondition(args);
                const verificationBypassed = isVerificationBypassed(proposeCondition);
                const result = await bridge.proposeArtifact({ ...args, verificationBypassed });
                memory.artifactLog.append({
                    eventType: "propose_artifact",
                    sessionId,
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
                    timestampAgeMs: Number.isFinite(args.snapshotTakenAt)
                        ? Math.max(0, Date.now() - args.snapshotTakenAt) : null,
                    verificationBypassed: verificationBypassed || undefined,
                    verificationState: verificationBypassed ? "unverified" : undefined,
                });
                memory.personPolicy.recordEvent({ sessionId, eventType: `propose_artifact:${result.payload && result.payload.status}`, targetObjectId, at: Date.now() });
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (err) {
                appendStudyIfActive(args, {
                    eventType: "artifact_pipeline_failure",
                    targetObjectId,
                    reasonCode: /capability|namespace/i.test(err.message) ? "capability_policy" : "transport_or_runtime",
                    blockedUnsafeArtifact: /capability|namespace/i.test(err.message),
                });
                return { content: [{ type: "text", text: `propose_artifact failed: ${err.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "rank_artifact_candidates",
        {
            title: "Rank independently verified artifact candidates",
            description: "Selects the best eligible candidate after every candidate has its own Validator/Critic and Verification Space result (or, in the agenticxr_no_verification study arm, a recorded dry-run skip). Accepts a single-candidate set for N=1 trials so candidate count and rank are always logged. Ranking never bypasses mode or proposal gates.",
            inputSchema: {
                sessionId: sessionIdSchema,
                correlationId: z.string(),
                targetObjectId: z.string(),
                candidateSetId: z.string(),
                candidates: z.array(z.object({
                    candidateId: z.string(), operation: z.enum(LIFECYCLE_OPERATIONS), code: z.string().optional(),
                    existingArtifactId: z.string().optional(), validationState: z.string(), simulationStatus: z.string(),
                    riskScore: z.number().min(0).max(1), authoringMode: z.string().optional(), experienceMode: z.string().optional(),
                    approach: z.string().optional(),
                })).min(1),
            },
        },
        async ({ sessionId, correlationId, targetObjectId, candidateSetId, candidates }) => {
            const rankCondition = activeStudyCondition({ sessionId, correlationId });
            const verificationBypassed = isVerificationBypassed(rankCondition);
            const result = rankCandidates(candidates, {
                profile: memory.personPolicy.getPolicy({ sessionId }),
                experienceContext: memory.experienceContext.get(sessionId),
                verificationBypassed,
            });
            const compact = (value, max = 90) => {
                const text = String(value || "No approach summary").replace(/\s+/g, " ").trim();
                return text.length <= max ? text : `${text.slice(0, max - 3)}...`;
            };
            result.comparisonSummary = result.ranked.map((candidate, index) =>
                `${index + 1}. ${candidate.candidateId} score=${candidate.ranking.score}: ${compact(candidate.approach)}`
            ).join("\n");
            if (result.selected) result.selected.selectionReason = result.comparisonSummary;
            for (const candidate of result.ranked) {
                memory.artifactLog.append({
                    eventType: candidate === result.selected ? "candidate_selected" : "candidate_rejected",
                    sessionId, operation: candidate.operation, targetObjectId, correlationId, candidateSetId,
                    candidateId: candidate.candidateId, riskScore: candidate.riskScore,
                    selectedCandidateRank: result.ranked.indexOf(candidate) + 1,
                    selectedCandidateScore: candidate.ranking.score,
                    status: candidate.ranking.eligible ? (candidate === result.selected ? "selected" : "not_selected") : "ineligible",
                    selectionReason: candidate === result.selected ? `highest deterministic score ${candidate.ranking.score}` : `score ${candidate.ranking.score}`,
                    verificationBypassed: verificationBypassed || undefined,
                });
            }
            memory.artifactLog.append({
                eventType: "candidate_selection",
                sessionId,
                correlationId,
                targetObjectId,
                candidateSetId,
                candidateCount: candidates.length,
                selectedCandidateId: result.selected && result.selected.candidateId,
                selectedCandidateRank: result.selected ? result.ranked.indexOf(result.selected) + 1 : null,
                selectedCandidateScore: result.selected && result.selected.ranking.score,
                status: result.selected ? "selected" : "no_eligible_candidate",
                verificationBypassed: verificationBypassed || undefined,
            });
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
                if (!lifecycle.accepted) {
                    appendStudyIfActive(args, {
                        eventType: "validation_failure",
                        targetObjectId,
                        failureStage: "validation",
                        reasonCode: "lifecycle_invariant",
                    });
                    return { content: [{ type: "text", text: `simulate_artifact lifecycle rejected: ${lifecycle.reasons.join("; ")}` }], isError: true };
                }
                // H2 condition arm (agenticxr_no_verification): skip the Verification
                // Space round trip entirely and record the skip honestly. Shared XR
                // Memory, freshness checks, preview, and consent are untouched -
                // only dry-run evidence is absent, and the eventual proposal is
                // marked unverified. Deterministic: driven by the registered trial
                // condition, never by the model.
                const simulateCondition = activeStudyCondition(args);
                if (isVerificationBypassed(simulateCondition)) {
                    memory.artifactLog.append({
                        eventType: "simulate_artifact",
                        sessionId,
                        targetObjectId,
                        correlationId,
                        intent: intent || null,
                        interactionMode: interactionMode || null,
                        operation: lifecycle.operation,
                        candidateId: args.candidateId || null,
                        candidateSetId: args.candidateSetId || null,
                        status: SIMULATION_SKIPPED_STATUS,
                        verificationOutcome: "bypassed",
                        verificationBypassed: true,
                        verificationDurationMs: 0,
                        condition: simulateCondition,
                    });
                    memory.personPolicy.recordEvent({ sessionId, eventType: `simulate_artifact:${SIMULATION_SKIPPED_STATUS}`, targetObjectId, at: Date.now() });
                    return {
                        content: [{
                            type: "text",
                            text: JSON.stringify({
                                correlationId: correlationId || null,
                                payload: {
                                    status: SIMULATION_SKIPPED_STATUS,
                                    note: "Verification Space dry-run skipped by study condition " +
                                        `'${simulateCondition}'. Treat this candidate's simulationStatus as ` +
                                        `'${SIMULATION_SKIPPED_STATUS}' when ranking; the proposal will be marked unverified.`,
                                },
                                verificationBypassed: true,
                            }, null, 2),
                        }],
                    };
                }
                const result = await bridge.proposeArtifact({ ...args, simulate: true });
                memory.artifactLog.append({
                    eventType: "simulate_artifact",
                    sessionId,
                    targetObjectId,
                    correlationId: result.correlationId,
                    intent: intent || null,
                    interactionMode: interactionMode || null,
                    operation: lifecycle.operation,
                    supersedesArtifactId: args.existingArtifactId || null,
                    candidateId: args.candidateId || null,
                    candidateSetId: args.candidateSetId || null,
                    status: result.payload && result.payload.status,
                    verificationOutcome: result.payload && result.payload.status === "simulated" ? "apply" : "reject",
                    verificationDurationMs: result.verificationDurationMs ?? null,
                });
                memory.personPolicy.recordEvent({ sessionId, eventType: `simulate_artifact:${result.payload && result.payload.status}`, targetObjectId, at: Date.now() });
                if (result.payload && result.payload.status === "simulated") {
                    memory.timeline.mark(result.correlationId, "experimental", "SimulateArtifact", result.timestamp);
                }
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (err) {
                appendStudyIfActive(args, {
                    eventType: "verification_outcome",
                    targetObjectId,
                    candidateId: args.candidateId || null,
                    candidateSetId: args.candidateSetId || null,
                    verificationOutcome: "reject",
                    status: "error",
                    reasonCode: /capability|namespace/i.test(err.message) ? "capability_policy" : "verification_failure",
                    blockedUnsafeArtifact: /capability|namespace/i.test(err.message),
                });
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
            const result = measureMemoryRetrieval(correlationId, "query_visual_memory",
                () => memory.visual.query({ objectId, filter }));
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
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
            const result = measureMemoryRetrieval(correlationId, "query_scene_graph",
                () => memory.sceneGraph.queryGraph({ objectId }));
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
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
            const result = measureMemoryRetrieval(correlationId, "query_affordances",
                () => memory.sceneGraph.queryAffordances({ objectId }));
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
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
            const result = measureMemoryRetrieval(correlationId, "get_script_context", () => {
                const visualEntry = memory.visual.byObjectId.get(objectId);
                return {
                    objectId,
                    components: (visualEntry && visualEntry.focus && visualEntry.focus.components) || [],
                    recentArtifacts: memory.artifactLog.history({ objectId, limit: 5 }),
                    capabilityPolicy: {
                        allowedNamespaces: ["UnityEngine"],
                        deniedNamespaces: ["System.IO", "System.Net", "System.Diagnostics", "System.Reflection"],
                        note: "Unity enforces a structured namespace/capability guard plus RoslynCSharp UseSettings security checks; this is defense in depth, not a formal process sandbox.",
                    },
                };
            });
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
            const result = measureMemoryRetrieval(correlationId, "get_artifact_history",
                () => memory.artifactLog.history({ objectId, limit }));
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
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
            const result = measureMemoryRetrieval(correlationId, "get_person_policy",
                () => memory.personPolicy.getPolicy({ sessionId }));
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
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
            inputSchema: { sessionId: sessionIdSchema, correlationId: z.string().optional() },
        },
        async ({ sessionId, correlationId }) => {
            const result = measureMemoryRetrieval(correlationId, "get_experience_context",
                () => memory.experienceContext.get(sessionId));
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
        }
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
        "get_activity_stream",
        {
            title: "Inspect recent normalized human-activity observations",
            description:
                "Reads the existing continuous SceneDelta/sensor stream. Threshold crossings are context " +
                "triggers only; they do not bypass mode policy, verification, Proposal Gate, or consent.",
            inputSchema: {
                sessionId: sessionIdSchema.optional(),
                targetObjectId: z.string().optional(),
                limit: z.number().int().min(1).max(200).optional(),
            },
        },
        async (args) => ({
            content: [{ type: "text", text: JSON.stringify(memory.activity.query(args), null, 2) }],
        })
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
        "start_study_trial",
        {
            title: "Start a validated objective-measurement trial",
            description: "Registers pseudonymous study identifiers in the existing temporal artifact log. No transcript or raw audio is stored.",
            inputSchema: {
                participantId: sessionIdSchema,
                sessionId: sessionIdSchema,
                trialId: sessionIdSchema,
                condition: sessionIdSchema,
                taskId: sessionIdSchema,
                interactionMode: z.enum(["baseline", "L1", "L2", "L3", "L4", "L5"]),
                correlationId: sessionIdSchema,
                candidateTarget: z.number().int().min(1).max(5).optional()
                    .describe("H4 per-trial candidate count: 1 for the single-candidate arm, >1 for best-of-N"),
            },
        },
        async (context) => {
            try {
                const record = memory.artifactLog.startStudyTrial({ ...context, studySource: "mcp" });
                return { content: [{ type: "text", text: JSON.stringify(record, null, 2) }] };
            } catch (error) {
                return { content: [{ type: "text", text: `start_study_trial failed: ${error.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "end_study_trial",
        {
            title: "Close a study trial with task/rubric outcomes",
            description: "Records completion, optional success, and structured rubric signals. Free-form transcripts and identifying content are not accepted.",
            inputSchema: {
                sessionId: sessionIdSchema,
                trialId: sessionIdSchema,
                correlationId: sessionIdSchema,
                taskCompletion: z.boolean(),
                taskSuccess: z.boolean().nullable().optional(),
                taskQualityScore: z.number().nullable().optional(),
                taskQualitySignals: z.record(z.string(), z.union([z.string(), z.number(), z.boolean(), z.null()])).optional(),
                reason: sessionIdSchema.optional(),
            },
        },
        async (args) => {
            try {
                const record = memory.artifactLog.endStudyTrial(args);
                return { content: [{ type: "text", text: JSON.stringify(record, null, 2) }] };
            } catch (error) {
                return { content: [{ type: "text", text: `end_study_trial failed: ${error.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "record_study_event",
        {
            title: "Record a structured study event not inferable from envelopes",
            description: "For researcher/runtime signals such as interruption, resumption, repair, clarification, grounding error, or explicit verification/live mismatch. No transcript field is accepted.",
            inputSchema: {
                sessionId: sessionIdSchema,
                correlationId: sessionIdSchema,
                eventType: z.enum([
                    "repair_attempt", "clarification_turn", "confirmation", "rejection",
                    "revision_requested", "interruption", "resumption", "grounding_error",
                    "stale_application", "unsafe_proposal", "verification_live_mismatch",
                ]),
                targetObjectId: z.string().optional(),
                artifactVersion: z.string().optional(),
                rollbackPointer: z.string().optional(),
                candidateId: z.string().optional(),
                candidateSetId: z.string().optional(),
                durationMs: z.number().nonnegative().optional(),
                status: sessionIdSchema.optional(),
                reasonCode: sessionIdSchema.optional(),
            },
        },
        async (args) => {
            try {
                const context = memory.artifactLog.getStudyContext(args);
                if (!context) throw new Error("no active study trial for these identifiers");
                const record = memory.artifactLog.appendStudyEvent({
                    ...args,
                    unsafeProposal: args.eventType === "unsafe_proposal",
                    verificationLiveMismatch: args.eventType === "verification_live_mismatch",
                    studySource: "mcp",
                });
                return { content: [{ type: "text", text: JSON.stringify(record, null, 2) }] };
            } catch (error) {
                return { content: [{ type: "text", text: `record_study_event failed: ${error.message}` }], isError: true };
            }
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
            appendStudyIfActive({ sessionId, correlationId }, {
                eventType: "intent_captured",
                studySource: "pipeline",
            });
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
            const result = measureMemoryRetrieval(correlationId, "get_evolution_history",
                () => memory.artifactLog.evolution({ objectId, limit }));
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
        }
    );

    server.registerTool(
        "create_bounded_goal",
        {
            title: "Create a persisted bounded autonomous goal",
            description: "Creates goal state in the existing temporal artifact log. Every goal has a verification level, concrete predicate, attempt bound, and wall-time bound.",
            inputSchema: {
                goalId: z.string().optional(),
                objective: z.string().min(1),
                sessionId: sessionIdSchema,
                correlationId: sessionIdSchema,
                targetObjectId: z.string().optional(),
                interactionMode: z.enum(["L1", "L2", "L3", "L4", "L5"]),
                authoringMode: z.enum(["automatic", "semi_auto_confirm", "semi_auto_steer"]),
                triggerSource: z.enum(["system_opportunity", "context", "schedule", "explicit_request"]),
                verificationLevel: z.number().int().min(1).max(5),
                terminationPredicate: z.record(z.string(), z.unknown()),
                maxAttempts: z.number().int().positive(),
                maxWallTimeMs: z.number().int().positive(),
                sceneEpoch: z.string().optional(),
                snapshotId: z.string().optional(),
                objectRevision: z.number().optional(),
                speculative: z.boolean().optional(),
            },
        },
        async (args) => {
            try {
                const goal = memory.goalLoops.create(args);
                return { content: [{ type: "text", text: JSON.stringify(goal, null, 2) }] };
            } catch (error) {
                return { content: [{ type: "text", text: `create_bounded_goal failed: ${error.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "record_goal_validator_judgment",
        {
            title: "Persist a Validator/Critic judgment for a level-4 goal",
            inputSchema: {
                goalId: z.string(),
                sessionId: sessionIdSchema,
                correlationId: sessionIdSchema,
                accepted: z.boolean(),
                score: z.number(),
                rationaleCode: sessionIdSchema.optional(),
            },
        },
        async (args) => {
            const goal = memory.goalLoops.get(args.goalId);
            if (!goal || goal.verificationLevel !== VERIFICATION_LEVELS.LLM_AS_JUDGE) {
                return { content: [{ type: "text", text: "goal is missing or is not verification level 4" }], isError: true };
            }
            const record = memory.artifactLog.append({
                eventType: "goal_validator_judgment",
                ...args,
                targetObjectId: goal.targetObjectId,
            });
            return { content: [{ type: "text", text: JSON.stringify(record, null, 2) }] };
        }
    );

    server.registerTool(
        "advance_goal_loop",
        {
            title: "Advance one persisted goal-loop iteration after the existing harness runs",
            description: "Evaluates machine evidence already recorded by scene, validation, simulation, consent, and result events. Bounds and mode policy are enforced in code.",
            inputSchema: {
                goalId: z.string(),
                triggerSource: z.enum(["explicit_request", "system_opportunity", "context", "schedule"]),
                triggerId: z.string().optional(),
                riskScore: z.number().min(0).max(1),
                reversible: z.boolean(),
                localOnly: z.boolean(),
                detailResolved: z.boolean().optional(),
            },
        },
        async (args) => {
            try {
                const goal = memory.goalLoops.get(args.goalId);
                if (!goal) throw new Error("unknown goal");
                const result = await memory.goalLoops.trigger(args.goalId, {
                    source: args.triggerSource,
                    id: args.triggerId || null,
                }, {
                    policy: {
                        riskScore: args.riskScore,
                        reversible: args.reversible,
                        localOnly: args.localOnly,
                        detailResolved: args.detailResolved,
                        userPreference: memory.personPolicy.getPolicy({ sessionId: goal.sessionId }),
                    },
                    execute: async ({ goal: current }) => deriveGoalEvidence(current),
                });
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (error) {
                return { content: [{ type: "text", text: `advance_goal_loop failed: ${error.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "resolve_delayed_goal",
        {
            title: "Resolve delayed ground truth on a later loop iteration",
            inputSchema: {
                goalId: z.string(),
                signal: z.string(),
                value: z.union([z.string(), z.number(), z.boolean(), z.null()]),
                riskScore: z.number().min(0).max(1),
                reversible: z.boolean(),
                localOnly: z.boolean(),
            },
        },
        async (args) => {
            try {
                const goal = memory.goalLoops.get(args.goalId);
                if (!goal) throw new Error("unknown goal");
                const result = await memory.goalLoops.resolveDelayed(args.goalId, {
                    signal: args.signal,
                    value: args.value,
                }, {
                    policy: {
                        riskScore: args.riskScore,
                        reversible: args.reversible,
                        localOnly: args.localOnly,
                        userPreference: memory.personPolicy.getPolicy({ sessionId: goal.sessionId }),
                    },
                    execute: async ({ goal: current }) => deriveGoalEvidence(current),
                });
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (error) {
                return { content: [{ type: "text", text: `resolve_delayed_goal failed: ${error.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "continue_goal_after_human",
        {
            title: "Explicitly continue or cancel an escalated goal",
            inputSchema: {
                goalId: z.string(),
                approved: z.boolean(),
                decisionCorrelationId: sessionIdSchema.optional(),
                maxAttempts: z.number().int().positive().optional(),
                maxWallTimeMs: z.number().int().positive().optional(),
            },
        },
        async (args) => {
            try {
                const existing = memory.goalLoops.get(args.goalId);
                if (!existing) throw new Error("unknown goal");
                let humanDecision = null;
                if (args.approved === true) {
                    humanDecision = memory.goalLoops.memory.verifiedHumanDecisionAfterEscalation(
                        existing,
                        args.decisionCorrelationId
                    );
                    if (!humanDecision) {
                        throw new Error("no later panel approval or explicit follow-up user turn proves human continuation");
                    }
                }
                const goal = memory.goalLoops.continueAfterHuman(args.goalId, { ...args, humanDecision });
                return { content: [{ type: "text", text: JSON.stringify(goal, null, 2) }] };
            } catch (error) {
                return { content: [{ type: "text", text: `continue_goal_after_human failed: ${error.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "set_goal_loop_kill_switch",
        {
            title: "Activate or explicitly clear the persisted global goal-loop kill switch",
            inputSchema: { active: z.boolean(), humanApproved: z.boolean().optional(), reasonCode: sessionIdSchema.optional() },
        },
        async ({ active, humanApproved, reasonCode }) => {
            try {
                const result = active
                    ? memory.goalLoops.activateKillSwitch(reasonCode || "operator_kill_switch")
                    : memory.goalLoops.clearKillSwitch({ humanApproved });
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (error) {
                return { content: [{ type: "text", text: `set_goal_loop_kill_switch failed: ${error.message}` }], isError: true };
            }
        }
    );

    server.registerTool(
        "get_goal_loop_state",
        {
            title: "Inspect one goal or list active persisted goals",
            inputSchema: { goalId: z.string().optional(), sessionId: sessionIdSchema.optional() },
        },
        async ({ goalId, sessionId }) => {
            const result = goalId ? memory.goalLoops.get(goalId) :
                memory.goalLoops.memory.listGoals({ sessionId });
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
        }
    );

    server.registerTool(
        "register_speculative_candidate",
        {
            title: "Register an idle-time future-goal candidate after independent validation and dry-run",
            description: "Stores a prepared candidate only. It cannot commit and must later match the actual goal plus the exact scene freshness tuple.",
            inputSchema: {
                goalId: z.string(),
                candidateId: z.string(),
                candidateSetId: z.string().optional(),
                validationState: z.enum(["accepted", "rejected"]),
                simulationStatus: z.string(),
                riskScore: z.number().min(0).max(1),
                preparedArtifact: z.record(z.string(), z.unknown()).optional(),
            },
        },
        async (candidate) => {
            const goal = memory.goalLoops.get(candidate.goalId);
            if (!goal || goal.speculative !== true) {
                return { content: [{ type: "text", text: "speculative goal not found" }], isError: true };
            }
            const accepted = candidate.validationState === "accepted" && candidate.simulationStatus === "simulated";
            const record = memory.goalLoops.memory.saveSpeculativeCandidate(goal, {
                ...candidate,
                status: accepted ? "prepared" : "rejected",
            });
            return { content: [{ type: "text", text: JSON.stringify(record, null, 2) }], isError: !accepted };
        }
    );

    server.registerTool(
        "select_speculative_candidate",
        {
            title: "Select a fresh precomputed candidate for an actual goal",
            description: "Selection saves generation latency but never bypasses normal validation, Proposal Gate, or consent.",
            inputSchema: {
                sessionId: sessionIdSchema,
                correlationId: sessionIdSchema,
                actualObjective: z.string(),
                targetObjectId: z.string(),
                sceneEpoch: z.string(),
                snapshotId: z.string(),
                objectRevision: z.number(),
            },
        },
        async (args) => {
            const result = memory.futureGoals.selectForActualGoal(args);
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
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
                sessionId: sessionIdSchema.optional(),
                sceneEpoch: z.string().optional(),
                objectRevision: z.number().optional(),
                snapshotId: z.string().optional(),
                snapshotTakenAt: z.number().optional(),
                authoringMode: z.enum(["automatic", "semi_auto_confirm", "semi_auto_steer"]).optional(),
                consentRoute: z.string().optional(),
                validationState: z.string().optional(),
            },
        },
        async (args) => {
            const result = cacheExchange.proposalGate.checkProposal(args);
            recordProposalGate(args, result);
            return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
        }
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
            recordProposalGate(args, preflight);
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
