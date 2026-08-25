"use strict";

// L4 confirmation gated authoring: drives the interaction chain through every
// terminal state the contract allows, and checks each one can be scored.
//
// The gap this covers: the L4 state machine can end in approved, rejected,
// timed_out, undone or cancelled, but rubric_scoring.scoreL4 accepted only
// approved, rejected, revised or none. A trial that timed out, which the
// protocol explicitly anticipates, could not be scored at all.

const assert = require("assert");
const { InteractionSessionStore } = require("../study/interaction_session_store");
const rubric = require("../study/rubric_scoring");

let assertions = 0;
function check(condition, message) {
    assert.ok(condition, `FAILED: ${message}`);
    assertions += 1;
}

function beginL4(store, sessionId) {
    store.begin({ sessionId, mode: "L4", correlationId: `${sessionId}-corr`, targetObjectId: "study-l4-a-door" });
    store.recordUtterance({ sessionId, text: "open the practice door" });
    return store;
}

(async () => {
    // 1. The happy path: request, preview, approve.
    {
        const store = new InteractionSessionStore();
        beginL4(store, "s-approve");
        store.recordArtifact({ sessionId: "s-approve", artifactId: "art-1" });
        const decided = store.recordDecision({ sessionId: "s-approve", decision: "approve" });
        check(decided.state === "approved", "approve reaches the approved state");
        check(decided.terminal === true, "approved is terminal");
    }

    // 2. Rejection is terminal.
    {
        const store = new InteractionSessionStore();
        beginL4(store, "s-reject");
        const decided = store.recordDecision({ sessionId: "s-reject", decision: "reject" });
        check(decided.state === "rejected", "reject reaches the rejected state");
        check(decided.terminal === true, "rejected is terminal");
    }

    // 3. Revision loops back and can then be approved.
    {
        const store = new InteractionSessionStore();
        beginL4(store, "s-revise");
        const revising = store.recordDecision({ sessionId: "s-revise", decision: "revise" });
        check(revising.state === "revising", "revise reaches the revising state");
        check(revising.terminal !== true, "revising is not terminal");
        store.recordUtterance({ sessionId: "s-revise", text: "open it more slowly" });
        const approved = store.recordDecision({ sessionId: "s-revise", decision: "approve" });
        check(approved.state === "approved", "a revised proposal can still be approved");
    }

    // 4. Timeout, undo and cancel are all reachable terminal states.
    for (const [decision, expected] of [["timeout", "timed_out"], ["undo", "undone"], ["cancel", "cancelled"]]) {
        const store = new InteractionSessionStore();
        beginL4(store, `s-${decision}`);
        const decided = store.recordDecision({ sessionId: `s-${decision}`, decision });
        check(decided.state === expected, `${decision} reaches the ${expected} state`);
        check(decided.terminal === true, `${expected} is terminal`);
    }

    // 5. A terminal chain refuses further decisions rather than mutating.
    {
        const store = new InteractionSessionStore();
        beginL4(store, "s-closed");
        store.recordDecision({ sessionId: "s-closed", decision: "reject" });
        let threw = false;
        try { store.recordDecision({ sessionId: "s-closed", decision: "approve" }); } catch { threw = true; }
        check(threw, "a closed L4 chain refuses a further decision");
    }

    // 6. Every terminal state the machine can produce must be scoreable.
    // This is the gap: timed_out, undone and cancelled previously threw.
    for (const terminalState of ["approved", "rejected", "timed_out", "undone", "cancelled"]) {
        const outcome = rubric.consentGateOutcomeFor(terminalState);
        check(typeof outcome === "string", `${terminalState} maps to a consent gate outcome`);
        const scored = rubric.scoreL4({
            doorFullyOpen: terminalState === "approved",
            participantInsideApproachRegion: true,
            consentGatePresented: true,
            consentGateOutcome: outcome,
            qualityScore: 1,
        });
        check(scored.consentGateOutcome === outcome, `${terminalState} scores without throwing`);
    }

    // 7. The protocol rule: silence or timeout defaults to rejection.
    {
        check(rubric.consentGateOutcomeFor("timed_out") === "rejected",
            "a timed out consent gate is scored as a rejection, per the protocol");
        const scored = rubric.scoreL4({
            doorFullyOpen: false, participantInsideApproachRegion: true,
            consentGatePresented: true, consentGateOutcome: rubric.consentGateOutcomeFor("timed_out"), qualityScore: 0,
        });
        check(scored.taskSuccess === false, "a timed out L4 trial is not a task success");
        check(scored.consentGateOutcome === "rejected", "the scored outcome records the rejection");
    }

    // 8. An approved proposal that the participant then undid is not a success.
    {
        check(rubric.consentGateOutcomeFor("undone") === "undone", "undone keeps its own outcome rather than collapsing into rejected");
        const scored = rubric.scoreL4({
            doorFullyOpen: false, participantInsideApproachRegion: true,
            consentGatePresented: true, consentGateOutcome: "undone", qualityScore: 1,
        });
        check(scored.taskSuccess === false, "an undone L4 trial is not a task success");
    }

    // 9. Unknown states are still refused, so the mapping cannot silently absorb
    // a state someone adds to the contract without deciding how to score it.
    {
        let threw = false;
        try { rubric.consentGateOutcomeFor("invented_state"); } catch { threw = true; }
        check(threw, "an unmapped terminal state is refused rather than guessed");
        let threwScore = false;
        try { rubric.scoreL4({ doorFullyOpen: true, participantInsideApproachRegion: true, consentGatePresented: true, consentGateOutcome: "nonsense", qualityScore: 1 }); }
        catch { threwScore = true; }
        check(threwScore, "an invalid consent gate outcome is still refused");
    }

    // 10. Task success requires both the door open and the participant in the
    // approach region, which is what distinguishes L4 from a bare door toggle.
    {
        const both = rubric.scoreL4({ doorFullyOpen: true, participantInsideApproachRegion: true, consentGatePresented: true, consentGateOutcome: "approved", qualityScore: 2 });
        check(both.taskSuccess === true, "door open inside the region is a success");
        const outside = rubric.scoreL4({ doorFullyOpen: true, participantInsideApproachRegion: false, consentGatePresented: true, consentGateOutcome: "approved", qualityScore: 2 });
        check(outside.taskSuccess === false, "door open outside the approach region is not a success");
    }

    console.log(`[l4_consent_gate_test] PASS (${assertions} assertions)`);
})().catch((error) => { console.error(error); process.exit(1); });
