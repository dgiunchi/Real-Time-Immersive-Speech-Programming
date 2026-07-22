"use strict";

// Dev-only stand-in for the Unity/Ubiq client. Joins the same Ubiq room as a second
// peer and answers SceneQuery/ArtifactProposal/Cache-Exchange messages the way Unity
// eventually will, so the bridge, its MCP tools, and the Cache Exchange Layer can be
// smoke-tested end-to-end before any C# changes exist.
//
// Automatically simulates, on join, the Cache Exchange Layer test flow
// (docs/cache-exchange-layer.md): sends a CacheSnapshot, then four SceneDelta
// updates for the same object, DELIBERATELY dropping deltaSeq 3 on the wire (while
// still recording it in its own history) to simulate packet loss - the backend's
// CacheReconciler should notice the gap and request backfill automatically.
//
// Usage (three terminals, from Server/):
//   1. cd samples/apps/code_runtime_generator && node app.js   (hosts the room)
//   2. node mcp/unity_scene_bridge/mock_unity_peer.js          (this script)
//   3. npx @modelcontextprotocol/inspector node mcp/unity_scene_bridge/server.js
//      (or point a real MCP client at server.js)

const path = require("path");
const nconf = require("nconf");
const { NetworkScene, NetworkId, UbiqTcpConnection } = require("ubiq/ubiq");
const { RoomClient } = require("ubiq/components");
const { CHANNELS, makeEnvelope } = require("./protocol");
const { CACHE_MESSAGE_TYPES, makeCacheEnvelope, toWireFormat, fromWireFormat, createDeltaSeqCounter } = require("../../cache/protocol");

const configPath = process.argv[2] || path.join(__dirname, "config.json");
nconf.file(configPath);
const config = nconf.get();

const scene = new NetworkScene();
const roomClient = new RoomClient(scene);

class Listener {
    constructor(networkId, onEnvelope) {
        this.networkId = new NetworkId(networkId);
        this.onEnvelope = onEnvelope;
        scene.register(this);
    }
    processMessage(msg) {
        try {
            this.onEnvelope(msg.toObject());
        } catch (err) {
            console.warn(`[mock_unity_peer] dropped non-JSON message on channel ${this.networkId.toString()}: ${err.message}`);
        }
    }
}

// --- Cache Exchange Layer mock state (this script plays the role of the
// authoritative Unity client) ---
let mockSceneEpoch = "epoch-1";
let mockSnapshotId = "snap-1";
const objectRevisions = new Map(); // stableObjectId -> revision (authoritative)
const deltaHistoryBySession = new Map(); // sessionId -> compact delta records, for BackfillResponse
const seqCounterBySession = new Map(); // sessionId -> nextDeltaSeq()

function seqCounterFor(sessionId) {
    if (!seqCounterBySession.has(sessionId)) seqCounterBySession.set(sessionId, createDeltaSeqCounter());
    return seqCounterBySession.get(sessionId);
}

function bumpRevision(stableObjectId) {
    const rev = (objectRevisions.get(stableObjectId) || 0) + 1;
    objectRevisions.set(stableObjectId, rev);
    return rev;
}

function recordDeltaHistory(sessionId, compactEntry) {
    if (!deltaHistoryBySession.has(sessionId)) deltaHistoryBySession.set(sessionId, []);
    deltaHistoryBySession.get(sessionId).push(compactEntry);
}

function sendSnapshot(sessionId, correlationId) {
    const objects = [
        { stableObjectId: "obj-mock-0001", objectRevision: objectRevisions.get("obj-mock-0001") || bumpRevision("obj-mock-0001"), tag: "game", region: "workshop-entrance", state: { name: "MockSphere" } },
        { stableObjectId: "obj-mock-0002", objectRevision: objectRevisions.get("obj-mock-0002") || bumpRevision("obj-mock-0002"), tag: "game", region: "workshop-entrance", state: { name: "MockTable" } },
    ];
    const envelope = makeCacheEnvelope({
        type: CACHE_MESSAGE_TYPES.CACHE_SNAPSHOT,
        sessionId,
        correlationId,
        originAgent: "mock_unity_peer",
        sceneEpoch: mockSceneEpoch,
        snapshotId: mockSnapshotId,
        payload: { objects },
    });
    scene.send(CHANNELS.SCENE_DELTA, toWireFormat(envelope));
    console.log(`[mock_unity_peer] sent CacheSnapshot snapshotId=${mockSnapshotId} sceneEpoch=${mockSceneEpoch} objects=${objects.length}`);
    return envelope;
}

