"use strict";

// Backend comparability for the AgenticXR study.
//
// The study pins one model through study/model_pin.js. Comparing more than one
// backend (for example Claude against GPT or Gemini) needs a second, different
// guarantee: not "the model is the one we froze", but "every backend was given
// the same study and scored the same way, and the only things that differed are
// the ones we declared may differ".
//
// This module makes that guarantee executable. It computes digests over the
// artifacts and runtime invariants that must be identical across backends, and
// refuses to emit a trial record for a backend whose digests do not match the
// pinned comparison contract. Without it, a multi backend result is a set of
// separate studies reported side by side rather than a controlled comparison.

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const pin = require("./backend_pin.v1.json");
const { verifyAnalysisPlanLock } = require("./analysis_plan_lock");

const STUDY_DIR = __dirname;
const ORCHESTRATOR_PATH = path.join(__dirname, "..", "orchestrator", "app.js");

function canonicalSha256(text) {
    return crypto.createHash("sha256").update(String(text).replace(/\r\n/g, "\n")).digest("hex");
}

function fileSha256(filePath) {
    return canonicalSha256(fs.readFileSync(filePath, "utf8"));
}

// Per artifact hashes for everything the comparison holds constant. Any backend
// run against a different version of any of these is not comparable.
function heldConstantHashes() {
    const result = {};
    for (const name of pin.heldConstant.artifacts) {
        const filePath = path.join(STUDY_DIR, name);
        result[name] = fs.existsSync(filePath) ? fileSha256(filePath) : null;
    }
    return result;
}

function heldConstantDigest() {
    const hashes = heldConstantHashes();
    const canonical = Object.keys(hashes).sort().map((k) => `${k}:${hashes[k]}`).join("\n");
    return canonicalSha256(canonical);
}

// The agent tool surface must be identical across backends: the same tools, with
// the same names. Derived from the orchestrator source text rather than an
// import, because the tool list is not exported and reading it must not depend
// on the orchestrator's internals or trigger its side effects.
function toolSurface() {
    const source = fs.readFileSync(ORCHESTRATOR_PATH, "utf8");
    const names = new Set();
    const pattern = /bridgeTool\("([a-z_]+)"\)/g;
    let match;
    while ((match = pattern.exec(source)) !== null) names.add(match[1]);
    return Array.from(names).sort();
}

function toolSurfaceDigest() {
    return canonicalSha256(toolSurface().join(","));
}

function registeredBackends() {
    return pin.backends.filter((backend) => backend.status === "registered");
}

function findBackend(backendId) {
    return pin.backends.find((backend) => backend.backendId === backendId) || null;
}

// Validates the comparison contract itself: the held constant artifacts and the
// tool surface still hash to what the contract froze, and the locked analysis
// plan is intact. This is what fails when someone edits a task card or adds a
// tool without opening a new method version.
function validateBackendPin() {
    const actualHeldConstant = heldConstantDigest();
    const actualToolSurface = toolSurfaceDigest();
    const analysisLock = verifyAnalysisPlanLock();
    const missingArtifacts = Object.entries(heldConstantHashes())
        .filter(([, hash]) => hash === null).map(([name]) => name);

    const checks = [
        {
            id: "held-constant-digest",
            ok: actualHeldConstant === pin.heldConstantDigest,
            expected: pin.heldConstantDigest,
            actual: actualHeldConstant,
        },
        {
            id: "tool-surface-digest",
            ok: actualToolSurface === pin.toolSurfaceDigest,
            expected: pin.toolSurfaceDigest,
            actual: actualToolSurface,
        },
        {
            id: "held-constant-artifacts-present",
            ok: missingArtifacts.length === 0,
            expected: "all held-constant artifacts present",
            actual: missingArtifacts.length ? `missing: ${missingArtifacts.join(", ")}` : "all present",
        },
        {
            id: "analysis-plan-lock",
            ok: analysisLock.ok,
            expected: analysisLock.expectedSha256,
            actual: analysisLock.actualSha256,
        },
        {
            id: "at-least-one-registered-backend",
            ok: registeredBackends().length >= 1,
            expected: ">= 1 registered backend",
            actual: `${registeredBackends().length} registered`,
        },
    ];

    return { ok: checks.every((check) => check.ok), pin, checks };
}

const REQUIRED_BACKEND_FIELDS = [
    "backendId", "providerId", "modelId", "modelVersionString",
    "systemPromptHash", "candidateCountDefault", "toolsetVersion",
];

