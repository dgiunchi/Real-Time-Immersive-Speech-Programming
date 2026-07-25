"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");
const { AgentWorkingCache } = require("../cache/agent_working_cache");
const { EventJournal } = require("../cache/event_journal");
const { CacheReconciler } = require("../cache/cache_reconciler");
const { ProposalGate } = require("../cache/proposal_gate");
const { makeCacheEnvelope, toWireFormat, fromWireFormat, STRINGIFY_PAYLOAD_FOR_UNITY } = require("../cache/protocol");
const { checkModePolicy } = require("../orchestrator/mode_policy");
const { rankCandidates, validateLifecycle } = require("../orchestrator/candidate_selector");
const { PersonPolicyStore } = require("../memory/person_policy");
const { ExperienceContextStore } = require("../memory/experience_context");
const { ArtifactLog } = require("../memory/artifact_log");
const { CheckpointStore } = require("../memory/checkpoint_store");
const { ActivityMonitor } = require("../memory/activity_monitor");
const { TRIAL_COLUMNS, LONG_COLUMNS, buildStudyExports, writeCsv } = require("../evaluation/study_export");

const root = path.resolve(__dirname, "..");
let assertions = 0;
function ok(value, message) { assert.ok(value, message); assertions += 1; }
function equal(actual, expected, message) { assert.strictEqual(actual, expected, message); assertions += 1; }

function walk(dir, result = []) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        if (["node_modules", "vendor", "data"].includes(entry.name)) continue;
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) walk(full, result);
        else if (/\.(?:js|mjs)$/.test(entry.name)) result.push(full);
    }
    return result;
}

for (const file of walk(root)) {
    const checked = spawnSync(process.execPath, ["--check", file], { encoding: "utf8" });
    equal(checked.status, 0, `node --check failed for ${path.relative(root, file)}: ${checked.stderr}`);
}

const wire = toWireFormat(makeCacheEnvelope({
    type: "ArtifactProposal",
    sessionId: "wire-test-session",
    correlationId: "corr-wire",
    targetObjectId: "object-1",
    payload: { code: "class Test {}", validationState: "accepted" },
}));
equal(typeof wire.payload, "string", "Unity-bound payload must be a JSON string");
equal(fromWireFormat(wire).payload.validationState, "accepted", "wire payload must round-trip");
for (const legacyType of ["SceneQuery", "SceneDelta", "ArtifactProposal", "ArtifactResult", "AgentUtterance", "AgentPresenceHeartbeat", "UserDecision"]) {
    ok(STRINGIFY_PAYLOAD_FOR_UNITY.has(legacyType), `${legacyType} payload is JsonUtility-safe`);
}
assert.throws(() => makeCacheEnvelope({ type: "SceneDelta", payload: {} }), /sessionId is required/);
assertions += 1;

const workingCache = new AgentWorkingCache();
const journal = new EventJournal({ maxEntriesPerSession: 32 });
const reconciler = new CacheReconciler({ workingCache, journal });
function delta(seq, revision, overrides = {}) {
    return {
        type: "SceneDelta",
        sessionId: "test-session",
        correlationId: `delta-${seq}`,
        stableObjectId: "object-1",
        objectRevision: revision,
        deltaSeq: seq,
        sceneEpoch: "epoch-1",
        snapshotId: "snapshot-1",
        timestamp: Date.now(),
        ttlMs: 10000,
        payload: { tag: "game", region: "lab", state: { revision } },
        ...overrides,
    };
}

equal(reconciler.reconcileDelta(delta(1, 1)).outcome, "accepted", "first delta accepted");
const gap = reconciler.reconcileDelta(delta(3, 3));
equal(gap.recommendedAction, "backfill", "missing delta range requests backfill");
equal(gap.detail.gap.fromSeq, 2, "gap starts at missing sequence");
equal(reconciler.reconcileDelta(delta(2, 2), { isBackfill: true }).outcome, "accepted", "late backfill accepted");
equal(reconciler.reconcileDelta(delta(3, 3), { isBackfill: true }).outcome, "duplicate", "overlapping backfill is idempotent");
equal(workingCache.getByObjectId("object-1").objectRevision, 3, "late backfill cannot regress current state");

