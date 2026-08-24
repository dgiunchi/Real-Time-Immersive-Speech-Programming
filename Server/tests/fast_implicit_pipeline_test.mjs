import assert from "node:assert/strict";
import {
    PAIR_SELECTION_SYSTEM_PROMPT,
    compactScene,
    boundedSceneForMode,
    nearestBoundedPair,
    validateModelPair,
    renderGuidanceCode,
    validateBoundedCode,
} from "../orchestrator/fast_implicit_pipeline.mjs";

const envelope = {
    payload: {
        focus: {
            id: "study-l1-a-tool-2",
            name: "Yellow Tool",
            tag: "game",
            transform: { pos: [0, 1, 0] },
            components: [{ type: "Rigidbody" }, { type: "StableObjectId" }],
        },
        halo: [
            {
                id: "study-l1-a-tray-3",
                name: "Far Tray",
                tag: "game",
                transform: { pos: [4, 1, 0] },
                components: ["BoxCollider", "StableObjectId"],
            },
            {
                id: "study-l1-a-tray-1",
                name: "Near Tray",
                tag: "game",
                transform: { pos: [1, 1, 0] },
                components: ["BoxCollider", "StableObjectId"],
            },
        ],
    },
};

const scene = compactScene(envelope);
assert.equal(scene.focus.id, "study-l1-a-tool-2");
assert.deepEqual(scene.focus.components, ["Rigidbody", "StableObjectId"]);

const pair = validateModelPair(scene, "L1", {
    sourceId: "study-l1-a-tool-2",
    destinationId: "study-l1-a-tray-1",
    reason: "Nearest compatible tray.",
});
assert.equal(pair.source.id, scene.focus.id);
assert.equal(pair.destination.name, "Near Tray");

const fallback = nearestBoundedPair(scene, "L1");
assert.equal(fallback.destination.id, "study-l1-a-tray-1", "fallback uses spatial proximity, not matching suffixes");
const bounded = boundedSceneForMode({
    focus: scene.focus,
    halo: [...scene.halo, {
        id: "study-l1-a-bench-anchor",
        name: "Workbench",
        tag: "game",
        pos: [0.1, 1, 0],
        components: ["AgenticInertAnchor"],
    }],
}, "L1");
assert.equal(bounded.halo.length, 2, "irrelevant anchors are removed before the Claude request");
assert.ok(bounded.halo.every((item) => /tray/i.test(item.id)), "only compatible destinations reach Claude");
assert.throws(() => validateModelPair(scene, "L1", {
    sourceId: "study-l1-a-tool-2",
    destinationId: "outside-scene",
}), /outside the grounded scene/);

const code = renderGuidanceCode({ destinationName: pair.destination.name, correlationId: "activity-test-123" });
assert.match(code, /GameObject\.Find\("Near Tray"\)/);
assert.match(code, /GetComponentsInChildren<Renderer>\(true\)/);
assert.match(code, /MaterialPropertyBlock/);
assert.match(code, /sourceScale \* 1\.08f/);
assert.match(code, /destinationScale \* 1\.08f/);
assert.deepEqual(validateBoundedCode(code, "Near Tray"), { accepted: true, reasons: [] });
assert.match(PAIR_SELECTION_SYSTEM_PROMPT, /Never infer a pair from a shared numeric suffix/);

console.log("fast implicit direct pipeline tests passed");
