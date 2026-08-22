"use strict";

// Temporal memory layer / Version-Memory store (docs/agentic-xr-architecture.md
// §4.1/§8 phase 5; docs/shared-memory-and-experimental-space.md §1). Append-only
// JSON-lines log so history survives process restarts without a new dependency.
// Migrating to SQLite is the documented open decision in agentic-xr-architecture.md
// §9 - flat file is deliberately the starting point, not the final answer.

const fs = require("fs");
const path = require("path");

const STUDY_ID_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;
const STUDY_CONTEXT_FIELDS = Object.freeze([
    "participantId",
    "sessionId",
    "trialId",
    "condition",
    "taskId",
    "interactionMode",
]);
const STUDY_OPTIONAL_CONTEXT_FIELDS = Object.freeze([
    "protocolId",
    "blockId",
    "conditionAlias",
    "h4Arm",
    "sequenceIndex",
]);

function validateStudyContext(context, { requireCorrelationId = true } = {}) {
    if (!context || typeof context !== "object") throw new Error("study context is required");
    for (const field of STUDY_CONTEXT_FIELDS) {
        const value = context[field];
        if (typeof value !== "string" || !STUDY_ID_PATTERN.test(value)) {
            throw new Error(`${field} must be a 1-128 character pseudonymous safe identifier`);
        }
    }
    if (requireCorrelationId &&
        (typeof context.correlationId !== "string" || !STUDY_ID_PATTERN.test(context.correlationId))) {
        throw new Error("correlationId must be a 1-128 character safe identifier for every study event");
    }
    return context;
}

// Trial-scoped H4 configuration: how many candidates the generator should draft
// (N=1 vs. N>1, switchable per trial). Optional; when set it travels with the
// study context so the runtime can hand it to the orchestrator per turn.
function validateCandidateTarget(candidateTarget) {
    if (candidateTarget == null) return null;
    const value = Number(candidateTarget);
    if (!Number.isInteger(value) || value < 1 || value > 5) {
        throw new Error("candidateTarget must be an integer between 1 and 5");
    }
    return value;
}

function studyContextOf(entry) {
    const context = Object.fromEntries(STUDY_CONTEXT_FIELDS.map((field) => [field, entry[field]]));
    for (const field of STUDY_OPTIONAL_CONTEXT_FIELDS) {
        if (entry[field] != null) context[field] = entry[field];
    }
    if (Number.isInteger(entry.candidateTarget)) context.candidateTarget = entry.candidateTarget;
    return context;
}

class ArtifactLog {
    constructor({ filePath } = {}) {
        this.filePath = filePath || path.join(__dirname, "data", "artifact_log.jsonl");
        fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
        this.records = [];
        this.byObjectId = new Map(); // objectId -> [entries]
        this.byArtifactId = new Map();
        this.activeTrialBySession = new Map();
        this.studyContextByCorrelation = new Map();
        this.studyContextByRuntimeSession = new Map();
        this._loadExisting();
    }

    _loadExisting() {
        if (!fs.existsSync(this.filePath)) return;
        const lines = fs.readFileSync(this.filePath, "utf8").split("\n").filter(Boolean);
        for (const line of lines) {
            try {
                this._index(JSON.parse(line));
            } catch (err) {
                console.error(`[artifact_log] skipped malformed line: ${err.message}`);
            }
        }
    }

    _index(entry) {
        this.records.push(entry);
        if (entry.artifactId) this.byArtifactId.set(entry.artifactId, entry);
        if (entry.targetObjectId) {
            if (!this.byObjectId.has(entry.targetObjectId)) this.byObjectId.set(entry.targetObjectId, []);
            this.byObjectId.get(entry.targetObjectId).push(entry);
        }
        if (entry.eventType === "study_trial_started") {
            const context = studyContextOf(entry);
            this.activeTrialBySession.set(entry.sessionId, context);
            this.studyContextByCorrelation.set(entry.correlationId, context);
        } else if (entry.eventType === "study_trial_ended") {
            const active = this.activeTrialBySession.get(entry.sessionId);
            if (active && active.trialId === entry.trialId) this.activeTrialBySession.delete(entry.sessionId);
            for (const [runtimeSessionId, context] of this.studyContextByRuntimeSession.entries()) {
                if (context.trialId === entry.trialId && context.sessionId === entry.sessionId) {
                    this.studyContextByRuntimeSession.delete(runtimeSessionId);
                }
            }
        } else if (entry.eventType === "study_runtime_session_bound" && entry.runtimeSessionId) {
            const context = studyContextOf(entry);
            this.studyContextByRuntimeSession.set(entry.runtimeSessionId, context);
            this.studyContextByCorrelation.set(entry.correlationId, context);
        } else if (entry.studyEvent && entry.correlationId && entry.participantId) {
            this.studyContextByCorrelation.set(entry.correlationId, studyContextOf(entry));
        }
    }

