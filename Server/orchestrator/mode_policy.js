"use strict";

const { VERIFICATION_LEVELS } = require("./goal_schema");

function routeByVerifiability({ verificationLevel, boundExhausted = false } = {}) {
    if (boundExhausted) return {
        automaticEligible: false,
        requiresHuman: true,
        requiredRoute: "L4",
        reason: "goal bound exhausted",
    };
    if ([VERIFICATION_LEVELS.DETERMINISTIC, VERIFICATION_LEVELS.RULES_CONSTRAINTS].includes(verificationLevel)) {
        return { automaticEligible: true, requiresHuman: false, requiredRoute: null, reason: "machine-verifiable" };
    }
    if (verificationLevel === VERIFICATION_LEVELS.LLM_AS_JUDGE) {
        return { automaticEligible: false, requiresHuman: false, requiresValidator: true, requiredRoute: "L4", reason: "Validator/Critic judgment required" };
    }
    if (verificationLevel === VERIFICATION_LEVELS.DELAYED_GROUND_TRUTH) {
        return { automaticEligible: false, requiresHuman: true, requiredRoute: "L4", reason: "ground truth is delayed" };
    }
    if (verificationLevel === VERIFICATION_LEVELS.HUMAN_CHECKPOINT) {
        return { automaticEligible: false, requiresHuman: true, requiredRoute: "L4", reason: "human checkpoint required" };
    }
    return { automaticEligible: false, requiresHuman: false, requiredRoute: null, reason: "no goal verification level supplied" };
}

// Deterministic guard around the LLM's mode classification. The model may suggest a
// mode, but it cannot widen autonomy beyond the paper's consent/risk contract.
function checkModePolicy({ interactionMode, authoringMode, riskScore, triggerSource, reversible, localOnly,
    detailResolved, userPreference, verificationLevel, boundExhausted = false, speculative = false } = {}) {
    const reasons = [];
    const automatic = authoringMode === "automatic";
    const verificationRoute = routeByVerifiability({ verificationLevel, boundExhausted });
    if (automatic && !["L1", "L2"].includes(interactionMode)) reasons.push("automatic execution is only available to L1/L2");
    if (["L4", "L5"].includes(interactionMode) && automatic) reasons.push(`${interactionMode} requires explicit confirmation`);
    if (interactionMode === "L1" && triggerSource !== "system_opportunity") reasons.push("L1 requires a system_opportunity trigger");
    if (interactionMode === "L2" && triggerSource !== "context") reasons.push("L2 requires a context trigger");
    if (interactionMode === "L3" && detailResolved !== true) reasons.push("L3 cannot proceed until clarification is resolved");
    if (automatic && (typeof riskScore !== "number" || riskScore >= 0.3)) reasons.push("automatic execution requires riskScore < 0.3");
    if (automatic && reversible !== true) reasons.push("automatic execution must be reversible");
    if (automatic && localOnly !== true) reasons.push("automatic execution must be local to one object/session");
    if (automatic && verificationLevel != null && !verificationRoute.automaticEligible) {
        reasons.push(`automatic execution blocked by verification level ${verificationLevel}: ${verificationRoute.reason}`);
    }
    if (verificationLevel != null && verificationRoute.requiredRoute &&
        !["L4", "L5"].includes(interactionMode)) {
        reasons.push(`verification level ${verificationLevel} requires an L4/L5 route`);
    }
    if (automatic && boundExhausted) reasons.push("an exhausted goal bound requires explicit human continuation");
    if (automatic && speculative) reasons.push("speculative future-goal artifacts may be prepared but never committed automatically");
    if (automatic && userPreference && userPreference.learned) {
        const learned = userPreference.learned;
        const automaticEvents = learned.byAuthoringMode && learned.byAuthoringMode.automatic || 0;
        if (automaticEvents >= 3 && learned.rejected / Math.max(1, learned.events) >= 0.5) {
            reasons.push("learned preference restricts automatic execution after repeated rejection");
        }
    }
    return { accepted: reasons.length === 0, reasons, verificationRoute };
}

module.exports = { checkModePolicy, routeByVerifiability };
