"use strict";

const { EventEmitter } = require("events");
const { NetworkScene, NetworkId, UbiqTcpConnection } = require("ubiq/ubiq");
const { RoomClient } = require("ubiq/components");
const { CHANNELS, makeEnvelope } = require("./protocol");

const DEFAULT_TIMEOUT_MS = 8000;

// A minimal NetworkScene Component: forwards every message received on a
// fixed NetworkId to a callback, decoding it as a JSON envelope. Mirrors
// ubiq-genie-components/message_reader.js, which forwards raw Buffers instead.
class EnvelopeListener {
    constructor(scene, networkId, onEnvelope) {
        this.networkId = new NetworkId(networkId);
        this.onEnvelope = onEnvelope;
        this.context = scene.register(this);
    }

    processMessage(msg) {
        let envelope;
        try {
            envelope = msg.toObject();
        } catch (err) {
            console.error(`[unity_scene_bridge] dropped non-JSON message on channel ${this.networkId.toString()}: ${err.message}`);
            return;
        }
        this.onEnvelope(envelope);
    }
}

// Joins the same Ubiq room as the DreamCodeVR coordinator (Unity) and the
// existing code_runtime_generator server app, and exposes the new channel
// scheme (docs/agentic-xr-architecture.md §2) as promise-based request/reply
// calls keyed by correlationId. This class owns no LLM calls and no
// validation logic - it is transport only. The MCP tool wrapper lives in
// server.js; the Validator/Code Generator/etc. agents are separate.
//
// NOTE: query_scene()/propose_artifact() will time out until Unity implements
// handlers for NetworkId 95/96/99/100 (roadmap phase 1-2). Use
// mock_unity_peer.js to smoke-test this bridge in the meantime.
class SceneBridgeClient extends EventEmitter {
    constructor(config) {
        super();
        this.config = config;
        this.scene = new NetworkScene();
        this.roomClient = new RoomClient(this.scene);
        this.pending = new Map(); // correlationId -> { resolve, reject, timer, expectType }
        this.lastKnown = new Map(); // correlationId -> last envelope seen (for get_artifact_status)
        this.connected = false;
        this.lastPeerPresenceAt = null;

        this.roomClient.on("OnJoinedRoom", () => {
            this.connected = true;
            this.emit("connected", this.roomClient.room);
        });

        new EnvelopeListener(this.scene, CHANNELS.SCENE_DELTA, (envelope) => this.#handleInbound(envelope));
        new EnvelopeListener(this.scene, CHANNELS.USER_DECISION, (envelope) => this.#handleInbound(envelope));
        new EnvelopeListener(this.scene, CHANNELS.AGENT_PRESENCE, (envelope) => {
            this.lastPeerPresenceAt = Date.now();
            this.emit("presence", envelope);
        });
    }

    connect() {
        const host = this.config.host || "localhost";
        const port = this.config.roomserver.tcp.port;
        const connection = UbiqTcpConnection(host, port);
        this.scene.addConnection(connection);
        this.roomClient.join(this.config.roomGuid);

        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => {
                reject(new Error(
                    `Timed out connecting to Ubiq room server at ${host}:${port}. ` +
                    `Is the DreamCodeVR server (code_runtime_generator or equivalent) already running and ` +
                    `hosting roomGuid ${this.config.roomGuid}? This bridge joins an existing room, it does ` +
                    `not spawn its own room server.`
                ));
            }, this.config.connectTimeoutMs || DEFAULT_TIMEOUT_MS);
            this.once("connected", () => {
                clearTimeout(timer);
                resolve(this.roomClient.room);
            });
        });
    }

    #handleInbound(envelope) {
        this.lastKnown.set(envelope.correlationId, envelope);
        const waiter = this.pending.get(envelope.correlationId);
        if (waiter && waiter.expectType === envelope.type) {
            clearTimeout(waiter.timer);
            this.pending.delete(envelope.correlationId);
            waiter.resolve(envelope);
        }
        this.emit("envelope", envelope);
    }

    #effectiveTimeout(timeoutMs) {
        return timeoutMs || this.config.defaultTimeoutMs || DEFAULT_TIMEOUT_MS;
    }

    #awaitReply(correlationId, expectType, timeoutMs) {
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => {
                this.pending.delete(correlationId);
                reject(new Error(
                    `Timed out after ${timeoutMs}ms waiting for a '${expectType}' reply (correlationId ${correlationId}). ` +
                    `This is expected until Unity implements the corresponding NetworkId handler - ` +
                    `see docs/agentic-xr-architecture.md phase 1-2, or run mock_unity_peer.js to smoke-test.`
                ));
            }, timeoutMs);
            this.pending.set(correlationId, { resolve, reject, timer, expectType });
        });
    }

    // Requests a focus+halo scene summary for a specific object, or a filtered
    // set of objects (e.g. "tag:game"). Resolves with the SceneDelta envelope
    // Unity replies with on NetworkId 95. Accepts an optional correlationId so
    // a caller (the orchestrator) can thread one id across a whole authoring
    // turn - query_scene, propose_artifact, and agent utterances all sharing
    // one correlationId is what lets timeline_registry.js compute meaningful
    // per-turn "time to visible response"/"time to validated execution".
    async querySceneFocus({ objectId, filter, correlationId, timeoutMs } = {}) {
        const effectiveTimeout = this.#effectiveTimeout(timeoutMs);
        const envelope = makeEnvelope({
            type: "SceneQuery",
            correlationId,
            originAgent: "scene_analyst",
            targetObjectId: objectId || null,
            payload: { filter: filter || null },
        });
        const reply = this.#awaitReply(envelope.correlationId, "SceneDelta", effectiveTimeout);
        this.scene.send(CHANNELS.SCENE_QUERY, envelope);
        this.emit("envelope", envelope);
        return reply;
    }

    // Sends a code artifact for Unity to attach or, when simulate is true,
    // to dry-run against the Experimental Space staging clone instead of the
    // live object (docs/shared-memory-and-experimental-space.md §2). Both
    // modes share the ArtifactProposal/ArtifactResult envelope types and
    // channels (99/100); they are distinguished by payload.mode, not a new
    // NetworkId. authoringMode controls whether Unity shows a confirm/
    // ghost-preview UI (any mode other than "automatic") or applies
    // immediately - meaningless when simulate is true, since nothing is
    // committed to the live object either way.
    async proposeArtifact({ code, targetObjectId, intent, authoringMode = "semi_auto_confirm", simulate = false, sessionId, correlationId, timeoutMs } = {}) {
        if (!code || !targetObjectId) {
            throw new Error("proposeArtifact requires both 'code' and 'targetObjectId'");
        }
        const effectiveTimeout = this.#effectiveTimeout(timeoutMs);
        const envelope = makeEnvelope({
            type: "ArtifactProposal",
            sessionId,
            correlationId,
            originAgent: "code_generator",
            targetObjectId,
            authoringMode,
            payload: { code, intent: intent || null, mode: simulate ? "simulate" : "commit" },
        });
        const reply = this.#awaitReply(envelope.correlationId, "ArtifactResult", effectiveTimeout);
        this.scene.send(CHANNELS.ARTIFACT_CHANNEL, envelope);
        this.emit("envelope", envelope);
        return reply;
    }

    // Non-blocking lookup for a correlationId issued by an earlier
    // querySceneFocus/proposeArtifact call - does not wait again.
    getArtifactStatus(correlationId) {
        if (this.pending.has(correlationId)) {
            return { status: "pending" };
        }
        if (this.lastKnown.has(correlationId)) {
            return { status: "resolved", envelope: this.lastKnown.get(correlationId) };
        }
        return { status: "unknown" };
    }

    // Fire-and-forget conversational filler for the embodied Coordinator -
    // never gates on this, it's not a claim that a change happened.
    sendAgentUtterance({ text, sessionId, correlationId } = {}) {
        const envelope = makeEnvelope({
            type: "AgentUtterance",
            sessionId,
            correlationId,
            originAgent: "coordinator",
            payload: { text },
        });
        this.scene.send(CHANNELS.AGENT_UTTERANCE, envelope);
        this.emit("envelope", envelope);
        return envelope.correlationId;
    }

    getStatus() {
        return {
            connectedToRoom: this.connected,
            roomGuid: this.config.roomGuid,
            lastUnityPresenceSeenAt: this.lastPeerPresenceAt,
            pendingRequestCount: this.pending.size,
        };
    }
}

module.exports = { SceneBridgeClient };
