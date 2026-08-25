"use strict";

// Deterministic gate for the Conflict Resolver's verdict.
//
// The conflict_resolver subagent is asked to return proceed, queue or redirect,
// and the six step pipeline tells the router to call it before propose_artifact.
// Nothing consumed that verdict, so it was advisory: a model that answered
// "queue" and then proposed anyway would not be stopped, and a model that
// skipped the step entirely would not be noticed.
//
// This mirrors checkModePolicy in mode_policy.js, which exists so the model's
// own classification cannot widen autonomy beyond the L1 to L5 contract. Same
// idea applied to conflicts: the model may report a conflict, it may not decide
// to ignore one.
//
// The gate fails closed. An absent, malformed or unrecognised verdict is treated
// as unsafe rather than as permission, because "the resolver never ran" and "the
// resolver said proceed" must not look the same to the committing code.

const CONFLICT_DECISIONS = Object.freeze({
    PROCEED: "proceed",
    QUEUE: "queue",
    REDIRECT: "redirect",
});

// What the caller should do next. Distinct from the model's verdict because
// "reject" is a gate outcome the model cannot ask for.
const CONFLICT_ACTIONS = Object.freeze({
    PROCEED: "proceed",
    QUEUE: "queue",
    REDIRECT: "redirect",
    REJECT: "reject",
});

const VALID_DECISIONS = new Set(Object.values(CONFLICT_DECISIONS));

function normalise(value) {
    return String(value == null ? "" : value).trim().toLowerCase();
}

/**
 * @param {object} verdict
 * @param {string} [verdict.decision]  the resolver's verdict
 * @param {string} [verdict.reason]
 * @param {string} [verdict.targetObjectId]
 * @param {string[]} [verdict.inFlightCorrelationIds] changes already in flight on the target
 * @param {object} [verdict.personPolicy] the policy the resolver consulted
 * @param {boolean} [verdict.resolverRan]  false when the pipeline skipped the step
 * @returns {{accepted: boolean, action: string, reasons: string[], commitAllowed: boolean}}
 */
function checkConflictDecision(verdict = {}) {
    const reasons = [];
    const decision = normalise(verdict.decision);
    const resolverRan = verdict.resolverRan !== false;

    // A skipped resolver is the case the prompt alone could never catch.
    if (!resolverRan) {
        reasons.push("conflict resolver did not run before the commit was attempted");
        return { accepted: false, action: CONFLICT_ACTIONS.REJECT, reasons, commitAllowed: false };
    }

    if (decision === "") {
        reasons.push("conflict resolver returned no decision");
        return { accepted: false, action: CONFLICT_ACTIONS.REJECT, reasons, commitAllowed: false };
    }

    if (!VALID_DECISIONS.has(decision)) {
        reasons.push(`conflict resolver returned an unrecognised decision '${verdict.decision}'`);
        return { accepted: false, action: CONFLICT_ACTIONS.REJECT, reasons, commitAllowed: false };
    }

    if (decision === CONFLICT_DECISIONS.QUEUE) {
        reasons.push(verdict.reason
            ? `queued behind an in-flight change: ${verdict.reason}`
            : "queued behind an in-flight change");
        return { accepted: true, action: CONFLICT_ACTIONS.QUEUE, reasons, commitAllowed: false };
    }

    if (decision === CONFLICT_DECISIONS.REDIRECT) {
        reasons.push(verdict.reason
            ? `redirected away from the live object: ${verdict.reason}`
            : "redirected away from the live object");
        return { accepted: true, action: CONFLICT_ACTIONS.REDIRECT, reasons, commitAllowed: false };
    }

    // proceed. The resolver may still have observed an in-flight change, in
    // which case its own verdict contradicts the evidence it was given, and the
    // evidence wins.
    const inFlight = Array.isArray(verdict.inFlightCorrelationIds) ? verdict.inFlightCorrelationIds : [];
    const foreign = inFlight.filter((id) => id && id !== verdict.correlationId);
    if (foreign.length > 0) {
        reasons.push(`resolver reported proceed while ${foreign.length} change(s) are in flight on the target`);
        return { accepted: false, action: CONFLICT_ACTIONS.QUEUE, reasons, commitAllowed: false };
    }

    return { accepted: true, action: CONFLICT_ACTIONS.PROCEED, reasons, commitAllowed: true };
}

// Convenience for the committing path: throws rather than returning, matching
// how assertCommitAllowed reads elsewhere in the study code.
function assertConflictClear(verdict = {}) {
    const checked = checkConflictDecision(verdict);
    if (!checked.commitAllowed) {
        throw new Error(`conflict gate refused the commit (${checked.action}): ${checked.reasons.join("; ")}`);
    }
    return checked;
}

module.exports = { checkConflictDecision, assertConflictClear, CONFLICT_DECISIONS, CONFLICT_ACTIONS };
