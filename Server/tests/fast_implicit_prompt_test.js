"use strict";

const assert = require("assert");
const { SYSTEM_PROMPT, FAST_IMPLICIT_SYSTEM_PROMPT, isFastImplicitMode } = require("../orchestrator/app");

assert.strictEqual(isFastImplicitMode({
    AGENTICXR_INTERACTION_MODE: "L1",
    AGENTICXR_TRIGGER_SOURCE: "system_opportunity",
}), true);
assert.strictEqual(isFastImplicitMode({
    AGENTICXR_INTERACTION_MODE: "L2",
    AGENTICXR_TRIGGER_SOURCE: "context",
}), true);
assert.strictEqual(isFastImplicitMode({
    AGENTICXR_INTERACTION_MODE: "L5",
    AGENTICXR_TRIGGER_SOURCE: "explicit_request",
}), false);
assert.strictEqual(isFastImplicitMode({
    AGENTICXR_INTERACTION_MODE: "L1",
    AGENTICXR_TRIGGER_SOURCE: "system_opportunity",
    AGENTICXR_FAST_IMPLICIT_PROMPT: "false",
}), false);
assert.strictEqual(isFastImplicitMode({
    AGENTICXR_INTERACTION_MODE: "L2",
    AGENTICXR_TRIGGER_SOURCE: "context",
    AGENTICXR_SPECULATIVE_ONLY: "true",
}), false);

assert.ok(FAST_IMPLICIT_SYSTEM_PROMPT.length < SYSTEM_PROMPT.length / 3,
    "fast implicit prompt stays materially smaller than the full route");
assert.match(FAST_IMPLICIT_SYSTEM_PROMPT, /exactly one minimal ASCII-only C# MonoBehaviour/);
assert.match(FAST_IMPLICIT_SYSTEM_PROMPT, /cyan-to-magenta color pulse/);
assert.match(FAST_IMPLICIT_SYSTEM_PROMPT, /originalScale \* 1\.08/);
assert.match(FAST_IMPLICIT_SYSTEM_PROMPT, /Do not choose or/);
assert.match(FAST_IMPLICIT_SYSTEM_PROMPT, /validator_critic once/);
assert.match(FAST_IMPLICIT_SYSTEM_PROMPT, /simulate_artifact/);
assert.match(FAST_IMPLICIT_SYSTEM_PROMPT, /propose_artifact/);
assert.match(FAST_IMPLICIT_SYSTEM_PROMPT, /No broad scene exploration/);

console.log("fast implicit prompt tests passed");
