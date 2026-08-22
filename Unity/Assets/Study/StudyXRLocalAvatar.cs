using System;
using System.Collections.Generic;
using AgenticCache;
using Ubiq;
using Ubiq.Avatars;
using Ubiq.Spawning;
using Ubiq.XR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

namespace AgenticXR.Study
{
    /// <summary>
    /// Restores the local network avatar that the authored Ubiq Player expects and
    /// supplies visible, tracked controller proxies with selection rays.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class StudyXRLocalAvatar : MonoBehaviour
    {
        private const string AvatarCatalogueResource = "Study Avatar Catalogue";
        private const float RayLength = 8f;

        private sealed class HandVisual
        {
            public HandController controller;
            public LineRenderer line;
            public Renderer proxyRenderer;
            public GameObject target;
            public XRNode node;
            public IGraspable rayGrasped;
            public bool triggerWasPressed;
            public Transform windowTarget;
            public Transform draggedWindow;
            public float windowGrabDistance;
            public Vector3 windowGrabOffset;
        }

        private readonly List<HandVisual> hands = new List<HandVisual>();
        private CodeGenerationManager codeGenerationManager;
        private Material rayMaterial;
        private Material leftHandMaterial;
        private Material rightHandMaterial;
        private Material toolMaterial;
        private Material trayMaterial;
        private readonly Material[] l2PairMaterials = new Material[3];
        private float nextHandRefresh;
        private Camera headCamera;

        public void Initialize(CodeGenerationManager targetOwner)
        {
            codeGenerationManager = targetOwner;
            EnsureEventSystem();
            EnsureAvatarManager();
            RefreshHands();
        }

        private void Start()
        {
            if (codeGenerationManager == null)
                codeGenerationManager = FindFirstObjectByType<CodeGenerationManager>();
            EnsureEventSystem();
            EnsureAvatarManager();
            RefreshHands();
        }

        private void OnEnable()
        {
            Application.onBeforeRender += DriveTrackedHands;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= DriveTrackedHands;
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.transform.SetParent(transform, false);
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextHandRefresh)
            {
                nextHandRefresh = Time.unscaledTime + 1f;
                HideLocalAvatarBody();
                ApplyStudyObjectColours();
                PrepareL1BoxTools();
                PrepareL2BoxParts();
                PrepareDraggablePanels();
                RefreshHands();
            }

        }

        private void LateUpdate()
        {
            DriveTrackedHands();
            GameObject selected = null;
            for (var index = 0; index < hands.Count; index++)
            {
                var hand = hands[index];
                if (hand.controller == null || hand.line == null) continue;
                UpdateRay(hand);
                UpdateRayGrab(hand);
                if (selected == null && hand.target != null) selected = hand.target;
            }

            if (codeGenerationManager != null && codeGenerationManager.targetObject != selected)
            {
                codeGenerationManager.targetObject = selected;
                if (selected != null)
                    Debug.Log("[StudyXR] selected " + selected.name, selected);
            }
        }

        private void DriveTrackedHands()
        {
            if (headCamera == null)
            {
                var player = FindFirstObjectByType<XRPlayerController>();
                headCamera = player != null ? player.headCamera : Camera.main;
            }
            if (headCamera == null ||
                !TryGetNodePose(XRNode.Head, out var headPosition, out var headRotation)) return;

            // Input-device poses are expressed in tracking space. Reconstruct that
            // tracking origin from the already-correct HMD world pose, then apply
            // it to both controllers. This bypasses the legacy pose-driver origin
            // mismatch without replacing Ubiq's trigger/button input handling.
            var trackingRotation = headCamera.transform.rotation * Quaternion.Inverse(headRotation);
            var trackingOrigin = headCamera.transform.position - trackingRotation * headPosition;
            for (var index = 0; index < hands.Count; index++)
            {
                var hand = hands[index];
                if (hand.controller == null ||
                    !TryGetNodePose(hand.node, out var position, out var rotation)) continue;
                hand.controller.transform.SetPositionAndRotation(
                    trackingOrigin + trackingRotation * position,
                    trackingRotation * rotation);
            }
        }

