"use strict";

// Gemini chat adapter for the provider agnostic agent loop.
//
// Speaks generateContent over plain HTTPS rather than through the vendor SDK,
// so no dependency is added for a comparison arm.
//
// Gemini differs from the other providers in three ways that matter, and all
// three are handled here so the loop stays provider neutral:
//   - the system prompt is a separate systemInstruction field, not a message;
//   - roles are "user" and "model", and there is no dedicated tool role, so tool
//     results are returned as functionResponse parts in a user turn;
//   - tool calls carry no id, so ids are synthesised positionally and the loop's
//     callId contract still holds.

const DEFAULT_BASE = "https://generativelanguage.googleapis.com/v1beta/models";

// Gemini has no tool role. A tool result is a functionResponse part inside a
// user turn, and consecutive results must be merged into one turn rather than
// sent as separate turns, which the API rejects.
function toWireContents(messages) {
    const contents = [];
    for (const message of messages) {
        if (message.role === "system") continue;
        if (message.role === "tool") {
            const part = {
                functionResponse: {
                    name: message.name,
                    response: { content: message.content, isError: Boolean(message.isError) },
                },
            };
            const previous = contents[contents.length - 1];
            if (previous && previous.role === "user" && previous.parts.every((p) => p.functionResponse)) {
                previous.parts.push(part);
            } else {
                contents.push({ role: "user", parts: [part] });
            }
            continue;
        }
        if (message.role === "assistant") {
            const parts = [];
            if (message.content) parts.push({ text: message.content });
            for (const call of message.toolCalls || []) {
                parts.push({ functionCall: { name: call.name, args: call.arguments || {} } });
            }
            contents.push({ role: "model", parts: parts.length ? parts : [{ text: "" }] });
            continue;
        }
        contents.push({ role: "user", parts: [{ text: message.content || "" }] });
    }
    return contents;
}

function systemInstructionFrom(messages) {
    const system = messages.find((message) => message.role === "system");
    return system && system.content ? { parts: [{ text: system.content }] } : undefined;
}

// Gemini returns no call id, so one is synthesised from the turn index and the
// position within the turn. The loop only requires ids to be unique within a
// turn so results can be matched back to calls.
function fromWireResponse(payload, iteration = 0) {
    const candidate = (payload.candidates && payload.candidates[0]) || {};
    const parts = (candidate.content && candidate.content.parts) || [];
    const text = parts.filter((part) => typeof part.text === "string").map((part) => part.text).join("");
    const toolCalls = parts
        .filter((part) => part.functionCall)
        .map((part, index) => ({
            callId: `gemini-${iteration}-${index}`,
            name: part.functionCall.name,
            arguments: part.functionCall.args || {},
        }));
    const usage = payload.usageMetadata || {};
    return {
        text,
        toolCalls,
        usage: {
            inputTokens: usage.promptTokenCount || 0,
            outputTokens: usage.candidatesTokenCount || 0,
        },
        raw: payload,
    };
}

/**
 * @param {object} config
 * @param {string} config.model
 * @param {string} [config.apiKey]     defaults to GEMINI_API_KEY
 * @param {string} [config.baseUrl]
 * @param {function} [config.fetchImpl] injected for tests
 * @returns {function} a `chat` implementation for runAgentTurn
 */
function createGeminiChat(config = {}) {
    const { model, baseUrl = DEFAULT_BASE, fetchImpl } = config;
    const doFetch = fetchImpl || globalThis.fetch;
    if (!model) throw new Error("createGeminiChat requires a model");
    if (typeof doFetch !== "function") throw new Error("no fetch implementation available");

    return async function chat({ messages, tools, iteration }) {
        const apiKey = config.apiKey || process.env.GEMINI_API_KEY;
        if (!apiKey) throw new Error("GEMINI_API_KEY is not set");

        const body = {
            contents: toWireContents(messages),
            ...(systemInstructionFrom(messages) ? { systemInstruction: systemInstructionFrom(messages) } : {}),
            ...(tools && tools.length > 0 ? { tools: [{ functionDeclarations: tools }] } : {}),
        };

        const response = await doFetch(`${baseUrl}/${model}:generateContent`, {
            method: "POST",
            headers: { "content-type": "application/json", "x-goog-api-key": apiKey },
            body: JSON.stringify(body),
        });

        if (!response.ok) {
            const text = await response.text().catch(() => "");
            throw new Error(`gemini request failed: ${response.status} ${text.slice(0, 300)}`);
        }
        return fromWireResponse(await response.json(), iteration || 0);
    };
}

module.exports = { createGeminiChat, toWireContents, systemInstructionFrom, fromWireResponse, DEFAULT_BASE };
