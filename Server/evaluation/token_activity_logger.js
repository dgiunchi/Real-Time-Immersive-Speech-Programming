"use strict";

// A compact, append-only CSV for live model spend. JSONL remains the
// authoritative forensic trace; this file is deliberately chart-friendly for
// the paper's token/latency analysis and contains no prompts, transcripts, or
// secrets.
const fs = require("fs");
const path = require("path");

const DEFAULT_PATH = path.join(__dirname, "data", "token-activity.csv");
const COLUMNS = [
    "recordedAt", "eventType", "activity", "model", "sessionId", "correlationId",
    "targetObjectId", "attempt", "interactionMode", "triggerSource", "experienceMode",
    "candidateCount", "latencyMs", "inputTokens", "outputTokens", "cacheReadTokens",
    "cacheCreationTokens", "totalTokens", "totalCostUsd", "resultSubtype", "outcome",
    "usageJson",
];

function csvValue(value) {
    const text = value == null ? "" : String(value);
    return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function numberOrNull(value) {
    return Number.isFinite(Number(value)) ? Number(value) : null;
}

function usageValue(usage, ...keys) {
    for (const key of keys) {
        if (usage && Object.prototype.hasOwnProperty.call(usage, key)) return numberOrNull(usage[key]);
    }
    return null;
}

function normaliseUsage(usage) {
    const inputTokens = usageValue(usage, "input_tokens", "inputTokens");
    const outputTokens = usageValue(usage, "output_tokens", "outputTokens");
    const cacheReadTokens = usageValue(usage, "cache_read_input_tokens", "cacheReadInputTokens");
    const cacheCreationTokens = usageValue(usage, "cache_creation_input_tokens", "cacheCreationInputTokens");
    const explicitTotal = usageValue(usage, "total_tokens", "totalTokens");
    const totalTokens = explicitTotal == null
        ? [inputTokens, outputTokens, cacheReadTokens, cacheCreationTokens]
            .filter((value) => value != null).reduce((sum, value) => sum + value, 0) || null
        : explicitTotal;
    return { inputTokens, outputTokens, cacheReadTokens, cacheCreationTokens, totalTokens };
}

function appendTokenActivity(event, { filePath = process.env.AGENTICXR_TOKEN_ACTIVITY_LOG || DEFAULT_PATH } = {}) {
    if (!event || typeof event !== "object") throw new Error("token activity event must be an object");
    const usage = normaliseUsage(event.usage || null);
    const row = {
        recordedAt: new Date(event.recordedAt || Date.now()).toISOString(),
        eventType: event.eventType || "model_turn",
        activity: event.activity || "agentic_orchestrator_turn",
        model: event.model || "unknown",
        sessionId: event.sessionId || null,
        correlationId: event.correlationId || null,
        targetObjectId: event.targetObjectId || null,
        attempt: event.attempt || null,
        interactionMode: event.interactionMode || null,
        triggerSource: event.triggerSource || null,
        experienceMode: event.experienceMode || null,
        candidateCount: event.candidateCount || null,
        latencyMs: numberOrNull(event.latencyMs),
        ...usage,
        totalCostUsd: numberOrNull(event.totalCostUsd),
        resultSubtype: event.resultSubtype || null,
        outcome: event.outcome || null,
        usageJson: event.usage ? JSON.stringify(event.usage) : null,
    };
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    if (!fs.existsSync(filePath) || fs.statSync(filePath).size === 0) {
        fs.appendFileSync(filePath, COLUMNS.join(",") + "\n");
    }
    fs.appendFileSync(filePath, COLUMNS.map((column) => csvValue(row[column])).join(",") + "\n");
    return row;
}

module.exports = { DEFAULT_PATH, COLUMNS, normaliseUsage, appendTokenActivity };
