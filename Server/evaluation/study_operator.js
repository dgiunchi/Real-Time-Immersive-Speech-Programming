"use strict";

const fs = require("fs");
const path = require("path");
const { ArtifactLog } = require("../memory/artifact_log");
const { TRIAL_COLUMNS, LONG_COLUMNS, readJsonLines, writeCsv, buildStudyExports } = require("./study_export");
const {
    STUDY_ROOT,
    PROTOCOL_PATH,
    QUESTIONNAIRES_PATH,
    RUBRICS_PATH,
    loadProtocol,
    validateParticipantId,
    generateParticipantPlan,
} = require("../study/protocol");
const { validateTaskReadiness } = require("../study/task_readiness");

const PARTICIPANTS_ROOT = path.resolve(__dirname, "data", "participants");
const QUESTIONNAIRE_COLUMNS = Object.freeze([
    "protocolId", "methodVersion", "questionnaireVersion", "participantId", "sessionId", "trialId",
    "taskId", "taskVariant", "condition", "interactionMode", "itemId", "response", "answeredAtUtc",
]);
const RUBRIC_COLUMNS = Object.freeze([
    "protocolId", "methodVersion", "rubricVersion", "participantId", "sessionId", "trialId", "taskId", "taskVariant",
    "raterPseudonym", "conditionBlinded", "taskCompletion", "taskSuccess", "qualityScore",
    "dimensionScoresJson", "codedAtUtc",
]);

function options(argv) {
    const result = {};
    for (const value of argv) {
        if (!value.startsWith("--") || !value.includes("=")) continue;
        const at = value.indexOf("=");
        result[value.slice(2, at)] = value.slice(at + 1);
    }
    return result;
}

function required(args, name) {
    if (!args[name]) throw new Error(`--${name}=... is required`);
    return args[name];
}

function participantPaths(participantId) {
    validateParticipantId(participantId);
    const directory = path.join(PARTICIPANTS_ROOT, participantId);
    if (path.dirname(directory) !== PARTICIPANTS_ROOT) throw new Error("participant path escaped the study data directory");
    return {
        directory,
        plan: path.join(directory, "plan.json"),
        log: path.join(directory, "artifact_log.jsonl"),
        questionnaireResponses: path.join(directory, "questionnaire_responses.jsonl"),
        rubricRatings: path.join(directory, "rubric_ratings.jsonl"),
        exportDirectory: path.join(directory, "export"),
    };
}

function writeJsonAtomic(filePath, value) {
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    const temporary = `${filePath}.${process.pid}.tmp`;
    fs.writeFileSync(temporary, JSON.stringify(value, null, 2) + "\n", { flag: "wx" });
    fs.renameSync(temporary, filePath);
}

function loadPlan(participantId) {
    const paths = participantPaths(participantId);
    if (!fs.existsSync(paths.plan)) throw new Error(`no plan exists for ${participantId}; run study:operator plan first`);
    const plan = JSON.parse(fs.readFileSync(paths.plan, "utf8"));
    if (plan.participantId !== participantId) throw new Error("plan participant identity mismatch");
    return { plan, paths };
}

function selectTrial(plan, args) {
    const trialId = required(args, "trial");
    const trial = plan.trials.find((candidate) => candidate.trialId === trialId);
    if (!trial) throw new Error(`trial '${trialId}' is not in the participant plan`);
    return trial;
}

function approvalStatus() {
    const localPath = path.join(STUDY_ROOT, "approvals.local.json");
    if (!fs.existsSync(localPath)) return { localPath, approvals: null };
    return { localPath, approvals: JSON.parse(fs.readFileSync(localPath, "utf8")) };
}

