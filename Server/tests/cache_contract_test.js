"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");
const os = require("os");
const { EventEmitter } = require("events");
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
const { ActivityMonitor, isAuthorableActivityTarget } = require("../memory/activity_monitor");
const { SharedMemory } = require("../memory");
const { FutureGoalPredictor } = require("../orchestrator/future_goal_predictor");
const { TRIAL_COLUMNS, LONG_COLUMNS, buildStudyExports, writeCsv } = require("../evaluation/study_export");
const { EXPLICIT_TASK_ORDERS, generateParticipantPlan, validateParticipantId } = require("../study/protocol");
const { InteractionSessionStore } = require("../study/interaction_session_store");
const { StudySessionMachine, analyzeInteractionContract, expandTransitions } = require("../study/study_session_machine");
const { replayStudyJournal } = require("../study/study_replay_verifier");
const { scanStudyControls } = require("../study/unity_control_scan");
const { validateTaskReadiness, validateSceneTechnicalReadiness } = require("../study/task_readiness");
const { auditDesign } = require("../study/design_audit");
const { validateQuestionnaireDefinition, questionnaireReadiness, scoreStudySpecificResponse,
    itemsForTrial, renderItemPrompt } = require("../study/questionnaire_scoring");
const { deriveImplicitBinaries, scoreL3, scoreL4, scoreL5 } = require("../study/rubric_scoring");
const { createRaterPacket, createImplicitRaterPacket, assertBlindedPacket, cohensKappa } = require("../study/rater_packet");
const { StudySessionRunner, DEFAULT_BREAK_MINIMUM_MS } = require("../study/session_runner");
const { verifyAnalysisPlanLock, EXPECTED_ANALYSIS_PLAN_SHA256 } = require("../study/analysis_plan_lock");
const { pin: modelPin, validateModelPin, runtimeSystemPromptHash } = require("../study/model_pin");
const { auditTranscriptPrivacy } = require("../study/privacy_audit");
const { runSyntheticPilot } = require("../study/pilot_harness");
const { parseSttResponse } = require("../samples/services/speech_to_text/service");
const { preflight } = require("../evaluation/study_operator");

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
const taskReadiness = validateTaskReadiness({ repositoryRoot: path.resolve(root, "..") });
equal(taskReadiness.ok, true, "authored Unity study scene satisfies every fail-closed readiness check");
ok(taskReadiness.checks.some((item) => item.id === "study-scene-authored" && item.ok),
    "readiness report confirms the dedicated Unity-authored study scene");
ok(taskReadiness.checks.filter((item) => item.id.startsWith("task-objects-")).every((item) => item.ok),
    "every L1-L5 mode has explicit A/B object contracts in the authored scene");
ok(taskReadiness.checks.filter((item) => item.id.startsWith("technical-")).every((item) => item.ok),
    "technical readiness proves the generated scene has real XR, interaction, and safe compiler routes");
const xrPlayerCheck = taskReadiness.checks.find((item) => item.id === "technical-real-xr-player-prefab");
const brokenXrScene = fs.readFileSync(taskReadiness.scenePath, "utf8").replaceAll(
    xrPlayerCheck.detail, "00000000000000000000000000000000");
equal(validateSceneTechnicalReadiness(brokenXrScene, path.resolve(root, "..")).find(
    (item) => item.id === "technical-real-xr-player-prefab").ok, false,
    "technical readiness has been observed refusing a scene whose canonical XR rig reference is broken");
const taskManifest = JSON.parse(fs.readFileSync(path.join(root, "study", "task_manifest.v1.json"), "utf8"));
const taskObjectIds = taskManifest.tasks.flatMap((task) =>
    taskManifest.variants.flatMap((variant) => task.variants[variant]));
equal(taskObjectIds.length, 68, "the complete ten-variant manifest contains all 68 authored object identifiers");
equal(new Set(taskObjectIds).size, taskObjectIds.length, "every manifest object identifier is globally unique");
const l3Manifest = taskManifest.tasks.find((task) => task.taskId === "L3-clarify");
for (const variant of taskManifest.variants) {
    equal(l3Manifest.variants[variant].length, 6, `L3 ${variant} contains root, marker, button, and three pads`);
    ok(l3Manifest.variants[variant].includes(`study-l3-${variant.toLowerCase()}-pad-3`),
        `L3 ${variant} explicitly contains pad 3`);
}
const studySceneSource = fs.readFileSync(taskReadiness.scenePath, "utf8");
for (const id of taskObjectIds) {
    const escaped = id.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    equal((studySceneSource.match(new RegExp(`value: ${escaped}(?:\\r?\\n)`, "g")) || []).length, 1,
        `${id} is assigned exactly once to StableObjectId.value`);
}
const questionnaireSchema = JSON.parse(fs.readFileSync(path.join(root, "study", "questionnaires.v1.json"), "utf8"));
equal(questionnaireSchema.studySpecificItemSlots.length, 12, "all twelve study-specific item slots are drafted");
const appropriatenessBipolar = questionnaireSchema.studySpecificItemSlots.find((item) => item.itemId === "appropriatenessBipolar");
ok(!appropriatenessBipolar.wording.toLowerCase().includes("how much"),
    "H3 appropriateness primary does not ask an ambiguous quantity question");
equal(appropriatenessBipolar.wording, "In this task, the amount the system did on its own was:",
    "H3 appropriateness stem is the investigator-specified anchor-completion fragment");
equal(JSON.stringify(appropriatenessBipolar.anchors.map((anchor) => anchor.label)),
    JSON.stringify(["Far too little", "About right", "Far too much"]),
    "H3 appropriateness anchors remain the declared far-too-little/about-right/far-too-much triple");
for (const instrument of questionnaireSchema.validatedInstruments) {
    equal(instrument.items.length, 0, `${instrument.instrumentId} contains no unlicensed item wording`);
    equal(instrument.approvalStatus, "pending", `${instrument.instrumentId} remains pending`);
    equal(instrument.timing, "at-break-and-end", `${instrument.instrumentId} is administered at breaks and end`);
    ok(typeof instrument.timingRationale === "string" && instrument.timingRationale.includes("ten headset doff/re-don cycles"),
        `${instrument.instrumentId} records the timing tradeoff and limitation`);
}
const questionnaireDefinition = validateQuestionnaireDefinition(questionnaireSchema);
equal(questionnaireDefinition.ok, true, "draft questionnaire definition satisfies structural and copyright guards");
const invalidApprovalSchema = JSON.parse(JSON.stringify(questionnaireSchema));
invalidApprovalSchema.studySpecificItemSlots[0].approvalStatus = "approved";
equal(validateQuestionnaireDefinition(invalidApprovalSchema).ok, false,
    "a study-specific approval without approvedBy and approvedAtUtc fails validation");
equal(questionnaireReadiness({ humanSession: true, schema: questionnaireSchema }).ok, false,
    "human session readiness fails closed while study and validated wording is unapproved");
equal(questionnaireReadiness({ humanSession: false, schema: questionnaireSchema }).ok, true,
    "reserved researcher dry-runs may use visibly drafted study-specific wording");
const reverseItems = questionnaireSchema.studySpecificItemSlots.filter((item) => item.reverseKeyed).map((item) => item.itemId).sort();
equal(JSON.stringify(reverseItems), JSON.stringify(["intrusiveness", "perceivedLatency"]),
    "only intrusiveness and perceivedLatency are reverse-keyed");
equal(scoreStudySpecificResponse("intrusiveness", 2, questionnaireSchema).scoredResponse, 6,
    "intrusiveness is reversed exactly once at scoring");
equal(scoreStudySpecificResponse("perceivedLatency", 7, questionnaireSchema).scoredResponse, 1,
    "perceivedLatency is reversed exactly once at scoring");
equal(scoreStudySpecificResponse("control", 2, questionnaireSchema).scoredResponse, 2,
    "non-reverse item scoring leaves the raw value unchanged");