const gate = new ProposalGate({ workingCache, reconciler });
const accepted = gate.checkProposal({
    correlationId: "proposal-current",
    targetObjectId: "object-1",
    sceneEpoch: "epoch-1",
    snapshotId: "snapshot-1",
    objectRevision: 3,
    snapshotTakenAt: Date.now(),
    authoringMode: "semi_auto_confirm",
    consentRoute: "explicit_confirmation",
    validationState: "accepted",
});
ok(accepted.accepted, "current validated proposal passes preflight");

const stale = gate.checkProposal({
    correlationId: "proposal-stale",
    targetObjectId: "object-1",
    sceneEpoch: "epoch-1",
    snapshotId: "snapshot-1",
    objectRevision: 1,
    snapshotTakenAt: Date.now() - 60000,
    authoringMode: "automatic",
    consentRoute: "automatic_low_risk",
    validationState: "accepted",
});
ok(!stale.accepted, "stale proposal is rejected");
ok(stale.reasons.some((reason) => reason.includes("objectRevision mismatch")), "revision rejection is explicit");
ok(stale.reasons.some((reason) => reason.includes("snapshot too old")), "age rejection is explicit");

ok(checkModePolicy({ interactionMode: "L1", authoringMode: "automatic", riskScore: 0.1,
    triggerSource: "system_opportunity", reversible: true, localOnly: true }).accepted,
"low-risk reversible L1 may be automatic");
ok(!checkModePolicy({ interactionMode: "L4", authoringMode: "automatic", riskScore: 0.1,
    triggerSource: "explicit_request", reversible: true, localOnly: true }).accepted,
"L4 cannot bypass confirmation");
ok(!checkModePolicy({ interactionMode: "L3", authoringMode: "semi_auto_confirm",
    triggerSource: "clarification", detailResolved: false }).accepted,
"L3 cannot continue before clarification is resolved");
ok(checkModePolicy({ interactionMode: "L2", authoringMode: "automatic", riskScore: 0.1,
    triggerSource: "context", reversible: true, localOnly: true, verificationLevel: 1 }).accepted,
"machine-verifiable low-risk context assistance may use L2");
ok(!checkModePolicy({ interactionMode: "L2", authoringMode: "automatic", riskScore: 0.7,
    triggerSource: "context", reversible: true, localOnly: true, verificationLevel: 1 }).accepted,
"continuous context cannot make high-risk assistance automatic");
ok(!checkModePolicy({ interactionMode: "L2", authoringMode: "automatic", riskScore: 0.1,
    triggerSource: "context", reversible: true, localOnly: true, verificationLevel: 3 }).accepted,
"continuous context cannot automatically execute delayed-verification assistance");

let activityNow = 100000;
const activity = new ActivityMonitor({
    threshold: 1.1,
    windowMs: 5000,
    cooldownMs: 30000,
    now: () => activityNow,
});
const activityOpportunity = activity.observeSceneDelta({
    type: "SceneDelta",
    sessionId: "activity-session",
    timestamp: activityNow,
    payload: {
        focus: { id: "activity-object" },
        sensorEvents: [
            { sensorType: "proximity", targetObjectId: "activity-object", confidence: 1 },
            { sensorType: "gaze", targetObjectId: "activity-object", confidence: 1 },
            { sensorType: "locomotion", confidence: 1, value: { regionId: "workshop", entering: true } },
        ],
    },
});
ok(activityOpportunity && activityOpportunity.triggerSource === "context",
    "combined monitored activity crossing the threshold emits a context trigger");
