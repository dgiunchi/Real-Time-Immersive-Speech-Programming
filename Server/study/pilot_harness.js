"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { EventJournal } = require("../cache/event_journal");
const { ArtifactLog } = require("../memory/artifact_log");
const { buildStudyExports } = require("../evaluation/study_export");
const { generateParticipantPlan } = require("./protocol");
const { auditDesign } = require("./design_audit");
const { StudySessionRunner } = require("./session_runner");
const { loadInteractionContract, expandTransitions } = require("./study_session_machine");
const { replayStudyJournal } = require("./study_replay_verifier");
const modelPin = require("./model_pin.v1.json");

function shortestPath(definition, targetState) {
    const transitions = expandTransitions(definition);
    const pending = [{ state: definition.initialState, path: [] }];
    const visited = new Set();
    while (pending.length) {
        const current = pending.shift();
        if (current.state === targetState) return current.path;
        if (visited.has(current.state)) continue;
        visited.add(current.state);
        for (const transition of transitions.filter((item) => item.from === current.state)) {
            pending.push({ state: transition.to, path: [...current.path, transition] });
        }
    }
    throw new Error(`no path reaches '${targetState}'`);
}

function pathToTransition(definition, target) {
    if (Number.isInteger(definition.requiredRevisionCount) && target.from === "awaiting_decision") {
        const path = shortestPath(definition, "awaiting_revision");
        const revisionReceived = expandTransitions(definition).find((item) =>
            item.from === "awaiting_revision" && item.to === "revising" && item.event === "revision_received");
        const revisionRequested = expandTransitions(definition).find((item) =>
            item.from === "revising" && item.to === "awaiting_revision" && item.event === "revision_requested");
        const revisionComplete = expandTransitions(definition).find((item) =>
            item.from === "revising" && item.to === "awaiting_decision" && item.event === "revision_complete");
        for (let index = 0; index < definition.requiredRevisionCount; index += 1) {
            path.push(revisionReceived, index + 1 === definition.requiredRevisionCount ? revisionComplete : revisionRequested);
        }
        return [...path, target];
    }
    return [...shortestPath(definition, target.from), target];
}

function coverageScripts(contract) {
    const scripts = new Map();
    for (const [mode, definition] of Object.entries(contract.modes)) {
        scripts.set(mode, expandTransitions(definition).map((target) => pathToTransition(definition, target)));
    }
    return scripts;
}

