"use strict";

// Offline study exporter. It reads the existing append-only temporal ArtifactLog and
// produces analysis-ready CSVs; it never runs on the XR interaction hot path.

const fs = require("fs");
const path = require("path");

const TRIAL_COLUMNS = Object.freeze([
    "participantId", "sessionId", "trialId", "condition", "taskId", "interactionMode",
    "trialStartedAtUtc", "trialEndedAtUtc", "taskCompletion", "taskSuccess",
    "taskQualityScore", "taskQualitySignalsJson", "totalTaskTimeMs", "correlationIds",
    "intentCapturedAtUtc", "firstAcknowledgementAtUtc", "validatedExecutionAtUtc",
    "immediateAcknowledgementLatencyMs", "validatedExecutionLatencyMs",
    "generatedArtifactCount", "compileFailureCount", "validationFailureCount",
    "runtimeFailureCount", "verificationApplyCount", "verificationClarifyCount",
    "verificationRepairCount", "verificationRejectCount",
    "verificationCandidateDurationsMsJson", "verificationTimeTotalMs",
    "previewToCommitTimeMs", "verificationLiveMismatchCount", "groundingErrorCount",
    "staleApplicationCount", "staleProposalCount", "invalidCorrelationIdCount",
    "invalidTargetObjectCount", "timestampAgeAtApplicationMsJson",
    "memoryRetrievalLatencyMsJson", "memoryRetrievalLatencyMeanMs",
    "unsafeProposalCount", "blockedUnsafeArtifactCount", "repairAttemptCount",
    "clarificationTurnCount", "confirmationCount", "rejectionCount", "undoCount",
    "rollbackCount", "interruptionCount", "resumptionCount",
    "interruptionTotalTimeMs", "candidatesGenerated", "selectedCandidateId",
    "selectedCandidateRank", "selectedCandidateScore",
    "firstProposalAcceptedWithoutRevision", "agentStatusMessageCount",
    "firstAgentStatusAtUtc",
]);

const LONG_COLUMNS = Object.freeze([
    "participantId", "sessionId", "trialId", "condition", "taskId", "interactionMode",
    "correlationId", "timestampUtc", "eventType", "targetObjectId", "artifactId",
    "candidateId", "candidateSetId", "status", "reasonCode", "durationMs",
    "verificationDurationMs", "commitAttachDurationMs", "timestampAgeMs",
    "correlationIdValid", "targetObjectValid", "selectedCandidateRank",
    "selectedCandidateScore", "studySource",
]);

function readJsonLines(filePath) {
    if (!fs.existsSync(filePath)) throw new Error(`study log not found: ${filePath}`);
    return fs.readFileSync(filePath, "utf8").split(/\r?\n/).filter(Boolean).map((line, index) => {
        try { return JSON.parse(line); }
        catch (error) { throw new Error(`invalid JSONL at line ${index + 1}: ${error.message}`); }
    });
}

function eventTime(event) {
    if (!event) return null;
    if (Number.isFinite(event.timestamp)) return event.timestamp;
    if (Number.isFinite(event.at)) return event.at;
    if (Number.isFinite(event.loggedAt)) return event.loggedAt;
    const parsed = Date.parse(event.timestampUtc);
    return Number.isFinite(parsed) ? parsed : null;
}

function iso(at) {
    return Number.isFinite(at) ? new Date(at).toISOString() : "";
}

function csvValue(value) {
    if (value == null) return "";
    const text = typeof value === "object" ? JSON.stringify(value) : String(value);
    return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, "\"\"")}"` : text;
}

function writeCsv(filePath, columns, rows) {
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    const lines = [
        columns.join(","),
        ...rows.map((row) => columns.map((column) => csvValue(row[column])).join(",")),
    ];
    fs.writeFileSync(filePath, lines.join("\n") + "\n");
}

function reasonCode(event) {
    if (event.reasonCode) return event.reasonCode;
    const reason = String(event.reason || event.error || "").toLowerCase();
    if (!reason) return "";
    if (reason.includes("capability") || reason.includes("namespace") || reason.includes("unsafe")) return "capability_policy";
    if (reason.includes("correlation")) return "invalid_correlation";
    if (reason.includes("target") || reason.includes("object not found")) return "invalid_target";
    if (reason.includes("epoch")) return "scene_epoch_mismatch";
    if (reason.includes("snapshotid") || reason.includes("snapshot id")) return "snapshot_id_mismatch";
    if (reason.includes("revision")) return "object_revision_mismatch";
    if (reason.includes("too old") || reason.includes("stale")) return "stale_snapshot";
    if (reason.includes("compile") || reason.includes("roslyn")) return "compile_failure";
    if (reason.includes("runtime") || reason.includes("watchdog")) return "runtime_failure";
    if (reason.includes("validation")) return "validation_failure";
    return "other";
}

