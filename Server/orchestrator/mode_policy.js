"use strict";

// Deterministic guard around the LLM's mode classification. The model may suggest a
// mode, but it cannot widen autonomy beyond the paper's consent/risk contract.
function checkModePolicy({ interactionMode, authoringMode, riskScore, triggerSource, reversible, localOnly, detailResolved, userPreference } = {}) {
    const reasons = [];
    const automatic = authoringMode === "automatic";
    if (automatic && !["L1", "L2"].includes(interactionMode)) reasons.push("automatic execution is only available to L1/L2");
    if (["L4", "L5"].includes(interactionMode) && automatic) reasons.push(`${interactionMode} requires explicit confirmation`);
    if (interactionMode === "L1" && triggerSource !== "system_opportunity") reasons.push("L1 requires a system_opportunity trigger");
    if (interactionMode === "L2" && triggerSource !== "context") reasons.push("L2 requires a context trigger");
    if (interactionMode === "L3" && detailResolved !== true) reasons.push("L3 cannot proceed until clarification is resolved");
    if (automatic && (typeof riskScore !== "number" || riskScore >= 0.3)) reasons.push("automatic execution requires riskScore < 0.3");
    if (automatic && reversible !== true) reasons.push("automatic execution must be reversible");
    if (automatic && localOnly !== true) reasons.push("automatic execution must be local to one object/session");
    if (automatic && userPreference && userPreference.learned) {
        const learned = userPreference.learned;
        const automaticEvents = learned.byAuthoringMode && learned.byAuthoringMode.automatic || 0;
        if (automaticEvents >= 3 && learned.rejected / Math.max(1, learned.events) >= 0.5) {
            reasons.push("learned preference restricts automatic execution after repeated rejection");
        }
    }
    return { accepted: reasons.length === 0, reasons };
}

module.exports = { checkModePolicy };