function runSyntheticPilot({ outputPath = path.join(os.tmpdir(), `agenticxr-pilot-${process.pid}.jsonl`) } = {}) {
    if (fs.existsSync(outputPath)) fs.unlinkSync(outputPath);
    const contract = loadInteractionContract();
    const scripts = coverageScripts(contract);
    const scriptIndex = new Map([...scripts.keys()].map((mode) => [mode, 0]));
    const plans = Array.from({ length: 24 }, (_, index) =>
        generateParticipantPlan(`P${String(index + 1).padStart(3, "0")}`));
    const log = new ArtifactLog({ filePath: outputPath });
    let clock = Date.UTC(2026, 7, 22, 9, 0, 0);
    const now = () => ++clock;
    const runnerJournalCounts = [];

    for (const plan of plans) {
        const journal = new EventJournal({ now });
        const runner = new StudySessionRunner({ participantId: plan.participantId, plan, journal, now,
            breakMinimumMs: 0, runMode: "researcher-dry-run", modelPin });
        runner.completeConsentAndDemographics();
        runner.recordTrainingCriterion("undo");
        runner.recordTrainingCriterion("reject");
        for (const assigned of plan.trials) {
            const trial = runner.startNextTrial();
            const correlationId = `${plan.participantId}-${trial.trialId}-root`;
            const common = {
                ...trial, correlationId, runMode: "researcher-dry-run", isDryRun: true,
                modelId: modelPin.modelId, modelVersionString: modelPin.modelVersionString,
                modelPinHash: modelPin.systemPromptHash,
            };
            log.startStudyTrial({ ...common, at: now(), studySource: "synthetic-pilot" });
            runner.markTaskCardDismissed();
            runner.markTaskT0();
            log.appendStudyEvent({ ...common, eventType: "study_trial_t0", at: now(), studySource: "session_runner" });
            if (trial.interactionMode === "L2") {
                runner.markL2Trigger();
                log.appendStudyEvent({ ...common, eventType: "study_l2_trigger", at: now(),
                    triggerType: "region-entry-or-dwell-threshold", studySource: "session_runner" });
            }
            log.appendStudyEvent({ ...common, eventType: "intent_captured", at: now(), studySource: "scripted-agent" });

            const modeScripts = scripts.get(trial.interactionMode);
            const index = scriptIndex.get(trial.interactionMode);
            const transitions = modeScripts[index % modeScripts.length];
            scriptIndex.set(trial.interactionMode, index + 1);
            for (const transition of transitions) {
                runner.transitionInteraction(transition.to, transition.event, { correlationId });
                log.appendStudyEvent({ ...common, eventType: "interaction_state_transition",
                    fromState: transition.from, toState: transition.to, transitionEvent: transition.event,
                    at: now(), studySource: "StudySessionMachine" });
            }

            const candidateCount = trial.candidateTarget || 1;
            for (let candidate = 1; candidate <= candidateCount; candidate += 1) {
                log.appendStudyEvent({ ...common, eventType: "propose_artifact", candidateId: `${trial.trialId}-c${candidate}`,
                    candidateSetId: `${trial.trialId}-set`, candidateCount, at: now(), studySource: "scripted-agent" });
            }
            if (["L3", "L4", "L5"].includes(trial.interactionMode)) {
                log.appendStudyEvent({ ...common, eventType: "transcript_ready", asrConfidence: 0.93,
                    asrWordCount: 8, transcriptCharacters: 42, at: now(), studySource: "scripted-stt" });
            }
            if (trial.condition === "agenticxr_no_verification") {
                log.appendStudyEvent({ ...common, eventType: "verification_bypassed", verificationBypassed: true,
                    at: now(), studySource: "scripted-agent" });
            }
            log.appendStudyEvent({ ...common, eventType: "artifactresult", status: "committed",
                candidateId: `${trial.trialId}-c1`, at: now(), studySource: "scripted-agent" });
            if (trial.interactionMode === "L2") {
                log.appendStudyEvent({ ...common, eventType: "study_l2_visible_change", status: "committed",
                    at: now(), studySource: "session_runner" });
            }
            runner.markTaskT1("detector");
            log.appendStudyEvent({ ...common, eventType: "study_trial_t1", arbitrationReason: "detector",
                totalTaskTimeMs: runner.taskClock.totalTaskTimeMs, at: now(), studySource: "session_runner" });
            log.endStudyTrial({ sessionId: trial.sessionId, trialId: trial.trialId, correlationId,
                taskCompletion: true, taskSuccess: null, at: now() });
            log.append({ eventType: "trial_reset", at: now(), studySource: "session_runner" });
            runner.completeInVrBattery();
            if (runner.phase === "break") {
                runner.recordDesktopBatteryComplete();
                runner.advanceAfterBreak();
            }
        }
        runner.recordDesktopBatteryComplete();
        runner.finishDebrief();
        runnerJournalCounts.push(journal.backfill(runner.journalSessionId, 0).length);
    }

    const replay = replayStudyJournal({ events: log.records, plans, contract });
    const exports = buildStudyExports(log.records);
    const design = auditDesign({ participantCount: 24 });
    return {
        ok: replay.ok && replay.coverageComplete && design.ok && exports.rejectedTrials.length === 0 &&
            exports.trialRows.length === 240 && exports.longRows.every((row) => row.participantId && row.trialId),
        outputPath,
        participantCount: plans.length,
        trialCount: replay.trialCount,
        exportTrialCount: exports.trialRows.length,
        rejectedTrialCount: exports.rejectedTrials.length,
        runnerJournalRecordCount: runnerJournalCounts.reduce((sum, value) => sum + value, 0),
        scientificState: { ok: replay.ok, findings: replay.findings },
        coverageComplete: replay.coverageComplete,
        coverage: replay.coverage,
        exportGuards: {
            everyTrialTimed: exports.trialRows.every((row) => Number.isFinite(row.t0) && Number.isFinite(row.t1) &&
                Number.isFinite(row.taskTimeMs) && ["detector", "declared", "timeout"].includes(row.trialEndReason)),
            everyTrialPinned: exports.trialRows.every((row) => row.isDryRun === true && row.modelPinHash === modelPin.systemPromptHash),
            everyL2TriggerMeasured: exports.trialRows.filter((row) => row.interactionMode === "L2")
                .every((row) => Number.isFinite(row.triggerToVisibleChangeMs)),
            everySpokenTrialHasAsrGuard: exports.trialRows.filter((row) => ["L3", "L4", "L5"].includes(row.interactionMode))
                .every((row) => JSON.parse(row.asrConfidenceJson).length > 0 && JSON.parse(row.asrWordCountJson).length > 0),
        },
        realizedDesignAudit: { ok: design.ok && replay.ok, generatedPlanAudit: design.ok,
            journalPlanSequenceAudit: replay.ok },
    };
}

if (require.main === module) {
    const report = runSyntheticPilot();
    console.log(JSON.stringify(report, null, 2));
    if (!report.ok) process.exitCode = 1;
}

module.exports = { shortestPath, pathToTransition, coverageScripts, runSyntheticPilot };
