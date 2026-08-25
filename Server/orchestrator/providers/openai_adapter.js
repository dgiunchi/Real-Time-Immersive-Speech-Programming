"use strict";

// OpenAI chat adapter for the provider agnostic agent loop.
//
// Speaks the Chat Completions API over plain HTTPS rather than through the
// vendor SDK, so no dependency is added to the server for a comparison arm.
//
// The adapter's only job is translation: transcript in the loop's shape to the
// wire shape, and the response back again. It holds no loop logic and no tool
// execution, so behaviour stays identical across providers and only the wire
// format differs.

const DEFAULT_ENDPOINT = "https://api.openai.com/v1/chat/completions";

// The loop keeps one neutral transcript. Each adapter renders it into the shape
// its API expects, so no provider's message format leaks into the loop.
function toWireMessages(messages) {
    return messages.map((message) => {
        if (message.role === "tool") {
            return { role: "tool", tool_call_id: message.callId, content: message.content };
        }
        if (message.role === "assistant" && message.toolCalls && message.toolCalls.length > 0) {
            return {
                role: "assistant",
                content: message.content || null,
                tool_calls: message.toolCalls.map((call) => ({
                    id: call.callId,
                    type: "function",
                    function: { name: call.name, arguments: JSON.stringify(call.arguments || {}) },
                })),
            };
        }
        return { role: message.role, content: message.content || "" };
    });
}

// Arguments arrive as a JSON string. A model can emit malformed JSON, and that
// must surface as a tool level failure the model can recover from rather than
// throwing out of the adapter and ending the turn.
function parseArguments(raw) {
    if (!raw) return {};
    try {
        return JSON.parse(raw);
    } catch (error) {
        return { __parseError: `arguments were not valid JSON: ${error.message}`, __raw: raw };
    }
}

function fromWireResponse(payload) {
    const choice = (payload.choices && payload.choices[0]) || {};
    const message = choice.message || {};
    const toolCalls = (message.tool_calls || []).map((call) => ({
        callId: call.id,
        name: call.function && call.function.name,
        arguments: parseArguments(call.function && call.function.arguments),
    }));
    return {
        text: message.content || "",
        toolCalls,
        usage: {
            inputTokens: (payload.usage && payload.usage.prompt_tokens) || 0,
            outputTokens: (payload.usage && payload.usage.completion_tokens) || 0,
        },
        raw: payload,
    };
}

/**
 * @param {object} config
 * @param {string} config.model
 * @param {string} [config.apiKey]    defaults to OPENAI_API_KEY
 * @param {string} [config.endpoint]
 * @param {function} [config.fetchImpl] injected for tests
 * @returns {function} a `chat` implementation for runAgentTurn
 */
function createOpenAIChat(config = {}) {
    const { model, endpoint = DEFAULT_ENDPOINT, fetchImpl } = config;
    const doFetch = fetchImpl || globalThis.fetch;
    if (!model) throw new Error("createOpenAIChat requires a model");
    if (typeof doFetch !== "function") throw new Error("no fetch implementation available");

    return async function chat({ messages, tools }) {
        const apiKey = config.apiKey || process.env.OPENAI_API_KEY;
        if (!apiKey) throw new Error("OPENAI_API_KEY is not set");

        const response = await doFetch(endpoint, {
            method: "POST",
            headers: { "content-type": "application/json", authorization: `Bearer ${apiKey}` },
            body: JSON.stringify({
                model,
                messages: toWireMessages(messages),
                ...(tools && tools.length > 0 ? { tools, tool_choice: "auto" } : {}),
            }),
        });

        if (!response.ok) {
            const body = await response.text().catch(() => "");
            throw new Error(`openai request failed: ${response.status} ${body.slice(0, 300)}`);
        }
        return fromWireResponse(await response.json());
    };
}

module.exports = { createOpenAIChat, toWireMessages, fromWireResponse, parseArguments, DEFAULT_ENDPOINT };
