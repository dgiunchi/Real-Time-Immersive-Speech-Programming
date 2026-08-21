"use strict";

require("./load-local-env");

async function main() {
    if (!process.env.ANTHROPIC_API_KEY) {
        throw new Error("ANTHROPIC_API_KEY is not configured");
    }

    const { query } = await import("@anthropic-ai/claude-agent-sdk");
    const intent = process.argv.slice(2).join(" ") || "Change the selected Cube color to red.";
    const startedAt = Date.now();
    let response = "";

    console.log(`[test] intent: ${intent}`);
    console.log("[test] sending one direct request to Claude (no Unity, STT, Ubiq, or MCP)...");

    for await (const message of query({
        prompt:
            `The selected Unity GameObject is named Cube. User request: ${JSON.stringify(intent)}\n` +
            "Return exactly one safe Unity C# MonoBehaviour implementation. " +
            "Do not use keyboard or mouse input. Do not change the object's tag. " +
            "Output only one csharp fenced code block.",
        options: {
            model: "sonnet",
            cwd: require("path").join(__dirname, "..", "orchestrator"),
            maxTurns: 1,
        },
    })) {
        if (message.type === "assistant") {
            for (const block of message.message.content || []) {
                if (block.type === "text") response += block.text;
            }
        }
        if (message.type === "result" && message.subtype !== "success") {
            throw new Error(`Claude result subtype: ${message.subtype}`);
        }
    }

    const elapsedSeconds = ((Date.now() - startedAt) / 1000).toFixed(1);
    if (!response.trim()) throw new Error("Claude returned no text");
    console.log(`[test] Claude replied in ${elapsedSeconds}s`);
    console.log("--- response ---");
    console.log(response.trim());
}

main().catch((error) => {
    console.error(`[test] FAILED: ${error.message}`);
    process.exitCode = 1;
});
