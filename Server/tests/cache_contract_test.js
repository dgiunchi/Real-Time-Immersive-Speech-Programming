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
const { EXPLICIT_TASK_ORDERS, generateParticipantPlan } = require("../study/protocol");
const { InteractionSessionStore } = require("../study/interaction_session_store");

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

const studyPlans = Array.from({ length: 24 }, (_, index) =>
    generateParticipantPlan(`P${String(index + 1).padStart(3, "0")}`));
const interactionContract = JSON.parse(fs.readFileSync(
    path.join(root, "study", "interaction_contract.v1.json"), "utf8"));
equal(interactionContract.methodVersion, "method-draft-2026-08-22",
    "interaction contract is pinned to the same method version as participant plans");
equal(interactionContract.modes.L1.triggerSource, "system_opportunity",
    "L1 contract cannot collapse into the L2 context trigger");
equal(interactionContract.modes.L3.sameCorrelationRequired, true,
    "L3 answer must retain the clarification correlation chain");
ok(interactionContract.modes.L4.decisions.includes("revise"),
    "L4 contract includes a real revise decision");
equal(interactionContract.modes.L5.requiredRevisionCount, 2,
    "L5 contract requires both planned conversational revisions");
equal(interactionContract.baseline.L5MultiTurnRule, "replace-active-study-artifact",
    "baseline L5 replacement semantics are frozen and auditable");

const interactionSessions = new InteractionSessionStore({ now: (() => { let at = 100; return () => ++at; })() });
interactionSessions.begin({ sessionId: "l3-session", mode: "L3", correlationId: "l3-chain", targetObjectId: "marker" });
const l3Question = interactionSessions.recordUtterance({ sessionId: "l3-session", text: "move this marker to the target" });
equal(l3Question.action, "request_clarification", "L3 first ambiguous utterance requests clarification without executing");
const l3Answer = interactionSessions.recordUtterance({ sessionId: "l3-session", text: "the green target" });
equal(l3Answer.action, "execute_resolved_request", "L3 second utterance resolves the request");
equal(l3Answer.correlationId, l3Question.correlationId, "L3 clarification and answer share one correlation chain");
assert.throws(() => interactionSessions.recordUtterance({ sessionId: "l3-session", text: "a forbidden third turn" }),
    /budget exhausted/, "L3 enforces the two-utterance study budget");
assertions += 1;

interactionSessions.begin({ sessionId: "l4-session", mode: "L4", correlationId: "l4-chain", targetObjectId: "door" });
interactionSessions.recordUtterance({ sessionId: "l4-session", text: "make the door open" });
const l4Revise = interactionSessions.recordDecision({ sessionId: "l4-session", decision: "revise" });
equal(l4Revise.action, "await_revision_utterance", "L4 revise does not approve or commit the proposal");
equal(interactionSessions.correlationFor("l4-session"), "l4-chain", "L4 revise preserves proposal correlation");
equal(interactionSessions.sessionForCorrelation("l4-chain"), "l4-session",
    "Unity decisions can resolve the runtime session from the preserved correlation");

interactionSessions.begin({ sessionId: "l5-session", mode: "L5", correlationId: "l5-chain", targetObjectId: "console" });
const l5Initial = interactionSessions.recordUtterance({ sessionId: "l5-session", text: "make a three-step inspection" });
const l5RevisionOne = interactionSessions.recordUtterance({ sessionId: "l5-session", text: "make each step four seconds" });
const l5RevisionTwo = interactionSessions.recordUtterance({ sessionId: "l5-session", text: "reset after three seconds" });
equal(l5Initial.action, "propose_initial", "L5 begins with an initial artifact proposal");
equal(l5RevisionOne.action, "revise_artifact", "L5 second utterance is an artifact revision");
equal(l5RevisionTwo.revisionCount, 2, "L5 records both required revisions");
equal(l5RevisionTwo.correlationId, "l5-chain", "all L5 turns preserve conversational identity");
interactionSessions.recordArtifact({ sessionId: "l5-session", artifactId: "artifact-v1" });
const l5ArtifactEdit = interactionSessions.recordArtifact({ sessionId: "l5-session", artifactId: "artifact-v2" });
equal(l5ArtifactEdit.previousArtifactId, "artifact-v1", "L5 artifact revisions preserve predecessor identity");