function preflight(participantId, { technicalOnly = false } = {}) {
    const protocol = loadProtocol();
    const { plan, paths } = loadPlan(participantId);
    const checks = [];
    const check = (ok, id, detail) => checks.push({ ok: Boolean(ok), id, detail });
    check(plan.protocolId === protocol.protocolId, "protocol-plan-match", plan.protocolId);
    check(plan.methodVersion === protocol.methodVersion, "method-version-plan-match", plan.methodVersion || "missing");
    check(plan.trials.length === protocol.design.trialsPerParticipant, "trial-count", `${plan.trials.length}`);
    check(plan.trials.every((trial) => trial.methodVersion === protocol.methodVersion),
        "method-version-trial-match", protocol.methodVersion);
    check(plan.trials.every((trial) => protocol.design.taskVariants.includes(trial.taskVariant)),
        "task-variant-valid", protocol.design.taskVariants.join(","));
    check(fs.existsSync(PROTOCOL_PATH), "protocol-manifest", PROTOCOL_PATH);
    check(fs.existsSync(QUESTIONNAIRES_PATH), "questionnaire-schema", QUESTIONNAIRES_PATH);
    check(fs.existsSync(RUBRICS_PATH), "rubric-schema", RUBRICS_PATH);
    const taskReadiness = validateTaskReadiness();
    check(taskReadiness.ok, "task-scene-readiness",
        taskReadiness.ok ? taskReadiness.scenePath : taskReadiness.checks.filter((item) => !item.ok).map((item) => item.id).join(","));
    const projectVersionPath = path.resolve(__dirname, "..", "..", "Unity", "ProjectSettings", "ProjectVersion.txt");
    const projectVersion = fs.existsSync(projectVersionPath) ? fs.readFileSync(projectVersionPath, "utf8") : "";
    check(projectVersion.includes(protocol.apparatus.unityVersion), "unity-version", protocol.apparatus.unityVersion);
    check(Boolean(process.env.STT_HTTP_URL), "stt-url", "STT_HTTP_URL is required for live speech");
    check(Boolean(process.env.ANTHROPIC_API_KEY), "agentic-model-key", "ANTHROPIC_API_KEY is required for AgenticXR arms");
    check(Boolean(process.env.OPENAI_API_KEY), "baseline-model-key", "OPENAI_API_KEY is required for baseline arms");
    const transcriptDebugValue = String(process.env.STUDY_DEBUG_TRANSCRIPTS || "").toLowerCase();
    check(!["1", "true", "yes"].includes(transcriptDebugValue),
    "transcript-debug-off", "verbatim transcript diagnostics must be disabled");
    check(Boolean(process.env.AGENTICXR_ARTIFACT_LOG) &&
        path.resolve(process.env.AGENTICXR_ARTIFACT_LOG) === paths.log,
    "participant-log-routing", `AGENTICXR_ARTIFACT_LOG must equal ${paths.log}`);
    const active = new ArtifactLog({ filePath: paths.log }).activeTrialBySession;
    check(active.size === 0, "no-active-trial", active.size ? `${active.size} trial(s) still active` : "none");

    if (technicalOnly) {
        check(Number(participantId.slice(1)) >= 900, "human-approval-gates",
            "TECHNICAL-ONLY is restricted to reserved P900-P999 IDs; no participant may be run");
    } else {
        const { localPath, approvals } = approvalStatus();
        const allApproved = approvals && protocol.humanApprovalGates.every((gate) => approvals[gate] === true);
        check(allApproved, "human-approval-gates", approvals ? "one or more approvals are false" : `missing ${localPath}`);
    }
    return { ok: checks.every((item) => item.ok), technicalOnly, participantId, artifactLog: paths.log, checks };
}

function appendStructuredResponse(filePath, record) {
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    fs.appendFileSync(filePath, JSON.stringify({ ...record, recordedAtUtc: new Date().toISOString() }) + "\n");
}

function approvedQuestionnaireResponse(itemId, rawResponse) {
    const schema = JSON.parse(fs.readFileSync(QUESTIONNAIRES_PATH, "utf8"));
    const items = [
        ...schema.studySpecificItemSlots,
        ...schema.validatedInstruments.flatMap((instrument) =>
            (instrument.items || []).map((item) => ({ ...item, instrumentApprovalStatus: instrument.approvalStatus }))),
    ];
    const item = items.find((candidate) => candidate.itemId === itemId);
    if (!item) throw new Error(`questionnaire item '${itemId}' is not in questionnaires.v1.json`);
    if (item.approvalStatus !== "approved" || item.instrumentApprovalStatus === "pending" || !item.wording) {
        throw new Error(`questionnaire item '${itemId}' has no approved verbatim wording`);
    }
    const numeric = Number(rawResponse);
    if (rawResponse !== "" && Number.isFinite(numeric)) return numeric;
    if (Array.isArray(item.anchors) && item.anchors.includes(rawResponse)) return rawResponse;
    throw new Error(`response for '${itemId}' must be numeric or one of its approved categorical anchors`);
}