function numericList(events, fields) {
    const values = [];
    for (const event of events) {
        for (const field of fields) {
            if (Number.isFinite(event[field])) {
                values.push(event[field]);
                break;
            }
        }
    }
    return values;
}

function count(events, predicate) {
    return events.reduce((total, event) => total + (predicate(event) ? 1 : 0), 0);
}

function first(events, predicate) {
    return events.find(predicate) || null;
}

function last(events, predicate) {
    return [...events].reverse().find(predicate) || null;
}

function verificationOutcome(event) {
    if (event.verificationOutcome) return event.verificationOutcome;
    const status = String(event.status || "").toLowerCase();
    if (status === "simulated" || status === "apply") return "apply";
    if (status.includes("clarif")) return "clarify";
    if (status.includes("repair")) return "repair";
    if (["rejected", "error", "failed", "ineligible"].includes(status)) return "reject";
    return null;
}

function trialKey(event) {
    return [event.participantId, event.sessionId, event.trialId, event.condition, event.taskId].join("\u001f");
}

function validateStudyEvents(events) {
    const fields = ["participantId", "sessionId", "trialId", "condition", "taskId", "interactionMode", "correlationId", "timestampUtc"];
    for (const [index, event] of events.entries()) {
        if (!event.studyEvent) continue;
        for (const field of fields) {
            if (event[field] == null || event[field] === "") {
                throw new Error(`study event ${index + 1} (${event.eventType || "unknown"}) is missing ${field}`);
            }
        }
    }
}