interactionSessions.begin({ sessionId: "l1-session", mode: "L1", correlationId: "l1-chain", targetObjectId: "bench-anchor" });
const l1Eligible = interactionSessions.startL1Opportunity({ sessionId: "l1-session", riskScore: 0.2,
    localOnly: true, reversible: true, persistent: false, artifactCount: 1 });
equal(l1Eligible.action, "execute_system_opportunity", "eligible L1 system opportunity may enter execution");
interactionSessions.markL1Applied("l1-session");
interactionSessions.begin({ sessionId: "l1-risky", mode: "L1", correlationId: "l1-risky-chain" });
const l1Declined = interactionSessions.startL1Opportunity({ sessionId: "l1-risky", riskScore: 0.4,
    localOnly: true, reversible: true, persistent: false, artifactCount: 1 });
equal(l1Declined.action, "decline_opportunity", "L1 fails closed when automatic constraints are not met");
equal(interactionSessions.reset("l5-session"), true, "trial reset clears transient conversational state");
equal(interactionSessions.active("l5-session"), null, "no conversational state leaks into the next trial");
for (const plan of studyPlans) {
    equal(plan.methodVersion, "method-draft-2026-08-22", "every plan pins the exact study method version");
    equal(plan.trials.length, 10, "paper protocol generates two arms for each of five tasks");
    equal(plan.trials[0].interactionMode, "L1", "L1 remains first in the fixed implicit block");
    equal(plan.trials[2].interactionMode, "L2", "L2 remains second in the fixed implicit block");
    equal(plan.trials.filter((trial) => trial.condition === "agenticxr_no_verification").length, 2,
        "H2 is isolated to the L1/L2 no-dry-run arms");
    equal(plan.trials.filter((trial) => trial.condition === "baseline").length, 3,
        "H1 baseline appears only once for each explicit L3-L5 task");
    equal(plan.trials.filter((trial) => trial.h4Arm === "single").length, 1,
        "H4 assigns exactly one full AgenticXR single-candidate proposal");
    equal(plan.trials.filter((trial) => trial.h4Arm === "best-of-3").length, 1,
        "H4 assigns exactly one full AgenticXR best-of-three proposal");
    ok(plan.trials.every((trial) => trial.methodVersion === plan.methodVersion),
        "every trial inherits the plan method version");
    for (const mode of ["L1", "L2", "L3", "L4", "L5"]) {
        equal(plan.trials.filter((trial) => trial.interactionMode === mode)
            .map((trial) => trial.taskVariant).sort().join(","), "A,B",
        `${mode} assigns both task variants within each participant`);
    }
}
equal(new Set(studyPlans.slice(0, 6).map((plan) => plan.assignment.explicitTaskOrder.join(""))).size,
    EXPLICIT_TASK_ORDERS.length, "the first six participants cover all explicit task orders");