equal(activityOpportunity.targetObjectId, "activity-object", "activity trigger retains the stable target");
equal(activity.observeSceneDelta({
    sessionId: "activity-session",
    timestamp: activityNow + 1000,
    payload: {
        focus: { id: "activity-object" },
        sensorEvents: [{ sensorType: "collision", targetObjectId: "activity-object", confidence: 1 }],
    },
}), null, "activity cooldown suppresses repeated assistance");
activityNow += 31000;
ok(activity.observeSceneDelta({
    sessionId: "activity-session",
    timestamp: activityNow,
    payload: {
        focus: { id: "activity-object" },
        sensorEvents: [{ sensorType: "collision", targetObjectId: "activity-object", confidence: 1 }],
    },
}), "a later assist-worthy activity can trigger after cooldown");

ok(!validateLifecycle({ operation: "remove" }).accepted, "remove requires an existing artifact");
ok(validateLifecycle({ operation: "remove", existingArtifactId: "artifact-1" }).accepted, "remove is a first-class no-code operation");
ok(!validateLifecycle({ operation: "edit", existingArtifactId: "artifact-1" }).accepted, "edit requires replacement code");
const candidateResult = rankCandidates([
    { candidateId: "safe", operation: "create", code: "class Safe {}", validationState: "accepted", simulationStatus: "simulated", riskScore: 0.1, authoringMode: "semi_auto_confirm", experienceMode: "training" },
    { candidateId: "risky", operation: "create", code: "class Risky {}", validationState: "accepted", simulationStatus: "simulated", riskScore: 0.8, authoringMode: "semi_auto_confirm", experienceMode: "training" },
    { candidateId: "broken", operation: "create", code: "class Broken {}", validationState: "accepted", simulationStatus: "error", riskScore: 0.0, authoringMode: "semi_auto_confirm", experienceMode: "training" },
], { experienceContext: { mode: "training" } });
equal(candidateResult.selected.candidateId, "safe", "ranking selects the lowest-risk verified context-fit candidate");
ok(!candidateResult.ranked.find((candidate) => candidate.candidateId === "broken").ranking.eligible, "failed dry-run candidate cannot be selected");
const entertainmentCandidates = rankCandidates([
    { candidateId: "training-fit", operation: "create", code: "class Training {}", validationState: "accepted", simulationStatus: "simulated", riskScore: 0.1, experienceMode: "training" },
    { candidateId: "entertainment-fit", operation: "create", code: "class Play {}", validationState: "accepted", simulationStatus: "simulated", riskScore: 0.1, experienceMode: "entertainment" },
], { experienceContext: { mode: "entertainment" } });
equal(entertainmentCandidates.selected.candidateId, "entertainment-fit",
    "non-authoring entertainment context changes deterministic candidate ranking");

const testDataDir = path.join(root, "evaluation", "data", "contract-test");
fs.rmSync(testDataDir, { recursive: true, force: true });
fs.mkdirSync(testDataDir, { recursive: true });
const profilePath = path.join(testDataDir, "profiles.json");
const people = new PersonPolicyStore({ filePath: profilePath });
people.setPersistenceConsent({ sessionId: "session-a", personId: "participant-007", consent: true, retentionDays: 30 });
for (let index = 0; index < 3; index += 1) people.recordEvent({ sessionId: "session-a", eventType: "rejected", authoringMode: "automatic" });
const learnedPolicy = people.getPolicy({ sessionId: "session-a" });
ok(learnedPolicy.persistenceConsent, "cross-session learning requires explicit opt-in");
ok(!checkModePolicy({ interactionMode: "L1", authoringMode: "automatic", riskScore: 0.1, triggerSource: "system_opportunity", reversible: true, localOnly: true, userPreference: learnedPolicy }).accepted,
    "learned rejection history can make autonomy stricter");
const reloadedPeople = new PersonPolicyStore({ filePath: profilePath });
ok(reloadedPeople.snapshotConsentedProfiles().length === 1, "consented pseudonymous profile survives restart");
ok(people.resetProfile({ sessionId: "session-a" }).reset, "profile reset/revocation deletes learned state");

