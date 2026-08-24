import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { appendEvaluationEvent } = require("../evaluation/event_logger");
const { appendTokenActivity } = require("../evaluation/token_activity_logger");

const here = path.dirname(fileURLToPath(import.meta.url));
const bridgeServerPath = path.join(here, "..", "mcp", "unity_scene_bridge", "server.js");
const DEFAULT_PIPELINE_BUDGET_MS = 55000;
const DEFAULT_MODEL_TIMEOUT_MS = 22000;

const PAIR_SELECTION_SYSTEM_PROMPT = `You select one bounded source/destination pair for a low-risk XR guidance cue.
Return ONLY compact JSON with keys sourceId, destinationId, and reason.
For L1, a manipulable tool is the source and a tray/receptacle is the destination.
For L2, a manipulable part is the source and a socket/receptacle is the destination.
The observed focus can be either side. It must remain one member of the selected pair.
Choose the compatible counterpart from the supplied focus+halo using semantic names, component types,
task affordance, and spatial proximity. Prefer the nearest compatible counterpart when choices are otherwise equal.
Never infer a pair from a shared numeric suffix or any prewritten id mapping. Copy ids exactly from the input.
Do not write code, prose outside JSON, or select objects outside the supplied scene.`;

function clampNumber(value, fallback, min, max) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? Math.min(max, Math.max(min, parsed)) : fallback;
}

function toolText(result, name) {
    const text = (result && Array.isArray(result.content) ? result.content : [])
        .filter((item) => item && item.type === "text")
        .map((item) => item.text || "")
        .join("\n")
        .trim();
    if (!result || result.isError) throw new Error(`${name} failed: ${text || "unknown MCP error"}`);
    return text;
}

function toolJson(result, name) {
    const text = toolText(result, name);
    try {
        return JSON.parse(text);
    } catch (error) {
        throw new Error(`${name} returned non-JSON output: ${error.message}`);
    }
}

function componentNames(components) {
    if (!Array.isArray(components)) return [];
    return components.map((item) => typeof item === "string" ? item : item && item.type)
        .filter(Boolean).map(String);
}

function compactObject(object) {
    if (!object || typeof object !== "object") return null;
    const pos = object.transform && Array.isArray(object.transform.pos)
        ? object.transform.pos.slice(0, 3).map(Number) : null;
    return {
        id: String(object.id || ""),
        name: String(object.name || ""),
        tag: String(object.tag || ""),
        type: String(object.type || ""),
        pos,
        components: componentNames(object.components),
    };
}

function compactScene(sceneEnvelope) {
    const payload = sceneEnvelope && sceneEnvelope.payload;
    const focus = compactObject(payload && payload.focus);
    const halo = Array.isArray(payload && payload.halo)
        ? payload.halo.map(compactObject).filter(Boolean) : [];
    if (!focus || !focus.id) throw new Error("Scene grounding returned no stable focus object.");
    return { focus, halo };
}

function semanticText(object) {
    return [object.id, object.name, object.tag, object.type, ...(object.components || [])]
        .join(" ").toLowerCase();
}

function hasSemanticToken(object, token) {
    const escaped = token.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    return new RegExp(`(^|[^a-z])${escaped}([^a-z]|$)`, "i").test(semanticText(object));
}

function roleFor(object, interactionMode) {
    if (!object) return null;
    if (interactionMode === "L1") {
        if (hasSemanticToken(object, "tool")) return "source";
        if (hasSemanticToken(object, "tray") || hasSemanticToken(object, "receptacle")) return "destination";
    }
    if (interactionMode === "L2") {
        if (hasSemanticToken(object, "part")) return "source";
        if (hasSemanticToken(object, "socket") || hasSemanticToken(object, "receptacle")) return "destination";
    }
    return null;
}

function distanceSquared(a, b) {
    if (!Array.isArray(a && a.pos) || !Array.isArray(b && b.pos)) return Number.POSITIVE_INFINITY;
    return a.pos.reduce((sum, value, index) => sum + Math.pow(value - b.pos[index], 2), 0);
}

