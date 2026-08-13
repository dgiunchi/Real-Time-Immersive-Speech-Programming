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
const { checkModePolicy, STUDY_CONDITIONS, SIMULATION_SKIPPED_STATUS, isVerificationBypassed } = require("../orchestrator/mode_policy");
const { rankCandidates, validateLifecycle, scoreCandidate } = require("../orchestrator/candidate_selector");
const { PersonPolicyStore } = require("../memory/person_policy");
const { ExperienceContextStore } = require("../memory/experience_context");
const { ArtifactLog, validateCandidateTarget } = require("../memory/artifact_log");
const { CheckpointStore } = require("../memory/checkpoint_store");
const { ActivityMonitor } = require("../memory/activity_monitor");
const { SharedMemory } = require("../memory");
const { FutureGoalPredictor } = require("../orchestrator/future_goal_predictor");
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

// --- Anticipation: predicted engagement before the trigger (implicit showcase §1) ---
let anticipationNow = 500000;
const anticipating = new ActivityMonitor({
    threshold: 1.1, windowMs: 5000, cooldownMs: 30000,
    anticipationThreshold: 0.6, anticipationCooldownMs: 60000,
    now: () => anticipationNow,
});
const predictions = [];
anticipating.on("predicted_engagement", (prediction) => predictions.push(prediction));
const directedDelta = (at) => ({
    sessionId: "anticipation-session",
    timestamp: at,
    payload: {
        focus: { id: "anchor-lamp" },
        sensorEvents: [
            { sensorType: "gaze", targetObjectId: "anchor-lamp", confidence: 0.8 },
            { sensorType: "proximity", targetObjectId: "anchor-lamp", confidence: 0.9 },
        ],
    },
});
equal(anticipating.observeSceneDelta(directedDelta(anticipationNow)), null,
    "directed attention below the assist threshold does not trigger assistance");
equal(predictions.length, 1, "sustained directed attention predicts engagement before the trigger");
equal(predictions[0].targetObjectId, "anchor-lamp", "prediction names the specific target");
ok(predictions[0].speculative === true && predictions[0].triggerSource === "context",
    "predicted engagement is speculative context, never a commitment");
equal(anticipating.observeSceneDelta({
    sessionId: "anticipation-session",
    timestamp: anticipationNow + 1000,
    payload: {
        focus: { id: "anchor-lamp" },
        sensorEvents: [{ sensorType: "gaze", targetObjectId: "anchor-lamp", confidence: 0.8 }],
    },
}), null, "continued sub-threshold attention still does not trigger assistance");
equal(predictions.length, 1, "anticipation cooldown suppresses repeated predictions");
const assistAfterPrediction = anticipating.observeSceneDelta({
    sessionId: "anticipation-session",
    timestamp: anticipationNow + 2000,
    payload: {
        focus: { id: "anchor-lamp" },
        sensorEvents: [{ sensorType: "collision", targetObjectId: "anchor-lamp", confidence: 1 }],
    },
});
ok(assistAfterPrediction, "a prediction never consumes the window - the real assist trigger still fires");

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

// --- H2 dry-run bypass condition (docs/code-study-readiness-2026-08-11.md §2) ---
ok(isVerificationBypassed(STUDY_CONDITIONS.NO_VERIFICATION), "agenticxr_no_verification bypasses dry-runs");
ok(!isVerificationBypassed(STUDY_CONDITIONS.VERIFICATION), "agenticxr_verification keeps dry-runs");
ok(!isVerificationBypassed(STUDY_CONDITIONS.BASELINE), "baseline keeps dry-runs");
ok(!isVerificationBypassed(null), "no registered trial means no bypass");
const skippedCandidate = { candidateId: "skipped", operation: "create", code: "class Skipped {}",
    validationState: "accepted", simulationStatus: SIMULATION_SKIPPED_STATUS, riskScore: 0.1 };
ok(!scoreCandidate(skippedCandidate, {}).eligible,
    "a skipped dry-run stays ineligible outside the no-verification arm");
ok(scoreCandidate(skippedCandidate, { verificationBypassed: true }).eligible,
    "the no-verification arm accepts the recorded dry-run skip as expected evidence");
ok(!scoreCandidate({ ...skippedCandidate, validationState: "rejected" }, { verificationBypassed: true }).eligible,
    "the bypass never excuses a rejected Validator/Critic verdict");
