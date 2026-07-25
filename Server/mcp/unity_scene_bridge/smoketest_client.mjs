import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import path from "node:path";
import { fileURLToPath } from "node:url";

const serverPath = path.join(path.dirname(fileURLToPath(import.meta.url)), "server.js");

const transport = new StdioClientTransport({ command: "node", args: [serverPath], env: { ...process.env } });
const client = new Client({ name: "smoketest", version: "0.0.2" });
await client.connect(transport);

const sessionId = "smoketest-session-1";
const correlationId = "smoketest-correlation-1";
const targetObjectId = "obj-test-42";

function log(label, result) {
    const text = result.content.map((c) => c.text).join("\n");
    console.log(`\n=== ${label} ===\n${text}`);
}

// --- Core round trip, with sessionId + interactionMode + region/intent (docs/next-build-prompt.md) ---
log("start_study_trial", await client.callTool({
    name: "start_study_trial",
    arguments: {
        participantId: "mock-participant-001",
        sessionId,
        trialId: "mock-trial-001",
        condition: "agenticxr_verification",
        taskId: "mock-authoring-task",
        interactionMode: "L4",
        correlationId,
    },
}));
log("record_intent", await client.callTool({ name: "record_intent", arguments: { text: "make this sphere pulse red when I touch it", sessionId, correlationId } }));
log("send_agent_status", await client.callTool({ name: "send_agent_status", arguments: { sessionId, correlationId, state: "thinking", detail: "Mock status visibility check." } }));
log("query_scene", await client.callTool({ name: "query_scene", arguments: { objectId: targetObjectId, sessionId, correlationId } }));
log("get_region_context", await client.callTool({ name: "get_region_context", arguments: { sessionId } }));
log("query_visual_memory", await client.callTool({ name: "query_visual_memory", arguments: { objectId: targetObjectId, correlationId } }));
log("query_scene_graph", await client.callTool({ name: "query_scene_graph", arguments: { objectId: targetObjectId, correlationId } }));
log("query_affordances", await client.callTool({ name: "query_affordances", arguments: { objectId: targetObjectId, correlationId } }));
log("get_script_context", await client.callTool({ name: "get_script_context", arguments: { objectId: targetObjectId, correlationId } }));
log("set_person_profile_consent", await client.callTool({ name: "set_person_profile_consent", arguments: { sessionId, personId: "mock-participant", consent: true, retentionDays: 1 } }));
log("set_experience_context", await client.callTool({ name: "set_experience_context", arguments: { sessionId, mode: "training" } }));

const candidateSetId = "smoketest-candidate-set-1";
for (const candidate of [
    { candidateId: "candidate-safe", code: "public class BounceSafe : MonoBehaviour {}", riskScore: 0.1 },
    { candidateId: "candidate-medium", code: "public class BounceMedium : MonoBehaviour {}", riskScore: 0.4 },
    { candidateId: "candidate-risky", code: "public class BounceRisky : MonoBehaviour {}", riskScore: 0.8 },
]) {
    log(`simulate_artifact ${candidate.candidateId}`, await client.callTool({
        name: "simulate_artifact",
        arguments: { ...candidate, operation: "create", candidateSetId, targetObjectId, intent: "make it bounce", sessionId, correlationId: `sim-${candidate.candidateId}` },
    }));
}
log("rank_artifact_candidates", await client.callTool({
    name: "rank_artifact_candidates",
    arguments: {
        sessionId, correlationId, targetObjectId, candidateSetId,
        candidates: [
            { candidateId: "candidate-safe", operation: "create", code: "public class BounceSafe : MonoBehaviour {}", validationState: "accepted", simulationStatus: "simulated", riskScore: 0.1, authoringMode: "semi_auto_confirm", experienceMode: "training" },
            { candidateId: "candidate-medium", operation: "create", code: "public class BounceMedium : MonoBehaviour {}", validationState: "accepted", simulationStatus: "simulated", riskScore: 0.4, authoringMode: "semi_auto_confirm", experienceMode: "training" },
            { candidateId: "candidate-risky", operation: "create", code: "public class BounceRisky : MonoBehaviour {}", validationState: "accepted", simulationStatus: "simulated", riskScore: 0.8, authoringMode: "semi_auto_confirm", experienceMode: "training" },
        ],
    },
}));