function nearestBoundedPair(scene, interactionMode) {
    const objects = [scene.focus, ...scene.halo];
    const focusRole = roleFor(scene.focus, interactionMode);
    if (!focusRole) throw new Error(`The grounded focus is not a supported ${interactionMode} tool/tray or part/socket object.`);
    const counterpartRole = focusRole === "source" ? "destination" : "source";
    const counterparts = objects.filter((object) => object.id !== scene.focus.id && roleFor(object, interactionMode) === counterpartRole)
        .sort((a, b) => distanceSquared(scene.focus, a) - distanceSquared(scene.focus, b) || a.id.localeCompare(b.id));
    if (!counterparts.length) throw new Error(`No compatible ${counterpartRole} is present in the grounded halo.`);
    return focusRole === "source"
        ? { source: scene.focus, destination: counterparts[0], selection: "nearest_semantic_fallback" }
        : { source: counterparts[0], destination: scene.focus, selection: "nearest_semantic_fallback" };
}

function boundedSceneForMode(scene, interactionMode) {
    const focusRole = roleFor(scene.focus, interactionMode);
    if (!focusRole) throw new Error(`The grounded focus is not a supported ${interactionMode} tool/tray or part/socket object.`);
    const counterpartRole = focusRole === "source" ? "destination" : "source";
    const counterparts = scene.halo
        .filter((object) => roleFor(object, interactionMode) === counterpartRole)
        .sort((a, b) => distanceSquared(scene.focus, a) - distanceSquared(scene.focus, b) || a.id.localeCompare(b.id))
        .slice(0, 6);
    if (!counterparts.length) throw new Error(`No compatible ${counterpartRole} is present in the grounded halo.`);
    return { focus: scene.focus, halo: counterparts };
}

function validateModelPair(scene, interactionMode, response) {
    const objects = [scene.focus, ...scene.halo];
    const byId = new Map(objects.map((object) => [object.id, object]));
    const source = byId.get(String(response && response.sourceId || ""));
    const destination = byId.get(String(response && response.destinationId || ""));
    if (!source || !destination) throw new Error("Claude selected an object outside the grounded scene.");
    if (source.id === destination.id) throw new Error("Claude selected the same object as source and destination.");
    if (roleFor(source, interactionMode) !== "source" || roleFor(destination, interactionMode) !== "destination") {
        throw new Error(`Claude selected an invalid ${interactionMode} source/destination role pair.`);
    }
    if (source.id !== scene.focus.id && destination.id !== scene.focus.id) {
        throw new Error("Claude's pair does not include the observed focus.");
    }
    return {
        source,
        destination,
        reason: String(response.reason || "Claude selected the grounded semantic counterpart."),
        selection: "claude_scene_pair",
    };
}

function parseJsonObject(text) {
    const trimmed = String(text || "").trim().replace(/^```(?:json)?\s*/i, "").replace(/\s*```$/, "");
    const start = trimmed.indexOf("{");
    const end = trimmed.lastIndexOf("}");
    if (start < 0 || end <= start) throw new Error("Claude returned no JSON object.");
    return JSON.parse(trimmed.slice(start, end + 1));
}

async function selectPairWithClaude({ scene, interactionMode, model, apiKey, timeoutMs }) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    const startedAt = Date.now();
    try {
        const response = await fetch("https://api.anthropic.com/v1/messages", {
            method: "POST",
            headers: {
                "content-type": "application/json",
                "x-api-key": apiKey,
                "anthropic-version": "2023-06-01",
            },
            body: JSON.stringify({
                model,
                max_tokens: 220,
                temperature: 0,
                system: PAIR_SELECTION_SYSTEM_PROMPT,
                messages: [{
                    role: "user",
                    content: `Interaction mode: ${interactionMode}\nObserved grounded scene: ${JSON.stringify(scene)}`,
                }],
            }),
            signal: controller.signal,
        });
        const body = await response.json().catch(() => ({}));
        if (!response.ok) {
            const detail = body && body.error && body.error.message ? body.error.message : `HTTP ${response.status}`;
            throw new Error(`Anthropic pair selection failed: ${detail}`);
        }
        const text = Array.isArray(body.content)
            ? body.content.filter((block) => block.type === "text").map((block) => block.text || "").join("\n") : "";
        return {
            pair: parseJsonObject(text),
            usage: body.usage || null,
            reportedModel: body.model || model,
            latencyMs: Date.now() - startedAt,
            stopReason: body.stop_reason || null,
        };
    } catch (error) {
        if (error && error.name === "AbortError") {
            throw new Error(`Anthropic pair selection exceeded its ${timeoutMs}ms budget.`);
        }
        throw error;
    } finally {
        clearTimeout(timer);
    }
}