function aggregateTrial(events) {
    events.sort((a, b) => eventTime(a) - eventTime(b));
    const started = first(events, (event) => event.eventType === "study_trial_started");
    if (!started) throw new Error(`trial '${events[0].trialId}' has no study_trial_started event`);
    const ended = last(events, (event) => event.eventType === "study_trial_ended");
    const intent = first(events, (event) => event.eventType === "intent_captured" || event.eventType === "turn_started");
    const acknowledgement = first(events, (event) =>
        ["agent_acknowledgement_surfaced", "agent_status_surfaced"].includes(event.eventType));
    const validated = first(events, (event) =>
        ["artifactresult", "commitaccepted"].includes(String(event.eventType || "").toLowerCase()) &&
        ["committed", "removed"].includes(String(event.status || "").toLowerCase()));
    const proposals = events.filter((event) => event.eventType === "propose_artifact");
    const verification = events.filter((event) =>
        event.eventType === "verification_outcome" || event.eventType === "simulate_artifact");
    const verificationDurations = numericList(verification, ["verificationDurationMs", "durationMs"]);
    const preview = first(events, (event) =>
        event.eventType === "proposal_preview_surfaced" || event.eventType === "propose_artifact");
    const commit = first(events, (event) =>
        ["artifactresult", "commitaccepted"].includes(String(event.eventType || "").toLowerCase()) &&
        ["committed", "removed"].includes(String(event.status || "").toLowerCase()));
    const memoryDurations = numericList(events.filter((event) => event.eventType === "memory_retrieval"), ["durationMs"]);
    const timestampAges = numericList(events.filter((event) =>
        event.eventType === "proposal_gate_checked" || event.eventType === "artifactresult"), ["timestampAgeMs"]);
    const candidateEvents = events.filter((event) => event.candidateId);
    const uniqueCandidates = new Set(candidateEvents.map((event) => event.candidateId));
    const selection = last(events, (event) => event.eventType === "candidate_selection");
    const selectedCandidateId = selection && (selection.selectedCandidateId ||
        (selection.selected && selection.selected.candidateId)) ||
        (last(candidateEvents, (event) => event.status === "selected") || {}).candidateId || "";
    const selectedEvent = last(candidateEvents, (event) => event.candidateId === selectedCandidateId) || selection || {};
    const decisionApproved = first(events, (event) =>
        event.eventType === "user_decision:approved" ||
        (event.eventType === "user_decision" && event.status === "approved"));
    const reviseBeforeApproval = events.some((event) => {
        const type = String(event.eventType || "").toLowerCase();
        return ["repair_attempt", "revision_requested", "user_decision:revise"].includes(type) &&
            (!decisionApproved || eventTime(event) <= eventTime(decisionApproved));
    });
    const interruptions = events.filter((event) => event.eventType === "interruption");
    const resumptions = events.filter((event) => event.eventType === "resumption");
    let interruptionTotal = 0;
    for (const interruption of interruptions) {
        const resumed = resumptions.find((event) => eventTime(event) >= eventTime(interruption));
        if (resumed) interruptionTotal += eventTime(resumed) - eventTime(interruption);
    }
    const reasonEvents = events.map((event) => ({ event, code: reasonCode(event) }));
    const mismatchExplicit = count(events, (event) => event.eventType === "verification_live_mismatch" || event.verificationLiveMismatch === true);
    const dryRunByCandidate = new Map(verification.filter((event) => event.candidateId)
        .map((event) => [event.candidateId, verificationOutcome(event)]));
    const mismatchDerived = count(events, (event) =>
        event.candidateId && dryRunByCandidate.get(event.candidateId) === "apply" &&
        ["artifactresult", "commitrejected"].includes(String(event.eventType || "").toLowerCase()) &&
        !["committed", "removed", "simulated"].includes(String(event.status || "").toLowerCase()));
    const startedAt = eventTime(started);
    const endedAt = ended ? eventTime(ended) : null;
    const intentAt = intent ? eventTime(intent) : null;
    const ackAt = acknowledgement ? eventTime(acknowledgement) : null;
    const validatedAt = validated ? eventTime(validated) : null;

    return {
        participantId: started.participantId,
        sessionId: started.sessionId,
        trialId: started.trialId,
        condition: started.condition,
        taskId: started.taskId,
        interactionMode: started.interactionMode,
        trialStartedAtUtc: iso(startedAt),
        trialEndedAtUtc: iso(endedAt),
        taskCompletion: ended ? ended.taskCompletion : false,
        taskSuccess: ended ? ended.taskSuccess : "",
        taskQualityScore: ended ? ended.taskQualityScore : "",
        taskQualitySignalsJson: ended && ended.taskQualitySignals != null ? JSON.stringify(ended.taskQualitySignals) : "",
        totalTaskTimeMs: Number.isFinite(startedAt) && Number.isFinite(endedAt) ? endedAt - startedAt : "",
        correlationIds: [...new Set(events.map((event) => event.correlationId).filter(Boolean))].join(";"),
        intentCapturedAtUtc: iso(intentAt),
        firstAcknowledgementAtUtc: iso(ackAt),
        validatedExecutionAtUtc: iso(validatedAt),
        immediateAcknowledgementLatencyMs: Number.isFinite(intentAt) && Number.isFinite(ackAt) ? ackAt - intentAt : "",
        validatedExecutionLatencyMs: Number.isFinite(intentAt) && Number.isFinite(validatedAt) ? validatedAt - intentAt : "",
        generatedArtifactCount: proposals.length || uniqueCandidates.size,
        compileFailureCount: count(reasonEvents, ({ event, code }) => code === "compile_failure" || event.failureStage === "compile"),
        validationFailureCount: count(reasonEvents, ({ event, code }) =>
            code === "validation_failure" || event.validationState === "rejected" || event.failureStage === "validation"),
        runtimeFailureCount: count(reasonEvents, ({ event, code }) =>
            code === "runtime_failure" || event.failureStage === "runtime" || event.status === "watchdog_disabled"),
        verificationApplyCount: count(verification, (event) => verificationOutcome(event) === "apply"),
        verificationClarifyCount: count(verification, (event) => verificationOutcome(event) === "clarify"),
        verificationRepairCount: count(verification, (event) => verificationOutcome(event) === "repair"),
        verificationRejectCount: count(verification, (event) => verificationOutcome(event) === "reject"),
        verificationCandidateDurationsMsJson: JSON.stringify(verificationDurations),
        verificationTimeTotalMs: verificationDurations.reduce((sum, value) => sum + value, 0),
        previewToCommitTimeMs: preview && commit ? eventTime(commit) - eventTime(preview) : "",
        verificationLiveMismatchCount: mismatchExplicit + mismatchDerived,
        groundingErrorCount: count(events, (event) => event.eventType === "grounding_error" ||
            event.groundingError === true ||
            event.correlationIdValid === false || event.targetObjectValid === false),
        staleApplicationCount: count(events, (event) =>
            event.eventType === "stale_application" || event.staleApplication === true),
        staleProposalCount: count(events, (event) => event.eventType === "stale_proposal" ||
            (event.eventType === "proposal_gate_checked" && event.stale === true)),
        invalidCorrelationIdCount: count(events, (event) => event.correlationIdValid === false),
        invalidTargetObjectCount: count(events, (event) => event.targetObjectValid === false),
        timestampAgeAtApplicationMsJson: JSON.stringify(timestampAges),
        memoryRetrievalLatencyMsJson: JSON.stringify(memoryDurations),
        memoryRetrievalLatencyMeanMs: memoryDurations.length
            ? Math.round((memoryDurations.reduce((sum, value) => sum + value, 0) / memoryDurations.length) * 100) / 100 : "",
        unsafeProposalCount: count(events, (event) => event.unsafeProposal === true ||
            (event.eventType === "propose_artifact" && Number(event.riskScore) >= 0.7)),
        blockedUnsafeArtifactCount: count(reasonEvents, ({ event, code }) =>
            event.blockedUnsafeArtifact === true || code === "capability_policy"),
        repairAttemptCount: count(events, (event) => event.eventType === "repair_attempt"),
        clarificationTurnCount: count(events, (event) => event.eventType === "clarification_turn"),
        confirmationCount: count(events, (event) => event.eventType === "user_decision:approved" ||
            event.eventType === "confirmation"),
        rejectionCount: count(events, (event) => event.eventType === "user_decision:rejected" ||
            event.eventType === "user_decision:timeout" || event.eventType === "rejection"),
        undoCount: count(events, (event) => event.eventType === "user_decision:undo" || event.eventType === "undo"),
        rollbackCount: count(events, (event) => String(event.eventType || "").includes("rollback") &&
            event.status !== "not_found"),
        interruptionCount: interruptions.length,
        resumptionCount: resumptions.length,
        interruptionTotalTimeMs: interruptionTotal,
        candidatesGenerated: uniqueCandidates.size || Number(selection && selection.candidateCount) || 1,
        selectedCandidateId,
        selectedCandidateRank: selectedEvent.selectedCandidateRank ?? selectedEvent.rank ?? "",
        selectedCandidateScore: selectedEvent.selectedCandidateScore ??
            (selectedEvent.ranking && selectedEvent.ranking.score) ?? selectedEvent.score ?? "",
        firstProposalAcceptedWithoutRevision: Boolean(decisionApproved && !reviseBeforeApproval),
        agentStatusMessageCount: count(events, (event) => event.eventType === "agent_status_surfaced"),
        firstAgentStatusAtUtc: iso(eventTime(first(events, (event) => event.eventType === "agent_status_surfaced"))),
    };
}

