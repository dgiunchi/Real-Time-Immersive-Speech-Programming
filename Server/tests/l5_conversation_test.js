"use strict";

// L5 conversational co-authoring: drives the chain through the two standardised
// revisions the protocol specifies, and checks every terminal state is recorded.
//
// The gap this covers: scoreL5 recorded no terminal state at all, so a trial
// that timed out, one the participant rejected and one that was cancelled all
// scored identically, with sequenceRuns: false as the only signal.

const assert = require("assert");
const { InteractionSessionStore } = require("../study/interaction_session_store");
const rubric = require("../study/rubric_scoring");

let assertions = 0;
function check(condition, message) {
    assert.ok(condition, `FAILED: ${message}`);
    assertions += 1;
}

function beginL5(sessionId) {
    const store = new InteractionSessionStore();
    store.begin({ sessionId, mode: "L5", correlationId: `${sessionId}-corr`, targetObjectId: "study-l5-a-root" });
    store.recordUtterance({ sessionId, text: "make a three step sequence" });
    return store;
}

// 1. The protocol's two revisions: the chain requires both before a decision.
{
    const store = beginL5("s-two");
    const first = store.recordUtterance({ sessionId: "s-two", text: "make the steps slower" });
    check(first.state !== "awaiting_decision", "one revision is not enough to reach a decision");
    const second = store.recordUtterance({ sessionId: "s-two", text: "make the sequence reset after it finishes" });
    check(second.state === "awaiting_decision", "the second revision reaches the decision point");
    check(second.revisionCount >= 2, `both revisions are counted (got ${second.revisionCount})`);
}

// 2. Every terminal state is reachable and terminal.
for (const [decision, expected] of [["approve", "approved"], ["reject", "rejected"],
    ["timeout", "timed_out"], ["undo", "undone"], ["cancel", "cancelled"]]) {
    const store = beginL5(`s-${decision}`);
    store.recordUtterance({ sessionId: `s-${decision}`, text: "slower" });
    store.recordUtterance({ sessionId: `s-${decision}`, text: "reset after finishing" });
    const decided = store.recordDecision({ sessionId: `s-${decision}`, decision });
    check(decided.state === expected, `${decision} reaches ${expected}`);
    check(decided.terminal === true, `${expected} is terminal`);
}

// 3. A terminal chain refuses further input.
{
    const store = beginL5("s-closed");
    store.recordUtterance({ sessionId: "s-closed", text: "a" });
    store.recordUtterance({ sessionId: "s-closed", text: "b" });
    store.recordDecision({ sessionId: "s-closed", decision: "approve" });
    let threw = false;
    try { store.recordDecision({ sessionId: "s-closed", decision: "reject" }); } catch { threw = true; }
    check(threw, "a closed L5 chain refuses a further decision");
}

// 4. Scoring records the terminal state, which it previously discarded.
for (const terminalState of ["approved", "rejected", "timed_out", "undone", "cancelled"]) {
    const scored = rubric.scoreL5({
        slowerStepsRevision: 2, resetAfterFinishRevision: 1, sequenceRuns: true,
        priorRequirementRestatementCount: 0, conversationTerminalState: terminalState,
    });
    check(scored.conversationTerminalState === terminalState, `${terminalState} is recorded verbatim`);
    check(typeof scored.conversationOutcome === "string", `${terminalState} maps to an outcome`);
}

// 5. The distinction that matters: a timeout scores as a rejection per the
// protocol, but it is not a decision the participant made.
{
    const timedOut = rubric.scoreL5({ slowerStepsRevision: 1, resetAfterFinishRevision: 1, sequenceRuns: false,
        priorRequirementRestatementCount: 0, conversationTerminalState: "timed_out" });
    const rejected = rubric.scoreL5({ slowerStepsRevision: 1, resetAfterFinishRevision: 1, sequenceRuns: false,
        priorRequirementRestatementCount: 0, conversationTerminalState: "rejected" });
    check(timedOut.conversationOutcome === "rejected", "a timeout scores as a rejection, per the protocol");
    check(rejected.conversationOutcome === "rejected", "an explicit rejection also scores as a rejection");
    check(timedOut.reachedDecision === false, "a timeout is not a decision the participant made");
    check(rejected.reachedDecision === true, "an explicit rejection is a decision");
    check(timedOut.conversationTerminalState !== rejected.conversationTerminalState,
        "the two remain distinguishable in the record, which they previously were not");
}

// 6. An absent terminal state is "none" and is not a decision, so an unscored
// trial cannot masquerade as one that reached a verdict.
{
    const scored = rubric.scoreL5({ slowerStepsRevision: 0, resetAfterFinishRevision: 0, sequenceRuns: false,
        priorRequirementRestatementCount: 0 });
    check(scored.conversationOutcome === "none", "an absent terminal state is none");
    check(scored.reachedDecision === false, "an absent terminal state is not a decision");
}

// 7. taskSuccess keeps its existing rule. Whether a trial that never reached a
// decision can still be a success is a design question, so the rule is
// unchanged and reachedDecision is reported alongside for the analysis.
{
    const base = { slowerStepsRevision: 1, resetAfterFinishRevision: 1, sequenceRuns: true, priorRequirementRestatementCount: 0 };
    check(rubric.scoreL5({ ...base, conversationTerminalState: "approved" }).taskSuccess === true,
        "both revisions plus a running sequence is a success");
    check(rubric.scoreL5({ ...base, slowerStepsRevision: 0, conversationTerminalState: "approved" }).taskSuccess === false,
        "a failed first revision is not a success");
    check(rubric.scoreL5({ ...base, sequenceRuns: false, conversationTerminalState: "approved" }).taskSuccess === false,
        "a sequence that does not run is not a success");
    check(rubric.scoreL5({ ...base, conversationTerminalState: "timed_out" }).taskSuccess === true,
        "taskSuccess is unchanged by the terminal state, which stays a design decision");
}

// 8. Restatement count is validated, since it measures whether the participant
// had to repeat a requirement the agent should have retained.
{
    for (const bad of [-1, 1.5, "2", null]) {
        let threw = false;
        try { rubric.scoreL5({ slowerStepsRevision: 1, resetAfterFinishRevision: 1, sequenceRuns: true, priorRequirementRestatementCount: bad }); }
        catch { threw = true; }
        check(threw, `a restatement count of ${JSON.stringify(bad)} is refused`);
    }
}

// 9. An unmapped terminal state is refused rather than silently scored.
{
    let threw = false;
    try {
        rubric.scoreL5({ slowerStepsRevision: 1, resetAfterFinishRevision: 1, sequenceRuns: true,
            priorRequirementRestatementCount: 0, conversationTerminalState: "invented" });
    } catch { threw = true; }
    check(threw, "an unmapped L5 terminal state is refused");
}

console.log(`[l5_conversation_test] PASS (${assertions} assertions)`);