function escapeCSharpString(value) {
    return String(value).replace(/\\/g, "\\\\").replace(/"/g, '\\"').replace(/\r/g, "\\r").replace(/\n/g, "\\n");
}

function classSuffix(correlationId) {
    const compact = String(correlationId || "cue").replace(/[^A-Za-z0-9]/g, "").slice(-16);
    return compact || "Cue";
}

function renderGuidanceCode({ destinationName, correlationId }) {
    const className = `AgenticPairCue${classSuffix(correlationId)}`;
    const escapedDestination = escapeCSharpString(destinationName);
    return `using UnityEngine;

public sealed class ${className} : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private Renderer sourceRenderer;
    private Renderer destinationRenderer;
    private Transform destinationTransform;
    private MaterialPropertyBlock sourceOriginal;
    private MaterialPropertyBlock destinationOriginal;
    private MaterialPropertyBlock sourceWorking;
    private MaterialPropertyBlock destinationWorking;
    private Vector3 sourceScale;
    private Vector3 destinationScale;
    private bool sourceHasColor;
    private bool sourceHasBaseColor;
    private bool destinationHasColor;
    private bool destinationHasBaseColor;
    private bool initialized;
    private bool restored;

    private static Renderer FirstEnabledRenderer(GameObject root)
    {
        if (root == null) return null;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer candidate = renderers[index];
            if (candidate != null && candidate.enabled && candidate.gameObject.activeInHierarchy) return candidate;
        }
        return null;
    }

    private void Awake()
    {
        GameObject destination = GameObject.Find("${escapedDestination}");
        sourceRenderer = FirstEnabledRenderer(gameObject);
        destinationRenderer = FirstEnabledRenderer(destination);
        if (sourceRenderer == null || destinationRenderer == null || destination == gameObject)
        {
            enabled = false;
            return;
        }

        destinationTransform = destination.transform;
        sourceScale = transform.localScale;
        destinationScale = destinationTransform.localScale;
        sourceOriginal = new MaterialPropertyBlock();
        destinationOriginal = new MaterialPropertyBlock();
        sourceWorking = new MaterialPropertyBlock();
        destinationWorking = new MaterialPropertyBlock();
        sourceRenderer.GetPropertyBlock(sourceOriginal);
        destinationRenderer.GetPropertyBlock(destinationOriginal);
        sourceRenderer.GetPropertyBlock(sourceWorking);
        destinationRenderer.GetPropertyBlock(destinationWorking);

        Material sourceMaterial = sourceRenderer.sharedMaterial;
        Material destinationMaterial = destinationRenderer.sharedMaterial;
        sourceHasColor = sourceMaterial != null && sourceMaterial.HasProperty(ColorId);
        sourceHasBaseColor = sourceMaterial != null && sourceMaterial.HasProperty(BaseColorId);
        destinationHasColor = destinationMaterial != null && destinationMaterial.HasProperty(ColorId);
        destinationHasBaseColor = destinationMaterial != null && destinationMaterial.HasProperty(BaseColorId);
        if ((!sourceHasColor && !sourceHasBaseColor) || (!destinationHasColor && !destinationHasBaseColor))
        {
            enabled = false;
            return;
        }
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || restored || destinationTransform == null) return;
        float phase = Mathf.PingPong(Time.unscaledTime, 1f);
        Color cue = Color.Lerp(Color.cyan, Color.magenta, phase);
        if (sourceHasColor) sourceWorking.SetColor(ColorId, cue);
        if (sourceHasBaseColor) sourceWorking.SetColor(BaseColorId, cue);
        if (destinationHasColor) destinationWorking.SetColor(ColorId, cue);
        if (destinationHasBaseColor) destinationWorking.SetColor(BaseColorId, cue);
        sourceRenderer.SetPropertyBlock(sourceWorking);
        destinationRenderer.SetPropertyBlock(destinationWorking);
        transform.localScale = Vector3.Lerp(sourceScale, sourceScale * 1.08f, phase);
        destinationTransform.localScale = Vector3.Lerp(destinationScale, destinationScale * 1.08f, phase);
    }

    private void Restore()
    {
        if (!initialized || restored) return;
        restored = true;
        if (sourceRenderer != null) sourceRenderer.SetPropertyBlock(sourceOriginal);
        if (destinationRenderer != null) destinationRenderer.SetPropertyBlock(destinationOriginal);
        transform.localScale = sourceScale;
        if (destinationTransform != null) destinationTransform.localScale = destinationScale;
    }

    private void OnDisable() { Restore(); }
    private void OnDestroy() { Restore(); }
}`;
}

function validateBoundedCode(code, destinationName) {
    const denied = ["System.IO", "System.Net", "System.Diagnostics", "System.Reflection", "UnityEditor", "Input."];
    const reasons = denied.filter((token) => code.includes(token)).map((token) => `denied token ${token}`);
    for (const required of [
        "MaterialPropertyBlock", "GetComponentsInChildren<Renderer>(true)", "Time.unscaledTime",
        "Color.Lerp(Color.cyan, Color.magenta", "sourceScale * 1.08f", "destinationScale * 1.08f",
        "private void OnDisable()", "private void OnDestroy()",
    ]) {
        if (!code.includes(required)) reasons.push(`missing bounded cue invariant ${required}`);
    }
    if (!code.includes(`GameObject.Find(\"${escapeCSharpString(destinationName)}\")`)) {
        reasons.push("destination identity is not embedded exactly");
    }
    return { accepted: reasons.length === 0, reasons };
}

function remainingTimeout(deadline, preferred, minimum = 1000) {
    const remaining = deadline - Date.now();
    if (remaining < minimum) throw new Error("Fast implicit pipeline exhausted its configured sub-minute deadline.");
    return Math.max(minimum, Math.min(preferred, remaining));
}

async function callTool(client, name, args) {
    return client.callTool({ name, arguments: args });
}

async function status(client, sessionId, correlationId, state, detail) {
    return callTool(client, "send_agent_status", { sessionId, correlationId, state, detail });
}

function recordStage(stage, startedAt, context, extra = {}) {
    appendEvaluationEvent({
        eventType: "fast_implicit_stage",
        stage,
        latencyMs: Date.now() - startedAt,
        ...context,
        ...extra,
    });
}

export async function runFastImplicitPipeline({ intent, targetObjectId, sessionId, correlationId, model, env = process.env }) {
    const pipelineStartedAt = Date.now();
    const budgetMs = clampNumber(env.AGENTICXR_FAST_IMPLICIT_BUDGET_MS, DEFAULT_PIPELINE_BUDGET_MS, 20000, 59000);
    const deadline = pipelineStartedAt + budgetMs;
    const interactionMode = String(env.AGENTICXR_INTERACTION_MODE || "").toUpperCase();
    const triggerSource = String(env.AGENTICXR_TRIGGER_SOURCE || "").toLowerCase();
    const experienceMode = env.AGENTICXR_EXPERIENCE_MODE || "unspecified";
    if (!["L1", "L2"].includes(interactionMode)) throw new Error("Direct fast path is restricted to L1/L2.");
    if (!env.ANTHROPIC_API_KEY) throw new Error("ANTHROPIC_API_KEY is required for direct pair selection.");

    const context = { sessionId, correlationId, targetObjectId, interactionMode, triggerSource, experienceMode };
    const transport = new StdioClientTransport({
        command: process.execPath,
        args: [bridgeServerPath],
        env: { ...env },
        stderr: "inherit",
    });
    const client = new Client({ name: "agenticxr-fast-implicit", version: "0.1.0" });
    let connected = false;
    try {
        let stageStartedAt = Date.now();
        await client.connect(transport);
        connected = true;
        recordStage("bridge_connect", stageStartedAt, context);

        await callTool(client, "record_intent", { text: intent, sessionId, correlationId });
        await status(client, sessionId, correlationId, "querying_memory", "Fast path: grounding the observed study object.");

        stageStartedAt = Date.now();
        const groundedEnvelope = toolJson(await callTool(client, "query_scene", {
            objectId: targetObjectId,
            sessionId,
            correlationId,
            timeoutMs: remainingTimeout(deadline, 7000),
        }), "query_scene");
        const scene = compactScene(groundedEnvelope);
        const boundedScene = boundedSceneForMode(scene, interactionMode);
        recordStage("scene_grounding", stageStartedAt, context, {
            haloCount: scene.halo.length,
            boundedHaloCount: boundedScene.halo.length,
        });

        const useModelPairing = String(env.AGENTICXR_FAST_IMPLICIT_MODEL_PAIRING || "true").toLowerCase() === "true";
        await status(client, sessionId, correlationId, "thinking", useModelPairing
            ? "Claude is selecting one semantic source/destination pair."
            : "Selecting the nearest compatible grounded source/destination pair.");
        stageStartedAt = Date.now();
        let pair;
        if (!useModelPairing) {
            pair = nearestBoundedPair(boundedScene, interactionMode);
            recordStage("deterministic_pair_selection", stageStartedAt, context, {
                sourceObjectId: pair.source.id,
                destinationObjectId: pair.destination.id,
            });
        } else try {
            const modelTimeoutMs = remainingTimeout(deadline,
                clampNumber(env.AGENTICXR_FAST_IMPLICIT_MODEL_TIMEOUT_MS, DEFAULT_MODEL_TIMEOUT_MS, 5000, 30000));
            const modelResult = await selectPairWithClaude({
                scene: boundedScene,
                interactionMode,
                model,
                apiKey: env.ANTHROPIC_API_KEY,
                timeoutMs: modelTimeoutMs,
            });
            if (modelResult.reportedModel !== model) {
                throw new Error(`live model drift: reported '${modelResult.reportedModel}', pinned '${model}'`);
            }
            pair = validateModelPair(boundedScene, interactionMode, modelResult.pair);
            recordStage("claude_pair_selection", stageStartedAt, context, {
                sourceObjectId: pair.source.id,
                destinationObjectId: pair.destination.id,
                usage: modelResult.usage,
            });
            appendTokenActivity({
                eventType: "orchestrator_result",
                activity: "fast_implicit_pair_selection",
                model,
                ...context,
                candidateCount: 1,
                latencyMs: modelResult.latencyMs,
                usage: modelResult.usage,
                resultSubtype: modelResult.stopReason || "success",
                outcome: "success",
            });
        } catch (modelError) {
            const allowFallback = String(env.AGENTICXR_FAST_IMPLICIT_ALLOW_PAIR_FALLBACK || "false").toLowerCase() === "true";
            if (!allowFallback) throw modelError;
            // The cue is already bounded to one known semantic pair. A slow or
            // malformed model response must not strand the XR trial: choose the
            // nearest compatible counterpart deterministically and log the
            // degradation explicitly so study analysis never mistakes it for a
            // successful Claude decision.
            pair = nearestBoundedPair(boundedScene, interactionMode);
            await status(client, sessionId, correlationId, "thinking",
                "Claude pair selection was unavailable; continuing with the logged bounded proximity fallback.");
            recordStage("pair_selection_fallback", stageStartedAt, context, {
                sourceObjectId: pair.source.id,
                destinationObjectId: pair.destination.id,
                modelError: modelError.message,
            });
            appendTokenActivity({
                eventType: "orchestrator_result",
                activity: "fast_implicit_pair_selection",
                model,
                ...context,
                candidateCount: 1,
                latencyMs: Date.now() - stageStartedAt,
                resultSubtype: "bounded_fallback",
                outcome: "fallback",
            });
        }

        const code = renderGuidanceCode({ destinationName: pair.destination.name, correlationId });
        const validation = validateBoundedCode(code, pair.destination.name);
        if (!validation.accepted) throw new Error(`Bounded validator rejected generated cue: ${validation.reasons.join("; ")}`);
        const candidateId = `fast-candidate-${classSuffix(correlationId)}`;
        const candidateSetId = `fast-set-${classSuffix(correlationId)}`;
        const riskScore = 0.12;
        const validationSummary = `Deterministic bounded validator accepted ${pair.source.name} -> ${pair.destination.name}; reversible local cue.`;

        await status(client, sessionId, correlationId, "validating", "Deterministic policy passed; compiling once in Verification Space.");
        stageStartedAt = Date.now();
        const simulationCorrelationId = `${correlationId}-sim`;
        const simulation = toolJson(await callTool(client, "simulate_artifact", {
            code,
            targetObjectId: pair.source.id,
            intent,
            interactionMode,
            operation: "create",
            candidateId,
            candidateSetId,
            sessionId,
            correlationId: simulationCorrelationId,
            timeoutMs: remainingTimeout(deadline, 10000),
        }), "simulate_artifact");
        const simulationStatus = simulation && simulation.payload && simulation.payload.status;
        if (!["simulated", "skipped_no_verification"].includes(simulationStatus)) {
            throw new Error(`Verification Space returned '${simulationStatus || "unknown"}'.`);
        }
        recordStage("verification_space", stageStartedAt, context, { simulationStatus });

        stageStartedAt = Date.now();
        const ranking = toolJson(await callTool(client, "rank_artifact_candidates", {
            sessionId,
            correlationId,
            targetObjectId: pair.source.id,
            candidateSetId,
            candidates: [{
                candidateId,
                operation: "create",
                code,
                validationState: "accepted",
                simulationStatus,
                riskScore,
                authoringMode: "automatic",
                experienceMode,
                approach: `Pulse grounded pair ${pair.source.name} and ${pair.destination.name}.`,
            }],
        }), "rank_artifact_candidates");
        if (!ranking.selected) throw new Error("Deterministic ranking found no eligible candidate.");
        recordStage("candidate_ranking", stageStartedAt, context);

        await status(client, sessionId, correlationId, "ready_to_preview", "Refreshing the selected source before automatic attachment.");
        stageStartedAt = Date.now();
        const freshEnvelope = toolJson(await callTool(client, "query_scene", {
            objectId: pair.source.id,
            sessionId,
            correlationId,
            timeoutMs: remainingTimeout(deadline, 7000),
        }), "query_scene refresh");
        const freshScene = compactScene(freshEnvelope);
        if (freshScene.focus.id !== pair.source.id) throw new Error("Selected source changed before proposal.");
        recordStage("freshness_refresh", stageStartedAt, context);

        stageStartedAt = Date.now();
        const proposal = toolJson(await callTool(client, "propose_artifact", {
            code,
            targetObjectId: pair.source.id,
            intent,
            authoringMode: "automatic",
            interactionMode,
            sceneEpoch: freshEnvelope.sceneEpoch,
            snapshotId: freshEnvelope.snapshotId,
            objectRevision: freshEnvelope.objectRevision,
            snapshotTakenAt: freshEnvelope.timestamp || Date.now(),
            validationState: "accepted",
            validationSummary,
            riskScore,
            requiredPermissions: ["attach_component"],
            expectedSideEffects: "Temporary color and 1.08x scale pulse on one grounded source/destination pair.",
            triggerSource,
            reversible: true,
            localOnly: true,
            detailResolved: true,
            operation: "create",
            candidateId,
            candidateSetId,
            candidateCount: 1,
            selectionReason: ranking.comparisonSummary || pair.reason,
            experienceMode,
            sessionId,
            correlationId,
            timeoutMs: remainingTimeout(deadline, 10000),
        }), "propose_artifact");
        const outcome = proposal && proposal.payload && proposal.payload.status;
        recordStage("unity_attachment", stageStartedAt, context, { outcome });
        if (outcome !== "committed") throw new Error(`Unity returned '${outcome || "unknown"}' instead of committed.`);

        const latencyMs = Date.now() - pipelineStartedAt;
        appendEvaluationEvent({
            eventType: "orchestrator_result",
            ...context,
            subtype: "success",
            latencyMs,
            model,
            candidateCount: 1,
            orchestratorRoute: "direct-fast-implicit",
            sourceObjectId: pair.source.id,
            destinationObjectId: pair.destination.id,
            pairSelection: pair.selection,
            modelPairingEnabled: useModelPairing,
        });
        console.log(`[orchestrator] direct fast implicit committed in ${latencyMs}ms source=${pair.source.id} destination=${pair.destination.id}`);
        return { outcome, latencyMs, pair, simulationStatus };
    } catch (error) {
        appendEvaluationEvent({
            eventType: "orchestrator_result",
            ...context,
            subtype: "error",
            latencyMs: Date.now() - pipelineStartedAt,
            model,
            candidateCount: 1,
            orchestratorRoute: "direct-fast-implicit",
            error: error.message,
        });
        if (connected) {
            try {
                await status(client, sessionId, correlationId, "failed", `Fast path stopped: ${error.message}`);
            } catch (_) { /* Best-effort status only. */ }
        }
        throw error;
    } finally {
        if (connected) await client.close().catch(() => {});
    }
}

export {
    PAIR_SELECTION_SYSTEM_PROMPT,
    compactScene,
    boundedSceneForMode,
    nearestBoundedPair,
    selectPairWithClaude,
    validateModelPair,
    renderGuidanceCode,
    validateBoundedCode,
};
