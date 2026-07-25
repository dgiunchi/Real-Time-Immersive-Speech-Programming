"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { ArtifactLog } = require("../memory/artifact_log");
const { GoalLoopController } = require("../orchestrator/goal_loop_controller");
const { FutureGoalPredictor } = require("../orchestrator/future_goal_predictor");
const {
    VERIFICATION_LEVELS,
    GLOBAL_MAX_ATTEMPTS,
    GLOBAL_MAX_WALL_TIME_MS,
} = require("../orchestrator/goal_schema");

let assertions = 0;
function ok(value, message) { assert.ok(value, message); assertions += 1; }
function equal(actual, expected, message) { assert.strictEqual(actual, expected, message); assertions += 1; }

const automaticPolicy = {
    riskScore: 0.1,
    reversible: true,
    localOnly: true,
    detailResolved: true,
};

function goalInput(overrides = {}) {
    return {
        objective: "verify the generated artifact",
        sessionId: "goal-test-session",
        correlationId: `corr-${overrides.goalId || "goal"}`,
        targetObjectId: "cube-1",
        interactionMode: "L1",
        authoringMode: "automatic",
        triggerSource: "system_opportunity",
        verificationLevel: VERIFICATION_LEVELS.DETERMINISTIC,
        terminationPredicate: { type: "field_equals", field: "artifactCommitted", value: true },
        maxAttempts: 3,
        maxWallTimeMs: 60000,
        ...overrides,
    };
}