const l1Battery = itemsForTrial({ interactionMode: "L1", condition: "agenticxr_verification" }, questionnaireSchema);
equal(l1Battery[0].itemId, "dryRunManipulationCheck", "the L1/L2 manipulation check is always presented first");
equal(l1Battery.at(-1).itemId, "validatedExecutionConfidence", "validated execution confidence follows the fixed core battery");
const immediateOne = renderItemPrompt("immediateProposalPerceivedLatency", { candidateTarget: 1 }, questionnaireSchema);
const immediateThree = renderItemPrompt("immediateProposalPerceivedLatency", { candidateTarget: 3 }, questionnaireSchema);
equal(immediateOne, immediateThree, "the rendered immediate proposal item is byte-identical for N=1 and N=3");
ok(!/candidate|option 1 of 3|best-of-3/i.test(immediateOne), "the immediate proposal item does not reveal the H4 arm");
const analysisPlan = JSON.parse(fs.readFileSync(path.join(root, "study", "analysis_plan.v1.json"), "utf8"));
ok(analysisPlan.predeclaredRules.some((rule) => rule.ruleId === "H2-dry-run-manipulation-check" &&
    rule.decision.includes("log-derived H2 measures carry H2") && rule.decision.includes("exploratory")),
    "the at-chance manipulation-check rule is predeclared before data collection");

const rubricSchema = JSON.parse(fs.readFileSync(path.join(root, "study", "rubrics.v1.json"), "utf8"));
equal(rubricSchema.rubricVersion, "rubric-v1", "rubric version is frozen to rubric-v1");
equal(rubricSchema.approved, false, "rubrics remain investigator-unapproved");
equal(rubricSchema.qualityScale, "0-2", "rubric quality scale is 0-2");
for (const taskId of ["L1-proactive", "L2-context"]) {
    const taskRubric = rubricSchema.tasks.find((task) => task.taskId === taskId);
    equal(taskRubric.successCriteria.length, 4, `${taskId} has all four D8 binaries`);
    equal(JSON.stringify(taskRubric.successCriteria.map((criterion) => criterion.source)),
        JSON.stringify(["log-derived", "log-derived", "log-derived", "blind-rater"]),
        `${taskId} derives binaries 1-3 from logs and reserves binary 4 for a blind rater`);
}
const implicitScore = deriveImplicitBinaries({ targetInCurrentTaskRegion: true, stableObjectIdValid: true,
    local: true, reversible: true, persistent: false, artifactCount: 1, riskScore: 0.2,
    visibleChangeAt: 10, regionExitAt: 12, subtaskCompletedAt: 15 }, true);
equal(implicitScore.taskSuccess, true, "implicit task success requires all four binaries");
equal(deriveImplicitBinaries({ ...implicitScore, targetInCurrentTaskRegion: false }, true).taskSuccess, false,
    "a failed log-derived implicit binary prevents task success");
// L3 is the ball-above-the-open-hand clarification task: success requires the
// clarification to have been asked, not merely a correctly placed ball.
equal(scoreL3({ clarificationAsked: true, statedHeightMeters: 0.3, spawnedHeightMeters: 0.3,
    ballSpawned: true, spawnedAboveOpenHand: true, handRaised: true,
    repairInitiator: "system", qualityScore: 2 }).taskSuccess,
    true, "L3 success combines the asked clarification with a correctly placed ball");
equal(scoreL3({ clarificationAsked: false, statedHeightMeters: 0.3, spawnedHeightMeters: 0.3,
    ballSpawned: true, spawnedAboveOpenHand: true, handRaised: true,
    repairInitiator: "none", qualityScore: 2 }).groundingFailure,
    true, "L3 records a guessed height as a grounding failure even when the ball lands correctly");
// L4 is the persistent proximity beacon: the trial-local door is retired, so
// success now requires firing on approach and surviving a reset via memory.
const baselineL4 = scoreL4({ beaconFiresOnApproach: true, survivesSceneReset: true,
    reattachedFromMemory: true, dryRunEvidenceShown: false,
    consentGatePresented: false, consentGateOutcome: "none", qualityScore: 2 });
equal(baselineL4.taskSuccess, true, "L4 baseline success does not normalize the intentionally absent consent gate");
const partialL5 = scoreL5({ slowerStepsRevision: 2, resetAfterFinishRevision: 0,
    sequenceRuns: true, priorRequirementRestatementCount: 1 });
equal(partialL5.taskSuccess, false, "L5 keeps independently scored revisions distinct when one fails");
equal(partialL5.slowerStepsRevision, 2, "L5 preserves the slower-steps score independently");
equal(partialL5.resetAfterFinishRevision, 0, "L5 preserves the reset-after-finish score independently");

const raterTemp = fs.mkdtempSync(path.join(os.tmpdir(), "agenticxr-rater-"));
const raterMapPath = path.join(raterTemp, "private", "mapping.jsonl");
const blindedPacket = createRaterPacket({ trial: { participantId: "P001", trialId: "T01",
    taskId: "L1-proactive", taskVariant: "A", condition: "baseline", candidateTarget: 3, h4Arm: "best-of-3" },
material: { observableContext: "tool beside tray", condition: "full", hiddenNote: "noDryRun arm" },
mappingFilePath: raterMapPath, randomBytes: (size) => Buffer.alloc(size, 7) });
equal(assertBlindedPacket(blindedPacket), true, "assembled rater packet passes the condition-token assertion");
ok(/^RC-[0-9a-f]{32}$/.test(blindedPacket.codingId), "rater coding ID is opaque random material, not a participant hash");
ok(!JSON.stringify(blindedPacket).includes("P001") && !JSON.stringify(blindedPacket).includes("T01"),
    "rater packet strips participant and trial identities");
const privateMap = JSON.parse(fs.readFileSync(raterMapPath, "utf8").trim());
equal(privateMap.participantId, "P001", "separate private mapping retains participant identity for later joining");
const implicitRater = createImplicitRaterPacket({ trial: { participantId: "P002", trialId: "T02",
    taskId: "L2-context", taskVariant: "B" }, evidence: { targetInCurrentTaskRegion: true,
    stableObjectIdValid: true, local: true, reversible: true, persistent: false, artifactCount: 1,
    riskScore: 0.1, visibleChangeAt: 5, regionExitAt: 8, subtaskCompletedAt: 9 },
observableMaterial: { context: "part beside matching socket", responseSummary: "local visible change" },
mappingFilePath: raterMapPath, randomBytes: (size) => Buffer.alloc(size, 8) });
equal(JSON.stringify(implicitRater.prefilledLogDerived), JSON.stringify({ grounded: true, inEnvelope: true, timely: true }),
    "implicit rater preparation pre-fills binaries 1-3 from journal evidence");
equal(implicitRater.raterField, "contextuallyAdmissible", "implicit rater is asked to code only binary 4");
ok(!JSON.stringify(implicitRater.packet).includes("riskScore") && !JSON.stringify(implicitRater.packet).includes("artifactCount"),
    "blind rater sees observable material, not the log-derived scoring fields");
fs.rmSync(raterTemp, { recursive: true, force: true });
const kappaReport = cohensKappa([1, 1, 0, 0], [1, 0, 0, 0]);
equal(kappaReport.kappa, 0.5, "Cohen's kappa uses observed and chance agreement from independent ratings");
equal(kappaReport.reliability, "unreliable-do-not-adjudicate", "kappa below 0.6 is reported as unreliable without adjudication");

const taskCards = JSON.parse(fs.readFileSync(path.join(root, "study", "task_cards.v1.json"), "utf8"));
equal(taskCards.cards.length, 10, "one condition-independent task card exists for every task-variant pair");
equal(new Set(taskCards.cards.map((card) => `${card.taskId}|${card.taskVariant}`)).size, 10,
    "task cards cover each task-variant pair exactly once");
ok(taskCards.cards.every((card) => !("condition" in card) && !("conditionAlias" in card)),
    "task cards contain no condition-specific branch");
const displayedCardText = taskCards.cards.flatMap((card) => [card.goal, card.scriptedFirstUtterance,
    card.goalCardDetail, ...(card.revisionOrder || [])]).filter(Boolean).join("\n");
ok(!/interaction mode|agenticxr|baseline|noDryRun|best-of-3|ask the system|tell the system/i.test(displayedCardText),
    "task cards name goal states and never reveal mechanism, mode, or condition");
const l3Cards = taskCards.cards.filter((card) => card.taskId === "L3-clarify");
equal(l3Cards[0].scriptedFirstUtterance, l3Cards[1].scriptedFirstUtterance,
    "L3 scripted first utterance is byte-identical across variants and therefore across arms");
ok(l3Cards.every((card) => card.maximumFurtherUtterances === 1), "L3 allows exactly one further self-initiated utterance");
ok(/Never signal that a result is wrong and never coach/.test(taskCards.experimenterInstruction),
    "task-card protocol forbids experimenter correction and coaching");
const l5Cards = taskCards.cards.filter((card) => card.taskId === "L5-converse");
ok(l5Cards.every((card) => card.revisionOrder.length === 2 && /slower/.test(card.revisionOrder[0]) &&
    /reset after/.test(card.revisionOrder[1])), "L5 cards fix the two revisions in the declared order");