for (const mode of ["L1", "L2", "L3", "L4", "L5"]) {
    const cells = new Map();
    for (const plan of studyPlans) {
        for (const trial of plan.trials.filter((candidate) => candidate.interactionMode === mode)) {
            const key = `${trial.conditionAlias}:${trial.taskVariant}`;
            cells.set(key, (cells.get(key) || 0) + 1);
        }
    }
    equal(cells.size, 4, `${mode} covers all condition-by-task-variant cells`);
    equal(new Set(cells.values()).size, 1, `${mode} balances condition independently of task variant`);
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
evolutionLog.append({ eventType: "trial_reset", operation: "rollback", status: "rolled_back", reason: "test_reset" });
equal(evolutionLog.activeArtifacts().length, 0, "global trial reset clears reconstructed active artifacts");

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
studyLog.appendStudyEvent({ sessionId: studyContext.sessionId, correlationId: "turn-correlation", eventType: "recording_start", at: 1005 });
studyLog.appendStudyEvent({ sessionId: studyContext.sessionId, correlationId: "turn-correlation", eventType: "recording_stop", at: 1070 });
studyLog.appendStudyEvent({ sessionId: studyContext.sessionId, correlationId: "turn-correlation", eventType: "transcript_ready",
    audioDurationMs: 65, transcriptionDurationMs: 10, transcriptCharacters: 24, at: 1080 });
studyLog.appendStudyEvent({ sessionId: studyContext.sessionId, correlationId: "turn-correlation", eventType: "agent_status_sent", status: "thinking", at: 1110 });
studyLog.appendStudyEvent({ sessionId: studyContext.sessionId, correlationId: "turn-correlation", eventType: "agent_status_surfaced",
    status: "thinking", clientRenderDurationMs: 2, at: 1125 });
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
    eventType: "proposal_sent", candidateId: "candidate-1", at: 1290,
});
studyLog.appendStudyEvent({
    sessionId: studyContext.sessionId, correlationId: "turn-correlation",
    eventType: "proposal_preview_surfaced", candidateId: "candidate-1", clientRenderDurationMs: 3, at: 1310,
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

assert.throws(() => studyLog.startStudyTrial({
    ...studyContext, sessionId: "premature-next-session", trialId: "premature-next-trial",
    correlationId: "premature-next-root", at: 2500,
}), /TrialReset/, "the next condition cannot start while prior generated behavior remains live");
assertions += 1;
studyLog.append({
    eventType: "trial_reset", sessionId: studyContext.sessionId, correlationId: "between-trial-reset",
    operation: "rollback", status: "rolled_back", reason: "technical_test_reset", at: 2600,
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
studyLog.append({
    eventType: "trial_reset", sessionId: bypassContext.sessionId, correlationId: "between-trial-reset-b",
    operation: "rollback", status: "rolled_back", reason: "technical_test_reset", at: 4500,
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
equal(verificationRow.speechCaptureDurationMsJson, "[65]", "push-to-talk capture duration is exported per turn");
equal(verificationRow.transcriptionLatencyMsJson, "[10]", "STT service latency is exported without transcript text");
equal(verificationRow.agentStatusTransportLatencyMsJson, "[15]", "status send-to-visible latency is exported");
equal(verificationRow.clientStatusRenderDurationMsJson, "[2]", "Unity status render duration is exported");
equal(verificationRow.proposalTransportLatencyMsJson, "[20]", "proposal send-to-visible latency is exported");
equal(verificationRow.clientPreviewRenderDurationMsJson, "[3]", "Unity preview render duration is exported");
equal(verificationRow.previewDecisionLatencyMsJson, "[90]", "visible-preview decision latency is exported");
equal(verificationRow.commitAttachDurationMsJson, "[12]", "live compilation and attachment duration is exported");
equal(verificationRow.endToEndTurnLatencyMsJson, "[400]", "intent-to-live-commit latency is exported per turn");
equal(verificationRow.previewToCommitTimeMs, 190, "preview-to-commit begins at confirmed Unity visibility");
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

// Live Unity/Ubiq identities differ from the researcher-selected study session.
// The sole active trial is the authority: runtime aliases must bind audibly and
// all subsequent events must retain the canonical study identity.
const aliasLogPath = path.join(testDataDir, "study-runtime-alias.jsonl");
const aliasLog = new ArtifactLog({ filePath: aliasLogPath });
equal(aliasLog.claimRuntimeSession({ correlationId: "ordinary-non-study-call" }), null,
    "a non-study call may omit runtimeSessionId when no trial is active");
const aliasContext = {
    participantId: "participant-alias",
    sessionId: "operator-session",
    trialId: "alias-trial",
    condition: "agenticxr_no_verification",
    taskId: "alias-task",
    interactionMode: "L2",
    correlationId: "alias-root",
    candidateTarget: 1,
};

// Production starts the long-lived server before the researcher opens a trial
// from a separate CLI process. The very first runtime message may not yet carry
// a turn correlation, so that first claim must refresh from disk and journal a
// durable alias rather than silently dropping the event.
const raceLogPath = path.join(testDataDir, "study-runtime-first-claim-race.jsonl");
const serverStartedFirst = new ArtifactLog({ filePath: raceLogPath });
const researcherProcess = new ArtifactLog({ filePath: raceLogPath });
researcherProcess.startStudyTrial({
    ...aliasContext,
    sessionId: "operator-race-session",
    trialId: "race-trial",
    correlationId: "race-root",
    at: 5900,
});
const firstClaim = serverStartedFirst.claimRuntimeSession({
    runtimeSessionId: "ubiq-peer-race",
    studySource: "ubiq_peer",
});
equal(firstClaim && firstClaim.trialId, "race-trial",
    "the first cross-process runtime claim refreshes the active trial from disk");
const raceBindings = fs.readFileSync(raceLogPath, "utf8").trim().split(/\r?\n/).map(JSON.parse)
    .filter((event) => event.eventType === "study_runtime_session_bound");
equal(raceBindings.length, 1, "the first cross-process claim journals exactly one runtime binding");
ok(Boolean(raceBindings[0].correlationId), "a correlation-free first claim receives a durable binding correlation id");
equal(new ArtifactLog({ filePath: raceLogPath }).getStudyContext({ sessionId: "ubiq-peer-race" }).trialId,
    "race-trial", "the runtime binding survives a fresh process");

aliasLog.startStudyTrial({ ...aliasContext, at: 6000 });
assert.throws(() => aliasLog.claimRuntimeSession({ correlationId: "unjoined-active-call" }),
    /runtimeSessionId must be/, "an active trial rejects an event that cannot be joined to a runtime identity");
assertions += 1;
const claimed = aliasLog.claimRuntimeSession({
    runtimeSessionId: "ubiq-peer-0001",
    correlationId: "alias-turn",
    studySource: "ubiq_peer",
});
equal(claimed.sessionId, aliasContext.sessionId, "a live peer claims the sole active canonical study session");
const aliasedIntent = aliasLog.appendStudyEvent({
    sessionId: "ubiq-peer-0001",
    correlationId: "alias-turn",
    eventType: "intent_captured",
    studySource: "code_runtime_generator",
    at: 6100,
});
equal(aliasedIntent.sessionId, aliasContext.sessionId, "runtime aliases cannot overwrite canonical sessionId");
equal(aliasedIntent.runtimeSessionId, "ubiq-peer-0001", "the original runtime identity remains auditable");
equal(aliasedIntent.condition, aliasContext.condition, "condition activation follows the canonical trial");
aliasLog.appendStudyEvent({
    sessionId: "ubiq-peer-0001",
    correlationId: "alias-turn",
    eventType: "simulate_artifact",
    verificationBypassed: true,
    status: SIMULATION_SKIPPED_STATUS,
    at: 6200,
});
aliasLog.endStudyTrial({
    sessionId: aliasContext.sessionId,
    trialId: aliasContext.trialId,
    correlationId: "alias-turn",
    taskCompletion: true,
    taskSuccess: true,
    at: 6300,
});
const reloadedAliasLog = new ArtifactLog({ filePath: aliasLogPath });
equal(reloadedAliasLog.records.find((entry) => entry.eventType === "intent_captured").sessionId,
    aliasContext.sessionId, "canonical runtime joins survive a process restart");
const aliasExport = buildStudyExports(reloadedAliasLog.records);
equal(aliasExport.trialRows[0].verificationBypassedCount, 1,
    "the live alias path preserves H2 bypass evidence in the exported trial");

const ambiguousLog = new ArtifactLog({ filePath: path.join(testDataDir, "study-runtime-ambiguous.jsonl") });
ambiguousLog.startStudyTrial({ ...aliasContext, sessionId: "ambiguous-a", trialId: "ambiguous-a", correlationId: "ambiguous-root-a" });
assert.throws(() => new ArtifactLog({ filePath: ambiguousLog.filePath }).startStudyTrial({
    ...aliasContext,
    sessionId: "ambiguous-b",
    trialId: "ambiguous-b",
    correlationId: "ambiguous-root-b",
}), /active study trial/, "a second process cannot create overlapping active trials");
assertions += 1;

const unjoinedEvents = [
    { ...aliasContext, eventType: "study_trial_started", studyEvent: true, timestampUtc: new Date(7000).toISOString(), loggedAt: 7000 },
    { eventType: "intent_captured", sessionId: "wrong-live-session", correlationId: "lost-turn", loggedAt: 7100 },
    { ...aliasContext, eventType: "study_trial_ended", studyEvent: true, taskCompletion: false,
        taskSuccess: null, timestampUtc: new Date(7200).toISOString(), loggedAt: 7200 },
];
const unjoinedExport = buildStudyExports(unjoinedEvents);
equal(unjoinedExport.trialRows.length, 0,
    "a trial with unjoined runtime events is quarantined from analysis rows");
equal(unjoinedExport.rejectedTrials.length, 1,
    "the exporter reports a rejected trial instead of silently discarding runtime events");
ok(/runtime event\(s\).*without joining/.test(unjoinedExport.rejectedTrials[0].reason),
    "the rejection preserves the unjoined-runtime failure reason");

const noCandidateLog = new ArtifactLog({ filePath: path.join(testDataDir, "study-no-candidate.jsonl") });
const noCandidateContext = { ...aliasContext, sessionId: "no-candidate-session", trialId: "no-candidate-trial",
    correlationId: "no-candidate-root", condition: "baseline", interactionMode: "L3", candidateTarget: null };
noCandidateLog.startStudyTrial({ ...noCandidateContext, at: 8000 });
noCandidateLog.appendStudyEvent({ sessionId: noCandidateContext.sessionId, correlationId: "no-candidate-turn",
    eventType: "intent_captured", studySource: "baseline_runtime", at: 8100 });
noCandidateLog.endStudyTrial({ sessionId: noCandidateContext.sessionId, trialId: noCandidateContext.trialId,
    correlationId: "no-candidate-turn", taskCompletion: true, taskSuccess: false, at: 8200 });
equal(buildStudyExports(noCandidateLog.records).trialRows[0].candidatesGenerated, "",
    "missing H4 evidence stays missing instead of fabricating one candidate");
const mixedBatchExport = buildStudyExports([...noCandidateLog.records, ...unjoinedEvents]);
equal(mixedBatchExport.trialRows.length, 1,
    "one rejected trial does not poison valid participant exports");
equal(mixedBatchExport.rejectedTrials.length, 1,
    "mixed exports retain a machine-readable rejection report");

// A separate process (e.g. the runtime spawning an orchestrator turn) must see the
// per-trial candidate target after reloading the shared log file. trial-02 above
// was ended, so register a fresh open trial for the cross-process check.
const crossProcessLog = new ArtifactLog({ filePath: studyLogPath });
crossProcessLog.startStudyTrial({ ...bypassContext, sessionId: "study-session-c", trialId: "trial-03",
    correlationId: "trial-c-correlation", at: 5000 });
equal(new ArtifactLog({ filePath: studyLogPath }).getStudyContext({ sessionId: "study-session-c" }).candidateTarget, 1,
    "candidateTarget survives the cross-process study-context reload");

const unityManager = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "CacheExchangeManager.cs"), "utf8");
const consentPanel = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "AgenticXRConsentPanel.cs"), "utf8");
const baselineUnityManager = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "CodeGenerationManager.cs"), "utf8");
const unityPublisher = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "CachePublisher.cs"), "utf8");
const runtimeAppSource = fs.readFileSync(path.join(root, "samples", "apps", "code_runtime_generator", "app.js"), "utf8");
const sttServiceSource = fs.readFileSync(path.join(root, "samples", "services", "speech_to_text", "service.js"), "utf8");
ok(runtimeAppSource.includes("if (DEBUG_TRANSCRIPTS)"),
    "verbatim transcript persistence is gated behind explicit debug consent");
