"use strict";

const { EventEmitter } = require("events");
const { NetworkScene, NetworkId, UbiqTcpConnection } = require("ubiq/ubiq");
const { RoomClient } = require("ubiq/components");
const { CHANNELS, makeEnvelope } = require("./protocol");
const { CACHE_MESSAGE_TYPES, makeCacheEnvelope, toWireFormat, fromWireFormat } = require("../../cache/protocol");

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
        // Server-side half of the paper's "stale-result rejection" (main.tex,
        // Communication Contract; docs/next-build-prompt.md §2.7): sessionId ->
        // { objectId, at } for whatever object a session most recently queried.
        // Unity doesn't exist yet, so this approximates "current selection" with
        // "server's last-known focus for that session," not literally headset state.
        this.sessionFocus = new Map();

        this.roomClient.on("OnJoinedRoom", () => {
            this.connected = true;
            this.emit("connected", this.roomClient.room);
        });

        // 95: SceneDelta, CacheSnapshot, BackfillResponse (all Unity -> Server pushes)
        new EnvelopeListener(this.scene, CHANNELS.SCENE_DELTA, (envelope) => this.#handleInbound(envelope));
        // 100: ArtifactResult/UserDecision, CommitAccepted, CommitRejected, RollbackResult
        new EnvelopeListener(this.scene, CHANNELS.USER_DECISION, (envelope) => this.#handleInbound(envelope));
        // 101: AgentPresenceHeartbeat, CacheInvalidation, DeltaAck, DeltaNack (bidirectional
        // control plane) - heartbeat gets its side effect, everything on this channel still
        // flows through #handleInbound so pending awaitReply()s (e.g. sendDeltaAck's caller,
        // if anyone awaits it) resolve and the "envelope" event fires for the cache reconciler.
        new EnvelopeListener(this.scene, CHANNELS.AGENT_PRESENCE, (envelope) => {
            if (envelope.type === "AgentPresenceHeartbeat") {
                this.lastPeerPresenceAt = Date.now();
                this.emit("presence", envelope);
            }
            this.#handleInbound(envelope);
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
        envelope = fromWireFormat(envelope);
        if (["ArtifactResult", "CommitAccepted", "CommitRejected"].includes(envelope.type)) {
            this.#annotateStaleness(envelope);
        }
        this.lastKnown.set(envelope.correlationId, envelope);
        const waiter = this.pending.get(envelope.correlationId);
        const matchesExpected = waiter && (Array.isArray(waiter.expectType) ? waiter.expectType.includes(envelope.type) : waiter.expectType === envelope.type);
        if (matchesExpected) {
            clearTimeout(waiter.timer);
            this.pending.delete(envelope.correlationId);
            waiter.resolve(envelope);
        }
        this.emit("envelope", envelope);
    }

    // Tags (does not block) an ArtifactResult with whether the session had already
    // moved its focus to a different object by the time the result arrived. This is
    // still resolved to the caller either way - staleness is informational/logged,
    // not a silent drop, so the caller who explicitly asked about this object still
    // gets their answer. Requires the caller to have passed the same sessionId to
    // querySceneFocus() earlier; without that, staleness can't be assessed
    // (annotated as { checked: false }), not silently assumed fresh.
    #annotateStaleness(envelope) {
        if (!envelope.sessionId) {
            envelope.staleness = { checked: false, reason: "no sessionId on this envelope" };
            return;
        }
        const focus = this.sessionFocus.get(envelope.sessionId);
        if (!focus) {
            envelope.staleness = { checked: false, reason: "no known prior focus for this sessionId" };
            return;
        }
        const isStale = !!envelope.targetObjectId && focus.objectId !== envelope.targetObjectId;
        envelope.staleness = {
            checked: true,
            isStale,
            focusObjectIdAtArrival: focus.objectId,
            focusSetAt: focus.at,
        };
        if (isStale) {
            this.emit("stale_proposal", envelope);
        }
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
    async querySceneFocus({ objectId, filter, sessionId, correlationId, timeoutMs } = {}) {
        const effectiveTimeout = this.#effectiveTimeout(timeoutMs);
        const envelope = makeEnvelope({
            type: "SceneQuery",
            sessionId,
            correlationId,
            originAgent: "scene_analyst",
            targetObjectId: objectId || null,
            payload: { filter: filter || null },
        });
        // Records this as the session's current focus (docs/next-build-prompt.md
        // §2.7) - optimistic (assumes the query succeeds), updated again if/when a
        // later query for a different object arrives, which is exactly the signal
        // that makes a still-pending proposal for the earlier object stale.
        if (sessionId && objectId) {
            this.sessionFocus.set(sessionId, { objectId, at: envelope.timestamp });
        }
        const reply = this.#awaitReply(envelope.correlationId, "SceneDelta", effectiveTimeout);
        this.#sendCache(CHANNELS.SCENE_QUERY, envelope);
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
    async proposeArtifact({ code, targetObjectId, intent, authoringMode = "semi_auto_confirm", interactionMode, simulate = false,
        sessionId, correlationId, timeoutMs, sceneEpoch, snapshotId, objectRevision, snapshotTakenAt,
        validationState = "accepted", validationSummary, riskScore, consentRoute, requiredPermissions,
        expectedSideEffects, artifactVersion } = {}) {
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
            interactionMode,
            payload: {
                code,
                intent: intent || null,
                mode: simulate ? "simulate" : "commit",
                snapshotTakenAt: snapshotTakenAt || Date.now(),
                validationState,
                validationSummary: validationSummary || null,
                riskScore: typeof riskScore === "number" ? riskScore : null,
                consentRoute: consentRoute || (authoringMode === "automatic" ? "automatic_low_risk" : "explicit_confirmation"),
                requiredPermissions: requiredPermissions || ["attach_component"],
                expectedSideEffects: expectedSideEffects || null,
                artifactVersion: artifactVersion || "1",
            },
        });
        envelope.sceneEpoch = sceneEpoch || null;
        envelope.snapshotId = snapshotId || null;
        envelope.objectRevision = objectRevision != null ? objectRevision : null;
        envelope.validationState = validationState;
        envelope.validationSummary = validationSummary || null;
        envelope.riskScore = typeof riskScore === "number" ? riskScore : null;
        envelope.consentRoute = envelope.payload.consentRoute;
        envelope.requiredPermissions = envelope.payload.requiredPermissions.join(",");
        envelope.expectedSideEffects = expectedSideEffects || null;
        envelope.artifactVersion = artifactVersion || "1";
        const reply = this.#awaitReply(envelope.correlationId, "ArtifactResult", effectiveTimeout);
        this.#sendCache(CHANNELS.ARTIFACT_CHANNEL, envelope);
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
        this.#sendCache(CHANNELS.AGENT_UTTERANCE, envelope);
        return envelope.correlationId;
    }

    // --- Cache Exchange Layer methods (docs/cache-exchange-layer.md) ---
    // These send on the SAME channels as the methods above (95/96/97/99/100/101),
    // distinguished by envelope.type, not new NetworkIds - see cache/protocol.js.

    #sendCache(channel, envelope) {
        const wire = toWireFormat(envelope);
        this.scene.send(channel, wire);
        this.emit("envelope", envelope); // internal listeners see the object form, not the wire-stringified one
        return envelope;
    }

    // Asks Unity to push a fresh CacheSnapshot - sent as a BackfillRequest with
    // payload.requestSnapshot=true rather than a distinct message type, since a
    // snapshot is conceptually "backfill everything from scratch."
    async requestSnapshot({ sessionId, correlationId, timeoutMs } = {}) {
        const effectiveTimeout = this.#effectiveTimeout(timeoutMs);
        const envelope = makeCacheEnvelope({
            type: CACHE_MESSAGE_TYPES.BACKFILL_REQUEST,
            sessionId,
            correlationId,
            originAgent: "cache_reconciler",
            payload: { requestSnapshot: true },
        });
        const reply = this.#awaitReply(envelope.correlationId, CACHE_MESSAGE_TYPES.CACHE_SNAPSHOT, effectiveTimeout);
        this.#sendCache(CHANNELS.SCENE_QUERY, envelope);
        return reply;
    }

    // Recovers a missing deltaSeq range for a session from Unity's own history.
    async requestBackfill({ sessionId, lastSeenSeq, correlationId, timeoutMs } = {}) {
        const effectiveTimeout = this.#effectiveTimeout(timeoutMs);
        const envelope = makeCacheEnvelope({
            type: CACHE_MESSAGE_TYPES.BACKFILL_REQUEST,
            sessionId,
            correlationId,
            originAgent: "cache_reconciler",
            payload: { lastSeenSeq: lastSeenSeq || 0 },
        });
        const reply = this.#awaitReply(envelope.correlationId, CACHE_MESSAGE_TYPES.BACKFILL_RESPONSE, effectiveTimeout);
        this.#sendCache(CHANNELS.SCENE_QUERY, envelope);
        return reply;
    }

    // Two-phase commit's second phase: propose_artifact/simulate_artifact already
    // covers phase one (draft + validate). CommitRequest asks Unity to actually
    // perform its authoritative compare-and-swap gate and apply (or reject) the
    // named snapshot/revision right now - the freshness window between drafting and
    // committing is exactly what the Proposal Gate and Unity's own check exist for.
    async sendCommitRequest({ correlationId, targetObjectId, snapshotId, objectRevision, sceneEpoch, authoringMode, consentRoute, validationState, sessionId, timeoutMs } = {}) {
        if (!correlationId || !targetObjectId) {
            throw new Error("sendCommitRequest requires correlationId and targetObjectId");
        }
        const effectiveTimeout = this.#effectiveTimeout(timeoutMs);
        const envelope = makeCacheEnvelope({
            type: CACHE_MESSAGE_TYPES.COMMIT_REQUEST,
            sessionId,
            correlationId,
            originAgent: "proposal_gate",
            targetObjectId,
            authoringMode,
            snapshotId,
            objectRevision,
            sceneEpoch,
            payload: { consentRoute: consentRoute || null, validationState: validationState || null },
        });
        const reply = this.#awaitReply(envelope.correlationId, [CACHE_MESSAGE_TYPES.COMMIT_ACCEPTED, CACHE_MESSAGE_TYPES.COMMIT_REJECTED], effectiveTimeout);
        this.#sendCache(CHANNELS.ARTIFACT_CHANNEL, envelope);
        return reply;
    }

    async sendRollbackRequest({ correlationId, targetObjectId, artifactId, sessionId, timeoutMs } = {}) {
        if (!correlationId || !targetObjectId) {
            throw new Error("sendRollbackRequest requires correlationId and targetObjectId");
        }
        const effectiveTimeout = this.#effectiveTimeout(timeoutMs);
        const envelope = makeCacheEnvelope({
            type: CACHE_MESSAGE_TYPES.ROLLBACK_REQUEST,
            sessionId,
            correlationId,
            originAgent: "version_memory",
            targetObjectId,
            payload: { artifactId: artifactId || null },
        });
        const reply = this.#awaitReply(envelope.correlationId, CACHE_MESSAGE_TYPES.ROLLBACK_RESULT, effectiveTimeout);
        this.#sendCache(CHANNELS.ARTIFACT_CHANNEL, envelope);
        return reply;
    }

    // Fire-and-forget: tells Unity a cached object/region/proposal is no longer
    // trustworthy (e.g. after a sceneEpoch change invalidated pending proposals).
    sendCacheInvalidation({ sessionId, correlationId, targetObjectId, reason } = {}) {
        const envelope = makeCacheEnvelope({
            type: CACHE_MESSAGE_TYPES.CACHE_INVALIDATION,
            sessionId,
            correlationId,
            originAgent: "cache_reconciler",
            targetObjectId,
            payload: { reason: reason || null },
        });
        this.#sendCache(CHANNELS.AGENT_PRESENCE, envelope);
        return envelope.correlationId;
    }

    // Fire-and-forget richer status than sendAgentUtterance's plain text - state is
    // one of thinking|querying_memory|validating|repairing|waiting_for_user|
    // ready_to_preview|failed per the spec.
    sendAgentStatus({ sessionId, correlationId, state, detail } = {}) {
        const envelope = makeCacheEnvelope({
            type: CACHE_MESSAGE_TYPES.AGENT_STATUS,
            sessionId,
            correlationId,
            originAgent: "coordinator",
            payload: { state: state || null, detail: detail || null },
        });
        this.#sendCache(CHANNELS.AGENT_UTTERANCE, envelope);
        return envelope.correlationId;
    }

    // Fire-and-forget: acknowledges (or rejects) one Unity-published SceneDelta by
    // deltaSeq. See cache/protocol.js's header comment for why this flows
    // Server->Unity despite the paper's compressed table listing "delta acks" under
    // the Unity-to-Server row.
    sendDeltaAck({ sessionId, deltaSeq, correlationId } = {}) {
        const envelope = makeCacheEnvelope({
            type: CACHE_MESSAGE_TYPES.DELTA_ACK,
            sessionId,
            correlationId,
            originAgent: "cache_reconciler",
            deltaSeq,
            payload: {},
        });
        this.#sendCache(CHANNELS.AGENT_PRESENCE, envelope);
        return envelope.correlationId;
    }

    sendDeltaNack({ sessionId, deltaSeq, correlationId, reason } = {}) {
        const envelope = makeCacheEnvelope({
            type: CACHE_MESSAGE_TYPES.DELTA_NACK,
            sessionId,
            correlationId,
            originAgent: "cache_reconciler",
            deltaSeq,
            payload: { reason: reason || null },
        });
        this.#sendCache(CHANNELS.AGENT_PRESENCE, envelope);
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