let runnerNow = 1000;
const runnerJournal = new EventJournal();
const runnerPlan = generateParticipantPlan("P901");
const runner = new StudySessionRunner({ participantId: "P901", plan: runnerPlan, journal: runnerJournal,
    now: () => runnerNow, breakMinimumMs: DEFAULT_BREAK_MINIMUM_MS });
runner.completeConsentAndDemographics();
assert.throws(() => runner.startNextTrial(), /not ready|both undo and reject/, "trial 1 cannot start before training criterion");
assertions += 1;
runner.recordTrainingCriterion("undo");
equal(runner.phase, "training", "one training criterion cannot release the gate");
runner.recordTrainingCriterion("reject");
equal(runner.phase, "ready_for_trial", "both demonstrated training criteria release the gate");
const firstAssigned = runner.startNextTrial();
equal(JSON.stringify(firstAssigned), JSON.stringify(runnerPlan.trials[0]), "runner dispatches the generated trial assignment verbatim");
const operatorView = runner.operatorDisplay();
ok(!/condition|baseline|full|nodryrun|best-of-3/i.test(JSON.stringify(operatorView)),
    "operator display exposes task operations but never condition or H4 arm");
runner.transitionInteraction("opportunity_detected", "opportunity_detected");
equal(runner.interactionState.state, "opportunity_detected", "runner sends interaction transitions through StudySessionMachine");
runner.markTaskCardDismissed();
runner.markTaskT0();
runnerNow += 1000;
runner.beginQuestionnairePause();
runnerNow += 2500;
runner.endQuestionnairePause();
runnerNow += 500;
runner.markTaskT1("detector");
equal(runner.taskClock.totalTaskTimeMs, 1500, "task time excludes the in-flight questionnaire pause");
runner.completeInVrBattery();
for (let completed = 1; completed < 4; completed++) {
    runner.startNextTrial();
    runner.markTaskCardDismissed();
    runner.markTaskT0();
    runnerNow += 100;
    runner.markTaskT1("declared");
    runner.completeInVrBattery();
}
equal(runner.phase, "break", "runner enforces the first break after trial 4");
runner.recordDesktopBatteryComplete();
assert.throws(() => runner.advanceAfterBreak(), /remaining/, "runner cannot advance before the five-minute break minimum");
assertions += 1;
runnerNow += DEFAULT_BREAK_MINIMUM_MS;
runner.advanceAfterBreak();
equal(runner.phase, "ready_for_trial", "runner advances only after both desktop battery and minimum break time");
const resumedRunner = StudySessionRunner.resume({ participantId: "P901", journal: runnerJournal,
    now: () => runnerNow, breakMinimumMs: DEFAULT_BREAK_MINIMUM_MS });
equal(resumedRunner.completedTrialCount, 4, "resume restores the exact completed-trial position");
equal(resumedRunner.phase, "ready_for_trial", "resume restores the exact orchestration phase");
const tamperedPlanRecord = runnerJournal.backfill("P901-study-runner", 0).find((record) =>
    record.eventType === "study_plan_journalled");
tamperedPlanRecord.data.plan.trials[0].taskVariant = tamperedPlanRecord.data.plan.trials[0].taskVariant === "A" ? "B" : "A";
assert.throws(() => StudySessionRunner.resume({ participantId: "P901", journal: runnerJournal }),
    /journalled plan differs/, "resume fails closed on any journalled assignment mismatch");
assertions += 1;
equal(interactionContract.modes.L2.triggerSource, "context",
    "L2 uses the context trigger required by the mode policy");
ok(interactionContract.modes.L2.states.includes("region_entered"),
    "L2 declares region_entered as a distinct latency-start state");
const designAudit = auditDesign({ participantCount: 24 });
equal(designAudit.ok, true, "target-sample design invariants pass before study startup");
equal(designAudit.checks.length, 12, "design audit reports every required invariant separately");
ok(designAudit.checks.every((check) => check.ok), "every target-sample design invariant passes");

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
const l5ArtifactEdit = interactionSessions.recordArtifact({ sessionId: "l5-session", artifactId: "artifact-v2",
    previousArtifactId: "artifact-v1" });
equal(l5ArtifactEdit.previousArtifactId, "artifact-v1", "L5 artifact revisions preserve predecessor identity");
assert.throws(() => interactionSessions.recordArtifact({ sessionId: "l5-session", artifactId: "artifact-v3",
    previousArtifactId: "wrong-artifact" }), /identify the previous artifact/,
"L5 rejects an artifact revision that breaks artifact identity");
assertions += 1;

const graphAnalysis = analyzeInteractionContract(interactionContract);
equal(graphAnalysis.ok, true, "declared study graph has no unreachable, dead-end, nonterminating, or gate-bypass paths");
ok(Object.values(graphAnalysis.modes).every((mode) => mode.requiredGateBypasses.length === 0),
    "approved is unreachable without awaiting_decision in every approval-gated mode");
const strictMachine = new StudySessionMachine({ contract: interactionContract });
const strictL4 = strictMachine.create({ sessionId: "strict-l4", mode: "L4", correlationId: "strict-chain" });
assert.throws(() => strictMachine.transitionState(strictL4, "approved", "approve"), /undeclared/,
    "the session machine throws on an undeclared direct approval transition");
assertions += 1;
strictMachine.transitionState(strictL4, "proposing", "request_received");
strictMachine.transitionState(strictL4, "awaiting_decision", "proposal_previewed");
strictMachine.transitionState(strictL4, "approved", "approve");
equal(strictL4.state, "approved", "L4 approval is accepted only after the preview decision gate");
equal(strictMachine.assertCommitAllowed(strictL4), true, "approved L4 state permits commit");

function completeJournalFor(plan) {
    const events = [];
    let at = 1000;
    for (const trial of plan.trials) {
        const correlationId = `${trial.trialId}-journal`;
        events.push({ ...trial, eventType: "study_trial_started", correlationId, studyEvent: true, at: ++at });
        events.push({ ...trial, eventType: "study_trial_ended", correlationId, studyEvent: true,
            taskCompletion: true, at: ++at });
    }
    return events;
}
const replayPlan = generateParticipantPlan("P024");
const replayEvents = completeJournalFor(replayPlan);
const replayReport = replayStudyJournal({ events: replayEvents, plans: replayPlan, contract: interactionContract });
equal(replayReport.ok, true, "journal replay accepts a complete plan-faithful session");
equal(replayReport.trialCount, 10, "journal replay reconciles every planned trial");
equal(replayReport.coverageComplete, false, "coverage reports unexercised declared transitions instead of silently passing");
ok(Object.values(replayReport.coverage).some((mode) => mode.unvisitedTransitions.length > 0),
    "unvisited transitions are explicitly classified as untested");
const coverageEvents = [];
let coverageAt = 5000;
let coverageOrdinal = 0;
for (const [mode, definition] of Object.entries(interactionContract.modes)) {
    for (const transition of expandTransitions(definition)) {
        coverageOrdinal += 1;
        const identity = {
            participantId: `coverage-${coverageOrdinal}`, sessionId: `coverage-session-${coverageOrdinal}`,
            trialId: `coverage-trial-${coverageOrdinal}`, condition: "coverage",
            taskId: `coverage-${mode}`, interactionMode: mode,
            correlationId: `coverage-chain-${coverageOrdinal}`, studyEvent: true,
        };
        coverageEvents.push({ ...identity, eventType: "study_trial_started", at: ++coverageAt });
        coverageEvents.push({ ...identity, eventType: "interaction_state_transition",
            fromState: transition.from, toState: transition.to, transitionEvent: transition.event, at: ++coverageAt });
        coverageEvents.push({ ...identity, eventType: "study_trial_ended", taskCompletion: false, at: ++coverageAt });
    }
}
const completeCoverage = replayStudyJournal({ events: coverageEvents, plans: [], contract: interactionContract });
equal(completeCoverage.ok, true, "declared-transition corpus is scientifically well formed");
equal(completeCoverage.coverageComplete, true, "every declared state and transition is exercised by the automated corpus");
ok(Object.values(completeCoverage.coverage).every((mode) => mode.unvisitedStates.length === 0),
    "coverage corpus leaves no declared state dead or untested");
ok(Object.values(completeCoverage.coverage).every((mode) => mode.unvisitedTransitions.length === 0),
    "coverage corpus leaves no declared transition untested");
