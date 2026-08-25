"use strict";

// Wire format tests for the OpenAI and Gemini adapters, plus an end to end run
// of each through the real agent loop with a fake HTTP layer. No API key and no
// network: fetch is injected.

const assert = require("assert");
const openai = require("../orchestrator/providers/openai_adapter");
const gemini = require("../orchestrator/providers/gemini_adapter");
const { runAgentTurn } = require("../orchestrator/providers/agent_loop");

let assertions = 0;
function check(condition, message) {
    assert.ok(condition, `FAILED: ${message}`);
    assertions += 1;
}

function jsonResponse(body, ok = true, status = 200) {
    return { ok, status, json: async () => body, text: async () => JSON.stringify(body) };
}

(async () => {
    // ---------- OpenAI wire format ----------
    {
        const wire = openai.toWireMessages([
            { role: "system", content: "SYS" },
            { role: "user", content: "hi" },
            { role: "assistant", content: "", toolCalls: [{ callId: "c1", name: "query_scene", arguments: { a: 1 } }] },
            { role: "tool", name: "query_scene", callId: "c1", content: '{"ok":true}' },
        ]);
        check(wire[0].role === "system" && wire[0].content === "SYS", "openai keeps the system message inline");
        check(wire[2].tool_calls[0].id === "c1", "openai preserves the call id");
        check(wire[2].tool_calls[0].function.name === "query_scene", "openai renders the tool name");
        check(JSON.parse(wire[2].tool_calls[0].function.arguments).a === 1, "openai serialises arguments to a JSON string");
        check(wire[3].role === "tool" && wire[3].tool_call_id === "c1", "openai matches the tool result to its call");
    }
    {
        const parsed = openai.fromWireResponse({
            choices: [{ message: { content: "hello", tool_calls: [{ id: "x", type: "function", function: { name: "t", arguments: '{"k":2}' } }] } }],
            usage: { prompt_tokens: 11, completion_tokens: 3 },
        });
        check(parsed.text === "hello", "openai extracts assistant text");
        check(parsed.toolCalls[0].arguments.k === 2, "openai parses tool arguments");
        check(parsed.usage.inputTokens === 11 && parsed.usage.outputTokens === 3, "openai extracts usage");
    }
    {
        // A model can emit malformed JSON. That must become a recoverable tool
        // level failure, not an exception out of the adapter.
        const broken = openai.parseArguments("{not json");
        check(broken.__parseError !== undefined, "malformed tool arguments are reported, not thrown");
        check(broken.__raw === "{not json", "the raw argument text is preserved for diagnosis");
    }

    // ---------- Gemini wire format ----------
    {
        const contents = gemini.toWireContents([
            { role: "system", content: "SYS" },
            { role: "user", content: "hi" },
            { role: "assistant", content: "", toolCalls: [{ callId: "g0", name: "query_scene", arguments: { a: 1 } }] },
            { role: "tool", name: "query_scene", callId: "g0", content: '{"ok":true}' },
            { role: "tool", name: "propose_artifact", callId: "g1", content: '{"ok":true}' },
        ]);
        check(contents.every((c) => c.role !== "system"), "gemini removes the system message from contents");
        check(contents[0].role === "user", "gemini maps the user turn");
        check(contents[1].role === "model", "gemini maps assistant to the model role");
        check(contents[1].parts.some((p) => p.functionCall), "gemini renders a functionCall part");
        const responseTurns = contents.filter((c) => c.parts.every((p) => p.functionResponse));
        check(responseTurns.length === 1, "consecutive tool results are merged into one turn");
        check(responseTurns[0].parts.length === 2, "both tool results land in that single turn");
    }
    {
        const instruction = gemini.systemInstructionFrom([{ role: "system", content: "SYS" }]);
        check(instruction.parts[0].text === "SYS", "gemini lifts the system prompt into systemInstruction");
        check(gemini.systemInstructionFrom([{ role: "user", content: "x" }]) === undefined,
            "gemini omits systemInstruction when there is no system prompt");
    }
    {
        const parsed = gemini.fromWireResponse({
            candidates: [{ content: { parts: [
                { text: "thinking" },
                { functionCall: { name: "a", args: { x: 1 } } },
                { functionCall: { name: "b", args: {} } },
            ] } }],
            usageMetadata: { promptTokenCount: 7, candidatesTokenCount: 2 },
        }, 3);
        check(parsed.text === "thinking", "gemini extracts text parts");
        check(parsed.toolCalls.length === 2, "gemini extracts every functionCall");
        check(new Set(parsed.toolCalls.map((c) => c.callId)).size === 2, "synthesised call ids are unique within a turn");
        check(parsed.toolCalls[0].callId.startsWith("gemini-3-"), "synthesised ids carry the iteration");
        check(parsed.usage.inputTokens === 7, "gemini extracts usage");
    }

    // ---------- End to end through the real loop ----------
    // Both providers run the same scripted scenario: one tool call, then a final
    // answer. The loop's observable result must be equivalent, since that is the
    // premise of comparing backends at all.
    const scenarios = [];

    {
        let call = 0;
        const chat = openai.createOpenAIChat({
            model: "test-model", apiKey: "test-key",
            fetchImpl: async () => {
                call += 1;
                if (call === 1) {
                    return jsonResponse({ choices: [{ message: { content: "", tool_calls: [{ id: "c1", type: "function", function: { name: "query_scene", arguments: '{"objectId":"obj-1"}' } }] } }], usage: { prompt_tokens: 10, completion_tokens: 5 } });
                }
                return jsonResponse({ choices: [{ message: { content: "all done" } }], usage: { prompt_tokens: 20, completion_tokens: 4 } });
            },
        });
        scenarios.push({ name: "openai", chat });
    }
    {
        let call = 0;
        const chat = gemini.createGeminiChat({
            model: "test-model", apiKey: "test-key",
            fetchImpl: async () => {
                call += 1;
                if (call === 1) {
                    return jsonResponse({ candidates: [{ content: { parts: [{ functionCall: { name: "query_scene", args: { objectId: "obj-1" } } }] } }], usageMetadata: { promptTokenCount: 10, candidatesTokenCount: 5 } });
                }
                return jsonResponse({ candidates: [{ content: { parts: [{ text: "all done" }] } }], usageMetadata: { promptTokenCount: 20, candidatesTokenCount: 4 } });
            },
        });
        scenarios.push({ name: "gemini", chat });
    }

    for (const scenario of scenarios) {
        const executed = [];
        const result = await runAgentTurn({
            chat: scenario.chat,
            callTool: async (name, args) => { executed.push({ name, args }); return { ok: true }; },
            tools: [{ name: "query_scene" }],
            systemPrompt: "SYS", prompt: "do the thing",
        });
        check(result.completed === true, `${scenario.name} completes the turn`);
        check(result.finalText === "all done", `${scenario.name} returns the final text`);
        check(executed.length === 1 && executed[0].name === "query_scene", `${scenario.name} executed the requested tool`);
        check(executed[0].args.objectId === "obj-1", `${scenario.name} passed tool arguments through intact`);
        check(result.iterations.length === 2, `${scenario.name} used two iterations`);
        check(result.usage.inputTokens === 30 && result.usage.outputTokens === 9, `${scenario.name} accumulated usage across iterations`);
    }

    // ---------- Failure handling ----------
    for (const [name, make] of [
        ["openai", () => openai.createOpenAIChat({ model: "m", apiKey: "k", fetchImpl: async () => jsonResponse({ error: "boom" }, false, 500) })],
        ["gemini", () => gemini.createGeminiChat({ model: "m", apiKey: "k", fetchImpl: async () => jsonResponse({ error: "boom" }, false, 500) })],
    ]) {
        let threw = false;
        try { await make()({ messages: [{ role: "user", content: "x" }], tools: [] }); }
        catch (error) { threw = /failed: 500/.test(error.message); }
        check(threw, `${name} surfaces a non-2xx response as an error`);
    }
    for (const [name, make, envVar] of [
        ["openai", (k) => openai.createOpenAIChat({ model: "m", apiKey: k, fetchImpl: async () => jsonResponse({}) }), "OPENAI_API_KEY"],
        ["gemini", (k) => gemini.createGeminiChat({ model: "m", apiKey: k, fetchImpl: async () => jsonResponse({}) }), "GEMINI_API_KEY"],
    ]) {
        const saved = process.env[envVar];
        delete process.env[envVar];
        let threw = false;
        try { await make(undefined)({ messages: [], tools: [] }); }
        catch (error) { threw = error.message.includes(envVar); }
        check(threw, `${name} refuses to run without ${envVar}`);
        if (saved !== undefined) process.env[envVar] = saved;
    }
    for (const [name, factory] of [["openai", openai.createOpenAIChat], ["gemini", gemini.createGeminiChat]]) {
        let threw = false;
        try { factory({ fetchImpl: async () => jsonResponse({}) }); } catch { threw = true; }
        check(threw, `${name} requires a model to be named`);
    }

    console.log(`[provider_adapters_test] PASS (${assertions} assertions)`);
})().catch((error) => { console.error(error); process.exit(1); });
