"use strict";

// The Conflict Resolver's verdict was advisory: nothing consumed it, so a model
// could answer "queue" and propose anyway, or skip the step entirely. These
// tests pin down the deterministic gate that now consumes it.

const assert = require("assert");
const { checkConflictDecision, assertConflictClear, CONFLICT_ACTIONS } = require("../orchestrator/conflict_policy");

let assertions = 0;
function check(condition, message) {
    assert.ok(condition, `FAILED: ${message}`);
    assertions += 1;
}

// 1. proceed is the only verdict that permits a commit.
{
    const result = checkConflictDecision({ decision: "proceed" });
    check(result.accepted === true, "proceed is accepted");
    check(result.action === CONFLICT_ACTIONS.PROCEED, "proceed maps to the proceed action");
    check(result.commitAllowed === true, "proceed permits a commit");
}

// 2. queue and redirect are honoured rather than ignored, and neither commits.
for (const [decision, action] of [["queue", CONFLICT_ACTIONS.QUEUE], ["redirect", CONFLICT_ACTIONS.REDIRECT]]) {
    const result = checkConflictDecision({ decision, reason: "another change in flight" });
    check(result.accepted === true, `${decision} is a valid verdict`);
    check(result.action === action, `${decision} maps to the ${action} action`);
    check(result.commitAllowed === false, `${decision} blocks the commit`);
    check(result.reasons.join(" ").includes("another change in flight"), `${decision} carries the resolver's reason`);
}

// 3. Case and whitespace do not change the verdict.
{
    check(checkConflictDecision({ decision: "  PROCEED " }).commitAllowed === true, "verdicts are normalised");
    check(checkConflictDecision({ decision: "Queue" }).action === CONFLICT_ACTIONS.QUEUE, "queue is normalised");
}

// 4. The gate fails closed. A skipped resolver must not look like permission.
{
    const skipped = checkConflictDecision({ resolverRan: false });
    check(skipped.accepted === false, "a skipped resolver is not accepted");
    check(skipped.action === CONFLICT_ACTIONS.REJECT, "a skipped resolver is rejected");
    check(skipped.commitAllowed === false, "a skipped resolver blocks the commit");
    check(skipped.reasons.join(" ").includes("did not run"), "the reason names the skipped step");
}

// 5. Absent, empty and malformed verdicts are all rejected, not defaulted.
for (const [verdict, label] of [
    [{}, "absent decision"],
    [{ decision: "" }, "empty decision"],
    [{ decision: null }, "null decision"],
    [{ decision: "yes" }, "unrecognised decision"],
    [{ decision: "PROCEED_ANYWAY" }, "decision that merely looks permissive"],
]) {
    const result = checkConflictDecision(verdict);
    check(result.commitAllowed === false, `${label} blocks the commit`);
    check(result.action === CONFLICT_ACTIONS.REJECT, `${label} is rejected`);
}

// 6. Evidence beats the verdict. A resolver that says proceed while reporting a
// foreign in-flight change is contradicting its own evidence, and the evidence
// wins, because that is the case the gate exists to catch.
{
    const contradiction = checkConflictDecision({
        decision: "proceed",
        correlationId: "mine",
        inFlightCorrelationIds: ["someone-else"],
    });
    check(contradiction.commitAllowed === false, "proceed is overridden by a foreign in-flight change");
    check(contradiction.action === CONFLICT_ACTIONS.QUEUE, "the contradiction is queued rather than rejected outright");
    check(contradiction.reasons.join(" ").includes("in flight"), "the reason explains the override");
}

// 7. The chain's own correlation id is not treated as a foreign conflict.
{
    const ownWork = checkConflictDecision({
        decision: "proceed",
        correlationId: "mine",
        inFlightCorrelationIds: ["mine"],
    });
    check(ownWork.commitAllowed === true, "a chain does not conflict with itself");
}

// 8. Empty and malformed in-flight lists do not create phantom conflicts.
{
    check(checkConflictDecision({ decision: "proceed", inFlightCorrelationIds: [] }).commitAllowed === true,
        "an empty in-flight list permits the commit");
    check(checkConflictDecision({ decision: "proceed", inFlightCorrelationIds: null }).commitAllowed === true,
        "a null in-flight list is tolerated");
    check(checkConflictDecision({ decision: "proceed", inFlightCorrelationIds: [null, ""] }).commitAllowed === true,
        "empty entries are not counted as conflicts");
}

// 9. assertConflictClear throws for every non-committable outcome.
{
    let passed = false;
    try { assertConflictClear({ decision: "proceed" }); passed = true; } catch { passed = false; }
    check(passed, "assertConflictClear passes a clear verdict");

    for (const verdict of [{ decision: "queue" }, { decision: "redirect" }, { resolverRan: false }, {}]) {
        let threw = false;
        try { assertConflictClear(verdict); } catch (error) { threw = /conflict gate refused/.test(error.message); }
        check(threw, `assertConflictClear throws for ${JSON.stringify(verdict)}`);
    }
}

console.log(`[conflict_policy_test] PASS (${assertions} assertions)`);