const changedConditionEvents = replayEvents.map((event) => ({ ...event }));
changedConditionEvents.splice(1, 0, { ...changedConditionEvents[0], eventType: "measured_latency",
    condition: "wrong-condition", at: changedConditionEvents[0].at + 0.5 });
const changedConditionReport = replayStudyJournal({ events: changedConditionEvents, plans: replayPlan,
    contract: interactionContract });
equal(changedConditionReport.ok, false, "journal replay rejects condition changes within a trial");
ok(changedConditionReport.findings.some((finding) => finding.id === "condition-changed"),
    "condition corruption is reported as a scientific-state finding");
const undeclaredTransitionEvents = replayEvents.map((event) => ({ ...event }));
undeclaredTransitionEvents.splice(1, 0, { ...undeclaredTransitionEvents[0],
    eventType: "interaction_state_transition", fromState: "awaiting_request", toState: "approved",
    transitionEvent: "approve", at: undeclaredTransitionEvents[0].at + 0.5 });
const undeclaredTransitionReport = replayStudyJournal({ events: undeclaredTransitionEvents, plans: replayPlan,
    contract: interactionContract });
ok(undeclaredTransitionReport.findings.some((finding) => finding.id === "undeclared-transition"),
    "journal replay rejects observed transitions absent from the declared graph");

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

// Regression: study protocol counterbalancing is balanced at the target sample size.
// Keep each numbered requirement as one aggregate check so a pre-fix run reports
// every broken design property instead of stopping at the first bad cell.
const targetSamplePlans = Array.from({ length: 24 }, (_, index) =>
    generateParticipantPlan(`P${String(index + 1).padStart(3, "0")}`));
const increment = (map, key) => map.set(key, (map.get(key) || 0) + 1);
const permutationCounts = new Map();
const conditionPositionCounts = new Map();
const conditionVariantCounts = new Map();
const positionVariantCounts = new Map();
const h4ModeArmCounts = new Map();
const h4FirstCounts = new Map();
for (const plan of targetSamplePlans) {
    increment(permutationCounts, plan.assignment.explicitTaskOrder.join(""));
    for (const taskId of new Set(plan.trials.map((trial) => trial.taskId))) {
        const pair = plan.trials.filter((trial) => trial.taskId === taskId)
            .sort((left, right) => left.sequenceIndex - right.sequenceIndex);
        pair.forEach((trial, position) => {
            increment(conditionPositionCounts, `${taskId}|${trial.conditionAlias}|${position}`);
            increment(conditionVariantCounts, `${taskId}|${trial.conditionAlias}|${trial.taskVariant}`);
            increment(positionVariantCounts, `${taskId}|${position}|${trial.taskVariant}`);
        });
    }
    const h4Trials = plan.trials.filter((trial) => trial.candidateTarget !== null)
        .sort((left, right) => left.sequenceIndex - right.sequenceIndex);
    for (const trial of h4Trials) increment(h4ModeArmCounts, `${trial.interactionMode}|N=${trial.candidateTarget}`);
    if (h4Trials[0]) increment(h4FirstCounts, `N=${h4Trials[0].candidateTarget}-first`);
}
const allCountsEqual = (map, expectedCount, expectedCells) =>
    map.size === expectedCells && [...map.values()].every((value) => value === expectedCount);
const targetCounterbalancingChecks = [
    [1, allCountsEqual(permutationCounts, 4, EXPLICIT_TASK_ORDERS.length),
        "each explicit-task permutation appears exactly four times"],
    [2, allCountsEqual(conditionPositionCounts, 12, 20),
        "each task condition appears exactly 12 times in each within-task position"],
    [3, allCountsEqual(conditionVariantCounts, 12, 20),
        "each task condition-by-variant cell contains exactly 12 observations"],
    [4, allCountsEqual(positionVariantCounts, 12, 20),
        "each task position-by-variant cell contains exactly 12 observations"],
    [5, targetSamplePlans.every((plan) => plan.trials.filter((trial) => trial.candidateTarget !== null).length === 2),
        "every participant receives exactly two H4 trials"],
    [6, allCountsEqual(h4ModeArmCounts, 12, 4),
        "each L4/L5-by-candidate-target cell contains exactly 12 observations"],
    [7, allCountsEqual(h4FirstCounts, 12, 2),
        "N=1 and N=3 are each encountered first by exactly 12 participants"],
    [8, targetSamplePlans.every((plan) => plan.trials.length === 10),
        "every participant receives exactly ten trials"],
    [9, targetSamplePlans.every((plan) => [...new Set(plan.trials.map((trial) => trial.taskId))].every((taskId) => {
        const variants = plan.trials.filter((trial) => trial.taskId === taskId).map((trial) => trial.taskVariant);
        return variants.length === 2 && new Set(variants).size === 2;
    })), "each participant uses different variants within every task pair"],
    [10, 24 % EXPLICIT_TASK_ORDERS.length === 0,
        "the target sample size must complete the explicit-task Latin square"],
];
const targetCounterbalancingFailures = [];
for (const [number, passed, message] of targetCounterbalancingChecks) {
    console.log(`[counterbalancing assertion ${number}] ${passed ? "PASS" : "FAIL"}: ${message}`);
    if (passed) assertions += 1;
    else targetCounterbalancingFailures.push(number);
}
if (targetCounterbalancingFailures.length) {
    throw new assert.AssertionError({
        message: `target-sample counterbalancing assertions failed: ${targetCounterbalancingFailures.join(", ")}`,
        actual: targetCounterbalancingFailures,
        expected: [],
        operator: "deepStrictEqual",
    });
}
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
ok(!isAuthorableActivityTarget("sensor:xr-user-head"), "HMD sensor ids are never authoring targets");
ok(isAuthorableActivityTarget("study-l1-a-tray-2"), "study objects remain authoring targets");
const sensorAddressedOpportunity = new ActivityMonitor({ threshold: 0.5, now: () => activityNow })
    .observeSceneDelta({
        sessionId: "sensor-addressed-session",
        targetObjectId: "sensor:xr-user-head",
        timestamp: activityNow,
        payload: {
            focus: { id: "study-l1-a-tray-2" },
            sensorEvents: [{ sensorType: "collision", targetObjectId: "sensor:xr-user-head", confidence: 1 }],
        },
    });
equal(sensorAddressedOpportunity.targetObjectId, "study-l1-a-tray-2",
    "authorable focus wins over a sensor-addressed envelope");
equal(new ActivityMonitor({ threshold: 0.5, now: () => activityNow }).observeSceneDelta({
    sessionId: "sensor-only-session",
    targetObjectId: "sensor:xr-user-head",
    timestamp: activityNow,
    payload: { sensorEvents: [{ sensorType: "collision", targetObjectId: "sensor:xr-user-head", confidence: 1 }] },
}), null, "sensor-only activity cannot launch an implicit model turn");
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
equal(activity.observeSceneDelta({
    type: "SceneDelta",
    sessionId: "activity-session",
    timestamp: activityNow + 1,
    objectRevision: 99,
    payload: {
        state: {},
        sensorEvents: [{ sensorType: "study_questionnaire_response", confidence: 1,
            value: { trialId: "trial-complete", response: "7" } }],
    },
}), null, "study telemetry cannot become an implicit activity trigger through objectRevision");

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

// Unity publishes study lifecycle evidence on the SceneDelta sensor channel.
// The monitor process must durably close the trial configured by the runtime
// process, without an API key or a live Unity Editor.
const studyEvidencePath = path.join(testDataDir, "unity-study-evidence.jsonl");
const studyEvidenceMemory = new SharedMemory({
    artifactLogPath: studyEvidencePath,
    personProfilePath: path.join(testDataDir, "unity-study-people.json"),
    experienceContextPath: path.join(testDataDir, "unity-study-context.json"),
    checkpointPath: path.join(testDataDir, "unity-study-checkpoint.json"),
});
const studyEvidenceContext = {
    participantId: "DEBUG",
    sessionId: "debug-L1-lifecycle",
    trialId: "debug-L1-A-lifecycle",
    condition: "agenticxr_verification",
    taskId: "L1-proactive",
    interactionMode: "L1",
    taskVariant: "A",
    correlationId: "debug-config-lifecycle",
};
studyEvidenceMemory.artifactLog.startStudyTrial(studyEvidenceContext);
const fakeStudyBridge = new EventEmitter();
studyEvidenceMemory.attach(fakeStudyBridge);
fakeStudyBridge.emit("envelope", {
    type: "SceneDelta",
    sessionId: "unity-runtime-peer",
    correlationId: "unity-t1-lifecycle",
    timestamp: 2000,
    objectRevision: 7,
    payload: {
        sensorEvents: [{
            sensorType: "study_trial_t1",
            sourceObjectId: "study-trial-director",
            confidence: 1,
            value: {
                participantId: studyEvidenceContext.participantId,
                sessionId: studyEvidenceContext.sessionId,
                trialId: studyEvidenceContext.trialId,
                condition: studyEvidenceContext.condition,
                taskId: studyEvidenceContext.taskId,
                interactionMode: studyEvidenceContext.interactionMode,
                taskVariant: studyEvidenceContext.taskVariant,
                detail: { arbitrationReason: "detector" },
            },
        }],
    },
});
equal(studyEvidenceMemory.artifactLog.activeTrialBySession.size, 0,
    "Unity t1 evidence closes the server-side debug trial");