// drop=true simulates packet loss: the delta is assigned a real deltaSeq and
// recorded in history (as Unity's own ledger would show it was "sent"), but never
// actually goes out on the wire - only a later BackfillRequest can recover it.
function sendDelta(sessionId, stableObjectId, changeDesc, { drop = false } = {}) {
    const seq = seqCounterFor(sessionId)();
    const rev = bumpRevision(stableObjectId);
    const compact = { deltaSeq: seq, stableObjectId, objectRevision: rev, tag: "game", region: "workshop-entrance", state: { change: changeDesc }, timestamp: Date.now() };
    recordDeltaHistory(sessionId, compact);

    if (drop) {
        console.log(`[mock_unity_peer] SIMULATED PACKET LOSS - deltaSeq=${seq} object=${stableObjectId} recorded but not sent (recoverable via BackfillRequest)`);
        return compact;
    }

    const envelope = makeCacheEnvelope({
        type: CACHE_MESSAGE_TYPES.SCENE_DELTA,
        sessionId,
        originAgent: "mock_unity_peer",
        targetObjectId: stableObjectId,
        sceneEpoch: mockSceneEpoch,
        snapshotId: mockSnapshotId,
        deltaSeq: seq,
        objectRevision: rev,
        payload: { tag: compact.tag, region: compact.region, ...compact.state },
    });
    scene.send(CHANNELS.SCENE_DELTA, toWireFormat(envelope));
    console.log(`[mock_unity_peer] sent SceneDelta seq=${seq} object=${stableObjectId} rev=${rev} change="${changeDesc}"`);
    return compact;
}

new Listener(CHANNELS.SCENE_QUERY, (envelope) => {
    if (envelope.type === CACHE_MESSAGE_TYPES.BACKFILL_REQUEST) {
        const decoded = fromWireFormat(envelope);
        if (decoded.payload && decoded.payload.requestSnapshot) {
            console.log(`[mock_unity_peer] BackfillRequest(requestSnapshot) received (correlationId ${decoded.correlationId})`);
            sendSnapshot(decoded.sessionId, decoded.correlationId);
            return;
        }
        const lastSeenSeq = (decoded.payload && decoded.payload.lastSeenSeq) || 0;
        const history = deltaHistoryBySession.get(decoded.sessionId) || [];
        const missing = history.filter((e) => e.deltaSeq > lastSeenSeq);
        const response = makeCacheEnvelope({
            type: CACHE_MESSAGE_TYPES.BACKFILL_RESPONSE,
            sessionId: decoded.sessionId,
            correlationId: decoded.correlationId,
            originAgent: "mock_unity_peer",
            sceneEpoch: mockSceneEpoch,
            snapshotId: mockSnapshotId,
            payload: { deltas: missing },
        });
        scene.send(CHANNELS.SCENE_DELTA, toWireFormat(response));
        console.log(`[mock_unity_peer] BackfillRequest lastSeenSeq=${lastSeenSeq} -> sent ${missing.length} missing delta(s) (seqs: ${missing.map((m) => m.deltaSeq).join(",") || "none"})`);
        return;
    }

    // Legacy/default: SceneQuery (docs/agentic-xr-architecture.md)
    envelope = fromWireFormat(envelope);
    console.log(`[mock_unity_peer] SceneQuery received (correlationId ${envelope.correlationId}, target ${envelope.targetObjectId || envelope.payload.filter})`);
    const reply = makeEnvelope({
        type: "SceneDelta",
        correlationId: envelope.correlationId,
        sessionId: envelope.sessionId,
        originAgent: "mock_unity_peer",
        payload: {
            focus: {
                id: envelope.targetObjectId || "obj-mock-0001",
                name: "MockSphere",
                tag: "game",
                transform: { pos: [0, 1, 0], rot: [0, 0, 0, 1], scale: [1, 1, 1] },
                components: [{ type: "MeshRenderer", fields: { "material.color": "#FFFFFF" } }],
            },
            halo: [{ id: "obj-mock-0002", name: "MockTable", tag: "game", type: "static" }],
            changedSince: null,
            // Synthetic sensor events (docs/shared-memory-and-experimental-space.md
            // "sensors" concept) - stands in for Unity-side proximity/gaze/collision
            // components, which are not implemented yet.
            sensorEvents: [
                {
                    sensorType: "proximity",
                    sourceObjectId: "obj-mock-user-hand",
                    targetObjectId: envelope.targetObjectId || "obj-mock-0001",
                    value: 0.4,
                    confidence: 0.9,
                },
                {
                    sensorType: "gaze",
                    sourceObjectId: "obj-mock-user-head",
                    targetObjectId: envelope.targetObjectId || "obj-mock-0001",
                    value: true,
                    confidence: 0.8,
                },
                {
                    sensorType: "locomotion",
                    sourceObjectId: envelope.sessionId || "mock-session",
                    value: { regionId: "workshop-entrance", entering: true },
                    confidence: 1,
                },
            ],
        },
    });
    scene.send(CHANNELS.SCENE_DELTA, reply);
    console.log(`[mock_unity_peer] replied SceneDelta for correlationId ${envelope.correlationId}`);
});

