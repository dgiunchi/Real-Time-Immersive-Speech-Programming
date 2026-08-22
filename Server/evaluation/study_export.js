"use strict";

// Offline study exporter. It reads the existing append-only temporal ArtifactLog and
// produces analysis-ready CSVs; it never runs on the XR interaction hot path.

const fs = require("fs");
const path = require("path");

const TRIAL_COLUMNS = Object.freeze([
    "protocolId", "methodVersion", "participantId", "sessionId", "trialId", "condition", "conditionAlias",
    "taskId", "interactionMode", "taskVariant", "blockId", "sequenceIndex", "h4Arm", "runMode", "isDryRun",
    "modelId", "modelVersionString", "modelPinHash",
    "trialStartedAtUtc", "trialEndedAtUtc", "taskCompletion", "taskSuccess",
    "taskQualityScore", "taskQualitySignalsJson", "t0", "t1", "taskTimeMs", "trialEndReason",
    "totalTaskTimeMs", "triggerToVisibleChangeMs", "correlationIds",
    "intentCapturedAtUtc", "firstAcknowledgementAtUtc", "firstProposalAtUtc",
    "validatedExecutionAtUtc", "immediateAcknowledgementLatencyMs",
    "proposalLatencyMs", "validatedExecutionLatencyMs",
    "speechCaptureDurationMsJson", "audioDurationMsJson", "transcriptionLatencyMsJson",
    "asrConfidenceJson", "asrWordCountJson", "transcriptionErrorCount", "transcriptionErrorRate",
    "agentStatusTransportLatencyMsJson", "clientStatusRenderDurationMsJson",
    "proposalTransportLatencyMsJson", "clientPreviewRenderDurationMsJson",
    "previewDecisionLatencyMsJson", "commitAttachDurationMsJson", "endToEndTurnLatencyMsJson",
    "generatedArtifactCount", "compileFailureCount", "validationFailureCount",
    "candidateAttemptCount", "dryRunAttemptCount", "dryRunSuccessCount", "dryRunFailureCount",
    "visibleProposalCount", "applicationAttemptCount", "committedApplicationCount",
    "observedErrorOpportunityCount", "observedErrorCount", "analysisExposureDurationMs",
    "runtimeFailureCount", "verificationApplyCount", "verificationClarifyCount",
    "verificationRepairCount", "verificationRejectCount",
    "verificationCandidateDurationsMsJson", "verificationTimeTotalMs",
    "verificationBypassedCount", "previewToCommitTimeMs",
    "verificationLiveMismatchCount", "groundingErrorCount",
    "staleApplicationCount", "staleProposalCount", "invalidCorrelationIdCount",
    "invalidTargetObjectCount", "timestampAgeAtApplicationMsJson",
    "memoryRetrievalLatencyMsJson", "memoryRetrievalLatencyMeanMs",
    "unsafeProposalCount", "blockedUnsafeArtifactCount", "repairAttemptCount",
    "clarificationTurnCount", "confirmationCount", "rejectionCount", "undoCount",
    "rollbackCount", "decisionRouteBreakdownJson", "interruptionCount",
    "resumptionCount", "interruptionTotalTimeMs", "candidateTargetCount",
    "candidatesGenerated", "selectedCandidateId",
    "selectedCandidateRank", "selectedCandidateScore",
    "firstProposalAcceptedWithoutRevision", "agentStatusMessageCount",
    "firstAgentStatusAtUtc", "goalCount", "goalIterationsTotal",
    "goalIterationsToCompletionJson", "goalVerificationLevelsJson",
    "goalEscalationCount", "goalBoundExhaustionCount",
    "goalDelayedResolutionLatencyMsJson", "implicitTriggerCount",
    "predictedEngagementCount", "implicitTriggerToVisibleChangeMsJson",
    "idlePredictionCount", "speculativeCandidatesPrepared",
    "speculativeCandidatesAdopted", "speculativePreparationLeadTimeMsJson",
]);