ok(!runtimeAppSource.includes('peerName + " -> Agent:: " + response'),
    "baseline diagnostics do not print verbatim participant speech");
ok(sttServiceSource.includes("characters=${responseText.length}"),
    "STT diagnostics default to transcript length instead of content");
ok(sttServiceSource.includes('emit("transcription_error"'),
    "STT failures produce a structured event for visible recovery and study logging");
ok(runtimeAppSource.includes('payload.status !== "trial_reset"'),
    "the server clears prior artifacts only after Unity confirms a complete reset");
ok(runtimeAppSource.includes("InteractionSessionStore"),
    "speech runtime uses the deterministic multi-turn interaction state");
ok(runtimeAppSource.includes('baselineRevisionRule = "baseline-l5-replace-v1"'),
    "baseline runtime stamps the frozen L5 replacement rule on every revision");
ok(runtimeAppSource.includes('eventType: "clarification_turn"'),
    "L3 surfaces and records a clarification before execution");
ok(consentPanel.includes('"Revise"') && unityManager.includes("RevisePending"),
    "Unity exposes a real revise control without approving the proposal");
ok(baselineUnityManager.includes("activeBaselineProxy.Dispose()"),
    "baseline Unity replaces its prior active generated script after a successful revision");
ok(unityManager.includes("ResetGeneratedStudyBehaviour"),
    "trial reset removes the baseline generated script before acknowledging completion");
