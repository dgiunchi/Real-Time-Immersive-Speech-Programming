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

class ActivityMonitor extends EventEmitter {
    constructor({
        threshold = 1.1,
        windowMs = 5000,
        cooldownMs = 30000,
        now = () => Date.now(),
    } = {}) {
        super();
        this.threshold = Math.max(0.5, Number(threshold) || 1.1);
        this.windowMs = Math.max(1000, Number(windowMs) || 5000);
        this.cooldownMs = Math.max(5000, Number(cooldownMs) || 30000);
        this.now = now;
        this.windows = new Map();
        this.lastTriggeredAt = new Map();
        this.recent = [];
    }

    observeSceneDelta(envelope = {}) {
        const at = Number.isFinite(envelope.timestamp) ? envelope.timestamp : this.now();
        const sessionId = envelope.sessionId;
        if (!sessionId) return null;
        const payload = envelope.payload || {};
        const focus = payload.focus || {};
        const sensorEvents = Array.isArray(payload.sensorEvents) ? payload.sensorEvents : [];
        const targetObjectId = envelope.targetObjectId || focus.id ||
            (sensorEvents.find((event) => event && event.targetObjectId) || {}).targetObjectId || null;
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
        if (score < this.threshold || at - lastTriggered < this.cooldownMs) return null;

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

module.exports = { ActivityMonitor, SENSOR_WEIGHTS };