// mode_policy is identical across conditions - the bypass cannot widen autonomy.
ok(!checkModePolicy({ interactionMode: "L4", authoringMode: "automatic", riskScore: 0.1,
    triggerSource: "explicit_request", reversible: true, localOnly: true }).accepted,
"L4 still cannot bypass confirmation in any study condition");

// --- H4 N=1 vs N>1 per-trial candidate switch (item 3) ---
const singleCandidateResult = rankCandidates([
    { candidateId: "only", operation: "create", code: "class Only {}", validationState: "accepted",
        simulationStatus: "simulated", riskScore: 0.2, authoringMode: "semi_auto_confirm" },
]);
equal(singleCandidateResult.selected.candidateId, "only", "a single-candidate set ranks its lone candidate");
equal(singleCandidateResult.ranked.length, 1, "single-candidate ranking logs exactly one rank");
assert.throws(() => rankCandidates([]), /at least one/, "an empty candidate set still fails");
assertions += 1;
assert.throws(() => validateCandidateTarget(0), /between 1 and 5/, "candidateTarget 0 is rejected");
assertions += 1;
assert.throws(() => validateCandidateTarget(2.5), /between 1 and 5/, "fractional candidateTarget is rejected");
assertions += 1;
equal(validateCandidateTarget(null), null, "candidateTarget stays optional");
equal(validateCandidateTarget("3"), 3, "numeric-string candidateTarget normalizes");

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

// --- Context-derived function choice (implicit showcase §2): the environmental
// context supplied to implicit turns is assembled from Shared XR Memory and
// differs per anchor - no trigger->function table exists anywhere in code. ---
const contextMemory = new SharedMemory({
    artifactLogPath: path.join(testDataDir, "context-artifacts.jsonl"),
    personProfilePath: path.join(testDataDir, "context-profiles.json"),
    experienceContextPath: path.join(testDataDir, "context-experience.json"),
    checkpointPath: path.join(testDataDir, "context-checkpoint.json"),
});
contextMemory.region.ingestLocomotionEvent({
    sourceObjectId: "ctx-session", value: { regionId: "assembly-station", entering: true }, timestamp: 1000,
});
contextMemory.experienceContext.set({ sessionId: "ctx-session", mode: "training" });
const anchorDelta = (id, role, extraComponent) => ({
    timestamp: 1000,
    payload: {
        focus: {
            id, name: role, tag: "game",
            components: [
                { type: "MeshRenderer", fields: {} },
                { type: "AgenticInertAnchor", fields: { anchorRole: role } },
                ...(extraComponent ? [{ type: extraComponent, fields: {} }] : []),
            ],
        },
        halo: [{ id: "bench-1", name: "Bench", tag: "game", type: "static" }],
    },
});
contextMemory.visual.ingestSceneDelta(anchorDelta("anchor-lamp", "unlit-lamp", "Light"));
contextMemory.sceneGraph.ingestSceneDelta(anchorDelta("anchor-lamp", "unlit-lamp", "Light"));
contextMemory.visual.ingestSceneDelta(anchorDelta("anchor-pedestal", "empty-pedestal", null));
const lampContext = contextMemory.describeContext({ sessionId: "ctx-session", targetObjectId: "anchor-lamp" });
const pedestalContext = contextMemory.describeContext({ sessionId: "ctx-session", targetObjectId: "anchor-pedestal" });
ok(lampContext.includes("assembly-station"), "context carries the region");
ok(lampContext.includes("AgenticInertAnchor(unlit-lamp)"), "context carries the anchor role");
ok(lampContext.includes("illuminates"), "context carries derived affordances");
ok(lampContext.includes("near:bench-1"), "context carries nearby objects");
ok(lampContext.includes("training"), "context carries the experience mode");
ok(pedestalContext.includes("empty-pedestal") && lampContext !== pedestalContext,
    "different anchors produce materially different contexts");