function longRow(event) {
    return {
        participantId: event.participantId,
        sessionId: event.sessionId,
        trialId: event.trialId,
        condition: event.condition,
        taskId: event.taskId,
        interactionMode: event.interactionMode,
        correlationId: event.correlationId,
        timestampUtc: event.timestampUtc,
        eventType: event.eventType,
        targetObjectId: event.targetObjectId || "",
        artifactId: event.artifactId || "",
        candidateId: event.candidateId || "",
        candidateSetId: event.candidateSetId || "",
        status: event.status || "",
        reasonCode: reasonCode(event),
        durationMs: event.durationMs ?? "",
        verificationDurationMs: event.verificationDurationMs ?? "",
        commitAttachDurationMs: event.commitAttachDurationMs ?? "",
        timestampAgeMs: event.timestampAgeMs ?? "",
        correlationIdValid: event.correlationIdValid ?? "",
        targetObjectValid: event.targetObjectValid ?? "",
        selectedCandidateRank: event.selectedCandidateRank ?? event.rank ?? "",
        selectedCandidateScore: event.selectedCandidateScore ??
            (event.ranking && event.ranking.score) ?? event.score ?? "",
        studySource: event.studySource || "pipeline",
    };
}

function buildStudyExports(allEvents) {
    const events = allEvents.filter((event) => event.studyEvent);
    validateStudyEvents(events);
    const grouped = new Map();
    for (const event of events) {
        const key = trialKey(event);
        if (!grouped.has(key)) grouped.set(key, []);
        grouped.get(key).push(event);
    }
    const trialRows = [...grouped.values()].map(aggregateTrial);
    const longRows = events.sort((a, b) => eventTime(a) - eventTime(b)).map(longRow);
    return { trialRows, longRows };
}

function argument(name, fallback) {
    const prefix = `--${name}=`;
    const found = process.argv.slice(2).find((value) => value.startsWith(prefix));
    return found ? found.slice(prefix.length) : fallback;
}

function runCli() {
    const input = path.resolve(argument("input", path.join(__dirname, "..", "memory", "data", "artifact_log.jsonl")));
    const outputDir = path.resolve(argument("output-dir", path.join(__dirname, "data", "study-export")));
    const { trialRows, longRows } = buildStudyExports(readJsonLines(input));
    const trialsPath = path.join(outputDir, "trials.csv");
    const eventsPath = path.join(outputDir, "events.csv");
    writeCsv(trialsPath, TRIAL_COLUMNS, trialRows);
    writeCsv(eventsPath, LONG_COLUMNS, longRows);
    console.log(`[study_export] wrote ${trialRows.length} trial row(s) to ${trialsPath}`);
    console.log(`[study_export] wrote ${longRows.length} event row(s) to ${eventsPath}`);
}

if (require.main === module) runCli();

module.exports = {
    TRIAL_COLUMNS,
    LONG_COLUMNS,
    readJsonLines,
    writeCsv,
    buildStudyExports,
    validateStudyEvents,
};