const contextPath = path.join(testDataDir, "context.json");
const contexts = new ExperienceContextStore({ filePath: contextPath });
equal(contexts.observeIntent({ sessionId: "session-a", text: "guide me through this safety repair procedure" }).mode, "training", "experience context is inferred from intent");
contexts.set({ sessionId: "session-a", mode: "productivity" });
equal(contexts.observeIntent({ sessionId: "session-a", text: "start a fun game" }).mode, "productivity", "explicit context override wins over inference");
equal(new ExperienceContextStore({ filePath: contextPath }).get("session-a").mode, "productivity", "experience context survives restart");

const evolutionLog = new ArtifactLog({ filePath: path.join(testDataDir, "artifacts.jsonl") });
evolutionLog.append({ eventType: "propose_artifact", operation: "create", targetObjectId: "object-e", artifactId: "artifact-v1", artifactVersion: "1", status: "committed" });
evolutionLog.append({ eventType: "candidate_rejected", operation: "edit", targetObjectId: "object-e", candidateId: "candidate-b", candidateSetId: "set-1", status: "not_selected" });
evolutionLog.append({ eventType: "propose_artifact", operation: "edit", targetObjectId: "object-e", artifactId: "artifact-v2", artifactVersion: "2", supersedesArtifactId: "artifact-v1", status: "committed" });
equal(evolutionLog.evolution({ objectId: "object-e" })[1].candidateId, "candidate-b", "evolution history retains rejected alternatives");
equal(evolutionLog.activeArtifacts()[0].artifactId, "artifact-v2", "evolution history reconstructs current artifact");
const checkpoints = new CheckpointStore({ filePath: path.join(testDataDir, "checkpoint.json") });
checkpoints.save({ artifactLog: evolutionLog, personPolicy: people, experienceContext: contexts, sceneEpoch: "epoch-test" });
const resumed = checkpoints.load({ currentObjectIds: ["different-object"] });
equal(resumed.orphaned.length, 1, "checkpoint explicitly classifies missing object references as orphaned");

const studyLogPath = path.join(testDataDir, "study-artifacts.jsonl");
const studyLog = new ArtifactLog({ filePath: studyLogPath });
assert.throws(() => studyLog.startStudyTrial({ sessionId: "study-session" }), /participantId/,
    "study trial identifiers fail loudly when incomplete");
