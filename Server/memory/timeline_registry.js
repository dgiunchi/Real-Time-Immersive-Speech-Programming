"use strict";

// Implements "timelines and perceived synchronicity": an explicit generalization of
// the paper's existing two-clock model (main.tex, "Two Clocks" - interaction clock vs.
// deliberation clock). The system has (at least) three named timelines that do not
// tick at the same rate or start at the same moment:
//   - xr:           the live scene, wall-clock, driven by envelope timestamps Unity
//                    actually sends (SceneDelta/SceneQuery).
//   - deliberation:  per-agent reasoning, one lane per correlationId, spans however
//                     long backend agents actually take (ArtifactProposal/Result,
//                     AgentUtterance).
//   - experimental:  Experimental Space dry-run ticks, which are simulated and NOT
//                     wall-clock (see docs/shared-memory-and-experimental-space.md §2)
//                     - not populated by this pass since simulate_artifact currently
//                     round-trips through the same real-time channel; kept as a named
//                     timeline so a future headless/batch simulator can report ticks
//                     that are explicitly NOT comparable to wall-clock ms.
//
// "Perceived synchronicity" is not one number - it's the gap between when something
// *appears* to happen on the xr timeline (first AgentUtterance) and when it is
// *actually* validated and committed (final ArtifactResult). This directly
// operationalizes two of the paper's own dependent variables, "time to visible
// response" and "time to validated execution"
// (rag/drafts/agenticxr_design_study_sections.md, Dependent Variables) - it exists so
// that data accrues automatically during any use of the system, not only during a
// formal study session.

const TIMELINES = Object.freeze({
    XR: "xr",
    DELIBERATION: "deliberation",
    EXPERIMENTAL: "experimental",
});

function inferTimeline(envelopeType) {
    if (envelopeType === "SceneDelta" || envelopeType === "SceneQuery") return TIMELINES.XR;
    return TIMELINES.DELIBERATION;
}

class TimelineRegistry {
    constructor({ maxLanes = 500 } = {}) {
        this.lanes = new Map(); // correlationId -> { correlationId, events: [] }
        this.maxLanes = maxLanes;
    }

    _lane(correlationId) {
        if (!this.lanes.has(correlationId)) {
            this.lanes.set(correlationId, { correlationId, events: [] });
        }
        return this.lanes.get(correlationId);
    }

    mark(correlationId, timeline, label, at = Date.now()) {
        if (!correlationId) return;
        this._lane(correlationId).events.push({ timeline, label, at });
        this._prune();
    }

    // Call for every envelope that flows through the bridge (inbound or outbound) -
    // infers a reasonable timeline from the envelope type so callers don't have to
    // remember to log timing data at every call site.
    observeEnvelope(envelope) {
        if (!envelope || !envelope.correlationId) return;
        this.mark(envelope.correlationId, inferTimeline(envelope.type), envelope.type, envelope.timestamp || Date.now());
    }

    synchronicity(correlationId) {
        const lane = this.lanes.get(correlationId);
        if (!lane || lane.events.length === 0) return null;
        const sorted = [...lane.events].sort((a, b) => a.at - b.at);
        const intentAt = sorted[0].at;
        const firstUtterance = sorted.find((e) => e.label === "AgentUtterance");
        const finalResult = [...sorted].reverse().find((e) => e.label === "ArtifactResult");
        return {
            correlationId,
            intentAt,
            timeToVisibleResponseMs: firstUtterance ? firstUtterance.at - intentAt : null,
            timeToValidatedExecutionMs: finalResult ? finalResult.at - intentAt : null,
            eventCount: sorted.length,
            events: sorted,
        };
    }

    _prune() {
        if (this.lanes.size <= this.maxLanes) return;
        const keys = Array.from(this.lanes.keys());
        for (const k of keys.slice(0, keys.length - this.maxLanes)) this.lanes.delete(k);
    }
}

module.exports = { TimelineRegistry, TIMELINES };
