"use strict";

// Researcher-facing CLI for opening/closing a trial in the existing ArtifactLog.
// It deliberately accepts identifiers and structured rubric signals only, never
// speech audio or transcript content.

const path = require("path");
const { ArtifactLog } = require("../memory/artifact_log");

function options(argv) {
    const result = {};
    for (const value of argv) {
        if (!value.startsWith("--") || !value.includes("=")) continue;
        const split = value.indexOf("=");
        result[value.slice(2, split)] = value.slice(split + 1);
    }
    return result;
}

function required(args, name) {
    if (!args[name]) throw new Error(`--${name}=... is required`);
    return args[name];
}

function parseBoolean(value, name) {
    if (value === "true") return true;
    if (value === "false") return false;
    if (value == null || value === "") return null;
    throw new Error(`--${name} must be true or false`);
}

function parseJson(value, name) {
    if (!value) return null;
    try { return JSON.parse(value); }
    catch (error) { throw new Error(`--${name} is not valid JSON: ${error.message}`); }
}

function run(argv = process.argv.slice(2)) {
    const command = argv[0];
    const args = options(argv.slice(1));
    const log = new ArtifactLog({
        filePath: path.resolve(args.log || path.join(__dirname, "..", "memory", "data", "artifact_log.jsonl")),
    });
    let record;
    if (command === "start") {
        record = log.startStudyTrial({
            participantId: required(args, "participant"),
            sessionId: required(args, "session"),
            trialId: required(args, "trial"),
            condition: required(args, "condition"),
            taskId: required(args, "task"),
            interactionMode: required(args, "mode"),
            correlationId: required(args, "correlation"),
            studySource: "researcher_cli",
        });
    } else if (command === "end") {
        record = log.endStudyTrial({
            sessionId: required(args, "session"),
            trialId: required(args, "trial"),
            correlationId: required(args, "correlation"),
            taskCompletion: parseBoolean(required(args, "completed"), "completed"),
            taskSuccess: parseBoolean(args.success, "success"),
            taskQualityScore: args["quality-score"] == null ? null : Number(args["quality-score"]),
            taskQualitySignals: parseJson(args["quality-signals-json"], "quality-signals-json"),
            reason: args.reason || null,
        });
    } else if (command === "event") {
        record = log.appendStudyEvent({
            sessionId: required(args, "session"),
            correlationId: required(args, "correlation"),
            eventType: required(args, "type"),
            durationMs: args["duration-ms"] == null ? null : Number(args["duration-ms"]),
            status: args.status || null,
            reasonCode: args["reason-code"] || null,
            studySource: "researcher_cli",
        });
    } else {
        throw new Error("usage: node evaluation/study_trial.js start|end|event --name=value ...");
    }
    console.log(JSON.stringify({
        eventType: record.eventType,
        participantId: record.participantId,
        sessionId: record.sessionId,
        trialId: record.trialId,
        timestampUtc: record.timestampUtc,
    }, null, 2));
}

if (require.main === module) {
    try { run(); }
    catch (error) {
        console.error(`[study_trial] ${error.message}`);
        process.exitCode = 1;
    }
}

module.exports = { run };
