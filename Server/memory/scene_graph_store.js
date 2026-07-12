"use strict";

// Semantic memory layer (docs/shared-memory-and-experimental-space.md §1): relations
// and affordances. Relations start from halo co-membership (naive "near") and sensor
// events (see sensor_registry.js); real relations like on/inside/attached-to/supports
// require Unity to publish hierarchy data, which is not implemented yet (roadmap
// phase 1). Affordances are a static tag/component lookup, not learned inference -
// this is deliberate and should be described honestly in the paper as such.

const AFFORDANCE_RULES = [
    { match: (c) => c.type === "MeshRenderer", affordance: "visible" },
    { match: (c) => /Grabbable/i.test(c.type || ""), affordance: "usable" },
    { match: (c) => /^GeneratedBehaviour/.test(c.type || ""), affordance: "scripted" },
    { match: (c) => /Light/i.test(c.type || ""), affordance: "illuminates" },
];

class SceneGraphStore {
    constructor(visualStore) {
        this.visualStore = visualStore;
        this.relations = []; // { from, to, relation, source, timestamp }
        this.maxRelations = 500;
    }

    ingestSceneDelta(envelope) {
        const payload = envelope.payload || {};
        const focusId = payload.focus && payload.focus.id;
        if (!focusId) return;
        const at = envelope.timestamp || Date.now();
        for (const h of payload.halo || []) {
            this.relations.push({ from: focusId, to: h.id, relation: "near", source: "halo-membership (naive)", timestamp: at });
        }
        this._prune();
    }

    _prune() {
        if (this.relations.length > this.maxRelations) {
            this.relations = this.relations.slice(-this.maxRelations);
        }
    }

    queryGraph({ objectId } = {}) {
        const rels = objectId
            ? this.relations.filter((r) => r.from === objectId || r.to === objectId)
            : this.relations;
        const dedup = new Map();
        for (const r of rels) dedup.set(`${r.from}|${r.to}|${r.relation}`, r);
        return Array.from(dedup.values());
    }

    queryAffordances({ objectId } = {}) {
        const entry = objectId && this.visualStore.byObjectId.get(objectId);
        if (!entry || !entry.focus) {
            return { objectId, affordances: [], note: "no focus data cached for this object yet - query_visual_memory or query_scene first" };
        }
        const affordances = new Set();
        for (const c of entry.focus.components || []) {
            for (const rule of AFFORDANCE_RULES) {
                if (rule.match(c)) affordances.add(rule.affordance);
            }
        }
        return { objectId, affordances: Array.from(affordances), note: "static tag/component lookup, not learned inference" };
    }
}

module.exports = { SceneGraphStore, AFFORDANCE_RULES };
