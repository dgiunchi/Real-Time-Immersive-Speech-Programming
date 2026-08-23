"use strict";

const { EventEmitter } = require("events");
const { randomUUID } = require("crypto");

const SENSOR_WEIGHTS = Object.freeze({
    gaze: 0.35,
    proximity: 0.55,
    locomotion: 0.45,
    collision: 1.2,
    gesture: 0.8,
    handTracking: 0.45,
});

// Directed sensor types that indicate attention TOWARD a specific object -
// the anticipation signal (docs/code-implicit-proactive-showcase-2026-08-13.md §1)
// requires at least two of these inside the window before predicting engagement.
const DIRECTED_SENSOR_TYPES = Object.freeze(["gaze", "proximity", "handTracking", "gesture"]);

function isAuthorableActivityTarget(value) {
    if (typeof value !== "string" || !value.trim()) return false;
    const id = value.trim().toLowerCase();
    return !id.startsWith("sensor:") &&
        !id.startsWith("xr-user-") &&
        !id.startsWith("avatar:");
}

class ActivityMonitor extends EventEmitter {
    constructor({
        threshold = 1.1,
        windowMs = 5000,
        cooldownMs = 30000,
        anticipationThreshold = 0.6,
        anticipationCooldownMs = 60000,
        now = () => Date.now(),
    } = {}) {
        super();
        this.threshold = Math.max(0.5, Number(threshold) || 1.1);
        this.windowMs = Math.max(1000, Number(windowMs) || 5000);
        this.cooldownMs = Math.max(5000, Number(cooldownMs) || 30000);
        // Anticipation fires BELOW the assist threshold - it predicts engagement
        // from sustained directed attention so speculative preparation can start
        // before the actual trigger. Clamped under the assist threshold so a
        // prediction can never replace or outrank the real trigger.
        this.anticipationThreshold = Math.min(this.threshold - 0.05,
            Math.max(0.3, Number(anticipationThreshold) || 0.6));
        this.anticipationCooldownMs = Math.max(10000, Number(anticipationCooldownMs) || 60000);
        this.now = now;
        this.windows = new Map();
        this.lastTriggeredAt = new Map();
        this.lastAnticipatedAt = new Map();
        this.recent = [];
    }

    observeSceneDelta(envelope = {}) {
        const at = Number.isFinite(envelope.timestamp) ? envelope.timestamp : this.now();
        const sessionId = envelope.sessionId;
        if (!sessionId) return null;
        const payload = envelope.payload || {};
        const focus = payload.focus || {};
        const sensorEvents = Array.isArray(payload.sensorEvents) ? payload.sensorEvents : [];
        // Study lifecycle/questionnaire events share SceneDelta transport but
        // are telemetry, not participant activity. Unity increments the object
        // revision for these envelopes, so treating that revision as a scene
        // change can incorrectly launch another implicit Claude turn after a
        // task has already completed.
        const stateIsEmpty = payload.state == null ||
            (typeof payload.state === "object" && Object.keys(payload.state).length === 0);
        const studyTelemetryOnly = sensorEvents.length > 0 &&
            sensorEvents.every((event) => event &&
                String(event.sensorType || "").startsWith("study_")) &&
            !payload.focus && !payload.changedSince && stateIsEmpty;
        if (studyTelemetryOnly) return null;
        // Sensor publishers may address a SceneDelta to the HMD/hand sensor
        // itself while its payload focuses on an authorable scene object. Never
        // spend a model turn attempting to attach a MonoBehaviour to that sensor.
        const targetObjectId = [focus.id, envelope.targetObjectId,
            ...sensorEvents.map((event) => event && event.targetObjectId)]
            .find(isAuthorableActivityTarget) || null;
        if (!targetObjectId) return null;
        const key = `${sessionId}:${targetObjectId || "activity"}`;
        const observations = [];

        for (const event of sensorEvents) {
            if (!event || !SENSOR_WEIGHTS[event.sensorType]) continue;
            const confidence = Number.isFinite(event.confidence)
                ? Math.max(0, Math.min(1, event.confidence)) : 1;
            if (confidence < 0.5) continue;
            const score = SENSOR_WEIGHTS[event.sensorType] * confidence;
            observations.push({
                type: event.sensorType,
                score,
                at: Number.isFinite(event.timestamp) ? event.timestamp : at,
            });
        }
        if (payload.changedSince || payload.state || envelope.objectRevision != null) {
            observations.push({ type: "scene_change", score: 0.25, at });
        }
        if (!observations.length) return null;

        const current = (this.windows.get(key) || []).filter((item) => at - item.at <= this.windowMs);
        current.push(...observations);
        this.windows.set(key, current);
        this.recent.push(...observations.map((item) => ({
            ...item,
            sessionId,
            targetObjectId,
        })));
        if (this.recent.length > 500) this.recent.splice(0, this.recent.length - 500);

        const score = current.reduce((sum, item) => sum + item.score, 0);
        const lastTriggered = this.lastTriggeredAt.get(key) || 0;
        if (score < this.threshold || at - lastTriggered < this.cooldownMs) {
            this.#maybePredictEngagement({ key, sessionId, targetObjectId, current, at });
            return null;
        }

        const opportunity = {
            triggerId: `activity-${randomUUID()}`,
            triggerSource: "context",
            sessionId,
            targetObjectId,
            score: Math.round(score * 1000) / 1000,
            threshold: this.threshold,
            signalTypes: [...new Set(current.map((item) => item.type))],
            observedAt: at,
            status: "pending_policy_and_verification",
        };
        this.lastTriggeredAt.set(key, at);
        this.windows.set(key, []);
        this.emit("assist_worthy", opportunity);
        return opportunity;
    }

