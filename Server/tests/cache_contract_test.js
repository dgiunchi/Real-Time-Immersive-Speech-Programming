"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");
const { AgentWorkingCache } = require("../cache/agent_working_cache");
const { EventJournal } = require("../cache/event_journal");
const { CacheReconciler } = require("../cache/cache_reconciler");
const { ProposalGate } = require("../cache/proposal_gate");
const { makeCacheEnvelope, toWireFormat, fromWireFormat } = require("../cache/protocol");
const { checkModePolicy } = require("../orchestrator/mode_policy");

const root = path.resolve(__dirname, "..");
let assertions = 0;
function ok(value, message) { assert.ok(value, message); assertions += 1; }
function equal(actual, expected, message) { assert.strictEqual(actual, expected, message); assertions += 1; }

function walk(dir, result = []) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        if (["node_modules", "vendor", "data"].includes(entry.name)) continue;
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) walk(full, result);
        else if (/\.(?:js|mjs)$/.test(entry.name)) result.push(full);
    }
    return result;
}

for (const file of walk(root)) {
    const checked = spawnSync(process.execPath, ["--check", file], { encoding: "utf8" });
    equal(checked.status, 0, `node --check failed for ${path.relative(root, file)}: ${checked.stderr}`);
}

const wire = toWireFormat(makeCacheEnvelope({
    type: "ArtifactProposal",
    correlationId: "corr-wire",
    targetObjectId: "object-1",
    payload: { code: "class Test {}", validationState: "accepted" },
}));
equal(typeof wire.payload, "string", "Unity-bound payload must be a JSON string");
equal(fromWireFormat(wire).payload.validationState, "accepted", "wire payload must round-trip");

const workingCache = new AgentWorkingCache();
const journal = new EventJournal({ maxEntriesPerSession: 32 });
const reconciler = new CacheReconciler({ workingCache, journal });
function delta(seq, revision, overrides = {}) {
    return {
        type: "SceneDelta",
        sessionId: "test-session",
        correlationId: `delta-${seq}`,
        stableObjectId: "object-1",
        objectRevision: revision,
        deltaSeq: seq,
        sceneEpoch: "epoch-1",
        snapshotId: "snapshot-1",
        timestamp: Date.now(),
        ttlMs: 10000,
        payload: { tag: "game", region: "lab", state: { revision } },
        ...overrides,
    };
}

equal(reconciler.reconcileDelta(delta(1, 1)).outcome, "accepted", "first delta accepted");
const gap = reconciler.reconcileDelta(delta(3, 3));
equal(gap.recommendedAction, "backfill", "missing delta range requests backfill");
equal(gap.detail.gap.fromSeq, 2, "gap starts at missing sequence");
equal(reconciler.reconcileDelta(delta(2, 2), { isBackfill: true }).outcome, "accepted", "late backfill accepted");
equal(reconciler.reconcileDelta(delta(3, 3), { isBackfill: true }).outcome, "duplicate", "overlapping backfill is idempotent");
equal(workingCache.getByObjectId("object-1").objectRevision, 3, "late backfill cannot regress current state");

const gate = new ProposalGate({ workingCache, reconciler });
const accepted = gate.checkProposal({
    correlationId: "proposal-current",
    targetObjectId: "object-1",
    sceneEpoch: "epoch-1",
    snapshotId: "snapshot-1",
    objectRevision: 3,
    snapshotTakenAt: Date.now(),
    authoringMode: "semi_auto_confirm",
    consentRoute: "explicit_confirmation",
    validationState: "accepted",
});
ok(accepted.accepted, "current validated proposal passes preflight");

const stale = gate.checkProposal({
    correlationId: "proposal-stale",
    targetObjectId: "object-1",
    sceneEpoch: "epoch-1",
    snapshotId: "snapshot-1",
    objectRevision: 1,
    snapshotTakenAt: Date.now() - 60000,
    authoringMode: "automatic",
    consentRoute: "automatic_low_risk",
    validationState: "accepted",
});
ok(!stale.accepted, "stale proposal is rejected");
ok(stale.reasons.some((reason) => reason.includes("objectRevision mismatch")), "revision rejection is explicit");
ok(stale.reasons.some((reason) => reason.includes("snapshot too old")), "age rejection is explicit");

ok(checkModePolicy({ interactionMode: "L1", authoringMode: "automatic", riskScore: 0.1,
    triggerSource: "system_opportunity", reversible: true, localOnly: true }).accepted,
"low-risk reversible L1 may be automatic");
ok(!checkModePolicy({ interactionMode: "L4", authoringMode: "automatic", riskScore: 0.1,
    triggerSource: "explicit_request", reversible: true, localOnly: true }).accepted,
"L4 cannot bypass confirmation");
ok(!checkModePolicy({ interactionMode: "L3", authoringMode: "semi_auto_confirm",
    triggerSource: "clarification", detailResolved: false }).accepted,
"L3 cannot continue before clarification is resolved");

const unityManager = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "CacheExchangeManager.cs"), "utf8");
const unityPublisher = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "AgenticCache", "CachePublisher.cs"), "utf8");
for (const required of ["CommitAccepted", "CommitRejected", "UserDecision", "RollbackResult", "ValidateProposalEnvelope", "BuildBackfillPayload"]) {
    ok(unityManager.includes(required) || unityPublisher.includes(required), `Unity contract contains ${required}`);
}
ok(unityPublisher.includes("PublishCurrentSnapshot();"), "production Unity publisher emits a snapshot");
ok(unityPublisher.includes("PublishStateDelta("), "production Unity publisher scans changed state");
const roslynRuntime = fs.readFileSync(path.join(root, "..", "Unity", "Assets", "Scenes", "Scripts", "TestRoslyn.cs"), "utf8");
for (const deniedCapability of ["system.io", "system.net", "system.diagnostics", "system.reflection",
    "system.runtime.interopservices", "unityengine.networking", "dllimport", "stackalloc",
    "application.quit", "application.openurl", "environment.exit"]) {
    ok(roslynRuntime.includes(`\"${deniedCapability}\"`), `capability policy denies ${deniedCapability}`);
}
ok(roslynRuntime.includes("allowedNamespaces"), "capability policy has an explicit namespace allowlist");

console.log(`[cache_contract_test] PASS (${assertions} assertions)`);
