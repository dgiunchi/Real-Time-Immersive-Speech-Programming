"use strict";

// Exercises the provider agnostic agent loop against scripted model responses.
// No API key, no network, no MCP session: the loop takes chat and callTool as
// arguments precisely so its behaviour can be pinned down deterministically.

const assert = require("assert");
const { runAgentTurn, AgentLoopError } = require("../orchestrator/providers/agent_loop");

let assertions = 0;
function check(condition, message) {
    assert.ok(condition, `FAILED: ${message}`);
    assertions += 1;
}

// Deterministic clock so latency attribution is asserted exactly rather than
// approximately.
function fakeClock(stepMs = 10) {
    let t = 1000;
    return () => { t += stepMs; return t; };
}

const TOOLS = [{ name: "query_scene" }, { name: "propose_artifact" }];

(async () => {
    // 1. A turn with no tool calls completes immediately.
    {
        const result = await runAgentTurn({
            chat: async () => ({ text: "done", toolCalls: [] }),
            callTool: async () => { throw new Error("must not be called"); },
            tools: TOOLS, systemPrompt: "sys", prompt: "hello", clock: fakeClock(),
        });
        check(result.completed === true, "a turn with no tool calls completes");
        check(result.stopReason === "model_completed", "stop reason is model_completed");
        check(result.finalText === "done", "final text is returned");
        check(result.iterations.length === 1, "one iteration recorded");
        check(result.toolCalls.length === 0, "no tool calls logged");
    }

    // 2. Tool calls are executed and their results fed back.
    {
        const seen = [];
        let turn = 0;
        const result = await runAgentTurn({
            chat: async () => {
                turn += 1;
                if (turn === 1) return { text: "", toolCalls: [{ callId: "c1", name: "query_scene", arguments: { objectId: "obj-1" } }] };
                return { text: "finished", toolCalls: [] };
            },
            callTool: async (name, args) => { seen.push({ name, args }); return { ok: true, name }; },
            tools: TOOLS, systemPrompt: "sys", prompt: "go", clock: fakeClock(),
        });
        check(seen.length === 1, "the tool was executed once");
        check(seen[0].name === "query_scene", "the correct tool was executed");
        check(seen[0].args.objectId === "obj-1", "tool arguments are passed through");
        check(result.completed === true, "the turn completes after the tool round trip");
        check(result.toolCalls[0].failed === false, "a successful tool call is not marked failed");
        const toolMessage = result.messages.find((m) => m.role === "tool");
        check(toolMessage && JSON.parse(toolMessage.content).ok === true, "the tool result is fed back into the transcript");
    }

    // 3. A failing tool does not end the turn. The model is told and continues.
    {
        let turn = 0;
        const result = await runAgentTurn({
            chat: async () => {
                turn += 1;
                if (turn === 1) return { text: "", toolCalls: [{ callId: "c1", name: "query_scene", arguments: {} }] };
                return { text: "recovered", toolCalls: [] };
            },
            callTool: async () => { throw new Error("unity unreachable"); },
            tools: TOOLS, systemPrompt: "s", prompt: "p", clock: fakeClock(),
        });
        check(result.completed === true, "a tool failure does not abort the turn");
        check(result.toolCalls[0].failed === true, "the failed tool call is recorded as failed");
        const toolMessage = result.messages.find((m) => m.role === "tool");
        check(toolMessage.isError === true, "the tool message is flagged as an error");
        check(JSON.parse(toolMessage.content).error.includes("unity unreachable"),
            "the error reason is passed to the model rather than swallowed");
        check(result.finalText === "recovered", "the model gets to respond after a tool failure");
    }

    // 4. A tool that hangs is bounded rather than hanging the turn.
    {
        const result = await runAgentTurn({
            chat: async ({ iteration }) => (iteration === 1
                ? { text: "", toolCalls: [{ callId: "c1", name: "query_scene", arguments: {} }] }
                : { text: "after timeout", toolCalls: [] }),
            callTool: () => new Promise(() => {}),
            tools: TOOLS, systemPrompt: "s", prompt: "p", toolTimeoutMs: 30,
        });
        check(result.toolCalls[0].failed === true, "a hanging tool is recorded as failed");
        const toolMessage = result.messages.find((m) => m.role === "tool");
        check(/exceeded 30ms/.test(toolMessage.content), "the timeout is reported to the model");
        check(result.completed === true, "the turn survives a hanging tool");
    }

    // 5. A model that never stops calling tools is bounded.
    {
        let calls = 0;
        const result = await runAgentTurn({
            chat: async () => ({ text: "", toolCalls: [{ callId: `c${++calls}`, name: "query_scene", arguments: {} }] }),
            callTool: async () => ({ ok: true }),
            tools: TOOLS, systemPrompt: "s", prompt: "p", maxIterations: 4, clock: fakeClock(),
        });
        check(result.completed === false, "an unbounded model does not report completion");
        check(result.stopReason === "max_iterations", "the loop stops on the iteration bound");
        check(result.iterations.length === 4, "the bound is respected exactly");
        check(result.toolCalls.length === 4, "every iteration's tool call is logged");
    }

    // 6. Concurrent tool calls in one turn, ordered by the model's call order.
    {
        const order = [];
        let turn = 0;
        const result = await runAgentTurn({
            chat: async () => {
                turn += 1;
                if (turn === 1) {
                    return { text: "", toolCalls: [
                        { callId: "a", name: "query_scene", arguments: { n: 1 } },
                        { callId: "b", name: "propose_artifact", arguments: { n: 2 } },
                    ] };
                }
                return { text: "ok", toolCalls: [] };
            },
            // b resolves before a, so completion order differs from call order.
            callTool: async (name) => {
                if (name === "query_scene") { await new Promise((r) => setTimeout(r, 25)); order.push("a"); return { name }; }
                order.push("b"); return { name };
            },
            tools: TOOLS, systemPrompt: "s", prompt: "p",
        });
        check(order.join(",") === "b,a", "tools ran concurrently, completing out of call order");
        const toolMessages = result.messages.filter((m) => m.role === "tool");
        check(toolMessages[0].callId === "a" && toolMessages[1].callId === "b",
            "the transcript preserves the model's call order, not completion order");
        check(result.toolCalls.length === 2, "both tool calls are logged");
    }

    // 7. Latency is attributed separately, which cross backend comparison needs.
    {
        const result = await runAgentTurn({
            chat: async ({ iteration }) => (iteration === 1
                ? { text: "", toolCalls: [{ callId: "c1", name: "query_scene", arguments: {} }] }
                : { text: "done", toolCalls: [] }),
            callTool: async () => ({ ok: true }),
            tools: TOOLS, systemPrompt: "s", prompt: "p", clock: fakeClock(10),
        });
        check(result.latency.totalMs > 0, "total latency is recorded");
        check(result.latency.modelMs > 0, "model latency is recorded separately");
        check(result.latency.toolMs > 0, "tool latency is recorded separately");
        check(result.latency.iterationCount === 2, "iteration count is reported");
        check(result.latency.modelMs + result.latency.toolMs <= result.latency.totalMs,
            "model and tool time do not exceed total time");
    }

    // 8. Token usage accumulates across iterations for API cost measurement.
    {
        const result = await runAgentTurn({
            chat: async ({ iteration }) => (iteration === 1
                ? { text: "", toolCalls: [{ callId: "c1", name: "query_scene", arguments: {} }], usage: { inputTokens: 100, outputTokens: 20 } }
                : { text: "done", toolCalls: [], usage: { inputTokens: 150, outputTokens: 30 } }),
            callTool: async () => ({ ok: true }),
            tools: TOOLS, systemPrompt: "s", prompt: "p", clock: fakeClock(),
        });
        check(result.usage.inputTokens === 250, "input tokens accumulate across iterations");
        check(result.usage.outputTokens === 50, "output tokens accumulate across iterations");
    }

    // 9. Misuse is refused loudly rather than producing a misleading transcript.
    for (const [bad, label] of [
        [{ chat: null, callTool: async () => {}, tools: [] }, "missing chat"],
        [{ chat: async () => {}, callTool: null, tools: [] }, "missing callTool"],
        [{ chat: async () => {}, callTool: async () => {}, tools: "nope" }, "tools not an array"],
    ]) {
        let threw = false;
        try { await runAgentTurn(bad); } catch (error) { threw = error instanceof AgentLoopError; }
        check(threw, `${label} is refused with AgentLoopError`);
    }

    // 10. The system prompt and user prompt open the transcript, in that order.
    {
        const result = await runAgentTurn({
            chat: async ({ messages }) => {
                check(messages[0].role === "system" && messages[0].content === "SYS", "system prompt is first");
                check(messages[1].role === "user" && messages[1].content === "USER", "user prompt is second");
                return { text: "ok", toolCalls: [] };
            },
            callTool: async () => ({}), tools: TOOLS, systemPrompt: "SYS", prompt: "USER", clock: fakeClock(),
        });
        check(result.completed === true, "the prompted turn completes");
    }

    console.log(`[agent_loop_test] PASS (${assertions} assertions)`);
})().catch((error) => { console.error(error); process.exit(1); });