log(
    "propose_artifact",
    await client.callTool({
        name: "propose_artifact",
        arguments: {
            code: "public class BounceSafe : MonoBehaviour {}",
            targetObjectId,
            intent: "make it bounce",
            authoringMode: "semi_auto_confirm",
            interactionMode: "L4",
            operation: "create",
            candidateId: "candidate-safe",
            candidateSetId,
            candidateCount: 3,
            selectionReason: "highest deterministic score after three independent dry-runs",
            experienceMode: "training",
            snapshotTakenAt: Date.now(),
            sessionId,
            correlationId,
        },
    })
);

log("get_artifact_history", await client.callTool({ name: "get_artifact_history", arguments: { objectId: targetObjectId, correlationId } }));
log("get_evolution_history", await client.callTool({ name: "get_evolution_history", arguments: { objectId: targetObjectId, correlationId } }));
log("simulate edit", await client.callTool({ name: "simulate_artifact", arguments: {
    code: "public class BounceEdited : MonoBehaviour {}", operation: "edit", existingArtifactId: "mock-smoketes",
    targetObjectId, intent: "refine the bounce", sessionId, correlationId: "edit-sim-001",
} }));
log("commit edit", await client.callTool({ name: "propose_artifact", arguments: {
    code: "public class BounceEdited : MonoBehaviour {}", operation: "edit", existingArtifactId: "mock-smoketes",
    targetObjectId, intent: "refine the bounce", authoringMode: "semi_auto_confirm", interactionMode: "L4",
    sessionId, correlationId: "editcorr-001",
} }));
log("simulate remove", await client.callTool({ name: "simulate_artifact", arguments: {
    operation: "remove", existingArtifactId: "mock-editcorr", targetObjectId, intent: "remove the bounce",
    sessionId, correlationId: "remove-sim-001",
} }));
log("commit remove", await client.callTool({ name: "propose_artifact", arguments: {
    operation: "remove", existingArtifactId: "mock-editcorr", targetObjectId, intent: "remove the bounce",
    authoringMode: "semi_auto_confirm", interactionMode: "L4", sessionId, correlationId: "removecorr-001",
} }));
log("get_evolution_history after edit/remove", await client.callTool({ name: "get_evolution_history", arguments: { objectId: targetObjectId } }));
log("get_person_policy (expect non-empty priorDecisions)", await client.callTool({ name: "get_person_policy", arguments: { sessionId, correlationId } }));
log("get_timeline_metrics", await client.callTool({ name: "get_timeline_metrics", arguments: { correlationId } }));

log(
    "simulate_artifact (expect an 'experimental' lane event in a later get_timeline_metrics)",
    await client.callTool({
        name: "simulate_artifact",
        arguments: { code: "public class Bounce2 : MonoBehaviour {}", targetObjectId, intent: "make it bounce higher", sessionId, correlationId: "smoketest-correlation-2" }
    })
);
log("get_timeline_metrics (simulate correlationId - look for an 'experimental' timeline event)", await client.callTool({ name: "get_timeline_metrics", arguments: { correlationId: "smoketest-correlation-2" } }));

// --- Stale-proposal rejection (docs/next-build-prompt.md §2.7) ---
// Query object A, then object B (same session) - the pending idea is "the user moved
// on" - then propose for A: expect ArtifactResult.staleness.isStale === true.
const objectA = "obj-stale-a";
const objectB = "obj-stale-b";
const staleCorrelationId = "smoketest-stale-1";
log("query_scene(A)", await client.callTool({ name: "query_scene", arguments: { objectId: objectA, sessionId, correlationId: "smoketest-stale-query-a" } }));
log("query_scene(B) - session focus moves to B", await client.callTool({ name: "query_scene", arguments: { objectId: objectB, sessionId, correlationId: "smoketest-stale-query-b" } }));
log(
    "propose_artifact(A) - expect staleness.isStale === true since session focus is now B",
    await client.callTool({
        name: "propose_artifact",
        arguments: { code: "public class Stale : MonoBehaviour {}", targetObjectId: objectA, intent: "stale test", sessionId, correlationId: staleCorrelationId },
    })
);
log("get_artifact_history(A) - expect a stale_proposal eventType entry", await client.callTool({ name: "get_artifact_history", arguments: { objectId: objectA } }));
log("reset_person_profile", await client.callTool({ name: "reset_person_profile", arguments: { sessionId, confirmReset: true } }));
log("end_study_trial", await client.callTool({
    name: "end_study_trial",
    arguments: {
        sessionId,
        trialId: "mock-trial-001",
        correlationId,
        taskCompletion: true,
        taskSuccess: true,
        taskQualityScore: 4,
        taskQualitySignals: { rubricVersion: "mock-v1", behaviorMatched: true },
    },
}));

await client.close();
process.exit(0);