    // Predicted engagement: sustained directed attention (>=2 gaze/proximity/hand
    // observations) toward a SPECIFIC object crossing the anticipation threshold,
    // before the assist threshold fires. Consumers may start speculative
    // preparation for this target - preparation only, never a commit, and the
    // real trigger still runs the full normal pipeline. The window is NOT
    // cleared, so the assist trigger is unaffected by a prediction.
    #maybePredictEngagement({ key, sessionId, targetObjectId, current, at }) {
        if (!targetObjectId) return null;
        const directed = current.filter((item) => DIRECTED_SENSOR_TYPES.includes(item.type));
        const directedScore = directed.reduce((sum, item) => sum + item.score, 0);
        const lastAnticipated = this.lastAnticipatedAt.get(key) || 0;
        if (directed.length < 2 || directedScore < this.anticipationThreshold ||
            at - lastAnticipated < this.anticipationCooldownMs) return null;
        const prediction = {
            predictionId: `predicted-engagement-${randomUUID()}`,
            triggerSource: "context",
            sessionId,
            targetObjectId,
            score: Math.round(directedScore * 1000) / 1000,
            threshold: this.anticipationThreshold,
            signalTypes: [...new Set(directed.map((item) => item.type))],
            observedAt: at,
            speculative: true,
            status: "predicted_engagement",
        };
        this.lastAnticipatedAt.set(key, at);
        this.emit("predicted_engagement", prediction);
        return prediction;
    }

    observeDecision(envelope = {}) {
        const observation = {
            type: "decision",
            sessionId: envelope.sessionId || null,
            targetObjectId: envelope.targetObjectId || null,
            decision: envelope.payload && envelope.payload.decision || null,
            at: Number.isFinite(envelope.timestamp) ? envelope.timestamp : this.now(),
        };
        this.recent.push(observation);
        if (this.recent.length > 500) this.recent.shift();
        return observation;
    }

    query({ sessionId, targetObjectId, limit = 50 } = {}) {
        return this.recent.filter((item) =>
            (!sessionId || item.sessionId === sessionId) &&
            (!targetObjectId || item.targetObjectId === targetObjectId)
        ).slice(-Math.max(1, limit));
    }
}

module.exports = { ActivityMonitor, SENSOR_WEIGHTS, DIRECTED_SENSOR_TYPES, isAuthorableActivityTarget };
