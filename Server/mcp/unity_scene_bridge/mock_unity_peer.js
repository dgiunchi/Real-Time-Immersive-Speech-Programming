"use strict";

// Dev-only stand-in for the Unity/Ubiq client. Joins the same Ubiq room as a
// second peer and answers SceneQuery/ArtifactProposal messages the way Unity
// eventually will (roadmap phase 1-2), so the bridge and its MCP tools can be
// smoke-tested end-to-end before any C# changes exist.
//
// Usage (three terminals, from Server/):
//   1. cd samples/apps/code_runtime_generator && node app.js   (hosts the room)
//   2. node mcp/unity_scene_bridge/mock_unity_peer.js          (this script)
//   3. npx @modelcontextprotocol/inspector node mcp/unity_scene_bridge/server.js
//      (or point a real MCP client at server.js) and call query_scene / propose_artifact

const path = require("path");
const nconf = require("nconf");
const { NetworkScene, NetworkId, UbiqTcpConnection } = require("ubiq/ubiq");
const { RoomClient } = require("ubiq/components");
const { CHANNELS, makeEnvelope } = require("./protocol");

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

new Listener(CHANNELS.SCENE_QUERY, (envelope) => {
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
            ],
        },
    });
    scene.send(CHANNELS.SCENE_DELTA, reply);
    console.log(`[mock_unity_peer] replied SceneDelta for correlationId ${envelope.correlationId}`);
});

new Listener(CHANNELS.ARTIFACT_CHANNEL, (envelope) => {
    const isSimulate = envelope.payload && envelope.payload.mode === "simulate";
    const needsConfirm = !isSimulate && envelope.authoringMode && envelope.authoringMode !== "automatic";
    console.log(
        `[mock_unity_peer] ArtifactProposal received (correlationId ${envelope.correlationId}, mode ${envelope.authoringMode}, ` +
        `payload.mode ${envelope.payload && envelope.payload.mode}, target ${envelope.targetObjectId}) - ` +
        `${isSimulate ? "running Experimental Space dry-run" : needsConfirm ? "simulating user confirmation delay" : "auto-applying"}`
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

roomClient.on("OnJoinedRoom", () => {
    console.log(`[mock_unity_peer] joined room ${roomClient.room.uuid} (joincode ${roomClient.room.joincode})`);
    setInterval(() => {
        scene.send(CHANNELS.AGENT_PRESENCE, makeEnvelope({ type: "AgentPresenceHeartbeat", originAgent: "mock_unity_peer", payload: {} }));
    }, 5000);
});

const host = config.host || "localhost";
const port = config.roomserver.tcp.port;
scene.addConnection(UbiqTcpConnection(host, port));
roomClient.join(config.roomGuid);
console.log(`[mock_unity_peer] connecting to ${host}:${port}, joining room ${config.roomGuid}...`);
