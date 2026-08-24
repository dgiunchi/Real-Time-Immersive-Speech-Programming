"use strict";

// Deterministic checks for the backend comparison contract. No network, no model,
// no Unity. Proves that a multi backend result would be a controlled comparison
// rather than several separate studies reported side by side.

const assert = require("assert");
const path = require("path");
const fs = require("fs");

const eq = require("../study/backend_equivalence");
const modelPin = require("../study/model_pin.v1.json");

let count = 0;
function check(label, condition) {
    assert.ok(condition, `FAILED: ${label}`);
    count += 1;
    console.log(`[backend_equivalence assertion ${count}] PASS: ${label}`);
}

// Contract integrity
const contract = eq.validateBackendPin();
check("the backend comparison contract validates against the current artifacts", contract.ok);
for (const item of contract.checks) {
    check(`contract check '${item.id}' passes`, item.ok);
}

// Held constant artifacts really exist and really hash
const hashes = eq.heldConstantHashes();
check("every held constant artifact resolves to a file", Object.values(hashes).every((h) => typeof h === "string" && h.length === 64));
check("the held constant set covers the task cards", Object.keys(hashes).includes("task_cards.v1.json"));
check("the held constant set covers the rubrics", Object.keys(hashes).includes("rubrics.v1.json"));
check("the held constant set covers the questionnaires", Object.keys(hashes).includes("questionnaires.v1.json"));
check("the held constant set covers the locked analysis plan", Object.keys(hashes).includes("analysis_plan.v1.json"));
check("the held constant set covers the interaction contract", Object.keys(hashes).includes("interaction_contract.v1.json"));
check("model_pin is deliberately not held constant, since it is the thing that varies", !Object.keys(hashes).includes("model_pin.v1.json"));

// Tool surface
const tools = eq.toolSurface();
check("the tool surface is non empty", tools.length > 0);
check("the tool surface is sorted and unique", JSON.stringify(tools) === JSON.stringify(Array.from(new Set(tools)).sort()));
check("the tool surface includes the verification space simulate operation", tools.includes("simulate_artifact"));
check("the tool surface includes propose_artifact", tools.includes("propose_artifact"));
check("the tool surface digest is stable across repeated computation", eq.toolSurfaceDigest() === eq.toolSurfaceDigest());
check("the held constant digest is stable across repeated computation", eq.heldConstantDigest() === eq.heldConstantDigest());

// The registered Claude backend mirrors the single model pin
const claude = eq.validateBackend("claude-sonnet-4");
check("the Claude backend is registered and valid", claude.ok);
check("the Claude backend model id agrees with study/model_pin.v1.json", claude.backend.modelId === modelPin.modelId);
check("the Claude backend system prompt hash agrees with study/model_pin.v1.json", claude.backend.systemPromptHash === modelPin.systemPromptHash);
check("the Claude backend candidate count agrees with study/model_pin.v1.json", claude.backend.candidateCountDefault === modelPin.candidateCountDefault);

// Unregistered backends must be refused
const declared = eq.pin.backends.filter((b) => b.status === "declared");
check("placeholder backends are present but not registered", declared.length >= 1);
for (const backend of declared) {
    const validated = eq.validateBackend(backend.backendId);
    check(`declared backend '${backend.backendId}' is refused until registered`, !validated.ok);
    assert.throws(() => eq.trialBackendPin(backend.backendId), /not comparable/);
    count += 1;
    console.log(`[backend_equivalence assertion ${count}] PASS: trialBackendPin refuses '${backend.backendId}'`);
}
check("an unknown backend id is refused", !eq.validateBackend("does-not-exist").ok);

// Trial record
const record = eq.trialBackendPin("claude-sonnet-4");
check("a trial backend record carries the comparison id", record.comparisonId === eq.pin.comparisonId);
check("a trial backend record carries the held constant digest", record.heldConstantDigest === eq.pin.heldConstantDigest);
check("a trial backend record carries the tool surface digest", record.toolSurfaceDigest === eq.pin.toolSurfaceDigest);
check("a trial backend record identifies the provider", record.providerId === "anthropic");

// Pairwise comparability, including self comparison as the trivial case
const self = eq.assertComparable("claude-sonnet-4", "claude-sonnet-4");
check("a backend is comparable with itself", self.ok);
check("self comparison reports no declared differences", self.declaredDifferences.length === 0);
const mixed = eq.assertComparable("claude-sonnet-4", "UNREGISTERED-openai");
check("a registered backend is not comparable with an unregistered one", !mixed.ok);

// Tamper detection: the contract must fail if a held constant artifact changes
const victim = path.join(__dirname, "..", "study", "rubrics.v1.json");
const original = fs.readFileSync(victim, "utf8");
try {
    fs.writeFileSync(victim, original.replace(/\}\s*$/, ', "__tamper__": true }'));
    delete require.cache[require.resolve("../study/backend_equivalence")];
    const tampered = require("../study/backend_equivalence");
    check("editing a held constant artifact breaks the comparison contract", !tampered.validateBackendPin().ok);
    assert.throws(() => tampered.trialBackendPin("claude-sonnet-4"), /backend pin mismatch/);
    count += 1;
    console.log(`[backend_equivalence assertion ${count}] PASS: a tampered artifact blocks trial export`);
} finally {
    fs.writeFileSync(victim, original);
    delete require.cache[require.resolve("../study/backend_equivalence")];
}

// Restored state must validate again, so the test leaves nothing behind
const restored = require("../study/backend_equivalence");
check("restoring the artifact restores the contract", restored.validateBackendPin().ok);

const report = restored.comparabilityReport();
check("the comparability report is ok", report.ok);
check("the comparability report lists every backend", report.backends.length === eq.pin.backends.length);

console.log(`\n[backend_equivalence_test] PASS (${count} assertions)`);
