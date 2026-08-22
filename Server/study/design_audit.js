"use strict";

const { loadProtocol, generateParticipantPlan } = require("./protocol");

function increment(map, key) {
    map.set(key, (map.get(key) || 0) + 1);
}

function factorial(value) {
    let result = 1;
    for (let current = 2; current <= value; current += 1) result *= current;
    return result;
}

function expectedCellsPresent(counts, keys, expectedCount) {
    return counts.size === keys.length && keys.every((key) => counts.get(key) === expectedCount);
}

function auditDesign({ participantCount } = {}) {
    const protocol = loadProtocol();
    const count = participantCount == null ? protocol.design.targetParticipants.maximum : Number(participantCount);
    const checks = [];
    const check = (ok, id, detail, evidence = null) => checks.push({ ok: Boolean(ok), id, detail, evidence });
    if (!Number.isInteger(count) || count < 1 || count > 999) {
        check(false, "participant-count-valid", "participantCount must be an integer from 1 to 999", count);
        return { ok: false, participantCount: count, protocolId: protocol.protocolId, methodVersion: protocol.methodVersion, checks };
    }

    const plans = [];
    try {
        for (let ordinal = 1; ordinal <= count; ordinal += 1) {
            plans.push(generateParticipantPlan(`P${String(ordinal).padStart(3, "0")}`));
        }
    } catch (error) {
        check(false, "plan-generation", error.message);
        return { ok: false, participantCount: count, protocolId: protocol.protocolId, methodVersion: protocol.methodVersion, checks };
    }

    const tasks = protocol.tasks;
    const variants = protocol.design.taskVariants;
    const orderCount = factorial(protocol.design.counterbalancedTaskSet.length);
    const permutationCounts = new Map();
    const conditionPositionCounts = new Map();
    const variantConditionCounts = new Map();
    const variantPositionCounts = new Map();
    const h4TaskArmCounts = new Map();
    const h4FirstCounts = new Map();
    const reachableConditions = new Set();
    let variantsDiffer = true;
    let h4TrialsExact = true;
    let trialsExact = true;
    let duplicateCondition = false;

    for (const plan of plans) {
        increment(permutationCounts, plan.assignment.explicitTaskOrder.join("|"));
        trialsExact = trialsExact && plan.trials.length === protocol.design.trialsPerParticipant;
        const h4Trials = plan.trials.filter((trial) => trial.candidateTarget !== null)
            .sort((left, right) => left.sequenceIndex - right.sequenceIndex);
        h4TrialsExact = h4TrialsExact && h4Trials.length === protocol.tasks.filter((task) => task.h4Eligible).length;
        if (h4Trials.length) increment(h4FirstCounts, String(h4Trials[0].candidateTarget));
        for (const trial of h4Trials) increment(h4TaskArmCounts, `${trial.taskId}|${trial.candidateTarget}`);

        for (const task of tasks) {
            const pair = plan.trials.filter((trial) => trial.taskId === task.taskId)
                .sort((left, right) => left.sequenceIndex - right.sequenceIndex);
            const aliases = pair.map((trial) => trial.conditionAlias);
            duplicateCondition = duplicateCondition || new Set(aliases).size !== aliases.length;
            variantsDiffer = variantsDiffer && pair.length === task.conditionPair.length &&
                new Set(pair.map((trial) => trial.taskVariant)).size === pair.length;
            pair.forEach((trial, position) => {
                reachableConditions.add(trial.conditionAlias);
                increment(conditionPositionCounts, `${task.taskId}|${trial.conditionAlias}|${position}`);
                increment(variantConditionCounts, `${task.taskId}|${trial.taskVariant}|${trial.conditionAlias}`);
                increment(variantPositionCounts, `${task.taskId}|${trial.taskVariant}|${position}`);
            });
        }
    }

    const equalFrequency = (map) => map.size > 0 && new Set(map.values()).size === 1;
    const conditionPositionKeys = tasks.flatMap((task) => task.conditionPair.flatMap((condition) =>
        task.conditionPair.map((_, position) => `${task.taskId}|${condition}|${position}`)));
    const variantConditionKeys = tasks.flatMap((task) => variants.flatMap((variant) =>
        task.conditionPair.map((condition) => `${task.taskId}|${variant}|${condition}`)));
    const variantPositionKeys = tasks.flatMap((task) => variants.flatMap((variant) =>
        task.conditionPair.map((_, position) => `${task.taskId}|${variant}|${position}`)));
    const h4Tasks = tasks.filter((task) => task.h4Eligible);
    const h4TaskArmKeys = h4Tasks.flatMap((task) => protocol.design.h4CandidateCounts
        .map((arm) => `${task.taskId}|${arm}`));
    const perCell = count / 2;

    check(count % orderCount === 0, "latin-square-complete", `${count} % ${orderCount} === 0`, Object.fromEntries(permutationCounts));
    check(permutationCounts.size === orderCount && equalFrequency(permutationCounts), "task-order-balanced",
        "each task-order permutation is equally frequent", Object.fromEntries(permutationCounts));
    check(expectedCellsPresent(conditionPositionCounts, conditionPositionKeys, perCell), "condition-position-balanced",
        "condition x within-task position is balanced per task", Object.fromEntries(conditionPositionCounts));
    check(expectedCellsPresent(variantConditionCounts, variantConditionKeys, perCell), "variant-condition-balanced",
        "variant x condition is balanced per task", Object.fromEntries(variantConditionCounts));
    check(expectedCellsPresent(variantPositionCounts, variantPositionKeys, perCell), "variant-position-balanced",
        "variant x within-task position is balanced per task", Object.fromEntries(variantPositionCounts));
    check(variantsDiffer, "within-task-variants-differ", "each participant receives distinct variants within a task");
    check(expectedCellsPresent(h4TaskArmCounts, h4TaskArmKeys, perCell), "h4-task-arm-balanced",
        "H4 arm x task is balanced", Object.fromEntries(h4TaskArmCounts));
    check(expectedCellsPresent(h4FirstCounts, protocol.design.h4CandidateCounts.map(String), perCell),
        "h4-presentation-order-balanced", "H4 arm x presentation order is balanced", Object.fromEntries(h4FirstCounts));
    check(h4TrialsExact, "h4-trials-per-participant", `exactly ${h4Tasks.length} H4 trials per participant`);
    check(trialsExact, "trials-per-participant", `exactly ${protocol.design.trialsPerParticipant} trials per participant`);
    check(Object.keys(protocol.conditions).every((condition) => reachableConditions.has(condition)), "conditions-reachable",
        "every declared condition is reachable", [...reachableConditions]);
    check(!duplicateCondition, "no-duplicate-condition-within-task", "no condition repeats within a participant task");

    return {
        ok: checks.every((item) => item.ok), participantCount: count,
        protocolId: protocol.protocolId, methodVersion: protocol.methodVersion,
        checks,
        summaries: {
            taskOrders: Object.fromEntries(permutationCounts),
            h4TaskArms: Object.fromEntries(h4TaskArmCounts),
            h4PresentationOrder: Object.fromEntries(h4FirstCounts),
        },
    };
}

module.exports = { auditDesign };
