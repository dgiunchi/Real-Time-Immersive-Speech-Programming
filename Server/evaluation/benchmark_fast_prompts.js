"use strict";

// Live, deliberately small prompt benchmark for the L1/L2 latency decision.
// It bypasses the Agent SDK/MCP loop so it measures the lower bound for a
// single model decision. Results are written under gitignored evaluation/data.
const fs = require("fs");
const path = require("path");
require("../scripts/load-local-env");

const MODEL_ID = process.env.AGENTICXR_MODEL_ID || "claude-sonnet-4-6";
const API_URL = "https://api.anthropic.com/v1/messages";
const targetId = process.env.AGENTICXR_BENCHMARK_TARGET || "study-l1-a-tool-1";
const destinationId = process.env.AGENTICXR_BENCHMARK_DESTINATION || "study-l1-a-tray-1";

const candidates = [
    {
        id: "fixed_action_json",
        maxTokens: 120,
        system: "Return only one compact JSON object. No analysis, prose, markdown, or alternatives.",
        prompt: `Create the fixed L1/L2 guidance command for target ${targetId} and destination ${destinationId}. ` +
            "Return exactly this schema: {\"action\":\"highlight_pair\",\"targetId\":string," +
            "\"destinationId\":string,\"colorA\":\"cyan\",\"colorB\":\"magenta\"," +
            "\"scalePulse\":1.08,\"durationSeconds\":0}. Use the supplied ids unchanged.",
    },
    {
        id: "bounded_action_choice_json",
        maxTokens: 160,
        system: "You select one safe visual guidance template. Return only compact JSON and no reasoning.",
        prompt: `L1/L2 task: move ${targetId} to ${destinationId}. Choose exactly one action from ` +
            "highlight_pair, pulse_destination, or pulse_target. Prefer the action that makes both the movable " +
            "object and its destination unambiguous. Return {\"action\":string,\"targetId\":string," +
            "\"destinationId\":string,\"colorA\":\"cyan\",\"colorB\":\"magenta\",\"scalePulse\":1.08}.",
    },
    {
        id: "minimal_csharp_pair",
        maxTokens: 900,
        system: "Return only ASCII C# source. No markdown, explanation, alternatives, or comments.",
        prompt: `Write one minimal Unity MonoBehaviour attached to ${targetId}. In Awake, find the destination ` +
            `GameObject named ${destinationId}. Continuously pulse both Renderers cyan to magenta and their scales ` +
            "from the exact original scale to 1.08x using unscaled time. Use MaterialPropertyBlock, never mutate a " +
            "shared Material. Restore exact original property blocks and scales idempotently in OnDisable and " +
            "OnDestroy. No input, networking, files, reflection, spawning, or other behavior.",
    },
];

function argument(name, fallback) {
    const prefix = `--${name}=`;
    const value = process.argv.slice(2).find((item) => item.startsWith(prefix));
    return value ? value.slice(prefix.length) : fallback;
}

async function runCandidate(candidate) {
    const startedAt = Date.now();
    const response = await fetch(API_URL, {
        method: "POST",
        headers: {
            "content-type": "application/json",
            "x-api-key": process.env.ANTHROPIC_API_KEY,
            "anthropic-version": "2023-06-01",
        },
        body: JSON.stringify({
            model: MODEL_ID,
            max_tokens: candidate.maxTokens,
            temperature: 0,
            system: candidate.system,
            messages: [{ role: "user", content: candidate.prompt }],
        }),
    });
    const latencyMs = Date.now() - startedAt;
    const body = await response.json();
    if (!response.ok) throw new Error(`${candidate.id}: HTTP ${response.status}: ${body.error && body.error.message || "unknown error"}`);
    const output = (body.content || []).filter((part) => part.type === "text").map((part) => part.text).join("");
    return {
        candidateId: candidate.id,
        model: body.model || MODEL_ID,
        latencyMs,
        inputTokens: body.usage && body.usage.input_tokens || 0,
        outputTokens: body.usage && body.usage.output_tokens || 0,
        stopReason: body.stop_reason || null,
        outputCharacters: output.length,
        output,
    };
}

async function main() {
    if (!process.env.ANTHROPIC_API_KEY) throw new Error("ANTHROPIC_API_KEY is required");
    const runs = Math.min(3, Math.max(1, Number(argument("runs", "1")) || 1));
    const results = [];
    for (let run = 1; run <= runs; run += 1) {
        for (const candidate of candidates) {
            process.stdout.write(`[prompt-benchmark] run=${run} candidate=${candidate.id} ... `);
            const result = await runCandidate(candidate);
            results.push({ run, ...result });
            console.log(`${result.latencyMs}ms tokens=${result.inputTokens}+${result.outputTokens}`);
        }
    }
    const report = {
        schemaVersion: "1.0",
        generatedAt: new Date().toISOString(),
        model: MODEL_ID,
        targetId,
        destinationId,
        note: "Direct single-call lower bound; Unity transport/compile/attach time is not included.",
        results,
    };
    const outputPath = path.join(__dirname, "data", `fast-prompt-benchmark-${Date.now()}.json`);
    fs.mkdirSync(path.dirname(outputPath), { recursive: true });
    fs.writeFileSync(outputPath, JSON.stringify(report, null, 2) + "\n");
    console.log(`[prompt-benchmark] wrote ${outputPath}`);
}

if (require.main === module) main().catch((error) => {
    console.error(`[prompt-benchmark] ${error.message}`);
    process.exitCode = 1;
});

module.exports = { candidates, runCandidate };
