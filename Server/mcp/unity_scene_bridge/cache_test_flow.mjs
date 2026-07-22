import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import path from "node:path";
import { fileURLToPath } from "node:url";

// Cache Exchange Layer mock/test flow (docs/cache-exchange-layer.md). Requires the
// Ubiq room server AND mock_unity_peer.js already running - see that doc for the
// three-terminal setup. This script only DRIVES the backend/MCP side; the snapshot
// + delta-with-a-gap sequence is generated automatically by mock_unity_peer.js on
// join (see its "Automatic Cache Exchange Layer test flow" block).

const serverPath = path.join(path.dirname(fileURLToPath(import.meta.url)), "server.js");
const sessionId = "cache-test-session"; // must match mock_unity_peer.js's demoSession

const transport = new StdioClientTransport({ command: "node", args: [serverPath], env: { ...process.env } });
const client = new Client({ name: "cache-test-flow", version: "0.0.1" });
await client.connect(transport);

// Synchronize deterministically even if this client joins after the mock's timed
// demo sequence. Ubiq does not replay old room messages to late peers.
await client.callTool({ name: "request_snapshot", arguments: { sessionId } });

function log(label, result) {
    const text = result.content.map((c) => c.text).join("\n");
    console.log(`\n=== ${label} ===\n${text}`);
}

function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

// mock_unity_peer.js waits 12s after joining before starting its snapshot+delta
// sequence (so a backend client started right after it - including this script's own
// subprocess startup/connect time - has margin to connect first; Ubiq does not
// replay missed messages to late joiners). This script connects immediately, then
// polls in stages so the automatic gap-triggered backfill (cache/index.js#attach)
// can be observed directly rather than only proven via an explicit backfill call.
console.log("Connected. Polling the Agent Working Cache while mock_unity_peer.js's automatic snapshot+delta(+gap) sequence runs...");

for (let i = 0; i < 8; i++) {
    await sleep(2000);
    const result = await client.callTool({ name: "query_agent_cache", arguments: { stableObjectId: "obj-mock-0001" } });
    const text = result.content.map((c) => c.text).join("");
    console.log(`[t+${(i + 1) * 2}s] working cache for obj-mock-0001: ${text.replace(/\s+/g, " ")}`);
}

log("query_agent_cache(obj-mock-0001) - final state after live deltas + automatic backfill", await client.callTool({ name: "query_agent_cache", arguments: { stableObjectId: "obj-mock-0001" } }));

// Explicit backfill call too, to show the tool directly and prove idempotence: by
// now deltaSeq 1-4 should already be reconciled (live sends + automatic gap-fill),
// so re-requesting from lastSeenSeq=0 exercises the dedup path (seenSet in
// cache_reconciler.js) rather than a fresh recovery.
log("request_backfill(lastSeenSeq=0) - idempotence check", await client.callTool({ name: "request_backfill", arguments: { sessionId, lastSeenSeq: 0 } }));

// 3. Pre-flight gate check + commit against the CURRENT (fresh) revision - expect
//    accepted, then CommitAccepted from the mock peer's own authoritative check.
const freshCorrelationId = "cache-test-commit-fresh";
log(
    "check_proposal_gate (fresh)",
    await client.callTool({
        name: "check_proposal_gate",
        arguments: {
            correlationId: freshCorrelationId,
            targetObjectId: "obj-mock-0001",
            sceneEpoch: "epoch-1",
            objectRevision: 5,
            snapshotId: "snap-1",
            snapshotTakenAt: Date.now(),
            authoringMode: "semi_auto_confirm",
            consentRoute: "user-confirm",
            validationState: "accepted",
        },
    })
);
log(
    "request_commit (fresh - expect committed: true)",
    await client.callTool({
        name: "request_commit",
        arguments: {
            correlationId: freshCorrelationId,
            targetObjectId: "obj-mock-0001",
            sceneEpoch: "epoch-1",
            objectRevision: 5,
            snapshotId: "snap-1",
            snapshotTakenAt: Date.now(),
            authoringMode: "semi_auto_confirm",
            consentRoute: "user-confirm",
            validationState: "accepted",
            sessionId,
        },
    })
);

// 4. Same proposal shape, but against a deliberately STALE objectRevision (as if the
//    agent drafted this against the state right after the snapshot, before any of
//    the deltas applied) - expect rejection, with a structured reason.
const staleCorrelationId = "cache-test-commit-stale";
log(
    "request_commit (stale objectRevision=2 - expect committed: false)",
    await client.callTool({
        name: "request_commit",
        arguments: {
            correlationId: staleCorrelationId,
            targetObjectId: "obj-mock-0001",
            sceneEpoch: "epoch-1",
            objectRevision: 2,
            snapshotId: "snap-1",
            snapshotTakenAt: Date.now(),
            authoringMode: "semi_auto_confirm",
            consentRoute: "user-confirm",
            validationState: "accepted",
            sessionId,
        },
    })
);

// 5. A proposal with a stale snapshotAge (simulating a proposal drafted too long
//    ago for automatic mode) - the backend's own ProposalGate should catch this
//    WITHOUT a round trip to Unity (stage: "preflight").
log(
    "request_commit (stale snapshotTakenAt for automatic mode - expect preflight rejection)",
    await client.callTool({
        name: "request_commit",
        arguments: {
            correlationId: "cache-test-commit-old-snapshot",
            targetObjectId: "obj-mock-0001",
            sceneEpoch: "epoch-1",
            objectRevision: 5,
            snapshotId: "snap-1",
            snapshotTakenAt: Date.now() - 60000,
            authoringMode: "automatic",
            consentRoute: "auto",
            validationState: "accepted",
            sessionId,
        },
    })
);

await client.close();
process.exit(0);