// --- Anticipated preparation -> adoption lead time (implicit showcase §1) ---
const speculativeLog = new ArtifactLog({ filePath: path.join(testDataDir, "speculative.jsonl") });
const predictor = new FutureGoalPredictor({ artifactLog: speculativeLog });
const speculativeGoal = {
    goalId: "predicted-goal-1", objective: "prepare local guidance for anchor-lamp in assembly-station",
    sessionId: "ctx-session", correlationId: "predicted-engagement-1", targetObjectId: "anchor-lamp",
    status: "waiting_trigger", currentIteration: 0, verificationLevel: 1, speculative: true,
    sceneEpoch: "epoch-ctx", snapshotId: "snap-ctx", objectRevision: 4, updatedAt: Date.now(),
};
predictor.memory.saveGoal(speculativeGoal, "goal_predicted");
predictor.memory.saveSpeculativeCandidate(speculativeGoal, {
    candidateId: "prepared-1", status: "prepared", validationState: "accepted",
    simulationStatus: "simulated", riskScore: 0.1, preparedArtifact: { code: "class Prepared {}" },
    preparedAt: Date.now() - 500,
});
const adoption = predictor.selectForActualGoal({
    sessionId: "ctx-session", correlationId: "real-trigger-1",
    actualObjective: "prepare local guidance for anchor-lamp",
    targetObjectId: "anchor-lamp", sceneEpoch: "epoch-ctx", snapshotId: "snap-ctx", objectRevision: 4,
});
ok(adoption.selected && adoption.selected.candidateId === "prepared-1",
    "a fresh, scene-pinned prepared candidate is adopted for the real trigger");
ok(adoption.requiresNormalGates === true && adoption.mayCommitAutomatically === false,
    "adoption never bypasses the normal pipeline and consent gates");
const adoptionRecord = speculativeLog.records.findLast((entry) => entry.eventType === "speculative_candidate_adopted");
ok(Number.isFinite(adoptionRecord.speculativePreparationLeadTimeMs) &&
    adoptionRecord.speculativePreparationLeadTimeMs >= 400,
"preparation-to-adoption lead time is recorded on the adoption event");

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
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "predicted_engagement", targetObjectId: "object-study",
    status: "predicted_engagement", speculative: true, at: 1040,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "activity_assist_triggered", targetObjectId: "object-study",
    status: "pending_policy_and_verification", at: 1060,
});
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
    status: "selected_for_normal_pipeline", speculative: true,
    speculativePreparationLeadTimeMs: 300, at: 1570,
});
studyLog.endStudyTrial({
    sessionId: studyContext.sessionId, trialId: studyContext.trialId,
    correlationId: "turn-correlation", taskCompletion: true, taskSuccess: true,
    taskQualityScore: 4, taskQualitySignals: { rubricVersion: "v1", behaviorMatched: true }, at: 2000,
});

// Second arm of the H2 contract: the SAME intent runs under agenticxr_no_verification
// (with the H4 N=1 switch). The pipeline shape is identical - the only differences
// are the recorded dry-run skip and the unverified proposal marking.
const bypassContext = {
    participantId: "participant-001",
    sessionId: "study-session-b",
    trialId: "trial-02",
    condition: "agenticxr_no_verification",
    taskId: "task-door-guidance",
    interactionMode: "L4",
    correlationId: "trial-b-correlation",
    candidateTarget: 1,
};
studyLog.startStudyTrial({ ...bypassContext, at: 3000 });
studyLog.appendStudyEvent({ sessionId: bypassContext.sessionId, correlationId: "turn-b", eventType: "intent_captured", at: 3100 });
studyLog.appendStudyEvent({ sessionId: bypassContext.sessionId, correlationId: "turn-b", eventType: "agent_status_surfaced", status: "thinking", at: 3120 });
studyLog.appendStudyEvent({
    sessionId: bypassContext.sessionId, correlationId: "turn-b",
    eventType: "simulate_artifact", candidateId: "candidate-b1", candidateSetId: "candidate-set-b",
    status: "skipped_no_verification", verificationOutcome: "bypassed",
    verificationBypassed: true, verificationDurationMs: 0, at: 3200,
});
studyLog.appendStudyEvent({
    sessionId: bypassContext.sessionId, correlationId: "turn-b",
    eventType: "candidate_selected", candidateId: "candidate-b1", candidateSetId: "candidate-set-b",
    selectedCandidateRank: 1, selectedCandidateScore: 96, status: "selected",
    verificationBypassed: true, at: 3210,
});
studyLog.appendStudyEvent({
    sessionId: bypassContext.sessionId, correlationId: "turn-b",
    eventType: "candidate_selection", candidateSetId: "candidate-set-b",
    candidateCount: 1, selectedCandidateId: "candidate-b1",
    selectedCandidateRank: 1, selectedCandidateScore: 96, verificationBypassed: true, at: 3220,
});
studyLog.appendStudyEvent({
    sessionId: bypassContext.sessionId, correlationId: "turn-b",
    eventType: "propose_artifact", candidateId: "candidate-b1", candidateSetId: "candidate-set-b",
    targetObjectId: "object-study", status: "pending", validationState: "accepted",
    verificationState: "unverified", verificationBypassed: true,
    authoringMode: "semi_auto_confirm", consentRoute: "explicit_confirmation", at: 3300,
});
studyLog.appendStudyEvent({
    sessionId: bypassContext.sessionId, correlationId: "turn-b",
    eventType: "user_decision:approved", targetObjectId: "object-study", status: "approved",
    authoringMode: "semi_auto_confirm", consentRoute: "explicit_confirmation", at: 3400,
});
studyLog.appendStudyEvent({
    sessionId: bypassContext.sessionId, correlationId: "turn-b",
    eventType: "artifactresult", targetObjectId: "object-study", artifactId: "artifact-study-b",
    candidateId: "candidate-b1", status: "committed", commitAttachDurationMs: 11, at: 3500,
});
studyLog.endStudyTrial({
    sessionId: bypassContext.sessionId, trialId: bypassContext.trialId,
    correlationId: "turn-b", taskCompletion: true, taskSuccess: true,
    taskQualityScore: 4, taskQualitySignals: { rubricVersion: "v1", behaviorMatched: true }, at: 4000,
});