ok(studyEvidenceMemory.artifactLog.records.some((entry) =>
    entry.eventType === "study_trial_ended" && entry.reason === "detector" && entry.taskSuccess === true),
"detector completion is recorded as a successful terminal trial event");
equal(studyEvidenceMemory.activity.recent.length, 0,
    "terminal study evidence does not launch another continuous-assistance turn");
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

function assertAdversarialOutcomes(name, result) {
    equal(result.participant.recoverable, true, `${name}: participant receives an understandable recoverable state`);
    equal(result.operator.actionable, true, `${name}: operator receives an actionable state`);
    equal(result.scientific.interpretable, true, `${name}: scientific record remains interpretable`);
}

const doubleFireStore = new InteractionSessionStore();
doubleFireStore.begin({ sessionId: "adv-double", mode: "L4", correlationId: "adv-double-chain" });
doubleFireStore.recordUtterance({ sessionId: "adv-double", text: "open the training door" });
doubleFireStore.recordDecision({ sessionId: "adv-double", decision: "approve" });
let doubleFireError;
try { doubleFireStore.recordDecision({ sessionId: "adv-double", decision: "approve" }); }
catch (error) { doubleFireError = error; }
assertAdversarialOutcomes("double-fire confirm", {
    participant: { recoverable: Boolean(doubleFireError) },
    operator: { actionable: /no active interaction chain/.test(doubleFireError && doubleFireError.message) },
    scientific: { interpretable: doubleFireStore.active("adv-double") === null },
});

const earlyAdvanceMachine = new StudySessionMachine({ contract: interactionContract });
const earlyAdvance = earlyAdvanceMachine.create({ sessionId: "adv-early", mode: "L4", correlationId: "adv-early-chain" });
let earlyAdvanceError;
try { earlyAdvanceMachine.transitionState(earlyAdvance, "approved", "approve"); }
catch (error) { earlyAdvanceError = error; }
assertAdversarialOutcomes("operator advances early", {
    participant: { recoverable: earlyAdvance.state === "awaiting_request" },
    operator: { actionable: /undeclared/.test(earlyAdvanceError && earlyAdvanceError.message) },
    scientific: { interpretable: earlyAdvance.state !== "approved" },
});

const timeoutStore = new InteractionSessionStore();
timeoutStore.begin({ sessionId: "adv-timeout", mode: "L4", correlationId: "adv-timeout-chain" });
timeoutStore.recordUtterance({ sessionId: "adv-timeout", text: "preview a safe change" });
const timedOut = timeoutStore.recordDecision({ sessionId: "adv-timeout", decision: "timeout" });
assertAdversarialOutcomes("participant gives no response", {
    participant: { recoverable: timedOut.state === "timed_out" },
    operator: { actionable: timedOut.action === "close_chain" },
    scientific: { interpretable: timedOut.terminal === true },
});

const cancelStore = new InteractionSessionStore();
cancelStore.begin({ sessionId: "adv-cancel", mode: "L4", correlationId: "adv-cancel-chain" });
cancelStore.recordUtterance({ sessionId: "adv-cancel", text: "preview a safe change" });
const cancelled = cancelStore.recordDecision({ sessionId: "adv-cancel", decision: "cancel" });
assertAdversarialOutcomes("cancel mid-proposal", {
    participant: { recoverable: cancelled.state === "cancelled" },
    operator: { actionable: cancelled.action === "close_chain" },
    scientific: { interpretable: cancelled.terminal === true },
});

const resetStore = new InteractionSessionStore();
resetStore.begin({ sessionId: "adv-reset", mode: "L4", correlationId: "adv-reset-chain" });
resetStore.recordUtterance({ sessionId: "adv-reset", text: "preview a safe change" });
const resetPending = resetStore.reset("adv-reset");
assertAdversarialOutcomes("reset during pending proposal", {
    participant: { recoverable: resetPending },
    operator: { actionable: resetPending },
    scientific: { interpretable: resetStore.active("adv-reset") === null },
});
const resetUnbound = resetStore.reset("never-bound");
assertAdversarialOutcomes("reset before runtime identity", {
    participant: { recoverable: resetUnbound === false },
    operator: { actionable: resetUnbound === false },
    scientific: { interpretable: resetStore.active("never-bound") === null },
});

const adversarialLogPath = path.join(testDataDir, "adversarial-study.jsonl");
const adversarialContext = {
    participantId: "participant-adversarial", sessionId: "session-adversarial", trialId: "trial-adversarial-1",
    condition: "agenticxr_verification", taskId: "L4-confirm", interactionMode: "L4",
    correlationId: "adversarial-chain",
};
const adversarialLog = new ArtifactLog({ filePath: adversarialLogPath });
adversarialLog.startStudyTrial({ ...adversarialContext, at: 2000 });
const restartedAdversarialLog = new ArtifactLog({ filePath: adversarialLogPath });
assertAdversarialOutcomes("server process restart mid-trial", {
    participant: { recoverable: restartedAdversarialLog.activeTrialBySession.has(adversarialContext.sessionId) },
    operator: { actionable: restartedAdversarialLog.activeTrialBySession.size === 1 },
    scientific: { interpretable: restartedAdversarialLog.getStudyContext({ sessionId: adversarialContext.sessionId }).trialId === adversarialContext.trialId },
});
restartedAdversarialLog.appendStudyEvent({ sessionId: adversarialContext.sessionId,
    correlationId: adversarialContext.correlationId, eventType: "scene_reload_interrupted", at: 2010 });
restartedAdversarialLog.appendStudyEvent({ sessionId: adversarialContext.sessionId,
    correlationId: adversarialContext.correlationId, eventType: "scene_reload_resumed", at: 2020 });
assertAdversarialOutcomes("scene reload mid-trial", {
    participant: { recoverable: restartedAdversarialLog.activeTrialBySession.has(adversarialContext.sessionId) },
    operator: { actionable: restartedAdversarialLog.records.some((event) => event.eventType === "scene_reload_resumed") },
    scientific: { interpretable: restartedAdversarialLog.records.filter((event) => /scene_reload_/.test(event.eventType)).every((event) => event.trialId === adversarialContext.trialId) },
});
for (const eventType of ["application_focus_lost", "application_paused", "application_resumed", "application_focus_regained"]) {
    restartedAdversarialLog.appendStudyEvent({ sessionId: adversarialContext.sessionId,
        correlationId: adversarialContext.correlationId, eventType, at: 2030 + restartedAdversarialLog.records.length });
}
assertAdversarialOutcomes("application focus loss pause resume", {
    participant: { recoverable: restartedAdversarialLog.records.some((event) => event.eventType === "application_focus_regained") },
    operator: { actionable: restartedAdversarialLog.records.some((event) => event.eventType === "application_paused") },
    scientific: { interpretable: restartedAdversarialLog.activeTrialBySession.has(adversarialContext.sessionId) },
});
const midTrialExport = buildStudyExports(restartedAdversarialLog.records);
assertAdversarialOutcomes("export attempted mid-trial", {
    participant: { recoverable: restartedAdversarialLog.activeTrialBySession.has(adversarialContext.sessionId) },
    operator: { actionable: midTrialExport.rejectedTrials.length === 1 },
    scientific: { interpretable: midTrialExport.trialRows.length === 0 },
});
let concurrentTrialError;
try { restartedAdversarialLog.startStudyTrial({ ...adversarialContext,
    participantId: "participant-second", sessionId: "session-second", trialId: "trial-second",
    correlationId: "second-chain", at: 2050 }); }
