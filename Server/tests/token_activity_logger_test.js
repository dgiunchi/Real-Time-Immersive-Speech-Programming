"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { normaliseUsage, appendTokenActivity, COLUMNS } = require("../evaluation/token_activity_logger");

const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "agenticxr-token-log-"));
const csvPath = path.join(tempDir, "token-activity.csv");

assert.deepStrictEqual(normaliseUsage({
    input_tokens: 120,
    output_tokens: 30,
    cache_read_input_tokens: 10,
    cache_creation_input_tokens: 5,
}), { inputTokens: 120, outputTokens: 30, cacheReadTokens: 10, cacheCreationTokens: 5, totalTokens: 165 });

appendTokenActivity({
    recordedAt: 0,
    eventType: "orchestrator_result",
    activity: "agentic_orchestrator_turn",
    model: "claude-test",
    sessionId: "session-1",
    correlationId: "correlation-1",
    interactionMode: "L1",
    triggerSource: "system_opportunity",
    candidateCount: 1,
    latencyMs: 456,
    totalCostUsd: 0.0123,
    resultSubtype: "success",
    outcome: "success",
    usage: { input_tokens: 10, output_tokens: 7 },
}, { filePath: csvPath });

const lines = fs.readFileSync(csvPath, "utf8").trim().split(/\r?\n/);
assert.strictEqual(lines.length, 2, "writes header and exactly one row");
assert.strictEqual(lines[0], COLUMNS.join(","), "uses stable analysis header");
assert.ok(lines[1].includes("agentic_orchestrator_turn"), "records activity");
assert.ok(lines[1].includes(",456,"), "records latency");
assert.ok(lines[1].includes(",10,7,"), "records token counts");

fs.rmSync(tempDir, { recursive: true, force: true });
console.log("token activity logger tests passed");
