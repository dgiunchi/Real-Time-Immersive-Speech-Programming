"use strict";

// Cache Exchange Layer aggregator - short-term synchronized state, distinct from
// Shared XR Memory (../memory, durable interpreted memory) per the paper's own
// framing: "cache is short-term synchronized state; Shared XR Memory is durable
// interpreted memory; Verification Space consumes named snapshots and returns
// evidence" (rag/prompts/cache_exchange_agenticxr_prompt.md).

const { AgentWorkingCache } = require("./agent_working_cache");
const { EventJournal } = require("./event_journal");
const { CacheReconciler } = require("./cache_reconciler");
const { ProposalGate } = require("./proposal_gate");
const { CACHE_MESSAGE_TYPES, fromWireFormat } = require("./protocol");

class CacheExchangeLayer {
    constructor() {
        this.workingCache = new AgentWorkingCache();
        this.journal = new EventJournal();
        this.reconciler = new CacheReconciler({ workingCache: this.workingCache, journal: this.journal });
        this.proposalGate = new ProposalGate({ workingCache: this.workingCache, reconciler: this.reconciler });
    }

    // A single-object SceneDelta already has the shape reconcileDelta expects
    // (stableObjectId/objectRevision on the envelope itself). CacheSnapshot and
    // BackfillResponse are naturally multi-object, so this synthesizes one
    // per-object envelope per entry, sharing the outer envelope's session/epoch/
    // snapshot/timestamp fields. objects/deltas payload shape:
    //   CacheSnapshot.payload.objects: [{ stableObjectId, objectRevision, tag, region, state }]
    //   BackfillResponse.payload.deltas: [{ stableObjectId, objectRevision, deltaSeq, tag, region, state, timestamp }]
    #reconcileOne(outer, entry, { isBackfill = false } = {}) {
        const synthetic = {
            ...outer,
            stableObjectId: entry.stableObjectId,
            objectRevision: entry.objectRevision,
            deltaSeq: entry.deltaSeq != null ? entry.deltaSeq : outer.deltaSeq,
            timestamp: entry.timestamp || outer.timestamp,
            payload: { tag: entry.tag, region: entry.region, state: entry.state },
        };
        const result = this.reconciler.reconcileDelta(synthetic, { isBackfill });
        console.error(
            `[cache_exchange] reconcile ${outer.type}${isBackfill ? "(backfill)" : ""} seq=${synthetic.deltaSeq} object=${entry.stableObjectId}: ${result.outcome}` +
            (result.detail && result.detail.gap ? ` gap=${JSON.stringify(result.detail.gap)}` : "")
        );
        return result;
    }

    // Wires the reconciler into a SceneBridgeClient's envelope stream. When a delta
    // implies missing detail (a gap), automatically issues a backfill request; when
    // it implies a stale/conflicted baseline, requests a fresh snapshot. This is the
    // "Cache Reconciler can request backfill, ask Unity for a fresh snapshot" bullet
    // made concrete - the reconciler decides, this glue acts on the bridge.
    attach(bridge) {
        bridge.on("envelope", (envelope) => {
            if (envelope.type === CACHE_MESSAGE_TYPES.SCENE_DELTA) {
                // SceneDelta is shared by two protocol planes. Focus+halo query
                // replies carry payload.focus but intentionally have no compact
                // stableObjectId/deltaSeq; Shared XR Memory consumes those. Only
                // compact cache deltas belong in the Agent Working Cache.
                if (!envelope.stableObjectId) return;
                const result = this.#reconcileOne(envelope, {
                    stableObjectId: envelope.stableObjectId,
                    objectRevision: envelope.objectRevision,
                    tag: envelope.payload && envelope.payload.tag,
                    region: envelope.payload && envelope.payload.region,
                    state: envelope.payload,
                });
                if (result.recommendedAction === "backfill" && result.detail.gap) {
                    bridge
                        .requestBackfill({ sessionId: envelope.sessionId, lastSeenSeq: result.detail.gap.fromSeq - 1 })
                        .catch((err) => console.error(`[cache_exchange] backfill request failed: ${err.message}`));
                } else if (result.recommendedAction === "snapshot") {
                    bridge.requestSnapshot({ sessionId: envelope.sessionId }).catch((err) => console.error(`[cache_exchange] snapshot request failed: ${err.message}`));
                }
                return;
            }

            if (envelope.type === CACHE_MESSAGE_TYPES.CACHE_SNAPSHOT) {
                const decoded = fromWireFormat(envelope);
                const objects = (decoded.payload && decoded.payload.objects) || [];
                for (const obj of objects) this.#reconcileOne(decoded, obj);
                return;
            }

            if (envelope.type === CACHE_MESSAGE_TYPES.BACKFILL_RESPONSE) {
                const decoded = fromWireFormat(envelope);
                const deltas = (decoded.payload && decoded.payload.deltas) || [];
                for (const delta of deltas) this.#reconcileOne(decoded, delta, { isBackfill: true });
                return;
            }
        });
    }
}

module.exports = { CacheExchangeLayer };
