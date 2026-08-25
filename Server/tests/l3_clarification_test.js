"use strict";

// L3 asks for a ball above the raised open hand without stating a height.
//
// The task is the clarification, not the ball. An agent that guesses a height and
// generates anyway can still put the ball in exactly the right place, and the
// protocol counts that as a grounding failure rather than a success, because the
// artifact was right by luck. These tests pin that distinction down.

const assert = require("assert");
const rubric = require("../study/rubric_scoring");

let assertions = 0;
function check(condition, message) {
    assert.ok(condition, `FAILED: ${message}`);
    assertions += 1;
}

const asked = {
    clarificationAsked: true, statedHeightMeters: 0.30, spawnedHeightMeters: 0.30,
    ballSpawned: true, spawnedAboveOpenHand: true, handRaised: true,
    repairInitiator: "none", qualityScore: 2,
};

// 1. The complete correct turn.
{
    const scored = rubric.scoreL3(asked);
    check(scored.taskSuccess === true, "asking, then spawning at the stated height, is a success");
    check(scored.groundingFailure === false, "a clarified turn is not a grounding failure");
    check(scored.heightWithinTolerance === true, "an exact height is within tolerance");
    check(scored.heightErrorMeters === 0, "the height error is reported");
}

// 2. The failure the task exists to detect: a correct ball, never clarified.
{
    const guessed = rubric.scoreL3({ ...asked, clarificationAsked: false });
    check(guessed.taskSuccess === false, "spawning without asking is not a success even when the height matches");
    check(guessed.groundingFailure === true, "spawning without asking is recorded as a grounding failure");
}

// 3. Clarifying and then getting the height wrong is a failure, but not a
// grounding failure. The two must stay distinguishable in the record.
{
    const wrongHeight = rubric.scoreL3({ ...asked, spawnedHeightMeters: 0.75 });
    check(wrongHeight.taskSuccess === false, "a wrong height is not a success");
    check(wrongHeight.groundingFailure === false, "a wrong height after asking is not a grounding failure");
    check(Math.abs(wrongHeight.heightErrorMeters - 0.45) < 1e-9, "the height error is measured, not just flagged");
}

// 4. Tolerance: the height is a spoken answer, so it is judged within a band.
{
    check(rubric.scoreL3({ ...asked, spawnedHeightMeters: 0.34 }).heightWithinTolerance === true,
        "4cm off is within the default 5cm tolerance");
    check(rubric.scoreL3({ ...asked, spawnedHeightMeters: 0.36 }).heightWithinTolerance === false,
        "6cm off is outside the default tolerance");
    check(rubric.scoreL3({ ...asked, spawnedHeightMeters: 0.36, heightToleranceMeters: 0.10 }).heightWithinTolerance === true,
        "the tolerance is configurable per protocol");
}

// 5. Every placement condition is required, since the task names all of them.
{
    check(rubric.scoreL3({ ...asked, ballSpawned: false }).taskSuccess === false, "no ball is not a success");
    check(rubric.scoreL3({ ...asked, spawnedAboveOpenHand: false }).taskSuccess === false,
        "a ball not above the open hand is not a success");
    check(rubric.scoreL3({ ...asked, handRaised: false }).taskSuccess === false,
        "a ball spawned without the hand raised is not a success");
}

// 6. Missing heights are reported as absent rather than silently passing.
{
    const noHeights = rubric.scoreL3({ ...asked, statedHeightMeters: null, spawnedHeightMeters: null });
    check(noHeights.taskSuccess === false, "absent heights cannot be a success");
    check(noHeights.heightWithinTolerance === false, "absent heights are not within tolerance");
    check(noHeights.heightErrorMeters === null, "an absent height error is null, not zero");
}

// 7. Validation is strict, so a malformed record is refused rather than scored.
{
    for (const [bad, label] of [
        [{ ...asked, clarificationAsked: "yes" }, "a non-boolean clarificationAsked"],
        [{ ...asked, repairInitiator: "somebody" }, "an unknown repairInitiator"],
        [{ ...asked, qualityScore: 3 }, "an out-of-range qualityScore"],
    ]) {
        let threw = false;
        try { rubric.scoreL3(bad); } catch { threw = true; }
        check(threw, `${label} is refused`);
    }
}

// 8. Who initiated a repair is preserved, since it separates the agent noticing
// its own error from the participant having to correct it.
{
    for (const initiator of ["system", "participant", "none"]) {
        check(rubric.scoreL3({ ...asked, repairInitiator: initiator }).repairInitiator === initiator,
            `repairInitiator '${initiator}' is preserved`);
    }
}

console.log(`[l3_clarification_test] PASS (${assertions} assertions)`);
