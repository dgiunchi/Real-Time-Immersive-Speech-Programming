"use strict";

const fs = require("fs");
const path = require("path");
const { createHash } = require("crypto");

const MAX_PRIOR_DECISIONS = 50;
const DEFAULT_RETENTION_DAYS = 90;

function emptyLearned() {
    return {
        events: 0, accepted: 0, rejected: 0, undo: 0, repair: 0,
        byInteractionMode: {}, byAuthoringMode: {}, responseLatencyMs: [],
        regions: {}, focusTargets: {}, preferredAuthoringMode: null, meanAcceptedRisk: null,
    };
}

class PersonPolicyStore {
    constructor({ filePath } = {}) {
        this.filePath = filePath || path.join(__dirname, "data", "person_profiles.json");
        this.sessions = new Map();
        this.profiles = new Map();
        this._load();
    }

    _personKey(personId) {
        if (!personId || !/^[A-Za-z0-9._:-]{1,128}$/.test(personId)) throw new Error("personId must be a 1-128 character pseudonymous identifier");
        return createHash("sha256").update(personId).digest("hex").slice(0, 24);
    }

    _load() {
        if (!fs.existsSync(this.filePath)) return;
        try {
            const data = JSON.parse(fs.readFileSync(this.filePath, "utf8"));
            let expired = false;
            for (const profile of data.profiles || []) {
                if (profile.retentionUntil > Date.now() && profile.persistenceConsent === true) this.profiles.set(profile.personKey, profile);
                else expired = true;
            }
            if (expired) this._save();
        } catch (error) { console.error(`[person_policy] ignored invalid persisted profiles: ${error.message}`); }
    }

    _save() {
        fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
        fs.writeFileSync(this.filePath, JSON.stringify({ schemaVersion: "1.0", profiles: Array.from(this.profiles.values()) }, null, 2) + "\n");
    }

    setPersistenceConsent({ sessionId, personId, consent, retentionDays = DEFAULT_RETENTION_DAYS } = {}) {
        if (!sessionId) throw new Error("sessionId is required");
        const personKey = this._personKey(personId);
        if (consent !== true) {
            this.profiles.delete(personKey);
            this.sessions.set(sessionId, { personKey: null, persistenceConsent: false });
            this._save();
            return { sessionId, persistenceConsent: false, revoked: true };
        }
        const existing = this.profiles.get(personKey) || { personKey, learned: emptyLearned(), createdAt: Date.now() };
        existing.persistenceConsent = true;
        existing.retentionDays = Math.min(365, Math.max(1, retentionDays));
        existing.retentionUntil = Date.now() + existing.retentionDays * 86400000;
        existing.updatedAt = Date.now();
        this.profiles.set(personKey, existing);
        this.sessions.set(sessionId, { personKey, persistenceConsent: true });
        this._save();
        return this.inspect({ sessionId });
    }

    resetProfile({ sessionId, personId } = {}) {
        const binding = sessionId ? this.sessions.get(sessionId) : null;
        const personKey = binding && binding.personKey ? binding.personKey : this._personKey(personId);
        const existed = this.profiles.delete(personKey);
        for (const [key, value] of this.sessions) if (value.personKey === personKey) this.sessions.set(key, { personKey: null, persistenceConsent: false });
        this._save();
        return { reset: existed, personKey };
    }

    _sessionPolicy(sessionId) {
        const key = sessionId || "default";
        if (!this.sessions.has(key)) this.sessions.set(key, { personKey: null, persistenceConsent: false, priorDecisions: [], learned: emptyLearned() });
        const policy = this.sessions.get(key);
        if (!policy.priorDecisions) policy.priorDecisions = [];
        if (!policy.learned) policy.learned = emptyLearned();
        return policy;
    }

    _profileForSession(sessionId) {
        const binding = this._sessionPolicy(sessionId);
        return binding.persistenceConsent && binding.personKey ? this.profiles.get(binding.personKey) : null;
    }

    getPolicy({ sessionId } = {}) {
        const session = this._sessionPolicy(sessionId);
        const persistent = this._profileForSession(sessionId);
        return {
            sessionId: sessionId || "default", personKey: session.personKey || null,
            persistenceConsent: !!session.persistenceConsent,
            role: "owner", permissions: ["select", "confirm", "reject", "undo", "persist"],
            consent: { sharedObjectMutation: true, crossSessionLearning: !!session.persistenceConsent },
            priorDecisions: session.priorDecisions,
            learned: persistent ? persistent.learned : session.learned,
            retentionUntil: persistent ? persistent.retentionUntil : null,
            note: "cross-session learning is pseudonymous and explicit-opt-in; it can only restrict autonomy",
        };
    }

    inspect({ sessionId } = {}) { return this.getPolicy({ sessionId }); }

    _updateLearned(learned, event) {
        learned.events += 1;
        const type = String(event.eventType || "").toLowerCase();
        if (/approved|committed|accepted/.test(type)) learned.accepted += 1;
        if (/reject|timeout|failed/.test(type)) learned.rejected += 1;
        if (/undo|rollback|removed/.test(type)) learned.undo += 1;
        if (/repair|edit/.test(type)) learned.repair += 1;
        if (event.interactionMode) learned.byInteractionMode[event.interactionMode] = (learned.byInteractionMode[event.interactionMode] || 0) + 1;
        if (event.authoringMode) learned.byAuthoringMode[event.authoringMode] = (learned.byAuthoringMode[event.authoringMode] || 0) + 1;
        if (event.region) learned.regions[event.region] = (learned.regions[event.region] || 0) + 1;
        if (event.targetObjectId) learned.focusTargets[event.targetObjectId] = (learned.focusTargets[event.targetObjectId] || 0) + 1;
        if (Number.isFinite(event.responseLatencyMs)) { learned.responseLatencyMs.push(event.responseLatencyMs); if (learned.responseLatencyMs.length > 50) learned.responseLatencyMs.shift(); }
        if (/approved|committed|accepted/.test(type) && Number.isFinite(event.riskScore)) {
            learned.meanAcceptedRisk = learned.meanAcceptedRisk == null ? event.riskScore : ((learned.meanAcceptedRisk * (learned.accepted - 1)) + event.riskScore) / learned.accepted;
        }
        learned.preferredAuthoringMode = Object.entries(learned.byAuthoringMode).sort((a, b) => b[1] - a[1])[0]?.[0] || null;
    }

    recordEvent(event = {}) {
        const policy = this._sessionPolicy(event.sessionId);
        const decision = { eventType: event.eventType, targetObjectId: event.targetObjectId || null, interactionMode: event.interactionMode || null, authoringMode: event.authoringMode || null, at: event.at || Date.now() };
        policy.priorDecisions.push(decision);
        if (policy.priorDecisions.length > MAX_PRIOR_DECISIONS) policy.priorDecisions.shift();
        this._updateLearned(policy.learned, event);
        const persistent = this._profileForSession(event.sessionId);
        if (persistent) { this._updateLearned(persistent.learned, event); persistent.updatedAt = Date.now(); this._save(); }
        return this.getPolicy({ sessionId: event.sessionId });
    }

    snapshotConsentedProfiles() { return Array.from(this.profiles.values()); }
}

module.exports = { PersonPolicyStore, DEFAULT_RETENTION_DAYS };
