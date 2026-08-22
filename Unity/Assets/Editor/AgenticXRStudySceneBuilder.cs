using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AgenticCache;
using AgenticXR.Study;
using Newtonsoft.Json.Linq;
using RoslynCSharp;
using Ubiq.Messaging;
using Ubiq.Networking;
using Ubiq.Rooms;
using Ubiq.Samples;
using Ubiq.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.SpatialTracking;

public static class AgenticXRStudySceneBuilder
{
    public const string ScenePath = "Assets/Scenes/AgenticXRStudy.unity";
    private const string ManifestRelativePath = "Server/study/task_manifest.v1.json";
    private const string MaterialRoot = "Assets/Study/Materials";
    private const string PlayerPrefabPath = "Assets/LegacyUbiqDependencies/Player.prefab";
    private const string QuestionnaireAssetPath = "Assets/Study/Definitions/questionnaires.v1.json";
    private const string TaskCardAssetPath = "Assets/Study/Definitions/task_cards.v1.json";
    private const string AnchorRoleL1 = "empty-workbench-anchor";
    private const string AnchorDescriptionL1 = "A stationary workbench surface beside three loose tools and three empty trays.";
    private const string AnchorRoleL2 = "station-guide-anchor";
    private const string AnchorDescriptionL2 = "A fixed guide marker beside a parts station with three parts and three sockets.";
    private const string TrainingAnchorRole = "practice-anchor";
    private const string TrainingAnchorDescription = "A neutral practice pedestal containing no study-task objects.";

    [MenuItem("AgenticXR/Build Study Scene")]
    public static void BuildStudyScene()
    {
        SyncStudyJsonAsset("Server/study/questionnaires.v1.json", QuestionnaireAssetPath);
        SyncStudyJsonAsset("Server/study/task_cards.v1.json", TaskCardAssetPath);
        var manifest = LoadManifest();
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Study", "Materials"));
        var materialA = EnsureMaterial("StudyVariantA", new Color(0.15f, 0.55f, 0.9f));
        var materialB = EnsureMaterial("StudyVariantB", new Color(0.9f, 0.45f, 0.15f));
        var neutral = EnsureMaterial("StudyNeutral", new Color(0.55f, 0.58f, 0.62f));

        var scene = File.Exists(Path.Combine(ProjectRoot(), "Unity", ScenePath))
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var studyRoot = EnsureRoot("AgenticXRStudy");
        ConfigureStudyLight(studyRoot);
        var xrRig = ConfigureXrRig(studyRoot);
        ConfigureTeleportFloor(studyRoot, neutral);
        var systemRoot = EnsureChild(studyRoot.transform, "AgenticSystem");
        var system = ConfigureAgenticSystem(systemRoot);
        var controlRoot = EnsureChild(studyRoot.transform, "StudyControl");
        var presenter = EnsureComponent<StudyTaskCardPresenter>(EnsureChild(controlRoot.transform, "StudyTaskCardPresenter"));
        var questionnairePresenter = EnsureComponent<StudyQuestionnairePresenter>(
            EnsureChild(controlRoot.transform, "StudyQuestionnairePresenter"));
        var director = EnsureComponent<StudyTrialDirector>(EnsureChild(controlRoot.transform, "StudyTrialDirector"));
        var detectorsRoot = EnsureChild(controlRoot.transform, "StudySuccessDetectors");
        detectorsRoot.SetActive(true);
        var training = EnsureChild(studyRoot.transform, "TrainingArea");
        ConfigureTrainingArea(training, neutral);
        var tasksRoot = EnsureChild(studyRoot.transform, "StudyTasks");

        var bindings = new List<StudyTrialDirector.VariantBinding>();
        foreach (var task in (JArray)manifest["tasks"])
        {
            var taskId = (string)task["taskId"];
            var mode = (string)task["interactionMode"];
            var timeout = (float)task["timeoutSeconds"];
            foreach (var variant in new[] { "A", "B" })
            {
                var ids = task["variants"][variant].Values<string>().ToArray();
                var root = ConfigureVariant(tasksRoot.transform, taskId, mode, variant, ids,
                    variant == "A" ? materialA : materialB, neutral, system.publisher);
                var detector = root.GetComponent<StudySuccessDetector>();
                var resetBody = FindResetBody(root, mode);
                var resetTransforms = mode == "L5"
                    ? ChildrenContaining(root, "marker-").Select(item => item.transform).ToArray()
                    : Array.Empty<Transform>();
                bindings.Add(new StudyTrialDirector.VariantBinding
                {
                    taskId = taskId,
                    interactionMode = mode,
                    taskVariant = variant,
                    root = root,
                    timeoutSeconds = timeout,
                    detector = detector,
                    resetBody = resetBody,
                    resetLocalPosition = resetBody != null ? resetBody.transform.localPosition : Vector3.zero,
                    resetLocalRotation = resetBody != null ? resetBody.transform.localRotation : Quaternion.identity,
                    resetTransforms = resetTransforms,
                    resetTransformLocalPositions = resetTransforms.Select(item => item.localPosition).ToArray(),
                    resetTransformLocalRotations = resetTransforms.Select(item => item.localRotation).ToArray(),
                    trialDoor = root.GetComponentInChildren<L4TrialDoor>(true),
                    l2Region = mode == "L2" ? root.GetComponentInChildren<AgenticRegionVolume>(true) : null,
                });
                root.SetActive(false);
            }
        }

        director.publisher = system.publisher;
        director.taskCardPresenter = presenter;
        director.questionnairePresenter = questionnairePresenter;
        director.variants = bindings.ToArray();
        system.initializer.trialDirector = director;
        system.initializer.enableDebugStudyLauncher = true;
        presenter.taskCardDefinition = AssetDatabase.LoadAssetAtPath<TextAsset>(TaskCardAssetPath);
        questionnairePresenter.questionnaireDefinition = AssetDatabase.LoadAssetAtPath<TextAsset>(QuestionnaireAssetPath);
        questionnairePresenter.publisher = system.publisher;
        questionnairePresenter.trialDirector = director;
        system.consentPanel.questionnairePresenter = questionnairePresenter;
        EditorUtility.SetDirty(presenter);
        EditorUtility.SetDirty(questionnairePresenter);
        EditorUtility.SetDirty(system.consentPanel);
        EditorUtility.SetDirty(system.initializer);
        EditorUtility.SetDirty(director);
        SmokeTestRuntimeCompiler(system.compiler);
        ValidateBuiltScene(scene, manifest, director, training);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Unity could not save the study scene.");
        AssetDatabase.Refresh();
        EnsureBuildSettings();
        AssetDatabase.SaveAssets();
        NormalizeGeneratedStudyText();
        Debug.Log("[AgenticXRStudySceneBuilder] PASS scene=" + ScenePath + " identifiers=" + ManifestIds(manifest).Count);
    }