const studyExports = buildStudyExports(fs.readFileSync(studyLogPath, "utf8").trim().split(/\r?\n/).map(JSON.parse));
equal(studyExports.trialRows.length, 2, "study exporter emits one row per participant task-trial");
const verificationRow = studyExports.trialRows.find((row) => row.trialId === studyContext.trialId);
const bypassRow = studyExports.trialRows.find((row) => row.trialId === bypassContext.trialId);
equal(verificationRow.participantId, studyContext.participantId, "study exporter joins the pseudonymous participant");
equal(verificationRow.trialId, studyContext.trialId, "study exporter joins events to the correct trial");
equal(verificationRow.immediateAcknowledgementLatencyMs, 25, "acknowledgement latency uses separate timestamps");
equal(verificationRow.proposalLatencyMs, 200, "proposal latency uses intent and first-proposal timestamps");
equal(verificationRow.validatedExecutionLatencyMs, 400, "validated execution latency uses intent and committed timestamps");
equal(verificationRow.candidatesGenerated, 3, "H4 candidate count is exported");
equal(verificationRow.firstProposalAcceptedWithoutRevision, true, "H4 first acceptance is cleanly derived");
equal(verificationRow.interruptionTotalTimeMs, 25, "interruption/resumption duration is exported");
equal(verificationRow.goalCount, 1, "goal count is exported");
equal(verificationRow.goalIterationsTotal, 1, "goal iterations are exported");
equal(verificationRow.goalDelayedResolutionLatencyMsJson, "[250]", "delayed goal latency is exported");
equal(verificationRow.speculativeCandidatesAdopted, 1, "speculative adoption is exported");
equal(verificationRow.implicitTriggerCount, 1, "implicit context triggers are counted per trial");
equal(verificationRow.predictedEngagementCount, 1, "predicted engagements are counted per trial");
equal(verificationRow.implicitTriggerToVisibleChangeMsJson, "[440]",
    "trigger-to-visible-change latency is derived from the trigger/result envelope pair");
equal(verificationRow.speculativePreparationLeadTimeMsJson, "[300]",
    "anticipated-preparation lead time is exported per trial");
// The H2 contrast: identical pipeline shape, differing only in dry-run evidence and
// validation-state marking, with the condition stamped on every event.
equal(verificationRow.verificationBypassedCount, 0, "the verification arm has no bypassed dry-runs");
ok(verificationRow.verificationApplyCount >= 1, "the verification arm carries dry-run apply evidence");
equal(bypassRow.condition, "agenticxr_no_verification", "the bypass arm is stamped with its condition");
ok(bypassRow.verificationBypassedCount >= 2, "the bypass arm records skips on simulate and proposal");
equal(bypassRow.verificationApplyCount, 0, "the bypass arm has no dry-run apply evidence");
equal(bypassRow.candidateTargetCount, 1, "the H4 N=1 switch is exported per trial");
equal(bypassRow.candidatesGenerated, 1, "the bypass trial generated a single candidate");
equal(bypassRow.selectedCandidateRank, 1, "the lone candidate's surfaced rank is logged");
ok(Number.isFinite(bypassRow.validatedExecutionLatencyMs), "the bypass arm still reaches validated execution");
const routeBreakdown = JSON.parse(bypassRow.decisionRouteBreakdownJson);
equal(routeBreakdown.explicit_confirmation.approved, 1, "per-route accept decisions are exported");
const bypassProposalRow = studyExports.longRows.find((row) =>
    row.trialId === bypassContext.trialId && row.eventType === "propose_artifact");
