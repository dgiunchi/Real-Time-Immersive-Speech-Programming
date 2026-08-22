"use strict";

const fs = require("fs");
const path = require("path");

const MANIFEST_PATH = path.join(__dirname, "task_manifest.v1.json");

function validateTaskReadiness({ repositoryRoot = path.resolve(__dirname, "..", "..") } = {}) {
    const manifest = JSON.parse(fs.readFileSync(MANIFEST_PATH, "utf8"));
    const protocol = JSON.parse(fs.readFileSync(path.join(__dirname, "protocol.v1.json"), "utf8"));
    const checks = [];
    const check = (ok, id, detail) => checks.push({ ok: Boolean(ok), id, detail });
    check(manifest.protocolId === protocol.protocolId, "task-protocol-match", manifest.protocolId);
    check(manifest.methodVersion === protocol.methodVersion, "task-method-match", manifest.methodVersion);
    check(JSON.stringify(manifest.variants) === JSON.stringify(protocol.design.taskVariants),
        "task-variants-match", manifest.variants.join(","));
    check(manifest.tasks.length === protocol.tasks.length, "task-count", String(manifest.tasks.length));

    const identifiers = [];
    for (const task of manifest.tasks) {
        const protocolTask = protocol.tasks.find((candidate) => candidate.taskId === task.taskId);
        check(Boolean(protocolTask) && protocolTask.interactionMode === task.interactionMode,
            `task-mode-${task.taskId}`, task.interactionMode);
        for (const variant of manifest.variants) {
            const objectIds = task.variants && task.variants[variant];
            check(Array.isArray(objectIds) && objectIds.length > 0,
                `task-objects-${task.taskId}-${variant}`, Array.isArray(objectIds) ? String(objectIds.length) : "missing");
            if (Array.isArray(objectIds)) identifiers.push(...objectIds);
        }
    }
    check(new Set(identifiers).size === identifiers.length, "stable-object-ids-unique",
        `${new Set(identifiers).size}/${identifiers.length}`);

    const scenePath = path.resolve(repositoryRoot, manifest.scenePath);
    check(fs.existsSync(scenePath), "study-scene-authored", scenePath);
    if (fs.existsSync(scenePath)) {
        const scene = fs.readFileSync(scenePath, "utf8");
        for (const id of identifiers) check(scene.includes(id), `scene-object-${id}`, id);
        const componentSources = {
            StableObjectId: path.resolve(repositoryRoot, "Unity", "Assets", "AgenticCache", "StableObjectId.cs"),
            AgenticInertAnchor: path.resolve(repositoryRoot, "Unity", "Assets", "AgenticCache", "AgenticInertAnchor.cs"),
            AgenticRegionVolume: path.resolve(repositoryRoot, "Unity", "Assets", "AgenticCache", "ImplicitTriggerSensors.cs"),
        };
        for (const component of new Set(manifest.tasks.flatMap((task) => task.requiredComponents || []))) {
            const source = componentSources[component];
            check(Boolean(source) && fs.existsSync(source) && scene.includes(component),
                `scene-component-${component}`, source || "unknown component");
        }
    }
    return { ok: checks.every((item) => item.ok), manifestPath: MANIFEST_PATH, scenePath, checks };
}

module.exports = { MANIFEST_PATH, validateTaskReadiness };