function booleanArgument(value, name) {
    if (value === "true") return true;
    if (value === "false") return false;
    throw new Error(`--${name} must be true or false`);
}

function approvedRubricRating(taskId, args) {
    const schema = JSON.parse(fs.readFileSync(RUBRICS_PATH, "utf8"));
    const rubric = schema.tasks.find((candidate) => candidate.taskId === taskId);
    if (!rubric || rubric.approved !== true || !rubric.rubricVersion || !rubric.qualityScale) {
        throw new Error(`task '${taskId}' has no approved versioned rubric`);
    }
    const qualityScore = Number(required(args, "quality-score"));
    if (!Number.isFinite(qualityScore) || qualityScore < rubric.qualityScale.minimum || qualityScore > rubric.qualityScale.maximum) {
        throw new Error(`--quality-score must be within the approved ${rubric.qualityScale.minimum}-${rubric.qualityScale.maximum} scale`);
    }
    let dimensions;
    try { dimensions = JSON.parse(required(args, "dimension-scores-json")); }
    catch (error) { throw new Error(`--dimension-scores-json is invalid: ${error.message}`); }
    const allowed = new Set(rubric.qualityDimensions.map((dimension) => dimension.dimensionId));
    if (!dimensions || typeof dimensions !== "object" || Array.isArray(dimensions) ||
        Object.keys(dimensions).some((key) => !allowed.has(key) || !Number.isFinite(Number(dimensions[key])))) {
        throw new Error("dimension scores must be numeric and match only approved rubric dimensions");
    }
    return {
        rubricVersion: rubric.rubricVersion,
        taskCompletion: booleanArgument(required(args, "completed"), "completed"),
        taskSuccess: booleanArgument(required(args, "success"), "success"),
        qualityScore,
        dimensionScoresJson: JSON.stringify(dimensions),
    };
}

function readOptionalJsonLines(filePath) {
    return fs.existsSync(filePath) ? readJsonLines(filePath) : [];
}