    append(entry) {
        if (!entry || typeof entry !== "object") throw new Error("artifact log entry must be an object");
        const context = this.studyContextByCorrelation.get(entry.correlationId) ||
            this.activeTrialBySession.get(entry.sessionId) ||
            this.studyContextByRuntimeSession.get(entry.sessionId) || null;
        const now = Date.now();
        const eventAt = Number.isFinite(entry.timestamp) ? entry.timestamp :
            Number.isFinite(entry.at) ? entry.at : now;
        const runtimeSessionId = context && entry.sessionId && entry.sessionId !== context.sessionId
            ? entry.sessionId : entry.runtimeSessionId;
        const record = {
            ...entry,
            ...(context || {}),
            ...(runtimeSessionId ? { runtimeSessionId } : {}),
            studyEvent: entry.studyEvent || Boolean(context && entry.correlationId),
            timestampUtc: entry.timestampUtc || new Date(eventAt).toISOString(),
            loggedAt: now,
        };
        if (context) {
            for (const field of STUDY_CONTEXT_FIELDS) {
                if (record[field] == null || record[field] === "") record[field] = context[field];
            }
        }
        if (record.studyEvent) validateStudyContext(record);
        fs.appendFileSync(this.filePath, JSON.stringify(record) + "\n");
        this._index(record);
        return record;
    }

    startStudyTrial(context) {
        validateStudyContext(context);
        const candidateTarget = validateCandidateTarget(context.candidateTarget);
        this._reloadStudyContextFromDisk();
        if (this.activeTrialBySession.size > 0) {
            const active = [...this.activeTrialBySession.values()][0];
            throw new Error(`active study trial '${active.trialId}' must be ended or aborted before starting '${context.trialId}'`);
        }
        const activeArtifacts = this.activeArtifacts();
        if (activeArtifacts.length > 0) {
            throw new Error(`${activeArtifacts.length} active artifact(s) remain from the previous trial; complete a TrialReset before starting '${context.trialId}'`);
        }
        return this.append({
            ...context,
            ...(candidateTarget != null ? { candidateTarget } : {}),
            eventType: "study_trial_started",
            studyEvent: true,
            taskCompletion: false,
            at: context.at || Date.now(),
        });
    }

    appendStudyEvent(entry) {
        return this.append({ ...entry, studyEvent: true });
    }

    endStudyTrial({ sessionId, correlationId, trialId, taskCompletion, taskSuccess,
        taskQualityScore = null, taskQualitySignals = null, reason = null, at } = {}) {
        const context = this.studyContextByCorrelation.get(correlationId) ||
            this.activeTrialBySession.get(sessionId);
        if (!context) throw new Error(`no active study trial for sessionId '${sessionId || "missing"}'`);
        if (trialId && trialId !== context.trialId) throw new Error("trialId does not match the active study trial");
        if (typeof taskCompletion !== "boolean") throw new Error("taskCompletion must be boolean");
        if (taskSuccess != null && typeof taskSuccess !== "boolean") throw new Error("taskSuccess must be boolean or null");
        if (taskQualityScore != null && !Number.isFinite(taskQualityScore)) throw new Error("taskQualityScore must be numeric or null");
        return this.appendStudyEvent({
            ...context,
            correlationId,
            eventType: "study_trial_ended",
            taskCompletion,
            taskSuccess: taskSuccess ?? null,
            taskQualityScore,
            taskQualitySignals,
            reason,
            at: at || Date.now(),
        });
    }

    getStudyContext({ sessionId, correlationId } = {}) {
        let context = this.studyContextByCorrelation.get(correlationId) ||
            this.activeTrialBySession.get(sessionId) ||
            this.studyContextByRuntimeSession.get(sessionId) || null;
        if (context || !fs.existsSync(this.filePath)) return context;
        this._reloadStudyContextFromDisk();
        context = this.studyContextByCorrelation.get(correlationId) ||
            this.activeTrialBySession.get(sessionId) ||
            this.studyContextByRuntimeSession.get(sessionId) || null;
        return context;
    }

