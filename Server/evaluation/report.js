"use strict";

const fs = require("fs");
const path = require("path");
const { DEFAULT_PATH } = require("./event_logger");

function argument(name, fallback) {
    const prefix = `--${name}=`;
    const found = process.argv.slice(2).find((value) => value.startsWith(prefix));
    return found ? found.slice(prefix.length) : fallback;
}

function readJsonLines(filePath) {
    if (!fs.existsSync(filePath)) return [];
    return fs.readFileSync(filePath, "utf8").split(/\r?\n/).filter(Boolean).map((line, index) => {
        try { return JSON.parse(line); }
        catch (error) { throw new Error(`invalid JSONL at line ${index + 1}: ${error.message}`); }
    });
}

function percentile(sorted, proportion) {
    if (!sorted.length) return null;
    return sorted[Math.min(sorted.length - 1, Math.max(0, Math.ceil(sorted.length * proportion) - 1))];
}

function statistics(values) {
    const sorted = values.filter(Number.isFinite).sort((a, b) => a - b);
    if (!sorted.length) return { n: 0, min: null, median: null, mean: null, p95: null, max: null };
    const sum = sorted.reduce((total, value) => total + value, 0);
    return {
        n: sorted.length,
        min: sorted[0],
        median: percentile(sorted, 0.5),
        mean: Math.round((sum / sorted.length) * 100) / 100,
        p95: percentile(sorted, 0.95),
        max: sorted[sorted.length - 1],
    };
}

function eventTime(event) { return event.timestamp || event.recordedAt; }

function buildReport(events, source) {
    const filtered = events.filter((event) => !source || event.source === source);
    const byCorrelation = new Map();
    for (const event of filtered) {
        if (!event.correlationId) continue;
        if (!byCorrelation.has(event.correlationId)) byCorrelation.set(event.correlationId, []);
        byCorrelation.get(event.correlationId).push(event);
    }

    const turns = [];
    for (const [correlationId, lane] of byCorrelation) {
        lane.sort((a, b) => eventTime(a) - eventTime(b));
        const isTurn = lane.some((event) =>
            ["recording_start", "turn_started", "orchestrator_attempt_started"].includes(event.eventType) ||
            (event.eventType === "envelope" && ["ArtifactResult", "CommitAccepted", "CommitRejected"].includes(event.envelopeType))
        );
        if (!isTurn) continue;
        const start = lane.find((event) => ["recording_start", "turn_started"].includes(event.eventType)) ||
            lane.find((event) => event.eventType === "envelope" && event.envelopeType === "SceneQuery") || lane[0];
        const visible = lane.find((event) => event.eventType === "envelope" && ["AgentStatus", "AgentUtterance"].includes(event.envelopeType));
        const validated = [...lane].reverse().find((event) => event.eventType === "envelope" && ["ArtifactResult", "CommitAccepted", "CommitRejected"].includes(event.envelopeType));
        const result = [...lane].reverse().find((event) => event.eventType === "envelope" && event.envelopeType === "ArtifactResult");
        turns.push({
            correlationId,
            sessionId: start.sessionId || null,
            targetObjectId: start.targetObjectId || null,
            startAt: eventTime(start),
            timeToVisibleResponseMs: visible ? eventTime(visible) - eventTime(start) : null,
            timeToValidatedExecutionMs: validated ? eventTime(validated) - eventTime(start) : null,
            finalEnvelopeType: validated ? validated.envelopeType : null,
            finalStatus: validated ? validated.status || null : null,
            verificationDurationMs: result ? result.verificationDurationMs || null : null,
            commitAttachDurationMs: result ? result.commitAttachDurationMs || null : null,
            staleness: result ? result.staleness || null : null,
        });
    }

    const reconcile = filtered.filter((event) => event.eventType === "cache_reconcile");
    const stalenessChecks = turns.filter((turn) => turn.staleness && turn.staleness.checked);
    const statusCounts = {};
    for (const turn of turns) {
        const key = turn.finalStatus || turn.finalEnvelopeType || "incomplete";
        statusCounts[key] = (statusCounts[key] || 0) + 1;
    }
    const resultEvents = filtered.filter((event) => event.eventType === "orchestrator_result");
    const usage = resultEvents.map((event) => event.usage).filter(Boolean);

    return {
        schemaVersion: "1.0",
        generatedAt: new Date().toISOString(),
        source: source || "all",
        evidenceLabel: source === "mock"
            ? "mock-integration technical evidence; not a live model, Unity runtime, headset, or user study"
            : source === "live-model"
                ? "live-model evidence; inspect individual runs for Unity/headset provenance"
                : "mixed or unspecified provenance; do not cite without filtering",
        sampleSizeTurns: turns.length,
        latencyMs: {
            timeToVisibleResponse: statistics(turns.map((turn) => turn.timeToVisibleResponseMs)),
            timeToValidatedExecution: statistics(turns.map((turn) => turn.timeToValidatedExecutionMs)),
            verificationSpace: statistics(turns.map((turn) => turn.verificationDurationMs)),
            liveCommitAttach: statistics(turns.map((turn) => turn.commitAttachDurationMs)),
        },
        outcomes: statusCounts,
        staleness: {
            checked: stalenessChecks.length,
            stale: stalenessChecks.filter((turn) => turn.staleness.isStale).length,
        },
        cacheRecovery: {
            events: reconcile.length,
            accepted: reconcile.filter((event) => event.outcome === "accepted").length,
            duplicates: reconcile.filter((event) => event.outcome === "duplicate").length,
            stale: reconcile.filter((event) => event.outcome === "stale").length,
            gapsDetected: reconcile.filter((event) => event.gap).length,
            backfilled: reconcile.filter((event) => event.isBackfill && event.outcome === "accepted").length,
        },
        modelUsage: { resultEvents: resultEvents.length, usage },
        turns,
    };
}

const input = path.resolve(argument("input", DEFAULT_PATH));
const source = argument("source", null);
const output = path.resolve(argument("output", path.join(__dirname, "data", `technical-evaluation-${source || "all"}.json`)));
const report = buildReport(readJsonLines(input), source);
fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, JSON.stringify(report, null, 2) + "\n");
console.log(JSON.stringify(report, null, 2));
console.error(`[evaluation] wrote ${output}`);

module.exports = { buildReport, statistics };