const continuousMonitorSource = fs.readFileSync(path.join(root, "orchestrator", "continuous_monitor.js"), "utf8");
ok(continuousMonitorSource.includes('? "system_opportunity" : "context"'),
    "continuous monitor has a distinct executable L1 system-opportunity route");
ok(!runtimeAppSource.includes("intent=${JSON.stringify(intent)}"),
    "agent diagnostics never print verbatim participant intent");
for (const required of ["CommitAccepted", "CommitRejected", "UserDecision", "RollbackResult", "AgentStatusVisible", "ValidateProposalEnvelope", "BuildBackfillPayload"]) {
    ok(unityManager.includes(required) || unityPublisher.includes(required), `Unity contract contains ${required}`);
}
ok(unityPublisher.includes("PublishCurrentSnapshot();"), "production Unity publisher emits a snapshot");
ok(unityPublisher.includes("PublishStateDelta("), "production Unity publisher scans changed state");
ok(unityManager.includes("verificationBypassed"), "Unity honors the H2 dry-run bypass flag without touching consent");
ok(unityManager.includes("IsolateRendererMaterials(clone)"),
    "Verification Space clones cannot mutate materials shared with the live scene");
ok(unityManager.includes("IsolateRendererMaterials(target)"),
    "committed generated behavior receives target-local material instances for reversible reset");
ok(!unityManager.includes("material.color = state.colors"),
    "trial restore never mutates a shared Material asset to recover visual state");
ok(unityManager.includes("originalObjects.Contains"),
    "trial restore removes generated child objects that were absent at capture");
ok(unityManager.includes("originalComponents.Contains"),
    "trial restore removes generated components that were absent at capture");
ok(unityManager.indexOf("File.Delete(CheckpointPath)") < unityManager.indexOf("SendDecision(NewEnvelope(CacheMessageTypes.TrialReset"),
    "Unity acknowledges reset only after checkpoint deletion succeeds");
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