        private static bool TryGetNodePose(XRNode node, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            var device = InputDevices.GetDeviceAtXRNode(node);
            var hasPosition = device.isValid &&
                device.TryGetFeatureValue(CommonUsages.devicePosition, out position);
            var hasRotation = device.isValid &&
                device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
            if (hasPosition && hasRotation) return true;

            position = InputTracking.GetLocalPosition(node);
            rotation = InputTracking.GetLocalRotation(node);
            return position != Vector3.zero || rotation != Quaternion.identity;
        }

        private void EnsureAvatarManager()
        {
            var manager = FindFirstObjectByType<AvatarManager>();
            var catalogue = Resources.Load<PrefabCatalogue>(AvatarCatalogueResource);
            if (catalogue == null || catalogue.prefabs == null || catalogue.prefabs.Count == 0)
            {
                Debug.LogError("[StudyXR] The local avatar catalogue resource is missing.", this);
                return;
            }

            if (manager == null)
            {
                var managerObject = new GameObject("Avatar Manager");
                managerObject.transform.SetParent(transform, false);
                manager = managerObject.AddComponent<AvatarManager>();
            }

            manager.avatarCatalogue = catalogue;
            // This study is locally operated in first person. Spawning the Ubiq
            // local avatar adds a second, offset head/hands rig in front of the
            // tracked XR Player and prevents direct manipulation. Networking,
            // controller tracking and rays do not require a local avatar model.
            manager.avatarPrefab = null;

            foreach (var input in FindObjectsByType<HeadAndHandsAvatarInput>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (input.gameObject.scene.IsValid()) input.enabled = true;
            }
        }

