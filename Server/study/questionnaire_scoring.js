"use strict";

const fs = require("fs");
const path = require("path");

const QUESTIONNAIRES_PATH = path.join(__dirname, "questionnaires.v1.json");

function loadQuestionnaires(filePath = QUESTIONNAIRES_PATH) {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
}

function validateQuestionnaireDefinition(schema = loadQuestionnaires()) {
    const checks = [];
    const check = (ok, id, detail) => checks.push({ ok: Boolean(ok), id, detail });
    for (const instrument of schema.validatedInstruments || []) {
        check(Array.isArray(instrument.items) && instrument.items.length === 0,
            `validated-items-empty-${instrument.instrumentId}`, `${(instrument.items || []).length}`);
        check(instrument.approvalStatus === "pending",
            `validated-pending-${instrument.instrumentId}`, instrument.approvalStatus);
        check(instrument.timing === "at-break-and-end" && Boolean(instrument.timingRationale),
            `validated-timing-${instrument.instrumentId}`, instrument.timing);
    }
    for (const item of schema.studySpecificItemSlots || []) {
        const approvalComplete = item.approvalStatus !== "approved" ||
            (typeof item.approvedBy === "string" && item.approvedBy.trim() !== "" &&
                typeof item.approvedAtUtc === "string" && !Number.isNaN(Date.parse(item.approvedAtUtc)));
        check(approvalComplete, `approval-evidence-${item.itemId}`, item.approvalStatus);
        check(typeof item.wording === "string" && item.wording.length > 0,
            `draft-wording-${item.itemId}`, item.approvalStatus);
    }
    check((schema.studySpecificItemSlots || []).length === 12, "study-specific-item-count", "12");
    return { ok: checks.every((entry) => entry.ok), checks };
}

function questionnaireReadiness({ humanSession, schema = loadQuestionnaires() }) {
    const definition = validateQuestionnaireDefinition(schema);
    const pendingValidated = schema.validatedInstruments.filter((instrument) =>
        instrument.approvalStatus !== "approved" || !instrument.items.length).map((instrument) => instrument.instrumentId);
    const pendingStudyItems = schema.studySpecificItemSlots.filter((item) =>
        item.approvalStatus !== "approved" || !item.approvedBy || !item.approvedAtUtc).map((item) => item.itemId);
    const humanReady = definition.ok && pendingValidated.length === 0 && pendingStudyItems.length === 0;
    return {
        ok: definition.ok && (!humanSession || humanReady),
        humanSession: Boolean(humanSession),
        researcherDryRunAllowed: definition.ok,
        pendingValidated,
        pendingStudyItems,
        checks: definition.checks,
    };
}

function itemById(itemId, schema = loadQuestionnaires()) {
    const item = schema.studySpecificItemSlots.find((candidate) => candidate.itemId === itemId);
    if (!item) throw new Error(`unknown study-specific questionnaire item '${itemId}'`);
    return item;
}

// This is the only reverse-scoring implementation. Raw responses remain untouched in storage.
function scoreStudySpecificResponse(itemId, rawResponse, schema = loadQuestionnaires()) {
    const item = itemById(itemId, schema);
    if (item.responseType === "forcedChoice") {
        const allowed = item.anchors.map((anchor) => anchor.value);
        if (!allowed.includes(rawResponse)) throw new Error(`${itemId} response is not an allowed forced choice`);
        return { itemId, rawResponse, scoredResponse: rawResponse, reverseApplied: false };
    }
    const numeric = Number(rawResponse);
    if (!Number.isInteger(numeric) || numeric < 1 || numeric > 7) {
        throw new Error(`${itemId} response must be an integer from 1 to 7`);
    }
    const scoredResponse = item.reverseKeyed ? 8 - numeric : numeric;
    return { itemId, rawResponse: numeric, scoredResponse, reverseApplied: item.reverseKeyed === true };
}

function itemsForTrial({ interactionMode, condition, timing = "after-task" }, schema = loadQuestionnaires()) {
    return schema.studySpecificItemSlots.filter((item) => {
        if (item.interactionModes && !item.interactionModes.includes(interactionMode)) return false;
        if (item.conditions && !item.conditions.includes(condition)) return false;
        if (timing === "immediate-proposal") return item.itemId === "immediateProposalPerceivedLatency";
        if (item.itemId === "immediateProposalPerceivedLatency") return false;
        return item.timing.startsWith("after-");
    }).sort((left, right) => left.presentationOrder - right.presentationOrder);
}

function renderItemPrompt(itemId, { candidateTarget = null } = {}, schema = loadQuestionnaires()) {
    const item = itemById(itemId, schema);
    return JSON.stringify({ itemId: item.itemId, wording: item.wording, responseType: item.responseType, anchors: item.anchors });
}

module.exports = {
    QUESTIONNAIRES_PATH,
    loadQuestionnaires,
    validateQuestionnaireDefinition,
    questionnaireReadiness,
    scoreStudySpecificResponse,
    itemsForTrial,
    renderItemPrompt,
};