assertions += 1;
const studyContext = {
    participantId: "participant-001",
    sessionId: "study-session",
    trialId: "trial-01",
    condition: "agenticxr_verification",
    taskId: "task-door-guidance",
    interactionMode: "L4",
    correlationId: "trial-correlation",
};
studyLog.startStudyTrial({ ...studyContext, at: 1000 });
studyLog.appendStudyEvent({ sessionId: studyContext.sessionId, correlationId: "turn-correlation", eventType: "intent_captured", at: 1100 });
studyLog.appendStudyEvent({ sessionId: studyContext.sessionId, correlationId: "turn-correlation", eventType: "agent_status_surfaced", status: "thinking", at: 1125 });
studyLog.appendStudyEvent({ sessionId: studyContext.sessionId, correlationId: "turn-correlation", eventType: "memory_retrieval", durationMs: 4.5, at: 1150 });
studyLog.appendStudyEvent({ sessionId: studyContext.sessionId, correlationId: "turn-correlation", eventType: "interruption", at: 1175 });
studyLog.appendStudyEvent({ sessionId: studyContext.sessionId, correlationId: "turn-correlation", eventType: "resumption", at: 1200 });
for (let rank = 1; rank <= 3; rank += 1) {
    studyLog.appendStudyEvent({
        sessionId: studyContext.sessionId, correlationId: "turn-correlation",
        eventType: rank === 1 ? "candidate_selected" : "candidate_rejected",
        candidateId: `candidate-${rank}`, candidateSetId: "candidate-set-1",
        selectedCandidateRank: rank, selectedCandidateScore: 110 - rank,
        status: rank === 1 ? "selected" : "not_selected", at: 1220 + rank,
    });
}
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "candidate_selection", candidateSetId: "candidate-set-1",
    candidateCount: 3, selectedCandidateId: "candidate-1",
    selectedCandidateRank: 1, selectedCandidateScore: 109, at: 1230,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "simulate_artifact", candidateId: "candidate-1", candidateSetId: "candidate-set-1",
    status: "simulated", verificationOutcome: "apply", verificationDurationMs: 40, at: 1250,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "propose_artifact", candidateId: "candidate-1", candidateSetId: "candidate-set-1",
    targetObjectId: "object-study", status: "pending", at: 1300,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "user_decision:approved", targetObjectId: "object-study", status: "approved", at: 1400,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "artifactresult", targetObjectId: "object-study", artifactId: "artifact-study",
    candidateId: "candidate-1", status: "committed", timestampAgeMs: 8,
    correlationIdValid: true, targetObjectValid: true, commitAttachDurationMs: 12, at: 1500,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "goal_created", goalId: "study-goal", goalIteration: 0,
    goalStatus: "pending", verificationLevel: 1, at: 1510,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "goal_iteration_executed", goalId: "study-goal", goalIteration: 1,
    goalStatus: "running", verificationLevel: 1, at: 1520,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "goal_delayed_evaluation_resolved", goalId: "study-goal",
    verificationLevel: 1, resolutionLatencyMs: 250, at: 1530,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "goal_terminated", goalId: "study-goal", goalIteration: 1,
    goalStatus: "completed", verificationLevel: 1, iterationsToCompletion: 1, at: 1540,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "idle_prediction_triggered", speculative: true, at: 1550,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "speculative_candidate_prepared", candidateId: "future-1",
    status: "prepared", speculative: true, at: 1560,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "speculative_candidate_adopted", candidateId: "future-1",
    status: "selected_for_normal_pipeline", speculative: true, at: 1570,
});
studyLog.endStudyTrial({
    sessionId: studyContext.sessionId, trialId: studyContext.trialId,
    correlationId: "turn-correlation", taskCompletion: true, taskSuccess: true,
    taskQualityScore: 4, taskQualitySignals: { rubricVersion: "v1", behaviorMatched: true }, at: 2000,
});
const studyExports = buildStudyExports(fs.readFileSync(studyLogPath, "utf8").trim().split(/\r?\n/).map(JSON.parse));
equal(studyExports.trialRows.length, 1, "study exporter emits one row per participant task-trial");
equal(studyExports.trialRows[0].participantId, studyContext.participantId, "study exporter joins the pseudonymous participant");
equal(studyExports.trialRows[0].trialId, studyContext.trialId, "study exporter joins events to the correct trial");
equal(studyExports.trialRows[0].immediateAcknowledgementLatencyMs, 25, "acknowledgement latency uses separate timestamps");
equal(studyExports.trialRows[0].validatedExecutionLatencyMs, 400, "validated execution latency uses intent and committed timestamps");
equal(studyExports.trialRows[0].candidatesGenerated, 3, "H4 candidate count is exported");
equal(studyExports.trialRows[0].firstProposalAcceptedWithoutRevision, true, "H4 first acceptance is cleanly derived");
equal(studyExports.trialRows[0].interruptionTotalTimeMs, 25, "interruption/resumption duration is exported");
equal(studyExports.trialRows[0].goalCount, 1, "goal count is exported");
equal(studyExports.trialRows[0].goalIterationsTotal, 1, "goal iterations are exported");
equal(studyExports.trialRows[0].goalDelayedResolutionLatencyMsJson, "[250]", "delayed goal latency is exported");
equal(studyExports.trialRows[0].speculativeCandidatesAdopted, 1, "speculative adoption is exported");
for (const column of TRIAL_COLUMNS) ok(Object.hasOwn(studyExports.trialRows[0], column), `trial export contains required column ${column}`);
for (const column of LONG_COLUMNS) ok(Object.hasOwn(studyExports.longRows[0], column), `long export contains required column ${column}`);
const trialCsvPath = path.join(testDataDir, "trials.csv");
const eventCsvPath = path.join(testDataDir, "events.csv");
writeCsv(trialCsvPath, TRIAL_COLUMNS, studyExports.trialRows);
writeCsv(eventCsvPath, LONG_COLUMNS, studyExports.longRows);
ok(fs.readFileSync(trialCsvPath, "utf8").startsWith(TRIAL_COLUMNS.join(",")), "trial CSV has the stable analysis header");
ok(fs.readFileSync(eventCsvPath, "utf8").startsWith(LONG_COLUMNS.join(",")), "long CSV has the stable event header");

