"use strict";

const fs = require("fs");
const path = require("path");

const MANIFEST_PATH = path.join(__dirname, "task_manifest.v1.json");

function assetGuid(repositoryRoot, relativePath) {
    const meta = fs.readFileSync(path.resolve(repositoryRoot, relativePath) + ".meta", "utf8");
    const match = meta.match(/^guid:\s*([0-9a-f]+)$/m);
    if (!match) throw new Error(`Unity asset has no GUID: ${relativePath}`);
    return match[1];
}

function validateSceneTechnicalReadiness(scene, repositoryRoot) {
    const checks = [];
    const check = (ok, id, detail) => checks.push({ ok: Boolean(ok), id, detail });
    const playerPath = "Unity/Assets/LegacyUbiqDependencies/Player.prefab";
    const player = fs.readFileSync(path.resolve(repositoryRoot, playerPath), "utf8");
    const playerGuid = assetGuid(repositoryRoot, playerPath);
    const handPrefabPath = "Unity/Assets/LegacyUbiqDependencies/Hand Controller.prefab";
    const handPrefab = fs.readFileSync(path.resolve(repositoryRoot, handPrefabPath), "utf8");
    const handPrefabGuid = assetGuid(repositoryRoot, handPrefabPath);
    const compilerGuid = assetGuid(repositoryRoot, "Unity/Assets/AgenticCache/AgenticRuntimeCompiler.cs");
    const demoCompilerGuid = assetGuid(repositoryRoot, "Unity/Assets/Scenes/Scripts/TestRoslyn.cs");
    const doneButtonGuid = assetGuid(repositoryRoot, "Unity/Assets/Study/StudyDoneButtonUseable.cs");
    const graspableGuid = assetGuid(repositoryRoot,
        "Unity/Assets/LegacyUbiqDependencies/RuntimeXR/Interaction/FollowGraspable.cs");
    const requiredPlayerScripts = [
        "Unity/Assets/LegacyUbiqDependencies/RuntimeXR/XRPlayerController.cs",
    ];
    const requiredHandScripts = [
        "Unity/Assets/LegacyUbiqDependencies/RuntimeXR/HandController.cs",
        "Unity/Assets/LegacyUbiqDependencies/RuntimeXR/Teleporting/TeleportRay.cs",
        "Unity/Assets/LegacyUbiqDependencies/RuntimeXR/Interaction/GraspableObjectGrasper.cs",
        "Unity/Assets/LegacyUbiqDependencies/RuntimeXR/Interaction/UseableObjectUser.cs",
    ];
    check(scene.includes(`guid: ${playerGuid}`) && scene.includes("value: StudyXRPlayer"),
        "technical-real-xr-player-prefab", playerGuid);
    for (const script of requiredPlayerScripts) {
        const guid = assetGuid(repositoryRoot, script);
        check(player.includes(`guid: ${guid}`), `technical-player-${path.basename(script, ".cs")}`, guid);
    }
    check(player.includes(`guid: ${handPrefabGuid}`), "technical-player-hand-prefab", handPrefabGuid);
    for (const script of requiredHandScripts) {
        const guid = assetGuid(repositoryRoot, script);
        check(handPrefab.includes(`guid: ${guid}`), `technical-hand-${path.basename(script, ".cs")}`, guid);
    }
    check(scene.includes("m_Name: StudyTeleportFloor") && scene.includes("m_TagString: Teleport"),
        "technical-teleport-floor", "StudyTeleportFloor/Teleport");
    check((scene.match(new RegExp(`guid: ${compilerGuid}`, "g")) || []).length === 1,
        "technical-study-safe-compiler", compilerGuid);
    check(!scene.includes(`guid: ${demoCompilerGuid}`), "technical-no-demo-compiler", demoCompilerGuid);
    check((scene.match(new RegExp(`guid: ${doneButtonGuid}`, "g")) || []).length === 2,
        "technical-l3-controller-buttons", doneButtonGuid);
    check((scene.match(new RegExp(`guid: ${graspableGuid}`, "g")) || []).length === 14,
        "technical-graspable-task-objects", graspableGuid);
    return checks;
}

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
            AgenticRegionVolume: path.resolve(repositoryRoot, "Unity", "Assets", "AgenticCache", "AgenticRegionVolume.cs"),
        };
        for (const component of new Set(manifest.tasks.flatMap((task) => task.requiredComponents || []))) {
            const source = componentSources[component];
            check(Boolean(source) && fs.existsSync(source) && scene.includes(component),
                `scene-component-${component}`, source || "unknown component");
        }
        checks.push(...validateSceneTechnicalReadiness(scene, repositoryRoot));
    }
    return { ok: checks.every((item) => item.ok), manifestPath: MANIFEST_PATH, scenePath, checks };
}

module.exports = { MANIFEST_PATH, validateTaskReadiness, validateSceneTechnicalReadiness };
