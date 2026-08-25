"use strict";

// The six step pipeline is described only in the router's system prompt, so its
// ordering was enforced solely by the model following instructions. These tests
// pin down the deterministic guard.

const assert = require("assert");
const { PipelineTracker, STEPS } = require("../orchestrator/pipeline_policy");
const { STUDY_CONDITIONS, SIMULATION_SKIPPED_STATUS } = require("../orchestrator/mode_policy");

let assertions = 0;
function check(condition, message) {
    assert.ok(condition, `FAILED: ${message}`);
    assertions += 1;
}

function fullPipeline(overrides = {}) {
    const tracker = new PipelineTracker({ correlationId: "corr-1", ...overrides });
    tracker.record(STEPS.QUERY_SCENE, { targetObjectId: "door-1", snapshotId: "snap-1" });
    tracker.record(STEPS.SIMULATE, {});
    tracker.record(STEPS.RANK, {});
    tracker.record(STEPS.CONFLICT, {});
    tracker.record(STEPS.QUERY_SCENE, { targetObjectId: "door-1", snapshotId: "snap-2" });
    return tracker;
}

// 1. A correctly ordered pipeline is allowed.
{
    const result = fullPipeline().checkProposeAllowed({ targetObjectId: "door-1", candidateCount: 3, snapshotId: "snap-2" });
    check(result.allowed === true, `a correctly ordered pipeline is allowed (${result.reasons.join("; ")})`);
    check(result.reasons.length === 0, "a correct pipeline reports no reasons");
}

// 2. Proposing without a dry run is refused. The prompt says the model may not
// skip one on its own; now it cannot.
{
    const tracker = new PipelineTracker({ correlationId: "c" });
    tracker.record(STEPS.QUERY_SCENE, {}).record(STEPS.CONFLICT, {});
    const result = tracker.checkProposeAllowed({ targetObjectId: "door-1" });
    check(result.allowed === false, "a proposal without a dry run is refused");
    check(result.reasons.some((r) => r.includes("no dry run")), "the reason names the missing dry run");
}

// 3. The H2 arm bypasses dry runs, but must record a skipped one rather than
// omit it, so "nobody looked" never reads as "the arm skipped it by design".
{
    const omitted = new PipelineTracker({ correlationId: "c", condition: STUDY_CONDITIONS.NO_VERIFICATION });
    omitted.record(STEPS.QUERY_SCENE, {}).record(STEPS.CONFLICT, {});
    const refused = omitted.checkProposeAllowed({ targetObjectId: "door-1" });
    check(refused.allowed === false, "the no-verification arm still needs a recorded dry run");
    check(refused.reasons.some((r) => r.includes(SIMULATION_SKIPPED_STATUS)),
        "the reason tells the arm to record a skipped dry run");

    const marked = new PipelineTracker({ correlationId: "c", condition: STUDY_CONDITIONS.NO_VERIFICATION });
    marked.record(STEPS.QUERY_SCENE, {}).record(STEPS.SIMULATE, { status: SIMULATION_SKIPPED_STATUS })
        .record(STEPS.CONFLICT, {}).record(STEPS.QUERY_SCENE, {});
    check(marked.checkProposeAllowed({ targetObjectId: "door-1" }).allowed === true,
        "a recorded skipped dry run satisfies the no-verification arm");
}

// 4. Multiple candidates must be ranked, or H4's best of N has no basis.
{
    const tracker = new PipelineTracker({ correlationId: "c" });
    tracker.record(STEPS.QUERY_SCENE, {}).record(STEPS.SIMULATE, {}).record(STEPS.CONFLICT, {}).record(STEPS.QUERY_SCENE, {});
    check(tracker.checkProposeAllowed({ targetObjectId: "d", candidateCount: 3 }).allowed === false,
        "three unranked candidates are refused");
    check(tracker.checkProposeAllowed({ targetObjectId: "d", candidateCount: 1 }).allowed === true,
        "a single candidate needs no ranking");
}