new Listener(CHANNELS.ARTIFACT_CHANNEL, (envelope) => {
    if (envelope.type === CACHE_MESSAGE_TYPES.COMMIT_REQUEST) {
        const decoded = fromWireFormat(envelope);
        const currentRev = objectRevisions.get(decoded.targetObjectId) || 0;
        const reasons = [];
        if (decoded.sceneEpoch && decoded.sceneEpoch !== mockSceneEpoch) reasons.push(`sceneEpoch mismatch: proposal=${decoded.sceneEpoch} current=${mockSceneEpoch}`);
        if (decoded.objectRevision != null && decoded.objectRevision !== currentRev) reasons.push(`objectRevision mismatch: proposal=${decoded.objectRevision} current=${currentRev}`);
        if (decoded.snapshotId && decoded.snapshotId !== mockSnapshotId) reasons.push(`snapshotId stale: proposal=${decoded.snapshotId} current=${mockSnapshotId}`);

        setTimeout(() => {
            const accepted = reasons.length === 0;
            const result = makeCacheEnvelope({
                type: accepted ? CACHE_MESSAGE_TYPES.COMMIT_ACCEPTED : CACHE_MESSAGE_TYPES.COMMIT_REJECTED,
                sessionId: decoded.sessionId,
                correlationId: decoded.correlationId,
                originAgent: "mock_unity_peer",
                targetObjectId: decoded.targetObjectId,
                payload: accepted ? { artifactId: `mock-commit-${decoded.correlationId.slice(0, 8)}` } : { reasons },
            });
            scene.send(CHANNELS.USER_DECISION, toWireFormat(result));
            console.log(`[mock_unity_peer] CommitRequest ${decoded.correlationId} -> ${accepted ? "CommitAccepted" : "CommitRejected: " + reasons.join("; ")}`);
        }, 300);
        return;
    }

    if (envelope.type === CACHE_MESSAGE_TYPES.ROLLBACK_REQUEST) {
        const decoded = fromWireFormat(envelope);
        setTimeout(() => {
            const result = makeCacheEnvelope({
                type: CACHE_MESSAGE_TYPES.ROLLBACK_RESULT,
                sessionId: decoded.sessionId,
                correlationId: decoded.correlationId,
                originAgent: "mock_unity_peer",
                targetObjectId: decoded.targetObjectId,
                payload: { status: "rolled_back", artifactId: decoded.payload && decoded.payload.artifactId },
            });
            scene.send(CHANNELS.USER_DECISION, toWireFormat(result));
            console.log(`[mock_unity_peer] RollbackRequest ${decoded.correlationId} -> RollbackResult rolled_back`);
        }, 200);
        return;
    }

    // Legacy/default: ArtifactProposal (docs/agentic-xr-architecture.md)
    envelope = fromWireFormat(envelope);
    const isSimulate = envelope.payload && envelope.payload.mode === "simulate";
    const needsConfirm = !isSimulate && envelope.authoringMode && envelope.authoringMode !== "automatic";
    console.log(
        `[mock_unity_peer] ArtifactProposal received (correlationId ${envelope.correlationId}, mode ${envelope.authoringMode}, ` +
        `payload.mode ${envelope.payload && envelope.payload.mode}, target ${envelope.targetObjectId}) - ` +
        `${isSimulate ? "running Verification Space dry-run" : needsConfirm ? "simulating user confirmation delay" : "auto-applying"}`
    );

    setTimeout(() => {
        const result = isSimulate
            ? makeEnvelope({
                  type: "ArtifactResult",
                  correlationId: envelope.correlationId,
                  sessionId: envelope.sessionId,
                  originAgent: "mock_unity_peer",
                  targetObjectId: envelope.targetObjectId,
                  payload: {
                      status: "simulated",
                      predictedIssues: [],
                      route: "confirm",
                      note: "mock dry-run: no exceptions, no NaN transforms, no out-of-bounds movement detected",
                  },
              })
            : makeEnvelope({
                  type: "ArtifactResult",
                  correlationId: envelope.correlationId,
                  sessionId: envelope.sessionId,
                  originAgent: "mock_unity_peer",
                  targetObjectId: envelope.targetObjectId,
                  payload: { status: "committed", artifactId: `mock-${envelope.correlationId.slice(0, 8)}` },
              });
        scene.send(CHANNELS.USER_DECISION, result);
        console.log(`[mock_unity_peer] replied ArtifactResult (${result.payload.status}) for correlationId ${envelope.correlationId}`);
    }, isSimulate ? 300 : needsConfirm ? 1500 : 200);
});