equal(bypassProposalRow.verificationBypassed, true, "the unverified proposal is marked in the long export");
for (const column of TRIAL_COLUMNS) ok(Object.hasOwn(verificationRow, column), `trial export contains required column ${column}`);
for (const column of LONG_COLUMNS) ok(Object.hasOwn(studyExports.longRows[0], column), `long export contains required column ${column}`);
const trialCsvPath = path.join(testDataDir, "trials.csv");
const eventCsvPath = path.join(testDataDir, "events.csv");
writeCsv(trialCsvPath, TRIAL_COLUMNS, studyExports.trialRows);
writeCsv(eventCsvPath, LONG_COLUMNS, studyExports.longRows);
ok(fs.readFileSync(trialCsvPath, "utf8").startsWith(TRIAL_COLUMNS.join(",")), "trial CSV has the stable analysis header");
ok(fs.readFileSync(eventCsvPath, "utf8").startsWith(LONG_COLUMNS.join(",")), "long CSV has the stable event header");
// A separate process (e.g. the runtime spawning an orchestrator turn) must see the
// per-trial candidate target after reloading the shared log file. trial-02 above
// was ended, so register a fresh open trial for the cross-process check.
const crossProcessLog = new ArtifactLog({ filePath: studyLogPath });
crossProcessLog.startStudyTrial({ ...bypassContext, sessionId: "study-session-c", trialId: "trial-03",
    correlationId: "trial-c-correlation", at: 5000 });
equal(new ArtifactLog({ filePath: studyLogPath }).getStudyContext({ sessionId: "study-session-c" }).candidateTarget, 1,
    "candidateTarget survives the cross-process study-context reload");

const unityManager = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "CacheExchangeManager.cs"), "utf8");
const unityPublisher = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "CachePublisher.cs"), "utf8");
for (const required of ["CommitAccepted", "CommitRejected", "UserDecision", "RollbackResult", "AgentStatusVisible", "ValidateProposalEnvelope", "BuildBackfillPayload"]) {
    ok(unityManager.includes(required) || unityPublisher.includes(required), `Unity contract contains ${required}`);
}
ok(unityPublisher.includes("PublishCurrentSnapshot();"), "production Unity publisher emits a snapshot");
ok(unityPublisher.includes("PublishStateDelta("), "production Unity publisher scans changed state");
ok(unityManager.includes("verificationBypassed"), "Unity honors the H2 dry-run bypass flag without touching consent");
const implicitSensors = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "ImplicitTriggerSensors.cs"), "utf8");
for (const required of ["AgenticRegionVolume", "PublishSensorEvent", "\\\"sensorType\\\":\\\"locomotion\\\"",
    "\\\"sensorType\\\":\\\"proximity\\\"", "\\\"sensorType\\\":\\\"gaze\\\"", "gazeDwellSeconds", "entering"]) {
    ok(implicitSensors.includes(required), `implicit trigger emitters contain ${required}`);
}
const bootstrap = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "AgenticXRBootstrap.cs"), "utf8");
ok(bootstrap.includes("ImplicitTriggerSensors"), "bootstrap installs the implicit trigger emitters");
const inertAnchor = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "AgenticInertAnchor.cs"), "utf8");
for (const required of ["anchorRole", "StableObjectId", "\"game\""]) {
    ok(inertAnchor.includes(required), `inert anchor contains ${required}`);
}
const monitorSource = fs.readFileSync(path.join(root, "orchestrator", "continuous_monitor.js"), "utf8");
ok(monitorSource.includes("predicted_engagement"), "monitor reacts to predicted engagement");
ok(monitorSource.includes("AGENTICXR_STUDY_ALLOW_SPECULATION"), "anticipated preparation respects study suppression");
ok(monitorSource.includes("describeContext"), "implicit objectives are built from Shared XR Memory context");
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
ok(runtimeGenerator.includes("CodeAttachResult") && runtimeGenerator.includes("lastBaselineAttach"),
    "baseline runtime logs Unity's direct-attach acknowledgement as validated execution");
const legacyManager = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "CodeGenerationManager.cs"), "utf8");
ok(legacyManager.includes("CodeAttachResult") && legacyManager.includes("TryCompileAndAttach"),
    "legacy Unity attach path acknowledges success/failure instead of silently applying");
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