catch (error) { concurrentTrialError = error; }
assertAdversarialOutcomes("two trials opened concurrently", {
    participant: { recoverable: restartedAdversarialLog.activeTrialBySession.size === 1 },
    operator: { actionable: /must be ended or aborted/.test(concurrentTrialError && concurrentTrialError.message) },
    scientific: { interpretable: !restartedAdversarialLog.records.some((event) => event.trialId === "trial-second") },
});
restartedAdversarialLog.endStudyTrial({ sessionId: adversarialContext.sessionId,
    correlationId: adversarialContext.correlationId, trialId: adversarialContext.trialId,
    taskCompletion: false, taskSuccess: null, reason: "adversarial-test", at: 2060 });
let reusedParticipantError;
try { restartedAdversarialLog.startStudyTrial({ ...adversarialContext, correlationId: "reused-chain", at: 2070 }); }
catch (error) { reusedParticipantError = error; }
assertAdversarialOutcomes("same participant ID reused", {
    participant: { recoverable: Boolean(reusedParticipantError) },
    operator: { actionable: /already been started/.test(reusedParticipantError && reusedParticipantError.message) },
    scientific: { interpretable: restartedAdversarialLog.records.filter((event) => event.eventType === "study_trial_started").length === 1 },
});
let invalidParticipantError;
try { validateParticipantId("participant/unsafe"); }
catch (error) { invalidParticipantError = error; }
assertAdversarialOutcomes("invalid participant ID", {
    participant: { recoverable: Boolean(invalidParticipantError) },
    operator: { actionable: /exactly three digits/.test(invalidParticipantError && invalidParticipantError.message) },
    scientific: { interpretable: Boolean(invalidParticipantError) },
});

const unityControlScan = scanStudyControls({ repositoryRoot: path.resolve(root, "..") });
ok(Array.isArray(unityControlScan.outOfBand), "Unity study-control scan reports callbacks that bypass the session machine");
ok(Array.isArray(unityControlScan.multiplyBound), "Unity study-control scan reports multiply-bound transition handlers");

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
equal(verificationRow.candidateAttemptCount, 3, "candidate attempts are derived from unique journalled candidate IDs");
equal(verificationRow.dryRunAttemptCount, 1, "dry-run attempts are counted from simulate_artifact events");
equal(verificationRow.dryRunSuccessCount, 1, "successful dry-runs are exposed as the H2 denominator diagnostic");
equal(verificationRow.dryRunFailureCount, 0, "successful verification produces no dry-run failure");
equal(verificationRow.visibleProposalCount, 1, "visible proposals are counted from Unity visibility acknowledgements");
equal(verificationRow.applicationAttemptCount, 1, "application attempts are counted from proposal operations");
equal(verificationRow.committedApplicationCount, 1, "committed applications are counted from live results");
equal(verificationRow.observedErrorOpportunityCount, 1, "H2 error opportunities use attempted applications");
equal(verificationRow.observedErrorCount, verificationRow.groundingErrorCount,
    "observed H2 errors retain the paper's grounding-error primary outcome");
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
ok(continuousMonitorSource.includes('entry.eventType === "study_trial_ended"') &&
    continuousMonitorSource.includes('reasonCode: trialEnded ? "study_trial_ended"'),
"an implicit Claude turn is cancelled when the easy physical detector ends its trial");
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
const regionVolume = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "AgenticRegionVolume.cs"), "utf8");
for (const required of ["AgenticRegionVolume", "PublishSensorEvent", "\\\"sensorType\\\":\\\"locomotion\\\"",
    "\\\"sensorType\\\":\\\"proximity\\\"", "\\\"sensorType\\\":\\\"gaze\\\"", "gazeDwellSeconds", "entering"]) {
    ok((implicitSensors + regionVolume).includes(required), `implicit trigger emitters contain ${required}`);
}
const unityStudyRoot = path.join(root, "..", "Unity", "Assets", "Study");
const sceneBuilderSource = fs.readFileSync(
    path.join(root, "..", "Unity", "Assets", "Editor", "AgenticXRStudySceneBuilder.cs"), "utf8");
const trialDirectorSource = fs.readFileSync(path.join(unityStudyRoot, "StudyTrialDirector.cs"), "utf8");
for (const required of [
    "door.GetComponents<Collider>().Length != 0",
    "door.offEgressPath",
    "door.trialLocal",
    "door.persistent",
    "door.participantLocomotionAllowed",
    "door.scriptedNpcProxyCount != 2",
    "proxies.Length != 2",
    "proxy.GetComponent<Collider>() != null",
]) ok(sceneBuilderSource.includes(required), `scene builder enforces L4 safety invariant: ${required}`);
ok(sceneBuilderSource.includes("SerializedObject(stable)") && sceneBuilderSource.includes("FindProperty(\"value\")"),
    "scene builder explicitly assigns the private StableObjectId.value field");
ok(sceneBuilderSource.includes("root.SetActive(false)") &&
    sceneBuilderSource.includes("director.variants.Count(binding => binding.root.activeSelf) != 0"),
    "scene builder leaves all ten task-variant roots inactive");
ok(sceneBuilderSource.includes("StructuralSignature(a, \"-a-\").SequenceEqual(StructuralSignature(b, \"-b-\"))"),
    "scene builder rejects structurally unequal A/B variants");
const buildSettingsSource = fs.readFileSync(
    path.join(root, "..", "Unity", "ProjectSettings", "EditorBuildSettings.asset"), "utf8").replace(/\r\n/g, "\n");
ok(buildSettingsSource.includes("m_Scenes:\n  - enabled: 1\n    path: Assets/Scenes/AgenticXRStudy.unity"),
    "the study scene is the first enabled build scene");
ok(sceneBuilderSource.includes("registered.guid.ToString()") &&
    sceneBuilderSource.includes("AssetDatabase.AssetPathToGUID(ScenePath)"),
    "the builder verifies Unity resolves the registered scene to its imported asset GUID");
for (const required of [
    'transitionAuthority = "StudySessionMachine"',
    'CountActiveVariantRoots() != 1',
    'study_trial_t0',
    'task_card_dismissed_and_root_active',
    'study_l2_region_entered',
    'study_trial_t1',
    'RequestTrialEnd("detector"',
    'RequestTrialEnd("declared"',
    'RequestTrialEnd("timeout"',
]) ok(trialDirectorSource.includes(required), `StudyTrialDirector delegates and logs required behavior: ${required}`);
ok(!/zeroBased|participantIndex|canonicalModeIndex|EXPLICIT_TASK_ORDERS/.test(trialDirectorSource),
    "StudyTrialDirector contains no client-side assignment or counterbalancing arithmetic");
ok(trialDirectorSource.includes("TrainingUndoCriterionMet") && trialDirectorSource.includes("TrainingRejectCriterionMet") &&
    trialDirectorSource.includes("study_training_criterion_met"),
    "training requires observed undo and reject criteria and emits evidence for both");
ok(trialDirectorSource.includes("trainingRequired && !TrainingCriteriaComplete") &&
    trialDirectorSource.includes("throw new InvalidOperationException"),
    "the training criterion is an enforced gate, not an instruction slide");
ok(trialDirectorSource.includes("resetTransforms[index].localPosition") &&
    trialDirectorSource.includes("resetTransforms[index].localRotation") &&
    sceneBuilderSource.includes('mode == "L5"') && sceneBuilderSource.includes('ChildrenContaining(root, "marker-")'),
    "StudyTrialDirector restores every L5 sequence marker before arming the next trial");
const detectorPaths = fs.readdirSync(path.join(unityStudyRoot, "Detectors"))
    .filter((name) => /^L[1-5].*Detector\.cs$/.test(name))
    .map((name) => path.join(unityStudyRoot, "Detectors", name));