new Listener(CHANNELS.AGENT_PRESENCE, (envelope) => {
    if (envelope.type === CACHE_MESSAGE_TYPES.CACHE_INVALIDATION) {
        const decoded = fromWireFormat(envelope);
        console.log(`[mock_unity_peer] CacheInvalidation received: target=${decoded.targetObjectId} reason=${decoded.payload && decoded.payload.reason}`);
    } else if (envelope.type === CACHE_MESSAGE_TYPES.DELTA_ACK) {
        console.log(`[mock_unity_peer] DeltaAck received: deltaSeq=${envelope.deltaSeq}`);
    } else if (envelope.type === CACHE_MESSAGE_TYPES.DELTA_NACK) {
        const decoded = fromWireFormat(envelope);
        console.log(`[mock_unity_peer] DeltaNack received: deltaSeq=${envelope.deltaSeq} reason=${decoded.payload && decoded.payload.reason}`);
    }
    // AgentPresenceHeartbeat (its own type) is not logged here to avoid spam.
});

roomClient.on("OnJoinedRoom", () => {
    console.log(`[mock_unity_peer] joined room ${roomClient.room.uuid} (joincode ${roomClient.room.joincode})`);
    setInterval(() => {
        scene.send(CHANNELS.AGENT_PRESENCE, makeEnvelope({ type: "AgentPresenceHeartbeat", sessionId: "mock-control-session", originAgent: "mock_unity_peer", payload: {} }));
    }, 5000);

    // Automatic Cache Exchange Layer test flow (docs/cache-exchange-layer.md):
    // snapshot, then four deltas for the same object with deltaSeq 3 dropped on the
    // wire to simulate packet loss, so the backend's CacheReconciler detects the gap
    // and automatically requests backfill. Delayed several seconds so a backend MCP
    // client (server.js) started right after this peer still has time to connect to
    // the room before the sequence fires - Ubiq does not replay missed messages to
    // late joiners.
    const demoSession = "cache-test-session";
    const startDelayMs = 12000;
    console.log(`[mock_unity_peer] will start the automatic Cache Exchange demo sequence in ${startDelayMs}ms - start the backend MCP client now if it isn't running yet`);
    setTimeout(() => sendSnapshot(demoSession), startDelayMs);
    setTimeout(() => sendDelta(demoSession, "obj-mock-0001", "color -> red"), startDelayMs + 400); // seq 1
    setTimeout(() => sendDelta(demoSession, "obj-mock-0001", "scale -> 1.5"), startDelayMs + 800); // seq 2
    setTimeout(() => sendDelta(demoSession, "obj-mock-0001", "position -> [1,1,1]", { drop: true }), startDelayMs + 1200); // seq 3, dropped
    setTimeout(() => sendDelta(demoSession, "obj-mock-0001", "position -> [2,1,1]"), startDelayMs + 1600); // seq 4 - gap should trigger backfill
});

const host = config.host || "localhost";
const port = config.roomserver.tcp.port;
scene.addConnection(UbiqTcpConnection(host, port));
roomClient.join(config.roomGuid);
console.log(`[mock_unity_peer] connecting to ${host}:${port}, joining room ${config.roomGuid}...`);