    _reloadStudyContextFromDisk() {
        if (!fs.existsSync(this.filePath)) return;
        // Another process (for example the researcher CLI or Unity bridge) may have
        // opened the trial after this ArtifactLog instance started. Refresh only
        // study context maps; do not duplicate the in-memory history index.
        this.activeTrialBySession.clear();
        this.studyContextByCorrelation.clear();
        this.studyContextByRuntimeSession.clear();
        for (const line of fs.readFileSync(this.filePath, "utf8").split(/\r?\n/).filter(Boolean)) {
            try {
                const entry = JSON.parse(line);
                if (entry.eventType === "study_trial_started") {
                    const loaded = studyContextOf(entry);
                    this.activeTrialBySession.set(entry.sessionId, loaded);
                    this.studyContextByCorrelation.set(entry.correlationId, loaded);
                } else if (entry.eventType === "study_trial_ended") {
                    const active = this.activeTrialBySession.get(entry.sessionId);
                    if (active && active.trialId === entry.trialId) this.activeTrialBySession.delete(entry.sessionId);
                    for (const [runtimeSessionId, bound] of this.studyContextByRuntimeSession.entries()) {
                        if (bound.trialId === entry.trialId && bound.sessionId === entry.sessionId) {
                            this.studyContextByRuntimeSession.delete(runtimeSessionId);
                        }
                    }
                } else if (entry.eventType === "study_runtime_session_bound" && entry.runtimeSessionId) {
                    const loaded = studyContextOf(entry);
                    this.studyContextByRuntimeSession.set(entry.runtimeSessionId, loaded);
                    this.studyContextByCorrelation.set(entry.correlationId, loaded);
                } else if (entry.studyEvent && entry.correlationId && entry.participantId) {
                    this.studyContextByCorrelation.set(entry.correlationId, studyContextOf(entry));
                }
            } catch (_) { /* malformed lines are reported during the normal load */ }
        }
    }

    claimRuntimeSession({ runtimeSessionId, correlationId, studySource = "runtime" } = {}) {
        let context = correlationId
            ? this.getStudyContext({ correlationId })
            : null;
        if (context) return context;
        if (typeof runtimeSessionId !== "string" || !STUDY_ID_PATTERN.test(runtimeSessionId)) {
            // Refresh before deciding that this is a non-study call. The
            // researcher CLI may have opened a trial after this process booted.
            this.getStudyContext({ sessionId: runtimeSessionId, correlationId });
            const active = [...this.activeTrialBySession.values()];
            // Non-study tools commonly omit sessionId. That is not an error unless
            // a trial is active and would otherwise risk producing unjoined data.
            if (active.length === 0) return null;
            throw new Error("runtimeSessionId must be a 1-128 character safe identifier");
        }
        context = this.getStudyContext({ sessionId: runtimeSessionId, correlationId });
        if (context) {
            if (correlationId) this.studyContextByCorrelation.set(correlationId, context);
            return context;
        }

        // The current study protocol is single-participant. A live Ubiq peer or
        // Unity cache identity may therefore claim the sole active trial, but an
        // ambiguous multi-trial state is an error rather than a guessed join.
        const active = [...this.activeTrialBySession.values()];
        if (active.length === 0) return null;
        if (active.length !== 1) {
            throw new Error(`cannot bind runtime session '${runtimeSessionId}': ${active.length} study trials are active`);
        }
        context = active[0];
        const bindingCorrelationId = correlationId ||
            `bind-${context.trialId}-${runtimeSessionId}`.slice(0, 128);
        // The append is the durable source of truth and indexes the alias only
        // after the write succeeds. Never mutate the in-memory maps first.
        this.appendStudyEvent({
            ...context,
            correlationId: bindingCorrelationId,
            eventType: "study_runtime_session_bound",
            runtimeSessionId,
            studySource,
        });
        return context;
    }

    history({ objectId, limit = 20 } = {}) {
        const entries = objectId
            ? (this.byObjectId.get(objectId) || [])
            : this.records;
        return entries.slice(-limit).reverse();
    }

    evolution({ objectId, limit = 100 } = {}) {
        return this.history({ objectId, limit }).reverse().map((entry) => ({
            at: entry.loggedAt || entry.at,
            eventType: entry.eventType,
            operation: entry.operation || (entry.eventType && entry.eventType.includes("rollback") ? "rollback" : null),
            targetObjectId: entry.targetObjectId || null,
            artifactId: entry.artifactId || null,
            artifactVersion: entry.artifactVersion || null,
            supersedesArtifactId: entry.supersedesArtifactId || entry.rollbackPointer || null,
            candidateId: entry.candidateId || null,
            candidateSetId: entry.candidateSetId || null,
            selectionReason: entry.selectionReason || null,
            intent: entry.intent || null,
            status: entry.status || null,
            reason: entry.reason || null,
            correlationId: entry.correlationId || null,
        }));
    }

    activeArtifacts() {
        const active = new Map();
        for (const entry of this.history({ limit: Number.MAX_SAFE_INTEGER }).reverse()) {
            if (entry.eventType === "trial_reset" && !entry.targetObjectId) {
                active.clear();
                continue;
            }
            if (!entry.targetObjectId) continue;
            const operation = entry.operation || "create";
            const committed = entry.status === "committed" || entry.status === "removed" || entry.eventType === "commitaccepted";
            if (committed && operation !== "remove") active.set(entry.targetObjectId, entry);
            if ((committed && operation === "remove") || /rollback|removed|trial_reset/.test(entry.eventType || "")) active.delete(entry.targetObjectId);
        }
        return Array.from(active.values());
    }
}

module.exports = { ArtifactLog, STUDY_CONTEXT_FIELDS, STUDY_OPTIONAL_CONTEXT_FIELDS, STUDY_ID_PATTERN, validateStudyContext, validateCandidateTarget };
