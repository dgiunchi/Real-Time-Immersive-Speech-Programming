import { createRequire } from "node:module";
import { selectPairWithClaude, validateModelPair } from "../orchestrator/fast_implicit_pipeline.mjs";

const require = createRequire(import.meta.url);
require("../scripts/load-local-env");

const model = process.env.AGENTICXR_MODEL_ID || "claude-sonnet-4-6";
const scene = {
    focus: {
        id: "study-l1-a-tool-2",
        name: "Yellow Tool",
        tag: "game",
        type: "dynamic",
        pos: [0, 1, 0],
        components: ["Rigidbody", "BoxCollider", "StableObjectId"],
    },
    halo: [
        {
            id: "study-l1-a-tray-3",
            name: "Far Tray",
            tag: "game",
            type: "static",
            pos: [4, 1, 0],
            components: ["BoxCollider", "StableObjectId"],
        },
        {
            id: "study-l1-a-tray-1",
            name: "Near Tray",
            tag: "game",
            type: "static",
            pos: [1, 1, 0],
            components: ["BoxCollider", "StableObjectId"],
        },
    ],
};

if (!process.env.ANTHROPIC_API_KEY) throw new Error("ANTHROPIC_API_KEY is missing.");
const result = await selectPairWithClaude({
    scene,
    interactionMode: "L1",
    model,
    apiKey: process.env.ANTHROPIC_API_KEY,
    timeoutMs: 22000,
});
const pair = validateModelPair(scene, "L1", result.pair);
console.log(JSON.stringify({
    model: result.reportedModel,
    latencyMs: result.latencyMs,
    usage: result.usage,
    sourceId: pair.source.id,
    destinationId: pair.destination.id,
    stopReason: result.stopReason,
}, null, 2));