equal(detectorPaths.length, 5, "exactly five task-specific success detectors are authored");
for (const detectorPath of detectorPaths) {
    const detectorSource = fs.readFileSync(detectorPath, "utf8");
    ok(detectorSource.includes("settleWindowSeconds"), `${path.basename(detectorPath)} uses a settle window`);
    ok(!/transform\.(?:local)?(?:Position|Rotation)\s*=|SetActive\s*\(|AddComponent\s*<|Destroy(?:Immediate)?\s*\(/.test(detectorSource),
        `${path.basename(detectorPath)} observes without moving objects, toggling roots, adding components, or destroying state`);
}
const detectorBaseSource = fs.readFileSync(path.join(unityStudyRoot, "Detectors", "StudySuccessDetector.cs"), "utf8");
ok(detectorBaseSource.includes("if (!IsArmed || HasFired) return") && detectorBaseSource.includes("HasFired = true"),
    "shared detector base is armed and idempotent and can fire only once");
ok(detectorBaseSource.includes("study_task_completion_observed") && !detectorBaseSource.includes("complete_task"),
    "detectors publish observation evidence and never grant task success");
const questionnairePresenterSource = fs.readFileSync(path.join(unityStudyRoot, "StudyQuestionnairePresenter.cs"), "utf8");
for (const item of questionnaireSchema.studySpecificItemSlots)
    ok(!questionnairePresenterSource.includes(item.wording), `${item.itemId} wording is not duplicated into C#`);
ok(questionnairePresenterSource.includes("questionnaireDefinition.text") &&
    questionnairePresenterSource.includes("JsonUtility.FromJson<QuestionnaireDefinition>"),
    "Unity questionnaire presenter renders the JSON source of truth");
ok(!/SkipCurrent|PreviousItem|CreateButton\("Back"/.test(questionnairePresenterSource) &&
    questionnairePresenterSource.includes('RecordResponse("declined", true)'),
    "questionnaire presenter has no skip/back route and records explicit declines");
for (const field of [...questionnaireSchema.responseIdentityFields, "respondedAtUtc", "responseLatencyMs"])
    ok(questionnairePresenterSource.includes(field), `Unity questionnaire response records ${field}`);
ok(questionnairePresenterSource.includes("BeginQuestionnairePause") &&
    questionnairePresenterSource.includes("EndQuestionnairePause"),
    "immediate questionnaire explicitly pauses and resumes the task clock");
const consentPanelSource = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "AgenticXRConsentPanel.cs"), "utf8");
equal((consentPanelSource.match(/if \(decisionLocked\) return;/g) || []).length, 3,
    "approve, reject, and revise are all unreachable while the immediate item is unanswered");
ok(consentPanelSource.includes("BeginImmediateProposalItem") && consentPanelSource.includes("ImmediateItemCompleted"),
    "proposal surfacing locks decisions until the immediate H4 item completes");
const taskCardPresenterSource = fs.readFileSync(path.join(unityStudyRoot, "StudyTaskCardPresenter.cs"), "utf8");
ok(taskCardPresenterSource.includes("taskCardDefinition.text") && taskCardPresenterSource.includes("definition.cards"),
    "Unity task-card presenter renders the condition-independent JSON source");
for (const cardText of taskCards.cards.flatMap((card) => [card.goal, card.scriptedFirstUtterance,
    card.goalCardDetail, ...(card.revisionOrder || [])]).filter(Boolean))
    ok(!taskCardPresenterSource.includes(cardText), "task-card wording is not duplicated into C#");
ok(trialDirectorSource.includes("study_task_card_dismissed") && trialDirectorSource.includes("excludedQuestionnaireMs"),
    "trial director logs card dismissal and excludes questionnaire pauses from task time");
ok(trialDirectorSource.includes("questionnairePresenter?.BeginAfterTrialBattery()") &&
    questionnairePresenterSource.includes("study_questionnaire_battery_complete"),
    "t1 automatically launches the post-trial battery and publishes explicit completion");
ok(sceneBuilderSource.includes("SyncStudyJsonAsset") && sceneBuilderSource.includes("Assets/Study/Definitions/questionnaires.v1.json") &&
    sceneBuilderSource.includes("Assets/Study/Definitions/task_cards.v1.json"),
    "scene builder deterministically mirrors server-owned study JSON as imported TextAssets");
const unityDefinitionRoot = path.join(root, "..", "Unity", "Assets", "Study", "Definitions");
equal(fs.existsSync(path.join(root, "..", "Unity", "Assets", "StreamingAssets", "Study")), false,
    "the incompatible StreamingAssets/Study mirror is absent");
equal(fs.readFileSync(path.join(unityDefinitionRoot, "questionnaires.v1.json"), "utf8"),
    fs.readFileSync(path.join(root, "study", "questionnaires.v1.json"), "utf8"),
    "Unity questionnaire JSON is byte-identical to the server source of truth");
equal(fs.readFileSync(path.join(unityDefinitionRoot, "task_cards.v1.json"), "utf8"),
    fs.readFileSync(path.join(root, "study", "task_cards.v1.json"), "utf8"),
    "Unity task-card JSON is byte-identical to the server source of truth");
const serializedVariants = (studySceneSource.match(/\n  - taskId: L[1-5]-[^\r\n]+\r?\n    interactionMode: L[1-5]\r?\n    taskVariant: [AB]\r?\n    root: \{fileID: [1-9][0-9]*\}/g) || []);
equal(serializedVariants.length, 10, "StudyTrialDirector serializes exactly ten non-null VariantBinding roots");
equal(new Set(serializedVariants.map((entry) => entry.match(/interactionMode: (L[1-5])[\s\S]*taskVariant: ([AB])/).slice(1).join("|"))).size,
    10, "serialized VariantBindings cover L1-L5 x A/B exactly once");
ok(!/questionnaireDefinition: \{fileID: 0\}/.test(studySceneSource) &&
    !/taskCardDefinition: \{fileID: 0\}/.test(studySceneSource),
    "both in-VR presenters serialize non-null TextAsset definitions");
equal((studySceneSource.match(/^--- !u!108 /gm) || []).length, 1, "the study scene serializes exactly one Light component");
ok(!/m_Sun: \{fileID: 0\}/.test(studySceneSource), "RenderSettings serializes the directional light as the sun");
for (const required of [
    "ConfigureStudyLight(studyRoot)", "LightType.Directional", "LightShadows.Soft", "RenderSettings.sun = light",
    "ValidateDistanceIsomorphism(mode, a, b, mode != \"L4\")", "AssertSameMultiset", "Mathf.Abs(value - b[index]) > 1e-4f",
    'mode == "L1" ? 38f', 'mode == "L2" ? -34f', 'mode == "L3" ? 45f', 'mode == "L5" ? -41f',
    'new[] { 2, 0, 1 }', 'new[] { 1, 2, 0 }', 'variant == "B" ? "pad-3" : "pad-2"',
    "new List<GameObject> { sequence[2], sequence[1], sequence[0] }",
]) ok(sceneBuilderSource.includes(required), `scene builder contains audited isomorph/lighting guard: ${required}`);
for (const required of [
    "PrefabUtility.InstantiatePrefab", "PlayerPrefabPath", "XRPlayerController", "TrackedPoseDriver",
    "TeleportRay", "GraspableObjectGrasper", "UseableObjectUser", "StudyTeleportFloor",
    "FollowGraspable", "StudyDoneButtonUseable", "RemoveComponents<TestRoslyn>",
    "GetComponentsInChildren<TestRoslyn>", "Assets/Scenes/Scripts/", "Assets/Demos/",
]) ok(sceneBuilderSource.includes(required), `scene builder enforces physical executability: ${required}`);
ok(sceneBuilderSource.includes("SmokeTestRuntimeCompiler(system.compiler)") &&
    sceneBuilderSource.includes("compiler.TryCompileAndAttach(target, source") && sceneBuilderSource.includes("proxy.Dispose()"),
    "every generated scene build exercises the real study compiler's compile, attach, and disposal path");
ok(sceneBuilderSource.includes("Quaternion.Euler(0f, -8f, 0f) * basePosition") &&
    sceneBuilderSource.includes('ValidateDistanceIsomorphism(mode, a, b, mode != "L4")'),
    "L4-B moves on a participant-centred -8 degree arc and receives origin-distance validation");
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
const roslynRuntime = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "AgenticRuntimeCompiler.cs"), "utf8");
for (const deniedCapability of ["system.io", "system.net", "system.diagnostics", "system.reflection",
    "system.runtime.interopservices", "unityengine.networking", "dllimport", "stackalloc",
    "application.quit", "application.openurl", "environment.exit"]) {
    ok(roslynRuntime.includes(`\"${deniedCapability}\"`), `capability policy denies ${deniedCapability}`);
}
ok(roslynRuntime.includes("allowedNamespaces"), "capability policy has an explicit namespace allowlist");
const testRoslynSource = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "Scenes", "Scripts", "TestRoslyn.cs"), "utf8");
ok(testRoslynSource.includes("class TestRoslyn : AgenticRuntimeCompiler") &&
    !roslynRuntime.includes("KeyCode.V") && !roslynRuntime.includes("connectionPanel"),
    "study-safe compiler owns compilation while V-key demo UI remains only in the legacy adapter");

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

equal(analysisPlan.confirmatoryTests.length, 3,
    "below-50% advance equivalence power leaves exactly three confirmatory tests");
equal(analysisPlan.confirmatoryTests.map((test) => test.hypothesis).join(","), "H1,H2,H4",
    "confirmatory tests are exactly H1, H2, and H4 after the predeclared H3 eligibility branch");