// A backend is only admissible if it declares every field the contract requires
// and agrees with the contract on the invariants that may not vary.
function validateBackend(backendId) {
    const backend = findBackend(backendId);
    if (!backend) {
        return { ok: false, backendId, checks: [{ id: "backend-registered", ok: false, expected: "a backend entry", actual: "not found" }] };
    }

    const missingFields = REQUIRED_BACKEND_FIELDS.filter((field) => backend[field] === undefined || backend[field] === null);

    const checks = [
        { id: "backend-status", ok: backend.status === "registered", expected: "registered", actual: backend.status },
        {
            id: "required-fields",
            ok: missingFields.length === 0,
            expected: REQUIRED_BACKEND_FIELDS.join(", "),
            actual: missingFields.length ? `missing: ${missingFields.join(", ")}` : "all present",
        },
        {
            id: "candidate-count-matches-contract",
            ok: backend.candidateCountDefault === pin.invariants.candidateCountDefault,
            expected: pin.invariants.candidateCountDefault,
            actual: backend.candidateCountDefault,
        },
        {
            id: "method-version-matches-contract",
            ok: backend.methodVersion === pin.methodVersion,
            expected: pin.methodVersion,
            actual: backend.methodVersion,
        },
    ];

    return { ok: checks.every((check) => check.ok), backendId, backend, checks };
}

// The pairwise question the paper actually needs to answer: were these two
// backends run under the same study? Differences listed in mayVary are expected
// and are reported, not failed.
function assertComparable(backendIdA, backendIdB) {
    const a = validateBackend(backendIdA);
    const b = validateBackend(backendIdB);
    const contract = validateBackendPin();

    const declaredDifferences = [];
    if (a.backend && b.backend) {
        for (const field of pin.mayVary) {
            if (a.backend[field] !== undefined && b.backend[field] !== undefined && a.backend[field] !== b.backend[field]) {
                declaredDifferences.push({ field, [backendIdA]: a.backend[field], [backendIdB]: b.backend[field] });
            }
        }
    }

    const checks = [
        { id: "contract-valid", ok: contract.ok, expected: "backend pin valid", actual: contract.ok ? "valid" : "invalid" },
        { id: `backend-valid:${backendIdA}`, ok: a.ok, expected: "valid", actual: a.ok ? "valid" : "invalid" },
        { id: `backend-valid:${backendIdB}`, ok: b.ok, expected: "valid", actual: b.ok ? "valid" : "invalid" },
        {
            id: "same-method-version",
            ok: Boolean(a.backend && b.backend && a.backend.methodVersion === b.backend.methodVersion),
            expected: pin.methodVersion,
            actual: a.backend && b.backend ? `${a.backend.methodVersion} vs ${b.backend.methodVersion}` : "unresolved",
        },
        {
            id: "same-candidate-count",
            ok: Boolean(a.backend && b.backend && a.backend.candidateCountDefault === b.backend.candidateCountDefault),
            expected: pin.invariants.candidateCountDefault,
            actual: a.backend && b.backend ? `${a.backend.candidateCountDefault} vs ${b.backend.candidateCountDefault}` : "unresolved",
        },
    ];

    return {
        ok: checks.every((check) => check.ok),
        comparisonId: pin.comparisonId,
        backends: [backendIdA, backendIdB],
        checks,
        declaredDifferences,
    };
}

// The record embedded in every trial export so a backend label in the results
// can be traced back to the exact comparison contract it ran under.
function trialBackendPin(backendId) {
    const contract = validateBackendPin();
    if (!contract.ok) {
        throw new Error(`backend pin mismatch: ${contract.checks.filter((c) => !c.ok).map((c) => c.id).join(", ")}`);
    }
    const backend = validateBackend(backendId);
    if (!backend.ok) {
        throw new Error(`backend not comparable: ${backendId}: ${backend.checks.filter((c) => !c.ok).map((c) => c.id).join(", ")}`);
    }
    return {
        comparisonId: pin.comparisonId,
        methodVersion: pin.methodVersion,
        backendId: backend.backend.backendId,
        providerId: backend.backend.providerId,
        modelId: backend.backend.modelId,
        modelVersionString: backend.backend.modelVersionString,
        systemPromptHash: backend.backend.systemPromptHash,
        candidateCountDefault: backend.backend.candidateCountDefault,
        toolsetVersion: backend.backend.toolsetVersion,
        heldConstantDigest: pin.heldConstantDigest,
        toolSurfaceDigest: pin.toolSurfaceDigest,
    };
}

function comparabilityReport() {
    const contract = validateBackendPin();
    const backends = pin.backends.map((backend) => ({
        backendId: backend.backendId,
        providerId: backend.providerId,
        status: backend.status,
        valid: backend.status === "registered" ? validateBackend(backend.backendId).ok : null,
    }));
    return {
        ok: contract.ok,
        comparisonId: pin.comparisonId,
        methodVersion: pin.methodVersion,
        heldConstantDigest: pin.heldConstantDigest,
        toolSurfaceDigest: pin.toolSurfaceDigest,
        toolCount: toolSurface().length,
        backends,
        contractChecks: contract.checks,
    };
}

module.exports = {
    pin,
    canonicalSha256,
    fileSha256,
    heldConstantHashes,
    heldConstantDigest,
    toolSurface,
    toolSurfaceDigest,
    registeredBackends,
    findBackend,
    validateBackendPin,
    validateBackend,
    assertComparable,
    trialBackendPin,
    comparabilityReport,
};