const LONG_COLUMNS = Object.freeze([
    "protocolId", "methodVersion", "participantId", "sessionId", "trialId", "condition", "conditionAlias",
    "taskId", "interactionMode", "taskVariant", "blockId", "sequenceIndex", "h4Arm",
    "correlationId", "timestampUtc", "eventType", "targetObjectId", "artifactId",
    "candidateId", "candidateSetId", "status", "reasonCode", "durationMs",
    "verificationDurationMs", "commitAttachDurationMs", "timestampAgeMs",
    "correlationIdValid", "targetObjectValid", "selectedCandidateRank",
    "selectedCandidateScore", "studySource", "goalId", "goalIteration",
    "verificationLevel", "goalStatus", "boundExhausted",
    "resolutionLatencyMs", "speculative", "authoringMode", "consentRoute",
    "validationState", "verificationBypassed", "operation", "previousArtifactId", "baselineRevisionRule",
    "audioDurationMs", "transcriptionDurationMs", "asrConfidence", "asrWordCount", "clientRenderDurationMs",
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

function pairedDurations(events, startType, endTypes) {
    const endings = Array.isArray(endTypes) ? endTypes : [endTypes];
    const values = [];
    for (const start of events.filter((event) => event.eventType === startType)) {
        const end = events.find((event) => endings.includes(event.eventType) &&
            event.correlationId === start.correlationId && eventTime(event) >= eventTime(start));
        if (end) values.push(eventTime(end) - eventTime(start));
    }
    return values;
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
    if (!ended) throw new Error(`trial '${started.trialId}' has no study_trial_ended event`);
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
    const preview = first(events, (event) => event.eventType === "proposal_preview_surfaced") ||
        first(events, (event) => event.eventType === "propose_artifact");
    const commit = first(events, (event) =>
        ["artifactresult", "commitaccepted"].includes(String(event.eventType || "").toLowerCase()) &&
        ["committed", "removed"].includes(String(event.status || "").toLowerCase()));
    const memoryDurations = numericList(events.filter((event) => event.eventType === "memory_retrieval"), ["durationMs"]);
    const timestampAges = numericList(events.filter((event) =>
        event.eventType === "proposal_gate_checked" || event.eventType === "artifactresult"), ["timestampAgeMs"]);
    const speechCaptureDurations = pairedDurations(events, "recording_start", "recording_stop");
    const audioDurations = numericList(events.filter((event) => event.eventType === "transcript_ready"), ["audioDurationMs"]);
    const transcriptionDurations = numericList(events.filter((event) => event.eventType === "transcript_ready"), ["transcriptionDurationMs"]);
    const transcriptEvents = events.filter((event) => event.eventType === "transcript_ready");
    const asrConfidences = numericList(transcriptEvents, ["asrConfidence"]);
    const asrWordCounts = numericList(transcriptEvents, ["asrWordCount"]);
    const transcriptionErrorCount = count(events, (event) => event.eventType === "transcription_error");
    if (!transcriptionDurations.length) transcriptionDurations.push(...pairedDurations(events, "recording_stop", "transcript_ready"));
    const statusTransportDurations = pairedDurations(events, "agent_status_sent", "agent_status_surfaced");
    const statusRenderDurations = numericList(events.filter((event) => event.eventType === "agent_status_surfaced"), ["clientRenderDurationMs"]);
    const proposalTransportDurations = pairedDurations(events, "proposal_sent", "proposal_preview_surfaced");
    const previewRenderDurations = numericList(events.filter((event) => event.eventType === "proposal_preview_surfaced"), ["clientRenderDurationMs"]);
    const previewDecisionDurations = pairedDurations(events, "proposal_preview_surfaced",
        ["user_decision:approved", "user_decision:rejected", "user_decision:timeout", "user_decision:undo"]);
    const commitAttachDurations = numericList(events.filter((event) =>
        ["artifactresult", "commitaccepted"].includes(String(event.eventType || "").toLowerCase())), ["commitAttachDurationMs"]);
    const endToEndTurnDurations = [];
    for (const captured of events.filter((event) => event.eventType === "intent_captured" || event.eventType === "turn_started")) {
        const committed = events.find((event) => event.correlationId === captured.correlationId &&
            ["artifactresult", "commitaccepted"].includes(String(event.eventType || "").toLowerCase()) &&
            ["committed", "removed"].includes(String(event.status || "").toLowerCase()) &&
            eventTime(event) >= eventTime(captured));
        if (committed) endToEndTurnDurations.push(eventTime(committed) - eventTime(captured));
    }
    const candidateEvents = events.filter((event) => event.candidateId && event.speculative !== true);
    const uniqueCandidates = new Set(candidateEvents.map((event) => event.candidateId));
    if (ended.taskCompletion === true && !intent) {
        throw new Error(`completed trial '${started.trialId}' has no joined runtime intent event`);
    }
    if (ended.taskCompletion === true && started.condition === "agenticxr_no_verification" &&
        !events.some((event) => event.verificationBypassed === true)) {
        throw new Error(`completed no-verification trial '${started.trialId}' has no bypass evidence`);
    }
    if (ended.taskCompletion === true && Number.isInteger(started.candidateTarget) && started.candidateTarget > 1) {
        const observedCandidateCount = Math.max(
            uniqueCandidates.size,
            ...events.map((event) => Number(event.candidateCount) || 0));
        if (observedCandidateCount < started.candidateTarget) {
            throw new Error(`completed trial '${started.trialId}' expected ${started.candidateTarget} candidates but observed ${observedCandidateCount}`);
        }
    }
    const selection = last(events, (event) => event.eventType === "candidate_selection");
    const selectedCandidateId = selection && (selection.selectedCandidateId ||
        (selection.selected && selection.selected.candidateId)) ||
        (last(candidateEvents, (event) => event.status === "selected") || {}).candidateId || "";
    // Prefer an event that actually carries the surfaced rank (candidate_selected /
    // candidate_selection) - the last mention of the candidate is usually the
    // ArtifactResult, which has no ranking fields.
    const selectedEvent = last(candidateEvents, (event) => event.candidateId === selectedCandidateId &&
            (Number.isFinite(event.selectedCandidateRank) || Number.isFinite(event.rank))) ||
        last(candidateEvents, (event) => event.candidateId === selectedCandidateId) || selection || {};
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
    const firstProposal = first(events, (event) => event.eventType === "propose_artifact");
    const firstProposalAt = firstProposal ? eventTime(firstProposal) : null;
    const taskT0Event = first(events, (event) => event.eventType === "study_trial_t0");
    const taskT1Event = first(events, (event) => event.eventType === "study_trial_t1");
    const taskT0 = eventTime(taskT0Event);
    const taskT1 = eventTime(taskT1Event);
    const trialEndReason = taskT1Event && taskT1Event.arbitrationReason || "";
    if (trialEndReason && !["detector", "declared", "timeout"].includes(trialEndReason)) {
        throw new Error(`trial '${started.trialId}' has invalid trialEndReason '${trialEndReason}'`);
    }
    const l2Trigger = first(events, (event) => event.eventType === "study_l2_trigger");
    const l2VisibleChange = l2Trigger && first(events, (event) =>
        eventTime(event) >= eventTime(l2Trigger) &&
        (event.eventType === "study_l2_visible_change" ||
            (["artifactresult", "commitaccepted"].includes(String(event.eventType || "").toLowerCase()) &&
                ["committed", "removed"].includes(String(event.status || "").toLowerCase()))));
    // Per-consent-route decision breakdown (paper Measures: per-route accept/
    // reject/undo). Route falls back to the authoring mode when the decision
    // envelope carried no explicit consent route.
    const decisionRouteBreakdown = {};
    for (const event of events) {
        const type = String(event.eventType || "");
        const decision = type === "user_decision:approved" || type === "confirmation" ? "approved"
            : type === "user_decision:rejected" || type === "rejection" ? "rejected"
            : type === "user_decision:timeout" ? "timeout"
            : type === "user_decision:undo" || type === "undo" ? "undo" : null;
        if (!decision) continue;
        const route = event.consentRoute || event.authoringMode || "unknown";
        if (!decisionRouteBreakdown[route]) decisionRouteBreakdown[route] = { approved: 0, rejected: 0, timeout: 0, undo: 0 };
        decisionRouteBreakdown[route][decision] += 1;
    }
    const goalIds = new Set(events.map((event) => event.goalId).filter(Boolean));
    const goalIterations = events.filter((event) => event.eventType === "goal_iteration_executed");
    const goalTerminations = events.filter((event) => event.eventType === "goal_terminated");
    const goalVerificationLevels = [...new Set(events.map((event) => event.verificationLevel)
        .filter((value) => Number.isInteger(value)))].sort((a, b) => a - b);
    const delayedResolutionLatencies = numericList(events.filter((event) =>
        event.eventType === "goal_delayed_evaluation_resolved"), ["resolutionLatencyMs"]);
    // Implicit-trigger payoff (L1/L2 arms): time from each context trigger to the
    // first committed visible change sharing that trigger's correlationId.
    const implicitTriggerToVisibleChange = [];
    for (const trigger of events.filter((event) => event.eventType === "activity_assist_triggered")) {
        const visible = events.find((event) =>
            event.correlationId === trigger.correlationId &&
            ["artifactresult", "commitaccepted"].includes(String(event.eventType || "").toLowerCase()) &&
            ["committed", "removed"].includes(String(event.status || "").toLowerCase()) &&
            eventTime(event) >= eventTime(trigger));
        if (visible) implicitTriggerToVisibleChange.push(eventTime(visible) - eventTime(trigger));
    }
    // H2 exposure counters are derived only from the append-only journal. They make
    // both defensible estimands auditable: errors per trial and errors per attempted
    // application. No parallel Unity counter is allowed to become a second authority.
    const dryRunAttempts = events.filter((event) => event.eventType === "simulate_artifact");
    const visibleProposals = events.filter((event) => event.eventType === "proposal_preview_surfaced");
    const applicationAttempts = events.filter((event) => event.eventType === "propose_artifact");
    const committedApplications = events.filter((event) =>
        ["artifactresult", "commitaccepted"].includes(String(event.eventType || "").toLowerCase()) &&
        ["committed", "removed"].includes(String(event.status || "").toLowerCase()));
    const groundingErrorCount = count(events, (event) => event.eventType === "grounding_error" ||
        event.groundingError === true || event.correlationIdValid === false || event.targetObjectValid === false);
    const analysisExposureDurationMs = taskT1Event && Number.isFinite(taskT1Event.totalTaskTimeMs)
        ? taskT1Event.totalTaskTimeMs : Number.isFinite(taskT0) && Number.isFinite(taskT1) ? taskT1 - taskT0 : "";

    return {
        protocolId: started.protocolId || "",
        methodVersion: started.methodVersion || "",
        participantId: started.participantId,
        sessionId: started.sessionId,
        trialId: started.trialId,
        condition: started.condition,
        conditionAlias: started.conditionAlias || "",
        taskId: started.taskId,
        interactionMode: started.interactionMode,
        taskVariant: started.taskVariant || "",
        blockId: started.blockId || "",
        sequenceIndex: started.sequenceIndex ?? "",
        h4Arm: started.h4Arm || "",
        runMode: started.runMode || "",
        isDryRun: started.isDryRun ?? "",
        modelId: started.modelId || "",
        modelVersionString: started.modelVersionString || "",
        modelPinHash: started.modelPinHash || "",
        trialStartedAtUtc: iso(startedAt),
        trialEndedAtUtc: iso(endedAt),
        taskCompletion: ended ? ended.taskCompletion : false,
        taskSuccess: ended ? ended.taskSuccess : "",
        taskQualityScore: ended ? ended.taskQualityScore : "",
        taskQualitySignalsJson: ended && ended.taskQualitySignals != null ? JSON.stringify(ended.taskQualitySignals) : "",
        t0: Number.isFinite(taskT0) ? taskT0 : "",
        t1: Number.isFinite(taskT1) ? taskT1 : "",
        taskTimeMs: analysisExposureDurationMs,
        trialEndReason,
        totalTaskTimeMs: Number.isFinite(startedAt) && Number.isFinite(endedAt) ? endedAt - startedAt : "",
        triggerToVisibleChangeMs: l2Trigger && l2VisibleChange ? eventTime(l2VisibleChange) - eventTime(l2Trigger) : "",
        correlationIds: [...new Set(events.map((event) => event.correlationId).filter(Boolean))].join(";"),
        intentCapturedAtUtc: iso(intentAt),
        firstAcknowledgementAtUtc: iso(ackAt),
        firstProposalAtUtc: iso(firstProposalAt),
        validatedExecutionAtUtc: iso(validatedAt),
        immediateAcknowledgementLatencyMs: Number.isFinite(intentAt) && Number.isFinite(ackAt) ? ackAt - intentAt : "",
        proposalLatencyMs: Number.isFinite(intentAt) && Number.isFinite(firstProposalAt) ? firstProposalAt - intentAt : "",
        validatedExecutionLatencyMs: Number.isFinite(intentAt) && Number.isFinite(validatedAt) ? validatedAt - intentAt : "",
        speechCaptureDurationMsJson: JSON.stringify(speechCaptureDurations),
        audioDurationMsJson: JSON.stringify(audioDurations),
        transcriptionLatencyMsJson: JSON.stringify(transcriptionDurations),
        asrConfidenceJson: JSON.stringify(asrConfidences),
        asrWordCountJson: JSON.stringify(asrWordCounts),
        transcriptionErrorCount,
        transcriptionErrorRate: transcriptEvents.length + transcriptionErrorCount > 0
            ? transcriptionErrorCount / (transcriptEvents.length + transcriptionErrorCount) : "",
        agentStatusTransportLatencyMsJson: JSON.stringify(statusTransportDurations),
        clientStatusRenderDurationMsJson: JSON.stringify(statusRenderDurations),
        proposalTransportLatencyMsJson: JSON.stringify(proposalTransportDurations),
        clientPreviewRenderDurationMsJson: JSON.stringify(previewRenderDurations),
        previewDecisionLatencyMsJson: JSON.stringify(previewDecisionDurations),
        commitAttachDurationMsJson: JSON.stringify(commitAttachDurations),
        endToEndTurnLatencyMsJson: JSON.stringify(endToEndTurnDurations),
        generatedArtifactCount: proposals.length || uniqueCandidates.size,
        candidateAttemptCount: uniqueCandidates.size || proposals.length,
        dryRunAttemptCount: dryRunAttempts.length,
        dryRunSuccessCount: count(dryRunAttempts, (event) => ["simulated", "apply"].includes(
            String(event.status || event.verificationOutcome || "").toLowerCase())),
        dryRunFailureCount: count(dryRunAttempts, (event) => !["simulated", "apply", "skipped_no_verification"].includes(
            String(event.status || event.verificationOutcome || "").toLowerCase())),
        visibleProposalCount: visibleProposals.length,
        applicationAttemptCount: applicationAttempts.length,
        committedApplicationCount: committedApplications.length,
        observedErrorOpportunityCount: applicationAttempts.length,
        observedErrorCount: groundingErrorCount,
        analysisExposureDurationMs,
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
        verificationBypassedCount: count(events, (event) => event.verificationBypassed === true),
        previewToCommitTimeMs: preview && commit ? eventTime(commit) - eventTime(preview) : "",
        verificationLiveMismatchCount: mismatchExplicit + mismatchDerived,
        groundingErrorCount,
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
        decisionRouteBreakdownJson: JSON.stringify(decisionRouteBreakdown),
        interruptionCount: interruptions.length,
        resumptionCount: resumptions.length,
        interruptionTotalTimeMs: interruptionTotal,
        candidateTargetCount: Number.isInteger(started.candidateTarget) ? started.candidateTarget : "",
        // Missing candidate evidence must stay missing. A default of 1 makes a
        // broken H4 join look like a legitimate single-candidate trial.
        candidatesGenerated: uniqueCandidates.size || Number(selection && selection.candidateCount) || "",
        selectedCandidateId,
        selectedCandidateRank: selectedEvent.selectedCandidateRank ?? selectedEvent.rank ?? "",
        selectedCandidateScore: selectedEvent.selectedCandidateScore ??
            (selectedEvent.ranking && selectedEvent.ranking.score) ?? selectedEvent.score ?? "",
        firstProposalAcceptedWithoutRevision: Boolean(decisionApproved && !reviseBeforeApproval),
        agentStatusMessageCount: count(events, (event) => event.eventType === "agent_status_surfaced"),
        firstAgentStatusAtUtc: iso(eventTime(first(events, (event) => event.eventType === "agent_status_surfaced"))),
        goalCount: goalIds.size,
        goalIterationsTotal: goalIterations.length,
        goalIterationsToCompletionJson: JSON.stringify(numericList(goalTerminations, ["iterationsToCompletion"])),
        goalVerificationLevelsJson: JSON.stringify(goalVerificationLevels),
        goalEscalationCount: count(events, (event) =>
            ["goal_escalated", "goal_bound_exhausted"].includes(event.eventType)),
        goalBoundExhaustionCount: count(events, (event) =>
            event.eventType === "goal_bound_exhausted" || event.boundExhausted === true),
        goalDelayedResolutionLatencyMsJson: JSON.stringify(delayedResolutionLatencies),
        implicitTriggerCount: count(events, (event) => event.eventType === "activity_assist_triggered"),
        predictedEngagementCount: count(events, (event) => event.eventType === "predicted_engagement"),
        implicitTriggerToVisibleChangeMsJson: JSON.stringify(implicitTriggerToVisibleChange),
        idlePredictionCount: count(events, (event) => event.eventType === "idle_prediction_triggered"),
        speculativeCandidatesPrepared: count(events, (event) =>
            event.eventType === "speculative_candidate_prepared" && event.status === "prepared"),
        speculativeCandidatesAdopted: count(events, (event) =>
            event.eventType === "speculative_candidate_adopted"),
        speculativePreparationLeadTimeMsJson: JSON.stringify(numericList(
            events.filter((event) => event.eventType === "speculative_candidate_adopted"),
            ["speculativePreparationLeadTimeMs"])),
    };
}

function longRow(event) {
    return {
        protocolId: event.protocolId || "",
        methodVersion: event.methodVersion || "",
        participantId: event.participantId,
        sessionId: event.sessionId,
        trialId: event.trialId,
        condition: event.condition,
        conditionAlias: event.conditionAlias || "",
        taskId: event.taskId,
        interactionMode: event.interactionMode,
        taskVariant: event.taskVariant || "",
        blockId: event.blockId || "",
        sequenceIndex: event.sequenceIndex ?? "",
        h4Arm: event.h4Arm || "",
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
        goalId: event.goalId || "",
        goalIteration: event.goalIteration ?? "",
        verificationLevel: event.verificationLevel ?? "",
        goalStatus: event.goalStatus || "",
        boundExhausted: event.boundExhausted ?? "",
        resolutionLatencyMs: event.resolutionLatencyMs ?? "",
        speculative: event.speculative ?? "",
        authoringMode: event.authoringMode || "",
        consentRoute: event.consentRoute || "",
        validationState: event.validationState || "",
        verificationBypassed: event.verificationBypassed ?? "",
        operation: event.operation || "",
        previousArtifactId: event.previousArtifactId || "",
        baselineRevisionRule: event.baselineRevisionRule || "",
        audioDurationMs: event.audioDurationMs ?? "",
        transcriptionDurationMs: event.transcriptionDurationMs ?? "",
        asrConfidence: event.asrConfidence ?? "",
        asrWordCount: event.asrWordCount ?? "",
        clientRenderDurationMs: event.clientRenderDurationMs ?? "",
    };
}

function buildStudyExports(allEvents, { questionnaireResponses = [] } = {}) {
    const starts = allEvents.filter((event) => event.studyEvent && event.eventType === "study_trial_started");
    const windows = starts.map((started) => {
        const ended = allEvents.find((event) => event.studyEvent &&
            event.eventType === "study_trial_ended" &&
            event.sessionId === started.sessionId && event.trialId === started.trialId);
        return {
            started,
            key: trialKey(started),
            from: eventTime(started),
            to: ended ? eventTime(ended) : Number.POSITIVE_INFINITY,
        };
    });
    const unjoinedByTrial = new Map();
    for (const event of allEvents) {
        if (event.studyEvent || !event.eventType || !event.correlationId) continue;
        const at = eventTime(event);
        if (!Number.isFinite(at)) continue;
        for (const window of windows) {
            if (!Number.isFinite(window.from) || at < window.from || at > window.to) continue;
            if (!unjoinedByTrial.has(window.key)) unjoinedByTrial.set(window.key, []);
            unjoinedByTrial.get(window.key).push(event);
        }
    }

    const events = allEvents.filter((event) => event.studyEvent);
    const grouped = new Map();
    for (const event of events) {
        const key = trialKey(event);
        if (!grouped.has(key)) grouped.set(key, []);
        grouped.get(key).push(event);
    }
    const trialRows = [];
    const acceptedEvents = [];
    const rejectedTrials = [];
    for (const [key, trialEvents] of grouped.entries()) {
        const identity = trialEvents.find((event) => event.eventType === "study_trial_started") || trialEvents[0] || {};
        try {
            validateStudyEvents(trialEvents);
            const t0 = first(trialEvents, (event) => event.eventType === "study_trial_t0");
            const t1 = first(trialEvents, (event) => event.eventType === "study_trial_t1");
            if (t0 && t1) {
                const from = eventTime(t0);
                const to = eventTime(t1);
                const overlap = questionnaireResponses.find((response) => response.trialId === identity.trialId &&
                    response.sessionId === identity.sessionId &&
                    [response.answeredAtUtc, response.respondedAtUtc].some((value) => {
                        const at = Date.parse(value || "");
                        return Number.isFinite(at) && at >= from && at <= to;
                    }));
                if (overlap) throw new Error(`questionnaire response '${overlap.itemId}' falls inside task window [t0,t1]`);
            }
            const unjoined = unjoinedByTrial.get(key) || [];
            if (unjoined.length) {
                const examples = unjoined.slice(0, 5)
                    .map((event) => `${event.eventType}:${event.sessionId || "missing-session"}`)
                    .join(", ");
                throw new Error(`${unjoined.length} runtime event(s) occurred during a study trial without joining it: ${examples}`);
            }
            trialRows.push(aggregateTrial(trialEvents));
            acceptedEvents.push(...trialEvents);
        } catch (error) {
            rejectedTrials.push({
                participantId: identity.participantId || null,
                sessionId: identity.sessionId || null,
                trialId: identity.trialId || null,
                condition: identity.condition || null,
                taskId: identity.taskId || null,
                interactionMode: identity.interactionMode || null,
                eventCount: trialEvents.length,
                reason: error.message,
            });
        }
    }
    const longRows = acceptedEvents.sort((a, b) => eventTime(a) - eventTime(b)).map(longRow);
    return { trialRows, longRows, rejectedTrials };
}

function argument(name, fallback) {
    const prefix = `--${name}=`;
    const found = process.argv.slice(2).find((value) => value.startsWith(prefix));
    return found ? found.slice(prefix.length) : fallback;
}

function runCli() {
    const input = path.resolve(argument("input", path.join(__dirname, "..", "memory", "data", "artifact_log.jsonl")));
    const outputDir = path.resolve(argument("output-dir", path.join(__dirname, "data", "study-export")));
    const { trialRows, longRows, rejectedTrials } = buildStudyExports(readJsonLines(input));
    const trialsPath = path.join(outputDir, "trials.csv");
    const eventsPath = path.join(outputDir, "events.csv");
    const rejectedPath = path.join(outputDir, "rejected_trials.json");
    writeCsv(trialsPath, TRIAL_COLUMNS, trialRows);
    writeCsv(eventsPath, LONG_COLUMNS, longRows);
    fs.mkdirSync(outputDir, { recursive: true });
    fs.writeFileSync(rejectedPath, JSON.stringify(rejectedTrials, null, 2) + "\n");
    console.log(`[study_export] wrote ${trialRows.length} trial row(s) to ${trialsPath}`);
    console.log(`[study_export] wrote ${longRows.length} event row(s) to ${eventsPath}`);
    console.log(`[study_export] wrote ${rejectedTrials.length} rejected trial record(s) to ${rejectedPath}`);
    if (rejectedTrials.length) {
        console.error(`[study_export] ${rejectedTrials.length} trial(s) rejected; valid trials were exported but this run is incomplete`);
        process.exitCode = 2;
    }
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
