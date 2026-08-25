"use strict";

// Provider agnostic agent loop.
//
// The Claude path delegates its whole loop to the Agent SDK, which is why there
// is no loop written down anywhere. A second backend has to run that loop
// itself: send the conversation, receive tool calls, execute them against the
// Unity Scene Bridge, feed results back, repeat until the model answers without
// requesting a tool.
//
// The loop is deliberately free of provider detail. It takes a `chat` function
// and a `callTool` function, so it can be exercised end to end against scripted
// responses with no API key and no network. Provider specifics live in the
// adapters next to this file.
//
// It records per iteration timings and tool timings because the study measures
// acknowledgement and validated execution latency separately, and a comparison
// across backends is meaningless if one arm's latency is measured differently
// from another's.

const DEFAULT_MAX_ITERATIONS = 12;
const DEFAULT_TOOL_TIMEOUT_MS = 60000;

class AgentLoopError extends Error {
    constructor(message, details = {}) {
        super(message);
        this.name = "AgentLoopError";
        Object.assign(this, details);
    }
}

function nowMs(clock) {
    return clock ? clock() : Date.now();
}

async function withTimeout(promise, timeoutMs, label) {
    if (!timeoutMs || timeoutMs <= 0) return promise;
    let timer;
    try {
        return await Promise.race([
            promise,
            new Promise((_, reject) => {
                timer = setTimeout(() => reject(new AgentLoopError(`${label} exceeded ${timeoutMs}ms`, { timedOut: true })), timeoutMs);
            }),
        ]);
    } finally {
        clearTimeout(timer);
    }
}

// A tool failure must not end the turn. The model is told the tool failed and
// gets to decide what to do, which is how the Agent SDK behaves and is required
// for the arms to be comparable. Silently dropping the error would let one
// backend appear to succeed where another visibly recovered.
function toolResultEnvelope(name, callId, result, error) {
    if (error) {
        return { name, callId, isError: true, content: JSON.stringify({ error: String(error.message || error) }) };
    }
    const content = typeof result === "string" ? result : JSON.stringify(result === undefined ? null : result);
    return { name, callId, isError: false, content };
}

/**
 * @param {object} options
 * @param {function} options.chat       async ({messages, tools}) => {text, toolCalls:[{callId,name,arguments}], usage}
 * @param {function} options.callTool   async (name, args) => any
 * @param {Array}    options.tools      rendered tool surface for this provider
 * @param {string}   options.systemPrompt
 * @param {string}   options.prompt
 * @param {number}   [options.maxIterations]
 * @param {number}   [options.toolTimeoutMs]
 * @param {function} [options.clock]    injectable for deterministic tests
 * @param {function} [options.onEvent]  observability hook
 */
async function runAgentTurn(options) {
    const {
        chat, callTool, tools, systemPrompt, prompt,
        maxIterations = DEFAULT_MAX_ITERATIONS,
        toolTimeoutMs = DEFAULT_TOOL_TIMEOUT_MS,
        clock, onEvent,
    } = options;

    if (typeof chat !== "function") throw new AgentLoopError("chat must be a function");
    if (typeof callTool !== "function") throw new AgentLoopError("callTool must be a function");
    if (!Array.isArray(tools)) throw new AgentLoopError("tools must be an array");

    const emit = (event) => { if (onEvent) onEvent(event); };
    const startedAt = nowMs(clock);
    const messages = [
        { role: "system", content: systemPrompt || "" },
        { role: "user", content: prompt || "" },
    ];

    const iterations = [];
    const toolCallLog = [];
    let finalText = null;
    let stopReason = null;
    const usageTotals = { inputTokens: 0, outputTokens: 0 };

    for (let iteration = 1; iteration <= maxIterations; iteration += 1) {
        const iterationStart = nowMs(clock);
        emit({ type: "iteration_start", iteration });

        const response = await chat({ messages: messages.slice(), tools, iteration });
        const modelLatencyMs = nowMs(clock) - iterationStart;

        if (response && response.usage) {
            usageTotals.inputTokens += response.usage.inputTokens || 0;
            usageTotals.outputTokens += response.usage.outputTokens || 0;
        }

        const calls = (response && response.toolCalls) || [];
        messages.push({ role: "assistant", content: (response && response.text) || "", toolCalls: calls });

        if (calls.length === 0) {
            finalText = (response && response.text) || "";
            stopReason = "model_completed";
            iterations.push({ iteration, modelLatencyMs, toolCalls: 0, toolLatencyMs: 0 });
            emit({ type: "iteration_end", iteration, toolCalls: 0 });
            break;
        }

        // Tool calls within one model turn are independent, so they run
        // concurrently. Ordering back into the transcript is by the model's own
        // call order, not completion order, so a transcript is reproducible.
        const toolStart = nowMs(clock);
        const settled = await Promise.all(calls.map(async (call) => {
            const callStart = nowMs(clock);
            try {
                const value = await withTimeout(
                    Promise.resolve(callTool(call.name, call.arguments)),
                    toolTimeoutMs,
                    `tool ${call.name}`
                );
                return { call, envelope: toolResultEnvelope(call.name, call.callId, value, null), latencyMs: nowMs(clock) - callStart, failed: false };
            } catch (error) {
                return { call, envelope: toolResultEnvelope(call.name, call.callId, null, error), latencyMs: nowMs(clock) - callStart, failed: true };
            }
        }));
        const toolLatencyMs = nowMs(clock) - toolStart;

        for (const entry of settled) {
            messages.push({ role: "tool", name: entry.call.name, callId: entry.call.callId, content: entry.envelope.content, isError: entry.envelope.isError });
            toolCallLog.push({ iteration, name: entry.call.name, callId: entry.call.callId, failed: entry.failed, latencyMs: entry.latencyMs });
            emit({ type: "tool_result", iteration, name: entry.call.name, failed: entry.failed, latencyMs: entry.latencyMs });
        }

        iterations.push({ iteration, modelLatencyMs, toolCalls: calls.length, toolLatencyMs });
        emit({ type: "iteration_end", iteration, toolCalls: calls.length });

        if (iteration === maxIterations) {
            stopReason = "max_iterations";
        }
    }

    if (stopReason === null) stopReason = "max_iterations";

    const totalMs = nowMs(clock) - startedAt;
    const modelMs = iterations.reduce((sum, item) => sum + item.modelLatencyMs, 0);
    const toolMs = iterations.reduce((sum, item) => sum + item.toolLatencyMs, 0);

    return {
        finalText,
        stopReason,
        completed: stopReason === "model_completed",
        messages,
        iterations,
        toolCalls: toolCallLog,
        usage: usageTotals,
        // Reported separately so a cross backend latency comparison can attribute
        // time to the model or to Unity rather than to one opaque number.
        latency: {
            totalMs,
            modelMs,
            toolMs,
            overheadMs: Math.max(0, totalMs - modelMs - toolMs),
            iterationCount: iterations.length,
        },
    };
}

module.exports = { runAgentTurn, AgentLoopError, DEFAULT_MAX_ITERATIONS, DEFAULT_TOOL_TIMEOUT_MS, toolResultEnvelope };