function run(argv = process.argv.slice(2)) {
    const command = argv[0];
    const args = options(argv.slice(1));
    const participantId = validateParticipantId(required(args, "participant"));
    const paths = participantPaths(participantId);

    if (command === "plan") {
        if (fs.existsSync(paths.plan) && args.overwrite !== "true") {
            throw new Error(`plan already exists for ${participantId}; pass --overwrite=true only before data collection`);
        }
        if (fs.existsSync(paths.log)) throw new Error("cannot overwrite a plan after participant logging has begun");
        const plan = generateParticipantPlan(participantId);
        writeJsonAtomic(paths.plan, plan);
        console.log(JSON.stringify({ planPath: paths.plan, artifactLog: paths.log, trialCount: plan.trials.length, assignment: plan.assignment }, null, 2));
        return plan;
    }

    const { plan } = loadPlan(participantId);
    if (command === "preflight") {
        const report = preflight(participantId, { technicalOnly: args["technical-only"] === "true" });
        console.log(JSON.stringify(report, null, 2));
        if (!report.ok) process.exitCode = 1;
        return report;
    }
    if (command === "status") {
        const log = new ArtifactLog({ filePath: paths.log });
        const report = { participantId, activeTrials: [...log.activeTrialBySession.values()], eventCount: log.records.length, artifactLog: paths.log };
        console.log(JSON.stringify(report, null, 2));
        return report;
    }
    if (command === "runtime") {
        const trial = selectTrial(plan, args);
        const mode = trial.condition === "baseline" ? "legacy" : "claude";
        const commandName = mode === "legacy" ? "npm run start:code-runtime-generator" : "npm run start:agenticxr";
        const report = {
            participantId,
            trialId: trial.trialId,
            condition: trial.condition,
            requiredEnvironment: {
                AGENTICXR_ARTIFACT_LOG: paths.log,
                AGENTICXR_MODE: mode,
                STUDY_DEBUG_TRANSCRIPTS: "0",
            },
            startCommand: commandName,
            instruction: "Restart the runtime when this mode differs from the preceding trial, then run preflight before start.",
        };
        console.log(JSON.stringify(report, null, 2));
        return report;
    }
    if (command === "start") {
        const trial = selectTrial(plan, args);
        const report = preflight(participantId, { technicalOnly: args["technical-only"] === "true" });
        if (!report.ok) {
            const failed = report.checks.filter((check) => !check.ok).map((check) => check.id).join(", ");
            throw new Error(`preflight failed: ${failed}`);
        }
        const expectedMode = trial.condition === "baseline" ? "legacy" : "claude";
        if (String(process.env.AGENTICXR_MODE || "").toLowerCase() !== expectedMode) {
            throw new Error(`AGENTICXR_MODE must be '${expectedMode}' for ${trial.trialId}; run the runtime command first`);
        }
        const log = new ArtifactLog({ filePath: paths.log });
        const record = log.startStudyTrial({
            ...trial,
            correlationId: `${trial.trialId}-root`,
            studySource: "study_operator",
        });
        console.log(JSON.stringify({ record, runtimeEnvironment: { AGENTICXR_ARTIFACT_LOG: paths.log, AGENTICXR_MODE: trial.condition === "baseline" ? "legacy" : "claude" } }, null, 2));
        return record;
    }
    if (command === "event") {
        const log = new ArtifactLog({ filePath: paths.log });
        const record = log.appendStudyEvent({
            sessionId: required(args, "session"),
            correlationId: required(args, "correlation"),
            eventType: required(args, "type"),
            status: args.status || null,
            reasonCode: args["reason-code"] || null,
            durationMs: args["duration-ms"] == null ? null : Number(args["duration-ms"]),
            studySource: "study_operator",
        });
        console.log(JSON.stringify(record, null, 2));
        return record;
    }
    if (command === "end" || command === "abort") {
        const trial = selectTrial(plan, args);
        const completed = command === "abort" ? false : args.completed === "true";
        if (command === "end" && !["true", "false"].includes(args.completed)) {
            throw new Error("--completed=true|false is required");
        }
        if (args.success != null || args["quality-score"] != null || args["quality-signals-json"] != null) {
            throw new Error("task success and quality must be entered later with the approved condition-blind rubric command");
        }
        const log = new ArtifactLog({ filePath: paths.log });
        const record = log.endStudyTrial({
            sessionId: trial.sessionId,
            trialId: trial.trialId,
            correlationId: args.correlation || `${trial.trialId}-root`,
            taskCompletion: completed,
            taskSuccess: null,
            taskQualityScore: null,
            taskQualitySignals: null,
            reason: command === "abort" ? required(args, "reason-code") : args["reason-code"] || null,
        });
        console.log(JSON.stringify(record, null, 2));
        return record;
    }
    if (command === "questionnaire") {
        const trial = selectTrial(plan, args);
        const itemId = required(args, "item");
        const response = approvedQuestionnaireResponse(itemId, required(args, "response"));
        if (readOptionalJsonLines(paths.questionnaireResponses).some((row) => row.trialId === trial.trialId && row.itemId === itemId)) {
            throw new Error(`questionnaire item '${itemId}' is already recorded for ${trial.trialId}`);
        }
        appendStructuredResponse(paths.questionnaireResponses, {
            protocolId: plan.protocolId,
            methodVersion: plan.methodVersion,
            questionnaireVersion: "1.0",
            participantId,
            sessionId: trial.sessionId,
            trialId: trial.trialId,
            taskId: trial.taskId,
            taskVariant: trial.taskVariant,
            condition: trial.condition,
            interactionMode: trial.interactionMode,
            itemId,
            response,
            answeredAtUtc: new Date().toISOString(),
        });
        console.log(JSON.stringify({ recorded: true, trialId: trial.trialId, itemId }, null, 2));
        return;
    }
    if (command === "rubric") {
        const trial = selectTrial(plan, args);
        if (args["condition-blinded"] !== "true") throw new Error("rubric coding requires --condition-blinded=true");
        const raterPseudonym = required(args, "rater");
        if (!/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/.test(raterPseudonym)) {
            throw new Error("--rater must be a pseudonymous safe identifier");
        }
        const rating = approvedRubricRating(trial.taskId, args);
        if (readOptionalJsonLines(paths.rubricRatings).some((row) =>
            row.trialId === trial.trialId && row.raterPseudonym === raterPseudonym)) {
            throw new Error(`rater '${raterPseudonym}' already coded ${trial.trialId}`);
        }
        appendStructuredResponse(paths.rubricRatings, {
            protocolId: plan.protocolId,
            methodVersion: plan.methodVersion,
            participantId,
            sessionId: trial.sessionId,
            trialId: trial.trialId,
            taskId: trial.taskId,
            taskVariant: trial.taskVariant,
            raterPseudonym,
            conditionBlinded: true,
            ...rating,
            codedAtUtc: new Date().toISOString(),
        });
        console.log(JSON.stringify({ recorded: true, trialId: trial.trialId, rubricVersion: rating.rubricVersion }, null, 2));
        return;
    }
    if (command === "export") {
        if (!fs.existsSync(paths.log)) throw new Error("participant artifact log does not exist");
        const result = buildStudyExports(readJsonLines(paths.log));
        writeCsv(path.join(paths.exportDirectory, "trials.csv"), TRIAL_COLUMNS, result.trialRows);
        writeCsv(path.join(paths.exportDirectory, "events.csv"), LONG_COLUMNS, result.longRows);
        const questionnaireRows = readOptionalJsonLines(paths.questionnaireResponses);
        const rubricRows = readOptionalJsonLines(paths.rubricRatings);
        writeCsv(path.join(paths.exportDirectory, "questionnaire_responses.csv"), QUESTIONNAIRE_COLUMNS, questionnaireRows);
        writeCsv(path.join(paths.exportDirectory, "rubric_ratings.csv"), RUBRIC_COLUMNS, rubricRows);
        writeJsonAtomic(path.join(paths.exportDirectory, "rejected_trials.json"), result.rejectedTrials);
        writeJsonAtomic(path.join(paths.exportDirectory, "export_manifest.json"), {
            protocolId: plan.protocolId,
            methodVersion: plan.methodVersion,
            participantId,
            exportedAtUtc: new Date().toISOString(),
            acceptedTrialCount: result.trialRows.length,
            rejectedTrialCount: result.rejectedTrials.length,
            expectedTrialCount: plan.trials.length,
            questionnaireResponseCount: questionnaireRows.length,
            rubricRatingCount: rubricRows.length,
        });
        console.log(JSON.stringify({ outputDirectory: paths.exportDirectory, acceptedTrials: result.trialRows.length, rejectedTrials: result.rejectedTrials.length }, null, 2));
        if (result.rejectedTrials.length) process.exitCode = 2;
        return result;
    }
    if (command === "withdraw") {
        if (args.confirm !== participantId || args["yes-delete"] !== "true") {
            throw new Error(`withdrawal requires --confirm=${participantId} --yes-delete=true`);
        }
        if (fs.existsSync(paths.directory)) fs.rmSync(paths.directory, { recursive: true, force: false });
        console.log(JSON.stringify({ withdrawn: true, participantId, deletedDirectory: paths.directory }, null, 2));
        return;
    }
    throw new Error("usage: study_operator.js plan|preflight|status|runtime|start|event|end|abort|questionnaire|rubric|export|withdraw --participant=P001 ...");
}

if (require.main === module) {
    try { run(); }
    catch (error) {
        console.error(`[study_operator] ${error.message}`);
        process.exitCode = 1;
    }
}

module.exports = { PARTICIPANTS_ROOT, participantPaths, preflight, run };
