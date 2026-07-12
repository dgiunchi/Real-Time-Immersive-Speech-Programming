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
const { SceneBridgeClient } = require("./scene_bridge_client");
const { SharedMemory } = require("../../memory");

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
                correlationId: z.string().optional().describe("Reuse an existing correlationId to thread this call into the same authoring-turn timeline as prior/later calls"),
                timeoutMs: z.number().int().positive().optional(),
            },
        },
        async ({ objectId, filter, correlationId, timeoutMs }) => {
            try {
                const result = await bridge.querySceneFocus({ objectId, filter, correlationId, timeoutMs });
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
                code: z.string().describe("Full C# source of the MonoBehaviour artifact"),
                targetObjectId: z.string().describe("Stable scene object id this artifact attaches to"),
                intent: z.string().optional().describe("Original natural-language intent, shown in the confirmation UI"),
                authoringMode: z.enum(["automatic", "semi_auto_confirm", "semi_auto_steer"]).optional(),
                sessionId: z.string().optional(),
                correlationId: z.string().optional().describe("Reuse an existing correlationId to thread this call into the same authoring-turn timeline as prior/later calls"),
                timeoutMs: z.number().int().positive().optional(),
            },
        },
        async ({ code, targetObjectId, intent, authoringMode, sessionId, correlationId, timeoutMs }) => {
            try {
                const result = await bridge.proposeArtifact({ code, targetObjectId, intent, authoringMode, sessionId, correlationId, timeoutMs });
                memory.artifactLog.append({
                    eventType: "propose_artifact",
                    targetObjectId,
                    correlationId: result.correlationId,
                    intent: intent || null,
                    authoringMode: authoringMode || null,
                    status: result.payload && result.payload.status,
                    artifactId: result.payload && result.payload.artifactId,
                });
                return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
            } catch (err) {
                return { content: [{ type: "text", text: `propose_artifact failed: ${err.message}` }], isError: true };
            }
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
                "risk. Requires the Unity-side staging-clone handler (not yet implemented against a real " +
                "headset - see docs/agentic-xr-architecture.md §9); mock_unity_peer.js answers it today.",
            inputSchema: {
                code: z.string().describe("Full C# source of the candidate artifact"),
                targetObjectId: z.string().describe("Stable scene object id the artifact would attach to"),
                intent: z.string().optional(),
                sessionId: z.string().optional(),
                correlationId: z.string().optional(),
                timeoutMs: z.number().int().positive().optional(),
            },
        },
        async ({ code, targetObjectId, intent, sessionId, correlationId, timeoutMs }) => {
            try {
                const result = await bridge.proposeArtifact({ code, targetObjectId, intent, sessionId, correlationId, timeoutMs, simulate: true });
                memory.artifactLog.append({
                    eventType: "simulate_artifact",
                    targetObjectId,
                    correlationId: result.correlationId,
                    intent: intent || null,
                    status: result.payload && result.payload.status,
                });
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
            },
        },
        async ({ objectId, filter }) => ({ content: [{ type: "text", text: JSON.stringify(memory.visual.query({ objectId, filter }), null, 2) }] })
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
            inputSchema: { objectId: z.string().optional() },
        },
        async ({ objectId }) => ({ content: [{ type: "text", text: JSON.stringify(memory.sceneGraph.queryGraph({ objectId }), null, 2) }] })
    );

    server.registerTool(
        "query_affordances",
        {
            title: "Query inferred affordances for an object",
            description:
                "Retrieve inferred usable actions for an object beyond simple proximity, e.g. 'usable' for a " +
                "Grabbable component. This is a static tag/component lookup table, NOT learned semantic " +
                "reasoning - do not describe it as inference in the paper without changing the implementation.",
            inputSchema: { objectId: z.string() },
        },
        async ({ objectId }) => ({ content: [{ type: "text", text: JSON.stringify(memory.sceneGraph.queryAffordances({ objectId }), null, 2) }] })
    );

    server.registerTool(
        "get_script_context",
        {
            title: "Query the Script/context memory layer",
            description:
                "Retrieve components, recent artifact history, and the static capability policy (allowed/denied " +
                "namespaces) for an object - what generated code touching this object can and cannot do.",
            inputSchema: { objectId: z.string() },
        },
        async ({ objectId }) => {
            const visualEntry = memory.visual.byObjectId.get(objectId);
            const result = {
                objectId,
                components: (visualEntry && visualEntry.focus && visualEntry.focus.components) || [],
                recentArtifacts: memory.artifactLog.history({ objectId, limit: 5 }),
                capabilityPolicy: {
                    allowedNamespaces: ["UnityEngine"],
                    deniedNamespaces: ["System.IO", "System.Net", "System.Diagnostics", "System.Reflection"],
                    note: "static allowlist mirroring Unity/Assets/RoslynCSharp/.../RoslynCSharpSecurityAllowance.cs - not yet wired to the new pipeline (docs/agentic-xr-architecture.md §7)",
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
            },
        },
        async ({ objectId, limit }) => ({ content: [{ type: "text", text: JSON.stringify(memory.artifactLog.history({ objectId, limit }), null, 2) }] })
    );

    server.registerTool(
        "get_person_policy",
        {
            title: "Query the Person/multi-user memory layer",
            description:
                "Retrieve roles, permissions, and consent policy for a session. Currently a static single-owner " +
                "stub (docs/shared-memory-and-experimental-space.md §4.2) - not a real multi-user policy engine.",
            inputSchema: { sessionId: z.string().optional() },
        },
        async ({ sessionId }) => ({ content: [{ type: "text", text: JSON.stringify(memory.personPolicy.getPolicy({ sessionId }), null, 2) }] })
    );

    server.registerTool(
        "commit_memory_event",
        {
            title: "Record a memory event",
            description:
                "Writes an arbitrary result, rollback, or decision to the Temporal memory layer / artifact log, " +
                "outside the automatic logging propose_artifact/simulate_artifact already do. Use for events " +
                "like an undo, a rejected clarification, or a conflict-resolution decision.",
            inputSchema: {
                targetObjectId: z.string(),
                eventType: z.string().describe("e.g. 'rollback', 'clarification_rejected', 'conflict_decision'"),
                correlationId: z.string().optional(),
                data: z.record(z.string(), z.unknown()).optional(),
            },
        },
        async ({ targetObjectId, eventType, correlationId, data }) => {
            const record = memory.artifactLog.append({ targetObjectId, eventType, correlationId, ...(data || {}) });
            return { content: [{ type: "text", text: JSON.stringify(record, null, 2) }] };
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