async function main() {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "agenticxr-goals-"));
    const logPath = path.join(tempDir, "artifact-log.jsonl");
    const artifactLog = new ArtifactLog({ filePath: logPath });
    const controller = new GoalLoopController({ artifactLog });

    const deterministic = controller.create(goalInput({ goalId: "deterministic-goal" }));
    const deterministicResult = await controller.trigger(
        deterministic.goalId,
        { source: "system_opportunity", id: "event-1" },
        { policy: automaticPolicy, execute: async () => ({ artifactCommitted: true }) }
    );
    equal(deterministicResult.status, "completed", "deterministic evidence terminates a goal");
    equal(deterministicResult.goal.currentIteration, 1, "one trigger produces one iteration");

    const rules = controller.create(goalInput({
        goalId: "rules-goal",
        objective: "keep runtime risk below threshold",
        verificationLevel: VERIFICATION_LEVELS.RULES_CONSTRAINTS,
        terminationPredicate: { type: "threshold", metric: "riskScore", operator: "<=", value: 0.3 },
    }));
    const rulesResult = await controller.trigger(
        rules.goalId,
        { source: "system_opportunity", id: "event-2" },
        { policy: automaticPolicy, execute: async () => ({ riskScore: 0.2 }) }
    );
    equal(rulesResult.status, "completed", "rules verifier evaluates its numeric threshold");

    const bounded = controller.create(goalInput({
        goalId: "bounded-goal",
        maxAttempts: 1,
    }));
    const boundedResult = await controller.trigger(
        bounded.goalId,
        { source: "system_opportunity", id: "event-3" },
        { policy: automaticPolicy, execute: async () => ({ artifactCommitted: false }) }
    );
    equal(boundedResult.status, "awaiting_human", "attempt exhaustion escalates");
    equal(boundedResult.reason, "bound_exhausted", "attempt exhaustion has a stable reason");
    ok(artifactLog.history({ limit: 500 }).some((entry) =>
        entry.goalId === bounded.goalId && entry.eventType === "goal_bound_exhausted"),
    "bound exhaustion is persisted");
    assert.throws(() => controller.continueAfterHuman(bounded.goalId, { approved: true }),
        /verified human decision/, "the model cannot self-approve continuation");
    assertions += 1;
    const escalation = artifactLog.history({ limit: 500 }).find((entry) =>
        entry.goalId === bounded.goalId && entry.eventType === "goal_bound_exhausted");
    artifactLog.append({
        eventType: "intent_captured",
        sessionId: bounded.sessionId,
        correlationId: "follow-up-turn",
        at: escalation.at + 1,
    });
    const verifiedFollowUp = controller.memory.verifiedHumanDecisionAfterEscalation(
        bounded,
        "follow-up-turn"
    );
    ok(verifiedFollowUp && verifiedFollowUp.verified, "a later distinct user turn proves human continuation");
    equal(controller.continueAfterHuman(bounded.goalId, {
        approved: true,
        humanDecision: verifiedFollowUp,
    }).status, "waiting_trigger", "verified later human input can continue an exhausted goal");

    const clamped = controller.create(goalInput({
        goalId: "clamped-goal",
        maxAttempts: GLOBAL_MAX_ATTEMPTS + 100,
        maxWallTimeMs: 999999999,
    }));
    equal(clamped.maxAttempts, GLOBAL_MAX_ATTEMPTS, "goal attempts cannot exceed the code-level global cap");
    equal(clamped.maxWallTimeMs, GLOBAL_MAX_WALL_TIME_MS, "goal wall time cannot exceed the code-level global cap");
    const versioned = controller.create(goalInput({
        goalId: "versioned-goal",
        artifactVersion: "3",
        rollbackPointer: "artifact-v2",
    }));
    equal(versioned.rollbackPointer, "artifact-v2", "goal metadata carries the existing rollback pointer");

    const widened = controller.create(goalInput({
        goalId: "widening-goal",
        verificationLevel: VERIFICATION_LEVELS.HUMAN_CHECKPOINT,
        terminationPredicate: { type: "human_approval" },
    }));
    const widenedResult = await controller.trigger(
        widened.goalId,
        { source: "system_opportunity", id: "event-4" },
        { policy: automaticPolicy, execute: async () => ({ humanApproved: true }) }
    );
    equal(widenedResult.status, "awaiting_human", "human verification cannot be widened to automatic");
    equal(widenedResult.reason, "mode_policy_rejected", "mode-policy rejection is explicit");
    const quietWidening = controller.create(goalInput({
        goalId: "quiet-widening-goal",
        authoringMode: "semi_auto_confirm",
        verificationLevel: VERIFICATION_LEVELS.HUMAN_CHECKPOINT,
        terminationPredicate: { type: "human_approval" },
    }));
    const quietWideningResult = await controller.trigger(
        quietWidening.goalId,
        { source: "system_opportunity", id: "event-4b" },
        { policy: automaticPolicy, execute: async () => ({ humanApproved: true }) }
    );
    equal(quietWideningResult.reason, "mode_policy_rejected",
        "semi-automatic labeling cannot hide a required L4/L5 route");

    const delayed = controller.create(goalInput({
        goalId: "delayed-goal",
        objective: "wait for the runtime observation",
        interactionMode: "L4",
        authoringMode: "semi_auto_confirm",
        triggerSource: "explicit_request",
        verificationLevel: VERIFICATION_LEVELS.DELAYED_GROUND_TRUTH,
        terminationPredicate: { type: "delayed_signal", signal: "runtime_observed", value: true },
    }));
    const delayedStart = await controller.trigger(
        delayed.goalId,
        { source: "explicit_request", id: "event-5" },
        { policy: { riskScore: 0.2, reversible: true, localOnly: true, detailResolved: true },
            execute: async () => ({}) }
    );
    equal(delayedStart.status, "waiting_delayed_ground_truth", "delayed verifier persists a pending evaluation");
    const pending = controller.memory.pendingDelayed(delayed.goalId);
    ok(pending && pending.signal === "runtime_observed", "pending delayed signal survives outside the iteration");
    const delayedResult = await controller.resolveDelayed(
        delayed.goalId,
        { signal: "runtime_observed", value: true, at: pending.queuedAt + 250 },
        { policy: { riskScore: 0.2, reversible: true, localOnly: true, detailResolved: true } }
    );
    equal(delayedResult.status, "completed", "later ground truth can terminate the persisted goal");
    ok(artifactLog.history({ limit: 500 }).some((entry) =>
        entry.goalId === delayed.goalId && entry.resolutionLatencyMs === 250),
    "delayed resolution latency is logged");

    controller.activateKillSwitch("operator_stop");
    const restarted = new GoalLoopController({ artifactLog: new ArtifactLog({ filePath: logPath }) });
    equal(restarted.killSwitch.active, true, "global kill switch persists across controller restart");
    const killGoal = restarted.create(goalInput({ goalId: "kill-goal" }));
    const killed = await restarted.trigger(
        killGoal.goalId,
        { source: "system_opportunity", id: "event-6" },
        { policy: automaticPolicy, execute: async () => ({ artifactCommitted: true }) }
    );
    equal(killed.status, "killed", "persistent kill switch stops the next iteration");
    assert.throws(() => restarted.clearKillSwitch({ humanApproved: false }), /explicit human approval/);
    assertions += 1;
    restarted.clearKillSwitch({ humanApproved: true });

    const predictor = new FutureGoalPredictor({
        artifactLog,
        predictionProvider: async () => [{
            objective: "prepare a reversible training cue for cube-1",
            targetObjectId: "cube-1",
            confidence: 0.9,
            rationaleCode: "recent_training_flow",
        }],
    });
    const context = {
        targetObjectId: "cube-1",
        sceneEpoch: "epoch-7",
        snapshotId: "snapshot-9",
        objectRevision: 4,
        experienceMode: "training",
    };
    const prepared = await predictor.prepareDuringIdle({
        sessionId: "goal-test-session",
        correlationId: "corr-prediction",
        context,
        isIdle: true,
        generateCandidate: async () => ({
            candidateId: "predicted-candidate",
            candidateSetId: "predicted-set",
            validationState: "accepted",
            riskScore: 0.1,
            artifact: { operation: "create", code: "public class TrainingCue {}" },
        }),
        simulateCandidate: async () => ({ status: "simulated" }),
    });
    equal(prepared.prepared.length, 1, "idle predictor retains validated and simulated drafts");
    ok(!artifactLog.history({ limit: 1000 }).some((entry) =>
        entry.correlationId === "corr-prediction" && ["artifact_committed", "commit_accepted"].includes(entry.eventType)),
    "speculative preparation never writes a commit event");
    const selected = predictor.selectForActualGoal({
        sessionId: "goal-test-session",
        correlationId: "corr-actual",
        actualObjective: "prepare a reversible training cue for cube-1",
        targetObjectId: "cube-1",
        ...context,
    });
    ok(selected.selected, "fresh semantically matching prediction can be selected");
    equal(selected.requiresNormalGates, true, "selected prediction must re-enter normal gates");
    equal(selected.mayCommitAutomatically, false, "selected prediction cannot commit automatically");
    const stale = predictor.selectForActualGoal({
        sessionId: "goal-test-session",
        correlationId: "corr-stale",
        actualObjective: "prepare a reversible training cue for cube-1",
        targetObjectId: "cube-1",
        ...context,
        objectRevision: 5,
    });
    equal(stale.selected, null, "stale predicted artifact is rejected");
    const busy = await predictor.prepareDuringIdle({
        sessionId: "goal-test-session",
        correlationId: "corr-busy",
        context,
        isIdle: false,
        generateCandidate: async () => { throw new Error("must not generate while busy"); },
        simulateCandidate: async () => { throw new Error("must not simulate while busy"); },
    });
    equal(busy.status, "skipped_busy", "prediction does no work outside an idle window");

    const persisted = new GoalLoopController({ artifactLog: new ArtifactLog({ filePath: logPath }) });
    equal(persisted.get(deterministic.goalId).status, "completed", "goal state survives process restart");
    console.log(`[goal_loop_test] PASS (${assertions} assertions)`);
}

main().catch((err) => {
    console.error(err.stack || err.message);
    process.exitCode = 1;
});