        private static void HideLocalAvatarBody()
        {
            // Hide only AvatarManager's local avatar instance. The separate
            // XR Player hierarchy must retain its controller trigger colliders.
            var manager = FindFirstObjectByType<AvatarManager>();
            var localAvatar = manager != null ? manager.LocalAvatar : null;
            if (localAvatar == null) return;
            foreach (var renderer in localAvatar.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (var collider in localAvatar.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

        private void ApplyStudyObjectColours()
        {
            if (toolMaterial == null)
                toolMaterial = CreateMaterial("Study Tool Material", new Color(1f, 0.72f, 0.05f), false);
            if (trayMaterial == null)
                trayMaterial = CreateMaterial("Study Tray Material", new Color(0.05f, 0.8f, 0.85f), false);

            foreach (var renderer in FindObjectsByType<Renderer>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var objectName = renderer.gameObject.name;
                if (objectName.IndexOf("-tool-", StringComparison.OrdinalIgnoreCase) >= 0)
                    renderer.sharedMaterial = toolMaterial;
                else if (objectName.IndexOf("-tray-", StringComparison.OrdinalIgnoreCase) >= 0)
                    renderer.sharedMaterial = trayMaterial;
            }
        }

        private void PrepareL1BoxTools()
        {
            foreach (var detector in FindObjectsByType<L1ToolStowedDetector>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (detector.tools == null) continue;
                foreach (var body in detector.tools)
                {
                    if (body == null || body.transform.Find("Study Box Tool Visual") != null) continue;

                    var originalRenderer = body.GetComponent<Renderer>();
                    if (originalRenderer != null) originalRenderer.enabled = false;
                    foreach (var collider in body.GetComponents<Collider>())
                        collider.enabled = false;
                    var boxCollider = body.gameObject.AddComponent<BoxCollider>();
                    boxCollider.size = Vector3.one;

                    var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    visual.name = "Study Box Tool Visual";
                    visual.transform.SetParent(body.transform, false);
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.identity;
                    visual.transform.localScale = Vector3.one;
                    var visualCollider = visual.GetComponent<Collider>();
                    if (visualCollider != null) Destroy(visualCollider);
                    visual.GetComponent<Renderer>().sharedMaterial = toolMaterial;

                    body.angularVelocity = Vector3.zero;
                    body.angularDamping = 8f;
                    body.linearDamping = 2f;
                    body.constraints |= RigidbodyConstraints.FreezeRotation;
                }

                if (detector.trays == null) continue;
                for (var index = 0; index < detector.trays.Length; index++)
                {
                    var original = detector.trays[index];
                    if (original == null || original.gameObject.name == "Study Tray Acceptance Volume") continue;
                    var volumeObject = new GameObject("Study Tray Acceptance Volume");
                    volumeObject.layer = LayerMask.NameToLayer("Ignore Raycast");
                    volumeObject.transform.SetParent(original.transform, false);
                    volumeObject.transform.localPosition = Vector3.zero;
                    volumeObject.transform.localRotation = Quaternion.identity;
                    volumeObject.transform.localScale = Vector3.one;
                    var volume = volumeObject.AddComponent<BoxCollider>();
                    volume.isTrigger = true;
                    // The authored cylinder is intentionally shallow. Extend the
                    // logical receptacle upward so a box resting on its top is
                    // considered inside without changing physical collisions.
                    volume.center = new Vector3(0f, 1.5f, 0f);
                    volume.size = new Vector3(1.25f, 4f, 1.25f);
                    detector.trays[index] = volume;
                }
            }
        }

        private void PrepareL2BoxParts()
        {
            foreach (var detector in FindObjectsByType<L2PartSeatedDetector>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (detector.parts == null || detector.matchingSockets == null) continue;
                var count = Mathf.Min(detector.parts.Length, detector.matchingSockets.Length);
                for (var index = 0; index < count; index++)
                {
                    var body = detector.parts[index];
                    var socket = detector.matchingSockets[index];
                    if (body == null || socket == null) continue;
                    var pairMaterial = L2PairMaterial(index);

                    if (body.transform.Find("Study Box Part Visual") == null)
                    {
                        var originalRenderer = body.GetComponent<Renderer>();
                        if (originalRenderer != null) originalRenderer.enabled = false;
                        foreach (var collider in body.GetComponents<Collider>())
                            collider.enabled = false;
                        var boxCollider = body.gameObject.AddComponent<BoxCollider>();
                        boxCollider.size = Vector3.one;

                        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        visual.name = "Study Box Part Visual";
                        visual.transform.SetParent(body.transform, false);
                        visual.transform.localPosition = Vector3.zero;
                        visual.transform.localRotation = Quaternion.identity;
                        visual.transform.localScale = Vector3.one;
                        var visualCollider = visual.GetComponent<Collider>();
                        if (visualCollider != null) Destroy(visualCollider);
                        visual.GetComponent<Renderer>().sharedMaterial = pairMaterial;

                        body.angularVelocity = Vector3.zero;
                        body.angularDamping = 8f;
                        body.linearDamping = 2f;
                        body.constraints |= RigidbodyConstraints.FreezeRotation;
                    }

                    var socketRenderer = socket.GetComponent<Renderer>();
                    if (socketRenderer != null) socketRenderer.sharedMaterial = pairMaterial;
                    if (socket.gameObject.name == "Study Socket Acceptance Volume") continue;

                    var volumeObject = new GameObject("Study Socket Acceptance Volume");
                    volumeObject.layer = LayerMask.NameToLayer("Ignore Raycast");
                    volumeObject.transform.SetParent(socket.transform, false);
                    volumeObject.transform.localPosition = Vector3.zero;
                    volumeObject.transform.localRotation = Quaternion.identity;
                    volumeObject.transform.localScale = Vector3.one;
                    var volume = volumeObject.AddComponent<BoxCollider>();
                    volume.isTrigger = true;
                    // Match L1's forgiving placement rule: a released box only
                    // needs to be visibly resting on the same-colour socket.
                    volume.center = new Vector3(0f, 1.5f, 0f);
                    volume.size = new Vector3(1.35f, 4f, 1.35f);
                    detector.matchingSockets[index] = volume;
                }
            }
        }

        private Material L2PairMaterial(int index)
        {
            index = Mathf.Clamp(index, 0, l2PairMaterials.Length - 1);
            if (l2PairMaterials[index] != null) return l2PairMaterials[index];
            var colours = new[]
            {
                new Color(1f, 0.72f, 0.05f),
                new Color(0.85f, 0.2f, 0.65f),
                new Color(0.18f, 0.78f, 0.3f),
            };
            l2PairMaterials[index] = CreateMaterial("Study L2 Pair Material " + (index + 1), colours[index], false);
            return l2PairMaterials[index];
        }

        private static void PrepareDraggablePanels()
        {
            foreach (var canvas in FindObjectsByType<Canvas>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.renderMode != RenderMode.WorldSpace || !IsDraggablePanel(canvas.gameObject.name)) continue;
                var rect = canvas.GetComponent<RectTransform>();
                if (rect == null) continue;
                var collider = canvas.GetComponent<BoxCollider>();
                if (collider == null) collider = canvas.gameObject.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.center = Vector3.zero;
                collider.size = new Vector3(rect.sizeDelta.x, rect.sizeDelta.y, 8f);
            }
        }

        private static bool IsDraggablePanel(string objectName)
        {
            return objectName == "AgenticXR Panel" || objectName == "Study Task Card" ||
                objectName == "Study Questionnaire Panel" || objectName == "Study Debug Launcher";
        }

        private void RefreshHands()
        {
            var controllers = FindObjectsByType<HandController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var controller in controllers)
            {
                if (!controller.gameObject.scene.IsValid() ||
                    controller.GetComponentInParent<XRPlayerController>() == null ||
                    controller.GetComponent<TrackedPoseDriver>() == null ||
                    HasHand(controller)) continue;
                EnsureGraspTrigger(controller);
                hands.Add(CreateHandVisual(controller));
            }
        }

        private static void EnsureGraspTrigger(HandController controller)
        {
            var grasper = controller.GetComponentInChildren<GraspableObjectGrasper>(true);
            if (grasper == null) return;
            if (grasper.controller == null) grasper.controller = controller;

            var trigger = grasper.GetComponent<SphereCollider>();
            if (trigger == null) trigger = grasper.gameObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.center = Vector3.zero;
            trigger.radius = 0.09f;

            var body = grasper.GetComponent<Rigidbody>();
            if (body == null) body = grasper.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        private bool HasHand(HandController controller)
        {
            for (var index = 0; index < hands.Count; index++)
                if (hands[index].controller == controller) return true;
            return false;
        }

        private HandVisual CreateHandVisual(HandController controller)
        {
            var poseDriver = controller.GetComponent<TrackedPoseDriver>();
            var left = controller.name.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (poseDriver != null && poseDriver.poseSource == TrackedPoseDriver.TrackedPose.LeftPose);
            var proxy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            proxy.name = left ? "Study Left Controller Visual" : "Study Right Controller Visual";
            proxy.transform.SetParent(controller.transform, false);
            proxy.transform.localPosition = new Vector3(0f, -0.015f, 0.055f);
            proxy.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            proxy.transform.localScale = new Vector3(0.035f, 0.065f, 0.035f);
            var proxyCollider = proxy.GetComponent<Collider>();
            if (proxyCollider != null) Destroy(proxyCollider);
            var proxyRenderer = proxy.GetComponent<Renderer>();
            proxyRenderer.sharedMaterial = HandMaterial(left);

            var rayObject = new GameObject(left ? "Study Left Selection Ray" : "Study Right Selection Ray");
            rayObject.transform.SetParent(controller.transform, false);
            var line = rayObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = 0.008f;
            line.endWidth = 0.004f;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = RayMaterial();
            line.startColor = Color.green;
            line.endColor = new Color(0f, 1f, 0f, 0.35f);

            return new HandVisual
            {
                controller = controller,
                line = line,
                proxyRenderer = proxyRenderer,
                node = left ? XRNode.LeftHand : XRNode.RightHand,
            };
        }

        private void UpdateRay(HandVisual hand)
        {
            var origin = hand.controller.transform.position + hand.controller.transform.forward * 0.035f;
            var direction = hand.controller.transform.forward;
            var end = origin + direction * RayLength;
            hand.target = null;
            hand.windowTarget = null;

            if (Physics.Raycast(origin, direction, out var hit, RayLength,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                end = hit.point;
                var canvas = hit.transform.GetComponentInParent<Canvas>();
                if (canvas != null && IsDraggablePanel(canvas.gameObject.name) &&
                    IsWindowDragHandle(canvas, hit.point))
                    hand.windowTarget = canvas.transform;
                else
                    hand.target = FindAuthorableTarget(hit.transform);
            }

            hand.line.SetPosition(0, origin);
            hand.line.SetPosition(1, end);
            var selected = hand.target != null || hand.windowTarget != null;
            hand.line.startColor = selected ? Color.red : Color.green;
            hand.line.endColor = selected
                ? new Color(1f, 0f, 0f, 0.4f)
                : new Color(0f, 1f, 0f, 0.35f);
        }

        private static bool IsWindowDragHandle(Canvas canvas, Vector3 worldHitPoint)
        {
            var rect = canvas.GetComponent<RectTransform>();
            if (rect == null) return false;
            var local = rect.InverseTransformPoint(worldHitPoint);
            // Reserve the top strip as a title bar. The rest remains available
            // to the existing XR button raycaster without accidental dragging.
            return local.y >= rect.rect.yMax - 85f;
        }

        private static void UpdateRayGrab(HandVisual hand)
        {
            if (hand.node != XRNode.RightHand || hand.controller == null) return;
            var pressed = hand.controller.TriggerState;
            if (pressed && !hand.triggerWasPressed && hand.windowTarget != null)
            {
                hand.draggedWindow = hand.windowTarget;
                var origin = hand.controller.transform.position + hand.controller.transform.forward * 0.035f;
                var hitPoint = hand.line.GetPosition(1);
                hand.windowGrabDistance = Mathf.Max(0.25f, Vector3.Distance(origin, hitPoint));
                hand.windowGrabOffset = hand.draggedWindow.position - hitPoint;
                // Panels are initially attached to the HMD only to establish a
                // comfortable spawn pose. Detach on first drag so they stay put.
                hand.draggedWindow.SetParent(null, true);
                hand.controller.Vibrate(0.18f, 0.04f);
            }
            else if (pressed && hand.draggedWindow != null)
            {
                var origin = hand.controller.transform.position + hand.controller.transform.forward * 0.035f;
                hand.draggedWindow.position = origin + hand.controller.transform.forward * hand.windowGrabDistance +
                    hand.windowGrabOffset;
            }
            else if (!pressed && hand.triggerWasPressed && hand.draggedWindow != null)
            {
                hand.draggedWindow = null;
            }
            else if (pressed && !hand.triggerWasPressed && hand.rayGrasped == null && hand.target != null)
            {
                foreach (var behaviour in hand.target.GetComponents<MonoBehaviour>())
                {
                    if (!(behaviour is IGraspable graspable)) continue;
                    hand.rayGrasped = graspable;
                    graspable.Grasp(hand.controller);
                    hand.controller.Vibrate(0.25f, 0.06f);
                    break;
                }
            }
            else if (!pressed && hand.triggerWasPressed && hand.rayGrasped != null)
            {
                hand.rayGrasped.Release(hand.controller);
                if (hand.rayGrasped is MonoBehaviour graspedBehaviour)
                {
                    var body = graspedBehaviour.GetComponent<Rigidbody>();
                    if (body != null)
                    {
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                        body.Sleep();
                    }
                }
                hand.rayGrasped = null;
            }
            hand.triggerWasPressed = pressed;
        }

        private static GameObject FindAuthorableTarget(Transform hit)
        {
            for (var current = hit; current != null; current = current.parent)
            {
                try
                {
                    if (current.CompareTag("game")) return current.gameObject;
                }
                catch (UnityException)
                {
                    return null;
                }
            }
            return null;
        }

        private Material RayMaterial()
        {
            if (rayMaterial == null)
                rayMaterial = CreateMaterial("Study XR Ray Material", Color.white, true);
            return rayMaterial;
        }

        private Material HandMaterial(bool left)
        {
            if (left)
            {
                if (leftHandMaterial == null)
                    leftHandMaterial = CreateMaterial("Study Left Hand Material", new Color(0.12f, 0.48f, 0.95f), false);
                return leftHandMaterial;
            }
            if (rightHandMaterial == null)
                rightHandMaterial = CreateMaterial("Study Right Hand Material", new Color(0.95f, 0.42f, 0.12f), false);
            return rightHandMaterial;
        }

        private static Material CreateMaterial(string name, Color color, bool unlit)
        {
            var shader = Shader.Find(unlit ? "Sprites/Default" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                name = name,
                color = color,
                hideFlags = HideFlags.HideAndDontSave,
            };
            return material;
        }

        private void OnDestroy()
        {
            DestroyMaterial(rayMaterial);
            DestroyMaterial(leftHandMaterial);
            DestroyMaterial(rightHandMaterial);
            DestroyMaterial(toolMaterial);
            DestroyMaterial(trayMaterial);
            for (var index = 0; index < l2PairMaterials.Length; index++)
                DestroyMaterial(l2PairMaterials[index]);
        }

        private static void DestroyMaterial(Material material)
        {
            if (material != null) Destroy(material);
        }
    }
}
