"use strict";

const fs = require("fs");
const path = require("path");

const DEFAULT_PATH = path.join(__dirname, "data", "runtime-events.jsonl");

function evaluationSource() {
    return process.env.AGENTICXR_EVALUATION_SOURCE || "unspecified";
}

function appendEvaluationEvent(event, { filePath = process.env.AGENTICXR_EVALUATION_LOG || DEFAULT_PATH } = {}) {
    if (!event || typeof event !== "object") throw new Error("evaluation event must be an object");
    const record = {
        schemaVersion: "1.0",
        source: evaluationSource(),
        recordedAt: Date.now(),
        ...event,
    };
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    fs.appendFileSync(filePath, JSON.stringify(record) + "\n");
    return record;
}

module.exports = { appendEvaluationEvent, evaluationSource, DEFAULT_PATH };