const unityManager = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "CacheExchangeManager.cs"), "utf8");
const unityPublisher = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "CachePublisher.cs"), "utf8");
for (const required of ["CommitAccepted", "CommitRejected", "UserDecision", "RollbackResult", "AgentStatusVisible", "ValidateProposalEnvelope", "BuildBackfillPayload"]) {
    ok(unityManager.includes(required) || unityPublisher.includes(required), `Unity contract contains ${required}`);
}
ok(unityPublisher.includes("PublishCurrentSnapshot();"), "production Unity publisher emits a snapshot");
ok(unityPublisher.includes("PublishStateDelta("), "production Unity publisher scans changed state");
const roslynRuntime = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "Scenes", "Scripts", "TestRoslyn.cs"), "utf8");
for (const deniedCapability of ["system.io", "system.net", "system.diagnostics", "system.reflection",
    "system.runtime.interopservices", "unityengine.networking", "dllimport", "stackalloc",
    "application.quit", "application.openurl", "environment.exit"]) {
    ok(roslynRuntime.includes(`\"${deniedCapability}\"`), `capability policy denies ${deniedCapability}`);
}
ok(roslynRuntime.includes("allowedNamespaces"), "capability policy has an explicit namespace allowlist");

const sttClient = fs.readFileSync(path.join(root, "samples", "services", "speech_to_text", "service.js"), "utf8");
ok(!sttClient.includes("130.136.2.161"), "STT client has no hardcoded lab-server fallback");
ok(sttClient.includes("STT_HTTP_URL is required"), "missing STT configuration fails actionably");
const orchestrator = fs.readFileSync(path.join(root, "orchestrator", "app.js"), "utf8");
ok(orchestrator.includes("AGENTICXR_ANTHROPIC_MAX_ATTEMPTS"), "Anthropic attempts are configurable");
ok(orchestrator.includes("sawMutatingToolCall"), "retry stops after a mutating tool call");
const runtimeGenerator = fs.readFileSync(path.join(root, "samples", "apps", "code_runtime_generator", "app.js"), "utf8");
ok(runtimeGenerator.includes("AGENTICXR_TURN_TIMEOUT_MS"), "authoring turn watchdog is configurable");
ok(runtimeGenerator.includes("AGENTICXR_IDLE_PREDICTION_ENABLED"), "idle prediction requires an explicit opt-in");
ok(runtimeGenerator.includes("continuous_assist_preempt_requested"),
    "explicit user activity preempts continuous assistance");
const continuousMonitor = fs.readFileSync(path.join(root, "orchestrator", "continuous_monitor.js"), "utf8");
ok(continuousMonitor.includes("AGENTICXR_CONTINUOUS_ASSIST_ENABLED"),
    "continuous assistance is an explicit opt-in over always-on monitoring");
ok(continuousMonitor.includes("sendAgentStatus"),
    "continuous assistance surfaces visible status before starting work");
const goalLoopTest = spawnSync(process.execPath, [path.join(root, "tests", "goal_loop_test.js")], { encoding: "utf8" });
equal(goalLoopTest.status, 0, `goal loop tests failed: ${goalLoopTest.stdout}\n${goalLoopTest.stderr}`);
const executionWatchdog = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "GeneratedBehaviourWatchdog.cs"), "utf8");
ok(executionWatchdog.includes("ReportExecutionWatchdog"), "generated behaviour watchdog signals Unity failure");

console.log(`[cache_contract_test] PASS (${assertions} assertions)`);