    private sealed class SystemReferences
    {
        public CachePublisher publisher;
        public AgenticXRConsentPanel consentPanel;
        public AgenticRuntimeCompiler compiler;
        public StudyAgenticSystemInitializer initializer;
    }

    private static SystemReferences ConfigureAgenticSystem(GameObject parent)
    {
        var runtime = EnsureChild(parent.transform, "AgenticXRBootstrap");
        EnsureComponent<NetworkScene>(runtime);
        var roomClient = EnsureComponent<RoomClient>(runtime);
        var localhost = AssetDatabase.LoadAssetAtPath<ConnectionDefinition>("Assets/Demos/Localhost.asset");
        if (localhost == null)
            throw new InvalidOperationException("Study scene requires Assets/Demos/Localhost.asset for localhost:8009.");
        roomClient.SetDefaultServer(localhost);
        var registry = EnsureComponent<AgenticSceneRegistry>(runtime);
        var publisher = EnsureComponent<CachePublisher>(runtime);
        var panel = EnsureComponent<AgenticXRConsentPanel>(runtime);
        var watchdog = EnsureComponent<GeneratedBehaviourWatchdog>(runtime);
        var exchange = EnsureComponent<CacheExchangeManager>(runtime);
        var sensors = EnsureComponent<ImplicitTriggerSensors>(runtime);
        RemoveComponents<TestRoslyn>(runtime);
        var compiler = EnsureComponent<AgenticRuntimeCompiler>(runtime);
        compiler.assemblyReferences = AssetDatabase.FindAssets("t:AssemblyReferenceAsset",
                new[] { "Assets/RoslynCSharp/AssemblyReferences" })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<AssemblyReferenceAsset>).Where(asset => asset != null).ToArray();
        if (compiler.assemblyReferences.Length == 0)
            throw new InvalidOperationException("Study runtime compiler has no assembly-reference assets.");
        var initializer = EnsureComponent<StudyAgenticSystemInitializer>(runtime);
        EnsureComponent<StudyXRLocalAvatar>(runtime);
        var baseline = EnsureComponent<CodeGenerationManager>(runtime);
        exchange.cachePublisher = publisher;
        exchange.sceneRegistry = registry;
        exchange.consentPanel = panel;
        exchange.executionWatchdog = watchdog;
        exchange.compiler = compiler;
        publisher.localCache = exchange.localCache;
        publisher.sceneRegistry = registry;
        publisher.sessionId = exchange.sessionId;
        sensors.publisher = publisher;
        sensors.sceneRegistry = registry;
        watchdog.manager = exchange;
        initializer.exchange = exchange;
        initializer.publisher = publisher;
        initializer.sceneRegistry = registry;
        initializer.consentPanel = panel;
        initializer.watchdog = watchdog;
        initializer.implicitSensors = sensors;
        initializer.compiler = compiler;
        baseline.runtimeCompiler = compiler;
        baseline.sceneRegistry = registry;
        foreach (var item in new UnityEngine.Object[] { exchange, publisher, sensors, watchdog, compiler, initializer, baseline }) EditorUtility.SetDirty(item);
        return new SystemReferences { publisher = publisher, consentPanel = panel, compiler = compiler, initializer = initializer };
    }

    private static void SmokeTestRuntimeCompiler(AgenticRuntimeCompiler compiler)
    {
        if (compiler == null) throw new InvalidOperationException("Study-safe runtime compiler is missing.");
        var target = new GameObject("AgenticXRCompilerSmokeTarget");
        try
        {
            const string source = "using UnityEngine; public sealed class AgenticXRCompilerSmoke : MonoBehaviour {}";
            if (!compiler.TryCompileAndAttach(target, source, out var proxy, out var error))
                throw new InvalidOperationException("Study runtime compile/attach smoke failed: " + error);
            if (proxy == null || proxy.MonoBehaviourInstance == null)
                throw new InvalidOperationException("Study runtime compiler returned no attached MonoBehaviour.");
            proxy.Dispose();
            Debug.Log("[StudyCompilerAudit] compileAttach=True; disposed=True; component=AgenticXRCompilerSmoke");
        }
        finally { UnityEngine.Object.DestroyImmediate(target); }
    }

    private static void SyncStudyJsonAsset(string sourceRelativePath, string targetAssetPath)
    {
        var sourcePath = Path.Combine(ProjectRoot(), sourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var targetPath = Path.Combine(ProjectRoot(), "Unity", targetAssetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Study JSON source is missing.", sourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
        var sourceBytes = File.ReadAllBytes(sourcePath);
        if (!File.Exists(targetPath) || !File.ReadAllBytes(targetPath).SequenceEqual(sourceBytes))
            File.WriteAllBytes(targetPath, sourceBytes);
        AssetDatabase.ImportAsset(targetAssetPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static GameObject ConfigureXrRig(GameObject studyRoot)
    {
        var obsolete = studyRoot.transform.Find("XRRig");
        if (obsolete != null) UnityEngine.Object.DestroyImmediate(obsolete.gameObject);
        var rigTransform = studyRoot.transform.Find("StudyXRPlayer");
        GameObject rig;
        if (rigTransform == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) throw new InvalidOperationException("The canonical Ubiq Player prefab is missing.");
            rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, studyRoot.scene);
            rig.name = "StudyXRPlayer";
            rig.transform.SetParent(studyRoot.transform, false);
        }
        else rig = rigTransform.gameObject;
        rig.transform.localPosition = Vector3.zero;
        rig.transform.localRotation = Quaternion.identity;
        var controller = rig.GetComponent<XRPlayerController>();
        if (controller == null) throw new InvalidOperationException("StudyXRPlayer must use Ubiq.XR.XRPlayerController.");
        controller.dontDestroyOnLoad = false;
        controller.joystickFlySpeed = 1.2f;
        if (controller.headCamera == null) throw new InvalidOperationException("The Ubiq XR player has no tracked head camera.");
        controller.headCamera.tag = "MainCamera";
        controller.headCamera.nearClipPlane = 0.05f;
        EditorUtility.SetDirty(controller);
        return rig;
    }

    private static void ConfigureTeleportFloor(GameObject studyRoot, Material material)
    {
        var floor = EnsurePrimitive(studyRoot.transform, "StudyTeleportFloor", PrimitiveType.Cube);
        floor.tag = "Teleport";
        SetTransform(floor, new Vector3(0f, -0.06f, 3f), new Vector3(14f, 0.1f, 14f));
        SetMaterial(floor, material);
    }

    private static GameObject ConfigureVariant(Transform tasksRoot, string taskId, string mode, string variant,
        string[] ids, Material variantMaterial, Material neutral, CachePublisher publisher)
    {
        if (ids.Length == 0 || !ids[0].EndsWith("-root", StringComparison.Ordinal))
            throw new InvalidOperationException(taskId + " " + variant + " has no root identifier.");
        var root = EnsureChild(tasksRoot, ids[0]);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        SetStableId(root, ids[0]);
        var expected = new HashSet<string>(ids.Skip(1));
        for (var index = root.transform.childCount - 1; index >= 0; index--)
        {
            var child = root.transform.GetChild(index).gameObject;
            if (child.name.StartsWith("study-", StringComparison.Ordinal) && !expected.Contains(child.name))
                UnityEngine.Object.DestroyImmediate(child);
        }
        foreach (var id in ids.Skip(1))
        {
            var primitive = PrimitiveFor(id, mode);
            var child = primitive.HasValue ? EnsurePrimitive(root.transform, id, primitive.Value) : EnsureChild(root.transform, id);
            SetStableId(child, id);
            SetMaterial(child, variantMaterial);
        }
        switch (mode)
        {
            case "L1": ConfigureL1(root, variant, variantMaterial); break;
            case "L2": ConfigureL2(root, variant, variantMaterial); break;
            case "L3": ConfigureL3(root, variantMaterial, neutral); break;
            case "L4": ConfigureL4(root, variant, variantMaterial); break;
            case "L5": ConfigureL5(root, variant, variantMaterial); break;
            default: throw new InvalidOperationException("Unsupported study mode " + mode);
        }
        ApplyVariantRotation(root, mode, variant);
        var detector = ConfigureDetector(root, taskId, mode, variant, publisher);
        detector.targetObjectId = ids[0];
        EditorUtility.SetDirty(detector);
        return root;
    }

    private static PrimitiveType? PrimitiveFor(string id, string mode)
    {
        if (id.Contains("region")) return null;
        if (id.Contains("avatar")) return PrimitiveType.Capsule;
        if (id.Contains("pad") || id.Contains("tray") || id.Contains("socket")) return PrimitiveType.Cylinder;
        if (id.Contains("part") || id.Contains("tool")) return PrimitiveType.Cube;
        if (id.Contains("marker")) return PrimitiveType.Sphere;
        return PrimitiveType.Cube;
    }

    private static void ConfigureL1(GameObject root, string variant, Material material)
    {
        var bench = ChildContaining(root, "bench-anchor");
        SetTransform(bench, new Vector3(0f, 0.65f, 3f), new Vector3(4f, 0.3f, 1.5f));
        SetAnchor(bench, AnchorRoleL1, AnchorDescriptionL1);
        var tools = ChildrenContaining(root, "tool-");
        var trays = ChildrenContaining(root, "tray-");
        for (var i = 0; i < 3; i++)
        {
            SetTransform(tools[i], new Vector3(-1f + i, 1.05f, 2.6f), Vector3.one * 0.22f);
            EnsureBody(tools[i]);
            EnsureComponent<FollowGraspable>(tools[i]);
            var traySlot = variant == "B" ? new[] { 2, 0, 1 }[i] : i;
            SetTransform(trays[i], new Vector3(-1f + traySlot, 0.88f, 3.35f), new Vector3(0.38f, 0.08f, 0.38f));
        }
    }

    private static void ConfigureL2(GameObject root, string variant, Material material)
    {
        var regionObject = ChildContaining(root, "station-region");
        regionObject.transform.localPosition = new Vector3(0f, 1.5f, 3.5f);
        var region = EnsureComponent<AgenticRegionVolume>(regionObject);
        region.regionId = regionObject.name;
        region.size = new Vector3(5f, 3f, 2f);
        RemoveComponents<Collider>(regionObject);
        SetAnchor(ChildContaining(root, "guide-anchor"), AnchorRoleL2, AnchorDescriptionL2);
        SetTransform(ChildContaining(root, "guide-anchor"), new Vector3(0f, 1f, 4.7f), new Vector3(0.3f, 1.4f, 0.3f));
        var parts = ChildrenContaining(root, "part-");
        var sockets = ChildrenContaining(root, "socket-");
        for (var i = 0; i < 3; i++)
        {
            SetTransform(parts[i], new Vector3(-1f + i, 0.45f, 3f), Vector3.one * 0.24f);
            EnsureBody(parts[i]);
            EnsureComponent<FollowGraspable>(parts[i]);
            var socketSlot = variant == "B" ? new[] { 1, 2, 0 }[i] : i;
            SetTransform(sockets[i], new Vector3(-1f + socketSlot, 0.15f, 4f), new Vector3(0.35f, 0.08f, 0.35f));
        }
    }

    private static void ConfigureL3(GameObject root, Material material, Material neutral)
    {
        var marker = ChildContaining(root, "-marker");
        SetTransform(marker, new Vector3(0f, 0.45f, 1.3f), Vector3.one * 0.25f);
        EnsureBody(marker);
        EnsureComponent<FollowGraspable>(marker);
        var button = ChildContaining(root, "-button");
        SetTransform(button, new Vector3(0f, 0.85f, 0.8f), new Vector3(0.5f, 0.15f, 0.5f));
        EnsureComponent<StudyDoneButtonState>(button);
        EnsureComponent<StudyDoneButtonUseable>(button);
        var pads = ChildrenContaining(root, "pad-");
        var positions = new[] { new Vector3(-1.5f, 0.08f, 2f), new Vector3(0f, 0.08f, 2.5f), new Vector3(1.5f, 0.08f, 2f) };
        for (var i = 0; i < pads.Count; i++)
        {
            SetTransform(pads[i], positions[i], new Vector3(0.55f, 0.05f, 0.55f));
            SetMaterial(pads[i], neutral);
        }
    }

    private static void ConfigureL4(GameObject root, string variant, Material material)
    {
        var regionObject = ChildContaining(root, "approach-region");
        regionObject.transform.localPosition = new Vector3(3.5f, 1.5f, 2.5f);
        var region = EnsureComponent<AgenticRegionVolume>(regionObject);
        region.regionId = regionObject.name;
        region.size = new Vector3(3f, 3f, 3f);
        RemoveComponents<Collider>(regionObject);
        var doorObject = ChildContaining(root, "training-door");
        SetTransform(doorObject, new Vector3(3.5f, 1.1f, 4f), new Vector3(1.4f, 2.2f, 0.12f));
        if (variant == "B")
        {
            var basePosition = doorObject.transform.localPosition;
            doorObject.transform.localPosition = Quaternion.Euler(0f, -8f, 0f) * basePosition;
        }
        RemoveComponents<Collider>(doorObject);
        var door = EnsureComponent<L4TrialDoor>(doorObject);
        door.trialLocal = true;
        door.persistent = false;
        door.offEgressPath = true;
        door.participantLocomotionAllowed = false;
        door.scriptedNpcProxyCount = 2;
        door.closedLocalEuler = Vector3.zero;
        door.fullyOpenLocalEuler = new Vector3(0f, 90f, 0f);
        var avatars = ChildrenContaining(root, "avatar-");
        for (var i = 0; i < avatars.Count; i++)
        {
            var avatarX = variant == "B" ? (i == 0 ? 5f : 2f) : (i == 0 ? 2f : 5f);
            SetTransform(avatars[i], new Vector3(avatarX, 1f, 4.2f), new Vector3(0.35f, 0.75f, 0.35f));
            RemoveComponents<Collider>(avatars[i]);
            var proxy = EnsureComponent<StudyNpcProxy>(avatars[i]);
            proxy.fixedPosition = avatars[i].transform.localPosition;
            proxy.idlePhase = i * Mathf.PI;
        }
    }

    private static void ConfigureL5(GameObject root, string variant, Material material)
    {
        SetTransform(ChildContaining(root, "console"), new Vector3(0f, 1f, 3.2f), new Vector3(2.2f, 1.4f, 0.5f));
        SetTransform(ChildContaining(root, "start-button"), new Vector3(0f, 1.2f, 2.65f), new Vector3(0.5f, 0.15f, 0.5f));
        var markers = ChildrenContaining(root, "marker-");
        for (var i = 0; i < markers.Count; i++)
        {
            var markerSlot = variant == "B" ? new[] { 2, 1, 0 }[i] : i;
            SetTransform(markers[i], new Vector3(-1f + markerSlot, 0.5f, 4f), Vector3.one * 0.25f);
        }
    }

    private static void ApplyVariantRotation(GameObject root, string mode, string variant)
    {
        root.transform.localRotation = Quaternion.identity;
        if (variant != "B" || mode == "L4") return;
        var angle = mode == "L1" ? 38f : mode == "L2" ? -34f : mode == "L3" ? 45f : mode == "L5" ? -41f
            : throw new InvalidOperationException("No B-layout rotation is declared for " + mode);
        root.transform.RotateAround(Vector3.zero, Vector3.up, angle);
    }

    private static void ConfigureStudyLight(GameObject studyRoot)
    {
        var lightObject = EnsureChild(studyRoot.transform, "StudyDirectionalLight");
        lightObject.transform.localPosition = Vector3.zero;
        lightObject.transform.localRotation = Quaternion.Euler(50f, -30f, 0f);
        var light = EnsureComponent<Light>(lightObject);
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        RenderSettings.sun = light;
        EditorUtility.SetDirty(light);
    }

    private static StudySuccessDetector ConfigureDetector(GameObject root, string taskId, string mode,
        string variant, CachePublisher publisher)
    {
        StudySuccessDetector detector;
        switch (mode)
        {
            case "L1":
                var l1 = EnsureComponent<L1ToolStowedDetector>(root);
                l1.tools = ChildrenContaining(root, "tool-").Select(item => item.GetComponent<Rigidbody>()).ToArray();
                l1.trays = ChildrenContaining(root, "tray-").Select(item => item.GetComponent<Collider>()).ToArray();
                detector = l1; break;
            case "L2":
                var l2 = EnsureComponent<L2PartSeatedDetector>(root);
                l2.parts = ChildrenContaining(root, "part-").Select(item => item.GetComponent<Rigidbody>()).ToArray();
                l2.matchingSockets = ChildrenContaining(root, "socket-").Select(item => item.GetComponent<Collider>()).ToArray();
                detector = l2; break;
            case "L3":
                var l3 = EnsureComponent<L3MarkerPlacedDetector>(root);
                l3.marker = ChildContaining(root, "-marker").GetComponent<Rigidbody>();
                l3.pads = new[] { ChildContaining(root, variant == "B" ? "pad-3" : "pad-2").GetComponent<Collider>() };
                l3.doneButton = ChildContaining(root, "-button").GetComponent<StudyDoneButtonState>();
                detector = l3; break;
            case "L4":
                var l4 = EnsureComponent<L4DoorOpenedDetector>(root);
                l4.door = ChildContaining(root, "training-door").GetComponent<L4TrialDoor>();
                l4.approachRegion = ChildContaining(root, "approach-region").GetComponent<AgenticRegionVolume>();
                detector = l4; break;
            case "L5":
                var l5 = EnsureComponent<L5SequenceCompletedDetector>(root);
                var sequence = ChildrenContaining(root, "marker-");
                if (variant == "B") sequence = new List<GameObject> { sequence[2], sequence[1], sequence[0] };
                l5.sequenceMarkers = sequence.Select(item => item.transform).ToArray();
                detector = l5; break;
            default: throw new InvalidOperationException(mode);
        }
        detector.publisher = publisher;
        detector.taskId = taskId;
        detector.variant = variant;
        detector.settleWindowSeconds = 0.5f;
        return detector;
    }

    private static void ConfigureTrainingArea(GameObject training, Material material)
    {
        var anchor1 = EnsurePrimitive(training.transform, "training-anchor-1", PrimitiveType.Cube);
        var anchor2 = EnsurePrimitive(training.transform, "training-anchor-2", PrimitiveType.Cube);
        SetStableId(anchor1, "training-anchor-1");
        SetStableId(anchor2, "training-anchor-2");
        SetAnchor(anchor1, TrainingAnchorRole, TrainingAnchorDescription);
        SetAnchor(anchor2, TrainingAnchorRole, TrainingAnchorDescription);
        SetTransform(anchor1, new Vector3(-1f, 0.6f, -2f), new Vector3(0.7f, 1.2f, 0.7f));
        SetTransform(anchor2, new Vector3(1f, 0.6f, -2f), new Vector3(0.7f, 1.2f, 0.7f));
        SetMaterial(anchor1, material);
        SetMaterial(anchor2, material);
        var regionObject = EnsureChild(training.transform, "training-region");
        SetStableId(regionObject, "training-region");
        var region = EnsureComponent<AgenticRegionVolume>(regionObject);
        region.regionId = "training-region";
        region.size = new Vector3(4f, 3f, 3f);
        regionObject.transform.localPosition = new Vector3(0f, 1.5f, -2f);
    }

    private static void ValidateBuiltScene(Scene scene, JObject manifest, StudyTrialDirector director, GameObject training)
    {
        var sceneRoots = scene.GetRootGameObjects();
        var xrPlayers = sceneRoots.SelectMany(root => root.GetComponentsInChildren<XRPlayerController>(true)).ToArray();
        var hands = sceneRoots.SelectMany(root => root.GetComponentsInChildren<HandController>(true)).ToArray();
        if (xrPlayers.Length != 1 || xrPlayers[0].headCamera == null ||
            xrPlayers[0].headCamera.GetComponent<TrackedPoseDriver>() == null)
            throw new InvalidOperationException("The study requires one Ubiq XR player with a tracked HMD camera.");
        if (hands.Length != 2 || hands.Any(hand => hand.GetComponent<TrackedPoseDriver>() == null) ||
            hands.Select(hand => hand.GetComponent<TrackedPoseDriver>().poseSource).Distinct().Count() != 2)
            throw new InvalidOperationException("The study requires distinct tracked left and right hand controllers.");
        if (xrPlayers[0].GetComponentsInChildren<TeleportRay>(true).Length < 2 ||
            xrPlayers[0].GetComponentsInChildren<GraspableObjectGrasper>(true).Length < 2 ||
            xrPlayers[0].GetComponentsInChildren<UseableObjectUser>(true).Length < 2)
            throw new InvalidOperationException("The XR player lacks teleport, grasp, or use interaction routes.");
        var teleportFloor = sceneRoots.SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(item => item.name == "StudyTeleportFloor");
        if (teleportFloor == null || !teleportFloor.CompareTag("Teleport") || teleportFloor.GetComponent<Collider>() == null)
            throw new InvalidOperationException("A colliding Teleport-tagged study floor is required for participant-controlled movement.");
        if (sceneRoots.SelectMany(root => root.GetComponentsInChildren<TestRoslyn>(true)).Any())
            throw new InvalidOperationException("The study scene must not contain the TestRoslyn demo component.");
        if (sceneRoots.SelectMany(root => root.GetComponentsInChildren<AgenticRuntimeCompiler>(true)).Count() != 1)
            throw new InvalidOperationException("The study scene requires exactly one study-safe runtime compiler.");
        foreach (var behaviour in sceneRoots.SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)))
        {
            if (behaviour == null) continue;
            var scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(behaviour)).Replace('\\', '/');
            if (scriptPath.StartsWith("Assets/Scenes/Scripts/", StringComparison.Ordinal) ||
                scriptPath.StartsWith("Assets/Demos/", StringComparison.Ordinal))
                throw new InvalidOperationException("Demo MonoBehaviour is not allowed in the study scene: " + scriptPath);
        }
        var expectedIds = ManifestIds(manifest);
        var stableIds = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<StableObjectId>(true))
            .Select(ReadStableId).ToList();
        foreach (var id in expectedIds)
            if (stableIds.Count(value => value == id) != 1) throw new InvalidOperationException("Manifest ID is not serialized exactly once: " + id);
        if (director.variants == null || director.variants.Length != 10 || director.variants.Count(binding => binding.root.activeSelf) != 0)
            throw new InvalidOperationException("All ten variant roots must exist and start inactive.");
        if (director.variants.Any(binding => binding == null || binding.root == null) ||
            director.variants.Select(binding => binding.root).Distinct().Count() != 10 ||
            director.variants.Select(binding => binding.interactionMode + "|" + binding.taskVariant).Distinct().Count() != 10)
            throw new InvalidOperationException("VariantBinding must cover each distinct L1-L5 x A/B root exactly once.");
        Debug.Log("[StudyBindingAudit] " + string.Join("; ", director.variants.Select(binding =>
            binding.taskId + "|" + binding.interactionMode + "|" + binding.taskVariant + "|" + binding.root.name)));
        if (director.taskCardPresenter == null || director.questionnairePresenter == null ||
            director.taskCardPresenter.taskCardDefinition == null || director.questionnairePresenter.questionnaireDefinition == null)
            throw new InvalidOperationException("Both in-VR JSON definitions must resolve to imported TextAssets.");
        var lights = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>(true)).ToArray();
        if (lights.Length != 1 || lights[0].type != LightType.Directional || lights[0].shadows == LightShadows.None ||
            RenderSettings.sun != lights[0])
            throw new InvalidOperationException("The scene requires exactly one shadow-casting directional sun light.");
        foreach (var variant in director.variants.Where(binding => binding.interactionMode == "L4"))
        {
            var door = variant.trialDoor;
            if (door == null || door.GetComponents<Collider>().Length != 0) throw new InvalidOperationException("L4 door must have no Collider.");
            if (!door.offEgressPath || Mathf.Abs(door.transform.position.x) < 1f) throw new InvalidOperationException("L4 door must remain off every egress path.");
            if (!door.trialLocal || door.persistent || door.participantLocomotionAllowed) throw new InvalidOperationException("L4 door must be trial-local, non-persistent, and never locomote the participant.");
            var proxies = variant.root.GetComponentsInChildren<StudyNpcProxy>(true);
            if (door.scriptedNpcProxyCount != 2 || proxies.Length != 2 || proxies.Any(proxy => proxy.GetComponent<Collider>() != null))
                throw new InvalidOperationException("L4 requires exactly two non-colliding scripted NPC proxies.");
            if (variant.root.GetComponentsInChildren<Transform>(true).Where(item => item != variant.root.transform)
                .Any(item => Mathf.Abs(item.position.x) < 1f))
                throw new InvalidOperationException("No L4 object may occupy the participant egress corridor.");
            if (variant.taskVariant == "B") Debug.Log("[StudyL4BAudit] doorColliders=0; doorX=" +
                door.transform.position.x.ToString("F4") + "; offEgressPath=" + door.offEgressPath +
                "; trialLocal=" + door.trialLocal + "; persistent=" + door.persistent +
                "; participantLocomotionAllowed=" + door.participantLocomotionAllowed +
                "; proxyCount=" + proxies.Length + "; proxyColliders=0; minDescendantAbsX=" +
                variant.root.GetComponentsInChildren<Transform>(true).Where(item => item != variant.root.transform)
                    .Min(item => Mathf.Abs(item.position.x)).ToString("F4"));
        }
        foreach (var task in (JArray)manifest["tasks"])
        {
            var mode = (string)task["interactionMode"];
            var a = director.variants.Single(binding => binding.interactionMode == mode && binding.taskVariant == "A").root;
            var b = director.variants.Single(binding => binding.interactionMode == mode && binding.taskVariant == "B").root;
            if (!StructuralSignature(a, "-a-").SequenceEqual(StructuralSignature(b, "-b-")))
                throw new InvalidOperationException(mode + " variants are not structurally isomorphic.");
            ValidateDistanceIsomorphism(mode, a, b, mode != "L4");
            foreach (var variantRoot in new[] { a, b })
            {
                if (mode == "L1" && ChildrenContaining(variantRoot, "tool-").Any(item => item.GetComponent<FollowGraspable>() == null))
                    throw new InvalidOperationException("Every L1 tool must be graspable.");
                if (mode == "L2" && ChildrenContaining(variantRoot, "part-").Any(item => item.GetComponent<FollowGraspable>() == null))
                    throw new InvalidOperationException("Every L2 part must be graspable.");
                if (mode == "L3" && (ChildContaining(variantRoot, "-marker").GetComponent<FollowGraspable>() == null ||
                    ChildContaining(variantRoot, "-button").GetComponent<StudyDoneButtonUseable>() == null))
                    throw new InvalidOperationException("L3 requires a graspable marker and a controller-usable done button.");
            }
        }
        var trainingIds = training.GetComponentsInChildren<StableObjectId>(true).Select(ReadStableId).ToArray();
        if (trainingIds.Any(id => id.StartsWith("study-", StringComparison.Ordinal) || expectedIds.Contains(id)))
            throw new InvalidOperationException("Training identifiers must be disjoint from study identifiers.");
        foreach (var anchor in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<AgenticInertAnchor>(true)))
        {
            var text = (anchor.anchorRole + " " + anchor.description).ToLowerInvariant();
            if (new[] { "should", "when approached", "turn on", "move to", "open when", "function to add" }.Any(text.Contains))
                throw new InvalidOperationException("Anchor context leaks a trigger-to-function mapping: " + anchor.name);
        }
    }

    private static void ValidateDistanceIsomorphism(string mode, GameObject a, GameObject b, bool requirePairwiseEquality)
    {
        var aObjects = StudyObjects(a).ToArray();
        var bObjects = StudyObjects(b).ToArray();
        if (aObjects.Length != bObjects.Length) throw new InvalidOperationException(mode + " A/B object counts differ.");
        var aByRole = aObjects.ToDictionary(item => item.name.Replace("-a-", "-v-"));
        var bByRole = bObjects.ToDictionary(item => item.name.Replace("-b-", "-v-"));
        if (!aByRole.Keys.OrderBy(value => value).SequenceEqual(bByRole.Keys.OrderBy(value => value)))
            throw new InvalidOperationException(mode + " A/B semantic roles differ.");
        foreach (var role in aByRole.Keys)
        {
            if (Mathf.Abs(aByRole[role].position.y - bByRole[role].position.y) > 1e-4f)
                throw new InvalidOperationException(mode + " B changed an object's height: " + role);
            if (mode != "L4" && Vector3.Distance(aByRole[role].position, bByRole[role].position) <= 1e-4f)
                throw new InvalidOperationException(mode + " B transform is identity for " + role);
        }
        AssertSameMultiset(mode + " origin distances", aObjects.Select(item => item.position.magnitude),
            bObjects.Select(item => item.position.magnitude));
        // L4 deliberately permutes labelled proxy roles while moving only the door along a
        // participant-centred arc. Participant reach is held constant; labelled proxy-to-door
        // pair distances need not be, and are not part of the task difficulty manipulation.
        if (requirePairwiseEquality)
            AssertSameMultiset(mode + " pairwise distances", PairwiseDistances(aObjects), PairwiseDistances(bObjects));
        Debug.Log("[StudyDistanceAudit] " + mode + " originA=" + FormatDistances(aObjects.Select(item => item.position.magnitude)) +
            " originB=" + FormatDistances(bObjects.Select(item => item.position.magnitude)) +
            " pairwiseA=" + FormatDistances(PairwiseDistances(aObjects)) +
            " pairwiseB=" + FormatDistances(PairwiseDistances(bObjects)) +
            " pairwiseRequired=" + requirePairwiseEquality);
    }

    private static IEnumerable<Transform> StudyObjects(GameObject root) =>
        root.GetComponentsInChildren<StableObjectId>(true).Select(item => item.transform).Where(item => item != root.transform);

    private static IEnumerable<float> PairwiseDistances(Transform[] items)
    {
        for (var left = 0; left < items.Length; left++)
            for (var right = left + 1; right < items.Length; right++)
                yield return Vector3.Distance(items[left].position, items[right].position);
    }

    private static void AssertSameMultiset(string label, IEnumerable<float> left, IEnumerable<float> right)
    {
        var a = left.OrderBy(value => value).ToArray();
        var b = right.OrderBy(value => value).ToArray();
        if (a.Length != b.Length || a.Where((value, index) => Mathf.Abs(value - b[index]) > 1e-4f).Any())
            throw new InvalidOperationException(label + " are not equal within 1e-4.");
    }

    private static string FormatDistances(IEnumerable<float> values) =>
        "[" + string.Join(",", values.OrderBy(value => value).Select(value => value.ToString("F4"))) + "]";

    private static IEnumerable<string> StructuralSignature(GameObject root, string variantToken) =>
        root.GetComponentsInChildren<Transform>(true).Where(item => item != root.transform).Select(item =>
            item.name.Replace(variantToken, "-v-") + "|" + string.Join(",", item.GetComponents<Component>()
                .Where(component => component != null).Select(component => component.GetType().FullName).OrderBy(value => value)))
            .OrderBy(value => value);

    private static JObject LoadManifest() => JObject.Parse(File.ReadAllText(Path.Combine(ProjectRoot(), ManifestRelativePath)));
    private static HashSet<string> ManifestIds(JObject manifest) => new HashSet<string>(((JArray)manifest["tasks"])
        .SelectMany(task => ((JObject)task["variants"]).Properties().SelectMany(property => property.Value.Values<string>())));
    private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    private static void NormalizeGeneratedStudyText()
    {
        var paths = Directory.GetFiles(Path.Combine(Application.dataPath, "Study"), "*", SearchOption.AllDirectories)
            .Concat(new[]
            {
                Path.Combine(Application.dataPath, "Study.meta"),
                Path.Combine(Application.dataPath, "Scenes", "AgenticXRStudy.unity"),
                Path.Combine(Application.dataPath, "Scenes", "AgenticXRStudy.unity.meta"),
            })
            .Where(File.Exists)
            .Where(path => new[] { ".json", ".mat", ".meta", ".unity" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        foreach (var path in paths)
        {
            var source = File.ReadAllText(path);
            var normalized = Regex.Replace(source, @"[ \t]+(?=\r?\n|$)", string.Empty);
            if (normalized != source) File.WriteAllText(path, normalized, new UTF8Encoding(false));
        }
    }

    private static void EnsureBuildSettings()
    {
        var guidText = AssetDatabase.AssetPathToGUID(ScenePath);
        if (string.IsNullOrEmpty(guidText) || guidText.All(character => character == '0'))
            throw new InvalidOperationException("The study scene must have an imported, non-zero GUID before build registration.");
        var scenes = EditorBuildSettings.scenes.Where(item => item.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(new GUID(guidText), true));
        EditorBuildSettings.scenes = scenes.ToArray();
        var registered = EditorBuildSettings.scenes.FirstOrDefault();
        if (registered == null || !registered.enabled || registered.path != ScenePath ||
            !string.Equals(registered.guid.ToString(), guidText, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The study scene was not registered first, enabled, and with its imported GUID.");
        Debug.Log("[AgenticXRStudySceneBuilder] build-scene-guid=" + registered.guid);
    }

    private static Material EnsureMaterial(string name, Color color)
    {
        var path = MaterialRoot + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        if (material.color != color) { material.color = color; EditorUtility.SetDirty(material); }
        return material;
    }

    private static GameObject EnsureRoot(string name)
    {
        var existing = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(item => item.name == name);
        return existing != null ? existing : new GameObject(name);
    }

    private static GameObject EnsureChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing.gameObject;
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static GameObject EnsurePrimitive(Transform parent, string name, PrimitiveType primitive)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing.gameObject;
        var child = GameObject.CreatePrimitive(primitive);
        child.name = name;
        child.transform.SetParent(parent, false);
        return child;
    }

    private static T EnsureComponent<T>(GameObject gameObject) where T : Component
    {
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
        var component = gameObject.GetComponent<T>();
        if (component == null) component = gameObject.AddComponent<T>();
        return component;
    }

    private static void RemoveComponents<T>(GameObject gameObject) where T : Component
    {
        foreach (var component in gameObject.GetComponents<T>()) UnityEngine.Object.DestroyImmediate(component);
    }

    private static void SetStableId(GameObject gameObject, string id)
    {
        gameObject.tag = "game";
        var stable = EnsureComponent<StableObjectId>(gameObject);
        var serialized = new SerializedObject(stable);
        var value = serialized.FindProperty("value") ?? throw new InvalidOperationException("StableObjectId.value is missing.");
        if (value.stringValue != id) { value.stringValue = id; serialized.ApplyModifiedPropertiesWithoutUndo(); }
        EditorUtility.SetDirty(stable);
    }

    private static string ReadStableId(StableObjectId stable)
    {
        var serialized = new SerializedObject(stable);
        return serialized.FindProperty("value").stringValue;
    }

    private static void SetAnchor(GameObject gameObject, string role, string description)
    {
        var anchor = EnsureComponent<AgenticInertAnchor>(gameObject);
        anchor.anchorRole = role;
        anchor.description = description;
        EditorUtility.SetDirty(anchor);
    }

    private static void SetTransform(GameObject gameObject, Vector3 position, Vector3 scale)
    {
        gameObject.transform.localPosition = position;
        gameObject.transform.localRotation = Quaternion.identity;
        gameObject.transform.localScale = scale;
    }

    private static void SetMaterial(GameObject gameObject, Material material)
    {
        var renderer = gameObject.GetComponent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != material) renderer.sharedMaterial = material;
    }

    private static Rigidbody EnsureBody(GameObject gameObject)
    {
        var body = EnsureComponent<Rigidbody>(gameObject);
        body.mass = 0.2f;
        body.linearDamping = 1f;
        body.angularDamping = 1f;
        return body;
    }

    private static GameObject ChildContaining(GameObject root, string token) =>
        root.GetComponentsInChildren<Transform>(true).Select(item => item.gameObject)
            .First(item => item != root && item.name.Contains(token));

    private static List<GameObject> ChildrenContaining(GameObject root, string token) =>
        root.GetComponentsInChildren<Transform>(true).Select(item => item.gameObject)
            .Where(item => item != root && item.name.Contains(token)).OrderBy(item => item.name).ToList();

    private static Rigidbody FindResetBody(GameObject root, string mode)
    {
        if (mode == "L3") return ChildContaining(root, "-marker").GetComponent<Rigidbody>();
        return null;
    }
}