const h3Estimation = analysisPlan.estimationAnalyses.find((test) => test.hypothesis === "H3");
equal(h3Estimation.classification, "non-confirmatory-estimation",
    "H3 is structurally labelled estimation rather than left in the confirmatory array");
equal(h3Estimation.equivalenceTest.marginScalePoints, 0.5,
    "H3 retains the substantive +/-0.5 TOST margin without power-driven widening");
equal(h3Estimation.varianceDecomposition.trialLevelVarianceShare, 0.8,
    "H3 declares 80% trial-level variance before pair averaging");
equal(h3Estimation.varianceDecomposition.stableParticipantByModeVarianceShare, 0.2,
    "H3 declares 20% stable participant-by-mode variance that pair averaging cannot shrink");
equal(h3Estimation.equivalencePowerAtN24.unaveragedReading.powerPercent, 0,
    "H3 unaveraged reading records zero advance equivalence power");
equal(h3Estimation.equivalencePowerAtN24.twoTrialPairAverageReading.powerPercent, 35.94150512,
    "H3 pair-average reading records 35.94% advance equivalence power");
equal(h3Estimation.equivalencePowerAtN24.twoTrialPairAverageReading.simultaneousTenContrastPowerUpperBoundPercent,
    35.94150512, "H3 records a rigorous below-50% upper bound on simultaneous ten-contrast power");
equal(h3Estimation.equivalencePowerAtN24.twoTrialPairAverageReading.simultaneousTenContrastPowerUnderIndependencePercent,
    0.0035971836, "H3 records but does not overclaim the independence-reference joint power");
equal(h3Estimation.confirmatoryEligibilityRule.declaredBranch, "estimation",
    "H3 deterministically lands on the estimation branch before participant data");
equal(analysisPlan.multiplicity.confirmatoryFamilySize, 3,
    "multiplicity plan cannot count H3 as confirmatory after its eligibility branch");
ok(analysisPlan.confirmatoryTests.find((test) => test.hypothesis === "H2").dispersionSwitchRule.includes("negative-binomial"),
    "H2 freezes the Poisson-to-negative-binomial dispersion switch rule");
ok(analysisPlan.confirmatoryTests.find((test) => test.hypothesis === "H4").incompleteFactorial.includes("not estimable"),
    "H4 records the incomplete factorial and non-estimable candidate-count-by-task interaction");
ok(analysisPlan.confirmatoryTests.every((test) => Number(test.mdeAtN24.value) > 0),
    "every confirmatory test records an advance MDE at N=24");
equal(analysisPlan.secondaryMeasures.classification, "exploratory",
    "all non-primary Table 5 measures are structurally labelled exploratory");
equal(analysisPlan.secondaryMeasures.significanceClaimsAllowed, false,
    "the report plan cannot promote secondary measures to significance claims");
const analysisLock = verifyAnalysisPlanLock();
equal(analysisLock.ok, true, "analysis plan bytes match the preregistered hash lock");
equal(analysisLock.actualSha256, EXPECTED_ANALYSIS_PLAN_SHA256, "analysis plan lock records the exact current SHA-256");

equal(runtimeSystemPromptHash(), modelPin.systemPromptHash,
    "model pin hashes the SYSTEM_PROMPT object actually loaded by the runtime");
equal(validateModelPin({ liveVersionString: modelPin.modelVersionString }).ok, true,
    "matching live model version, prompt, and candidate default satisfy the pin");
equal(validateModelPin({ liveVersionString: "unexpected-model-version" }).ok, false,
    "a live model version mismatch fails closed");
equal(auditTranscriptPrivacy().ok, true,
    "automated privacy sweep finds no unguarded participant/model text sink on the declared runtime paths");
equal(parseSttResponse('{"text":"short constrained answer","confidence":0.91}', "application/json").confidence, 0.91,
    "STT JSON response preserves per-utterance confidence");
equal(parseSttResponse('{"transcript":"fallback key","asrConfidence":0.82}', "application/json").text, "fallback key",
    "STT response supports the declared transcript/confidence schema without logging the text");
equal(parseSttResponse("legacy plain response", "text/plain").confidence, null,
    "legacy STT responses make missing confidence explicit rather than fabricating a value");
assert.throws(() => preflight(null, {}), /--mode=technical\/--mode=researcher-dry-run or --mode=human\/--mode=human-session is required/);
assertions += 1;
const humanPreflight = preflight(null, { mode: "human-session" });
equal(humanPreflight.ok, false, "human-session preflight remains fail-closed while approvals and licensed text are outstanding");
ok(humanPreflight.checks.some((check) => check.id === "questionnaire-approval-readiness" && !check.ok),
    "human-session preflight consults the questionnaire readiness gate");
const dryRunPreflight = preflight(null, { mode: "researcher-dry-run" });
ok(dryRunPreflight.checks.some((check) => check.id === "questionnaire-approval-readiness" && check.ok),
    "researcher dry-run explicitly permits drafted study wording while still running the full check set");
equal(preflight(null, { mode: "technical" }).mode, "researcher-dry-run",
    "technical preflight alias normalizes to the existing researcher dry-run gate");
equal(preflight(null, { mode: "human" }).mode, "human-session",
    "human preflight alias normalizes to the existing fail-closed human-session gate");

const syntheticPilot = runSyntheticPilot();
equal(syntheticPilot.ok, true, "24x10 synthetic pilot passes runner, journal, export, replay, coverage, and realised design gates");
equal(syntheticPilot.participantCount, 24, "synthetic pilot replays exactly 24 participant plans");
equal(syntheticPilot.trialCount, 240, "synthetic journal contains exactly 240 trials");
equal(syntheticPilot.exportTrialCount, 240, "every synthetic trial produces a complete joinable export row");
equal(syntheticPilot.rejectedTrialCount, 0, "synthetic export rejects no trial");
equal(syntheticPilot.scientificState.ok, true, "all scientific-state invariants hold on realised synthetic journals");
equal(syntheticPilot.coverageComplete, true, "real session-machine replay exercises every declared state and transition");
ok(Object.values(syntheticPilot.coverage).every((mode) =>
    mode.unvisitedStates.length === 0 && mode.unvisitedTransitions.length === 0),
"coverage report contains no silently unvisited state or transition");
equal(syntheticPilot.exportGuards.everyTrialTimed, true,
    "all synthetic exports contain t0, t1, taskTimeMs, and an explicit trialEndReason");
equal(syntheticPilot.exportGuards.everyTrialPinned, true,
    "every synthetic trial export is stamped dry-run and carries the model pin hash");
equal(syntheticPilot.exportGuards.everyL2TriggerMeasured, true,
    "every L2 export measures trigger-to-visible-change from the discrete trigger event");
equal(syntheticPilot.exportGuards.everySpokenTrialHasAsrGuard, true,
    "every spoken synthetic trial exports ASR confidence and word-count guards");
equal(syntheticPilot.realizedDesignAudit.ok, true, "design audit passes on the realised journal sequence");
const pilotHarnessSource = fs.readFileSync(path.join(root, "study", "pilot_harness.js"), "utf8");
ok(!/interactionState\.revisionCount\s*=/.test(pilotHarnessSource),
    "synthetic coverage never mutates revisionCount to bypass the required-revision gate");
const sessionMachineSource = fs.readFileSync(path.join(root, "study", "study_session_machine.js"), "utf8");
ok(sessionMachineSource.includes('if (event === "revision_received") state.revisionCount += 1'),
    "revision preconditions are earned by a declared revision_received transition");
const syntheticEvents = fs.readFileSync(syntheticPilot.outputPath, "utf8").trim().split(/\r?\n/).map(JSON.parse);
const firstSyntheticT0 = syntheticEvents.find((event) => event.eventType === "study_trial_t0");
const overlapExport = buildStudyExports(syntheticEvents, { questionnaireResponses: [{
    participantId: firstSyntheticT0.participantId, sessionId: firstSyntheticT0.sessionId,
    trialId: firstSyntheticT0.trialId, itemId: "overlap-guard", answeredAtUtc: firstSyntheticT0.timestampUtc,
}] });
ok(overlapExport.rejectedTrials.some((trial) => trial.trialId === firstSyntheticT0.trialId &&
    trial.reason.includes("falls inside task window")),
"export fails the affected trial when a questionnaire timestamp overlaps [t0,t1]");
equal(overlapExport.trialRows.length, 239,
    "questionnaire overlap cannot remain hidden inside an otherwise complete 240-trial export");

console.log(`[cache_contract_test] PASS (${assertions} assertions)`);