// 5. The conflict resolver must have run.
{
    const tracker = new PipelineTracker({ correlationId: "c" });
    tracker.record(STEPS.QUERY_SCENE, {}).record(STEPS.SIMULATE, {}).record(STEPS.QUERY_SCENE, {});
    const result = tracker.checkProposeAllowed({ targetObjectId: "d" });
    check(result.allowed === false, "a proposal without a conflict check is refused");
    check(result.reasons.some((r) => r.includes("conflict resolver")), "the reason names the conflict resolver");
}

// 6. The mandatory freshness refresh. A query_scene that predates the dry run
// does not ground the proposal, which is the staleness the step exists to stop.
{
    const stale = new PipelineTracker({ correlationId: "c" });
    stale.record(STEPS.QUERY_SCENE, {}).record(STEPS.SIMULATE, {}).record(STEPS.RANK, {}).record(STEPS.CONFLICT, {});
    const result = stale.checkProposeAllowed({ targetObjectId: "d", candidateCount: 2 });
    check(result.allowed === false, "a proposal whose last refresh predates the dry run is refused");
    check(result.reasons.some((r) => r.includes("predates")), "the reason explains the staleness");
}
{
    const none = new PipelineTracker({ correlationId: "c" });
    none.record(STEPS.SIMULATE, {}).record(STEPS.CONFLICT, {});
    check(none.checkProposeAllowed({ targetObjectId: "d" }).reasons.some((r) => r.includes("no query_scene")),
        "a pipeline with no refresh at all is refused");
}

// 7. The refresh must have grounded the object actually being proposed.
{
    const result = fullPipeline().checkProposeAllowed({ targetObjectId: "lamp-9", candidateCount: 1 });
    check(result.allowed === false, "a proposal targeting a different object than the refresh is refused");
    check(result.reasons.some((r) => r.includes("lamp-9")), "the reason names the mismatched target");
}

// 8. The proposal must carry the snapshot the refresh produced, not an older one.
{
    const result = fullPipeline().checkProposeAllowed({ targetObjectId: "door-1", candidateCount: 1, snapshotId: "snap-1" });
    check(result.allowed === false, "a proposal carrying a stale snapshotId is refused");
    check(result.reasons.some((r) => r.includes("snapshotId")), "the reason names the snapshot mismatch");
}

// 9. Every violation is reported at once, so one fix does not just reveal the next.
{
    const empty = new PipelineTracker({ correlationId: "c" });
    const result = empty.checkProposeAllowed({ targetObjectId: "d", candidateCount: 3 });
    check(result.reasons.length >= 4, `all violations are reported together (got ${result.reasons.length})`);
}

// 10. assertProposeAllowed throws, and misuse is refused.
{
    let threw = false;
    try { new PipelineTracker({ correlationId: "c" }).assertProposeAllowed({ targetObjectId: "d" }); }
    catch (error) { threw = /pipeline order violated/.test(error.message); }
    check(threw, "assertProposeAllowed throws on a violation");

    let passed = false;
    try { fullPipeline().assertProposeAllowed({ targetObjectId: "door-1", candidateCount: 3, snapshotId: "snap-2" }); passed = true; } catch { passed = false; }
    check(passed, "assertProposeAllowed passes a correct pipeline");

    let ctorThrew = false;
    try { new PipelineTracker({}); } catch { ctorThrew = true; }
    check(ctorThrew, "a tracker requires a correlationId");

    let stepThrew = false;
    try { new PipelineTracker({ correlationId: "c" }).record("invented_step"); } catch { stepThrew = true; }
    check(stepThrew, "an unknown pipeline step is refused");
}

// 11. The summary reports the observed order, for logging.
{
    const summary = fullPipeline().summary();
    check(summary.steps[0] === STEPS.QUERY_SCENE, "the summary preserves order");
    check(summary.steps.length === 5, "the summary lists every recorded step");
}

console.log(`[pipeline_policy_test] PASS (${assertions} assertions)`);
