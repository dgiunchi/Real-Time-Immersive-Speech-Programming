using System;
using System.Collections.Generic;
using System.Linq;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.Quest;
using DreamCodeVR2.SceneContext;
using DreamCodeVR2.UI;
using TMPro;
using Ubiq.XR;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    // Researcher-only, world-space controls. Ubiq XRUIRaycaster supplies trigger pointer events directly.
    public class ExperimentalResearcherPanel : MonoBehaviour
    {
        public bool researcherMode;
        public KeyCode toggleKey = KeyCode.F5;
        public float controllerToggleHoldSeconds = 1f;
        public ExperimentConditionManager conditionManager;
        public AuthoringProtocolClient protocol;
        public QuestRuntimeState quest;
        public InteractionContextProvider interaction;
        public SceneContextTransmitter sceneContext;
        public DreamCodeVR2ResearcherControlClient researcherControl;

        private readonly List<InputDevice> leftControllers = new List<InputDevice>();
        private readonly Queue<string> lines = new Queue<string>();
        private GameObject panel;
        private GameObject advancedPanel;
        private TMP_Text status;
        private TMP_Text advancedStatus;
        private TMP_Text uiInputStatus;
        private TMP_Text log;
        private ResearcherPanelXrDiagnostics xrDiagnostics;
        private DynamicStoryTaskController dynamicStory;
        private float controllerToggleStarted = -1f;
        private bool controllerToggleConsumed;
        private CanvasGroup participantUiGroup;
        private bool savedParticipantUiInteractable;
        private bool savedParticipantUiBlocksRaycasts;
        private readonly List<UiRayState> uiRayStates = new List<UiRayState>();
        private readonly List<ResearcherUiRayLineVisual> createdUiRayLineVisuals = new List<ResearcherUiRayLineVisual>();
        private readonly List<SelectObjectRayState> gameplayRayStates = new List<SelectObjectRayState>();
        private readonly List<GraphicRaycasterState> legacyRaycasterStates = new List<GraphicRaycasterState>();
        private readonly Dictionary<ExperimentCondition, Button> conditionButtons = new Dictionary<ExperimentCondition, Button>();
        private float nextUiDiagnosticRefresh;

        private void Start()
        {
            Resolve();
            if (!CanShow()) return;
            Build();
            panel.SetActive(false);
            ResearcherUiInteractionState.Close(FindFirstObjectByType<MicrophoneCapture>());
        }

        private void OnDestroy()
        {
            SetParticipantUiRaycasts(true);
            RestoreResearcherUiPath();
            ResearcherUiInteractionState.Close(FindFirstObjectByType<MicrophoneCapture>());
        }

        private void Update()
        {
            if (panel && Input.GetKeyDown(toggleKey)) Toggle();
            if (panel) UpdateControllerToggle();
            if (panel && panel.activeSelf) { Refresh(); RefreshUiInputDiagnostic(); }
        }

        private bool CanShow() => researcherMode || (conditionManager && conditionManager.studyConfiguration && conditionManager.studyConfiguration.researcherMode) || Debug.isDebugBuild;

        private void Resolve()
        {
            if (!conditionManager) conditionManager = FindFirstObjectByType<ExperimentConditionManager>();
            if (!protocol) protocol = FindFirstObjectByType<AuthoringProtocolClient>();
            if (!quest) quest = FindFirstObjectByType<QuestRuntimeState>();
            if (!interaction) interaction = FindFirstObjectByType<InteractionContextProvider>();
            if (!sceneContext) sceneContext = FindFirstObjectByType<SceneContextTransmitter>();
            if (!dynamicStory) dynamicStory = FindFirstObjectByType<DynamicStoryTaskController>();
            if (!researcherControl)
            {
                researcherControl = GetComponent<DreamCodeVR2ResearcherControlClient>() ?? gameObject.AddComponent<DreamCodeVR2ResearcherControlClient>();
                researcherControl.configuration = conditionManager ? conditionManager.studyConfiguration : null;
            }
        }

        private void Build()
        {
            var canvasObject = new GameObject("ExperimentalResearcherPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(XRUICanvas));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 100;
            // The Ubiq controller ray reaches this readable world-space canvas from the side that
            // Unity's GraphicRaycaster otherwise treats as reversed. Keep visual orientation intact
            // and allow this researcher-only canvas to receive the Ubiq pointer ray.
            canvasObject.GetComponent<GraphicRaycaster>().ignoreReversedGraphics = false;
            canvasObject.transform.localScale = Vector3.one * .0015f;
            // XRUIRaycaster first intersects this root RectTransform before GraphicRaycaster
            // resolves a button. The Unity default (100x100) covered only a small part of
            // the 710x540 panel, causing hover/click to occur away from visible controls.
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(800f, 1400f);

            PositionCanvas(canvasObject.transform);

            panel = CreateBox("Panel", canvasObject.transform, new Color(.035f, .05f, .075f, .97f));
            panel.GetComponent<Image>().raycastTarget = false;
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(710, 540);
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 10;
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            AddText(panel.transform, "DreamCodeVR2 Researcher", 28, FontStyles.Bold, 38);
            AddHeader(panel.transform, "SESSION");
            AddButtons(panel.transform, new (string, Action)[] { ("START", StartServerSession), ("END", EndServerSession), ("RESET", ResetCurrentRun) }, 52);
            AddHeader(panel.transform, "CONDITION");
            var conditionButtonRow = AddButtons(panel.transform, new (string, Action)[] { ("C1 VOICE", () => Switch(ExperimentCondition.VoiceCommandBaseline)), ("C2 AUTHOR", () => Switch(ExperimentCondition.PlayerAuthoring)), ("C3 STORY", () => Switch(ExperimentCondition.DynamicStorytelling)) }, 52);
            conditionButtons[ExperimentCondition.VoiceCommandBaseline] = conditionButtonRow[0];
            conditionButtons[ExperimentCondition.PlayerAuthoring] = conditionButtonRow[1];
            conditionButtons[ExperimentCondition.DynamicStorytelling] = conditionButtonRow[2];
            AddHeader(panel.transform, "STATUS");
            status = AddText(panel.transform, "Session: NOT READY\nActive condition: --\nSelected condition: --\nPeer: NOT CONNECTED\nServer API: UNVERIFIED\nLogging: --", 17, FontStyles.Normal, 132);
            AddButtons(panel.transform, new (string, Action)[] { ("MARK TEST", DreamCodeVR2ClientLogger.MarkTest), ("ADVANCED", ToggleAdvanced) }, 44);

            advancedPanel = new GameObject("Advanced", typeof(RectTransform), typeof(VerticalLayoutGroup));
            advancedPanel.transform.SetParent(panel.transform, false);
            var advancedLayout = advancedPanel.GetComponent<VerticalLayoutGroup>();
            advancedLayout.padding = new RectOffset(8, 8, 8, 8);
            advancedLayout.spacing = 6;
            advancedLayout.childControlWidth = true;
            advancedLayout.childControlHeight = false;
            AddHeader(advancedPanel.transform, "ADVANCED / DEBUG");
            advancedStatus = AddText(advancedPanel.transform, string.Empty, 14, FontStyles.Normal, 95);
            uiInputStatus = AddText(advancedPanel.transform, "UI TARGET: NONE\nTRIGGER: UP", 13, FontStyles.Normal, 42);
            AddButtons(advancedPanel.transform, new (string, Action)[] { ("REFRESH CONTEXT", () => sceneContext?.SendSceneContextSnapshot("manual_refresh")) }, 38);
            AddHeader(advancedPanel.transform, "OBJECT SELECTION");
            AddButtons(advancedPanel.transform, new (string, Action)[] { ("DRAWER", () => Select("table_drawer_001")), ("KEY", () => Select("key_001")), ("LOCK", () => Select("lock_001")), ("DOOR", () => Select("door_001")) }, 38);
            AddHeader(advancedPanel.transform, "LOCAL TEST INJECTION");
            AddButtons(advancedPanel.transform, new (string, Action)[] { ("C1 OPEN", () => Predefined("OPEN")), ("C1 CLOSE", () => Predefined("CLOSE")), ("C2 GRABBABLE", () => Affordance("grabbable", true)), ("C3 NEXT TASK", SimulateNextTask) }, 38);
            log = AddText(advancedPanel.transform, string.Empty, 12, FontStyles.Normal, 72);
            advancedPanel.SetActive(false);

            xrDiagnostics = canvasObject.AddComponent<ResearcherPanelXrDiagnostics>();
            xrDiagnostics.panelRoot = panel.transform;
            RefreshConditionButtonVisuals();
            Note("researcher console ready");
        }

        private void PositionCanvas(Transform canvasTransform)
        {
            var camera = Camera.main;
            if (!camera) return;
            canvasTransform.position = camera.transform.position + camera.transform.forward * 1.15f - camera.transform.right * .42f - camera.transform.up * .04f;
            canvasTransform.rotation = Quaternion.LookRotation(canvasTransform.position - camera.transform.position);
        }

        private void Toggle()
        {
            panel.SetActive(!panel.activeSelf);
            var microphone = FindFirstObjectByType<MicrophoneCapture>();
            if (panel.activeSelf)
            {
                DreamCodeVR2ClientLogger.Event("researcher", "RESEARCHER_PANEL_OPENED");
                ResearcherUiInteractionState.Open(microphone);
                SetParticipantUiRaycasts(false);
                EnableResearcherUiPath();
                PositionCanvas(panel.transform.parent);
            }
            else
            {
                DreamCodeVR2ClientLogger.Event("researcher", "RESEARCHER_PANEL_CLOSED");
                ResearcherUiInteractionState.Close(microphone);
                SetParticipantUiRaycasts(true);
                RestoreResearcherUiPath();
            }
        }

        private void UpdateControllerToggle()
        {
            leftControllers.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand, leftControllers);
            var held = false;
            foreach (var controller in leftControllers)
            {
                if (controller.TryGetFeatureValue(CommonUsages.secondaryButton, out bool yPressed) && yPressed) { held = true; break; }
            }

            if (held)
            {
                if (controllerToggleStarted < 0)
                {
                    controllerToggleStarted = Time.unscaledTime;
                    DreamCodeVR2ClientLogger.Event("researcher", "RESEARCHER_PANEL_TOGGLE_HOLD_START", null, new { input = "left_secondaryButton" });
                }
                else if (!controllerToggleConsumed && Time.unscaledTime - controllerToggleStarted >= controllerToggleHoldSeconds)
                {
                    controllerToggleConsumed = true;
                    Toggle();
                }
            }
            else { controllerToggleStarted = -1f; controllerToggleConsumed = false; }
        }

        private void SetParticipantUiRaycasts(bool enabled)
        {
            var participantUi = FindFirstObjectByType<DreamCodeVRAuthoringUIController>();
            if (!participantUi) return;
            if (!participantUiGroup)
            {
                participantUiGroup = participantUi.GetComponent<CanvasGroup>() ?? participantUi.gameObject.AddComponent<CanvasGroup>();
                savedParticipantUiInteractable = participantUiGroup.interactable;
                savedParticipantUiBlocksRaycasts = participantUiGroup.blocksRaycasts;
            }
            participantUiGroup.interactable = enabled ? savedParticipantUiInteractable : false;
            participantUiGroup.blocksRaycasts = enabled ? savedParticipantUiBlocksRaycasts : false;
        }

        private void EnableResearcherUiPath()
        {
            uiRayStates.Clear();
            createdUiRayLineVisuals.Clear();
            gameplayRayStates.Clear();
            legacyRaycasterStates.Clear();

            var researcherCanvas = panel ? panel.transform.parent.GetComponent<Canvas>() : null;
            foreach (var raycaster in Resources.FindObjectsOfTypeAll<XRUIRaycaster>())
            {
                if (!IsSceneObject(raycaster)) continue;
                var hand = raycaster.GetComponentInParent<HandController>();
                uiRayStates.Add(new UiRayState(raycaster));
                raycaster.gameObject.SetActive(true);
                raycaster.enabled = true;
                raycaster.ignorePhysicsOcclusion = true;
                EnsureResearcherUiRayLine(raycaster);
                foreach (var cursor in raycaster.GetComponentsInChildren<XRUIRaycasterCursor>(true))
                {
                    uiRayStates.Add(new UiRayState(cursor));
                    cursor.gameObject.SetActive(true);
                    cursor.enabled = true;
                    if (cursor.renderer) cursor.renderer.enabled = true;
                }
                foreach (var line in raycaster.GetComponentsInChildren<XRUIRaycasterLine>(true))
                {
                    uiRayStates.Add(new UiRayState(line));
                    line.gameObject.SetActive(true);
                    line.enabled = true;
                    var lineRenderer = line.GetComponent<LineRenderer>();
                    if (lineRenderer) lineRenderer.enabled = true;
                }
                if (raycaster.GetComponentsInChildren<XRUIRaycasterCursor>(true).Length == 0 && raycaster.GetComponentsInChildren<XRUIRaycasterLine>(true).Length == 0)
                    ResearcherUiError("UI Ray '" + raycaster.name + "' has no XRUIRaycasterCursor or XRUIRaycasterLine visual.");
                if (!hand || !hand.gameObject.activeInHierarchy)
                    ResearcherUiError("HandController inactive or missing for UI Ray '" + raycaster.name + "'.");
            }

            foreach (var gameplayRay in Resources.FindObjectsOfTypeAll<global::SelectObjectRay>())
            {
                if (!IsSceneObject(gameplayRay)) continue;
                gameplayRayStates.Add(new SelectObjectRayState(gameplayRay));
                gameplayRay.SetResearcherUiSuppressed(true);
            }

            DisableActiveLegacyUiRaycasts(researcherCanvas);
            xrDiagnostics?.AttachAllRaycasters();
            VerifyResearcherUiPath(researcherCanvas);
            nextUiDiagnosticRefresh = 0f;
        }

        private void RestoreResearcherUiPath()
        {
            foreach (var state in gameplayRayStates) state.Restore();
            foreach (var state in legacyRaycasterStates) state.Restore();
            foreach (var state in uiRayStates) state.Restore();
            foreach (var visual in createdUiRayLineVisuals) visual.Destroy();
            gameplayRayStates.Clear();
            legacyRaycasterStates.Clear();
            uiRayStates.Clear();
            createdUiRayLineVisuals.Clear();
        }

        private void EnsureResearcherUiRayLine(XRUIRaycaster raycaster)
        {
            if (raycaster.GetComponent<XRUIRaycasterLine>()) return;
            var lineRenderer = raycaster.gameObject.AddComponent<LineRenderer>();
            var gameplayRay = FindFirstObjectByType<global::SelectObjectRay>();
            var gameplayLine = gameplayRay ? gameplayRay.GetComponent<LineRenderer>() : null;
            if (gameplayLine && gameplayLine.sharedMaterial) lineRenderer.sharedMaterial = gameplayLine.sharedMaterial;
            lineRenderer.useWorldSpace = true;
            lineRenderer.startWidth = .006f;
            lineRenderer.endWidth = .003f;
            lineRenderer.startColor = new Color(.15f, .85f, 1f, .95f);
            lineRenderer.endColor = new Color(.15f, .85f, 1f, .15f);
            var uiLine = raycaster.gameObject.AddComponent<XRUIRaycasterLine>();
            uiLine.showRayOnMiss = true;
            uiLine.missRayDistance = 2.0f;
            createdUiRayLineVisuals.Add(new ResearcherUiRayLineVisual(uiLine, lineRenderer));
            DreamCodeVR2ClientLogger.Event("researcher", "RESEARCHER_UI_RAY_VISUAL_CREATED", null, new { ray = raycaster.name, hand = HandName(raycaster) });
        }

        private void DisableActiveLegacyUiRaycasts(Canvas researcherCanvas)
        {
            foreach (var legacyCanvas in Resources.FindObjectsOfTypeAll<XRUICanvas>())
            {
                if (!IsSceneObject(legacyCanvas) || legacyCanvas.GetComponent<Canvas>() == researcherCanvas || !legacyCanvas.gameObject.activeInHierarchy) continue;
                if (legacyCanvas.name != "Canvas") continue;
                var graphicRaycaster = legacyCanvas.GetComponent<GraphicRaycaster>();
                if (!graphicRaycaster || !graphicRaycaster.enabled) continue;
                legacyRaycasterStates.Add(new GraphicRaycasterState(graphicRaycaster));
                graphicRaycaster.enabled = false;
                DreamCodeVR2ClientLogger.Event("researcher", "LEGACY_UI_RAYCAST_DISABLED", null, new { canvas = legacyCanvas.name, hierarchy = Hierarchy(legacyCanvas.transform) });
            }
        }

        private void VerifyResearcherUiPath(Canvas researcherCanvas)
        {
            if (!researcherCanvas || !researcherCanvas.gameObject.activeInHierarchy) { ResearcherUiError("Researcher Canvas inactive or missing."); return; }
            var camera = Camera.main;
            if (!camera) DreamCodeVR2ClientLogger.Error("researcher", "RESEARCHER_UI_CAMERA_ERROR", "Camera.main is null while opening the researcher panel.");
            else
            {
                researcherCanvas.worldCamera = camera;
                PositionCanvas(researcherCanvas.transform);
                DreamCodeVR2ClientLogger.Event("researcher", "RESEARCHER_UI_CAMERA_OK", null, new { camera = camera.name });
            }
            var graphicRaycaster = researcherCanvas.GetComponent<GraphicRaycaster>();
            if (!graphicRaycaster || !graphicRaycaster.enabled) ResearcherUiError("Researcher Canvas GraphicRaycaster inactive or missing.");
            if (!researcherCanvas.GetComponent<XRUICanvas>()) ResearcherUiError("Researcher Canvas XRUICanvas missing.");
            if (!XRUICanvas.Canvases.Any(canvas => canvas == researcherCanvas)) ResearcherUiError("Researcher Canvas is not registered in XRUICanvas.Canvases.");
            var eventSystems = Resources.FindObjectsOfTypeAll<EventSystem>().Where(IsSceneObject).ToArray();
            if (eventSystems.Length != 1 || EventSystem.current == null) ResearcherUiError("Expected exactly one active EventSystem; found " + eventSystems.Length + ", current=" + (EventSystem.current ? EventSystem.current.name : "null") + ".");
            if (uiRayStates.All(state => !(state.component is XRUIRaycaster))) ResearcherUiError("No installed Ubiq XRUIRaycaster was found.");
        }

        private void RefreshUiInputDiagnostic()
        {
            if (!uiInputStatus || Time.unscaledTime < nextUiDiagnosticRefresh) return;
            nextUiDiagnosticRefresh = Time.unscaledTime + .15f;
            XRUIRaycaster targetRaycaster = null;
            GameObject target = null;
            foreach (var raycaster in uiRayStates.Select(state => state.component).OfType<XRUIRaycaster>())
            {
                if (!raycaster || !raycaster.enabled) continue;
                var currentTarget = raycaster.CurrentTarget;
                if (currentTarget && currentTarget.transform.IsChildOf(panel.transform)) { targetRaycaster = raycaster; target = currentTarget; break; }
            }
            var hand = targetRaycaster ? targetRaycaster.GetComponentInParent<HandController>() : null;
            uiInputStatus.text = "UI TARGET: " + (target ? target.name : "NONE") + "\nTRIGGER: " + (hand && hand.TriggerState ? "DOWN" : "UP");
        }

        private static bool IsSceneObject(UnityEngine.Object value) => value && value is Component component && component.gameObject.scene.IsValid();
        private static string HandName(XRUIRaycaster raycaster) { var hand = raycaster.GetComponentInParent<HandController>(); return hand && hand.Left ? "left" : hand && hand.Right ? "right" : "unknown"; }
        private static string Hierarchy(Transform value) => value.parent ? Hierarchy(value.parent) + "/" + value.name : value.name;
        private static void ResearcherUiError(string detail) => DreamCodeVR2ClientLogger.Error("researcher", "RESEARCHER_UI_INPUT_ERROR", detail);

        private sealed class UiRayState
        {
            public readonly Behaviour component;
            private readonly bool componentEnabled;
            private readonly bool gameObjectActive;
            private readonly bool ignorePhysicsOcclusion;
            private readonly Renderer visualRenderer;
            private readonly bool visualRendererEnabled;
            public UiRayState(Behaviour component)
            {
                this.component = component; componentEnabled = component.enabled; gameObjectActive = component.gameObject.activeSelf;
                if (component is XRUIRaycaster raycaster) ignorePhysicsOcclusion = raycaster.ignorePhysicsOcclusion;
                if (component is XRUIRaycasterCursor cursor) visualRenderer = cursor.renderer;
                if (component is XRUIRaycasterLine line) visualRenderer = line.GetComponent<LineRenderer>();
                visualRendererEnabled = visualRenderer && visualRenderer.enabled;
            }
            public void Restore()
            {
                if (!component) return;
                component.gameObject.SetActive(gameObjectActive);
                component.enabled = componentEnabled;
                if (component is XRUIRaycaster raycaster) raycaster.ignorePhysicsOcclusion = ignorePhysicsOcclusion;
                if (visualRenderer) visualRenderer.enabled = visualRendererEnabled;
            }
        }

        private sealed class SelectObjectRayState
        {
            private readonly global::SelectObjectRay ray;
            private readonly bool suppressed;
            public SelectObjectRayState(global::SelectObjectRay ray) { this.ray = ray; suppressed = ray.ResearcherUiSuppressed; }
            public void Restore() { if (ray) ray.SetResearcherUiSuppressed(suppressed); }
        }

        private sealed class ResearcherUiRayLineVisual
        {
            private readonly XRUIRaycasterLine line;
            private readonly LineRenderer renderer;
            public ResearcherUiRayLineVisual(XRUIRaycasterLine line, LineRenderer renderer) { this.line = line; this.renderer = renderer; }
            public void Destroy()
            {
                if (line) UnityEngine.Object.Destroy(line);
                if (renderer) UnityEngine.Object.Destroy(renderer);
            }
        }

        private sealed class GraphicRaycasterState
        {
            private readonly GraphicRaycaster raycaster;
            private readonly bool enabled;
            public GraphicRaycasterState(GraphicRaycaster raycaster) { this.raycaster = raycaster; enabled = raycaster.enabled; }
            public void Restore() { if (raycaster) raycaster.enabled = enabled; }
        }

        private void ToggleAdvanced()
        {
            advancedPanel.SetActive(!advancedPanel.activeSelf);
            DreamCodeVR2ClientLogger.Event("researcher", advancedPanel.activeSelf ? "RESEARCHER_ADVANCED_OPENED" : "RESEARCHER_ADVANCED_CLOSED");
        }

        private void Switch(ExperimentCondition condition)
        {
            conditionManager?.PrepareResearcherCondition(condition);
            DreamCodeVR2ClientLogger.Correlate(null, null, condition);
            DreamCodeVR2ClientLogger.Event("session", "CONDITION_SELECTED", null, new { condition = DreamCodeVR2ResearcherControlClient.ServerCondition(condition) });
            Note("condition selected: " + condition + ". Press START.");
            RefreshConditionButtonVisuals();
            Refresh();
        }

        private void StartServerSession()
        {
            var peer = protocol?.CurrentPeerUuid;
            if (string.IsNullOrEmpty(peer)) { Note("NO PEER UUID — wait for Ubiq connection."); return; }
            var restarting = conditionManager.sessionStarted;
            conditionManager.InvalidateResearcherSessionReady();
            if (restarting) FindFirstObjectByType<DreamCodeVRSpeechStatusBridge>()?.CancelProcessing("Speech: Cancelled", "Session restart requested.");
            DreamCodeVR2ClientLogger.Event("session", restarting ? "SESSION_RESTART_REQUEST" : "SESSION_START_REQUEST", null, new { peer_uuid = peer, condition = DreamCodeVR2ResearcherControlClient.ServerCondition(conditionManager.selectedCondition) });
            researcherControl.Health(health =>
            {
                // The deployed health endpoint returns HTTP 200 but does not include the optional
                // `healthy` JSON field. A successful request is therefore the authoritative check.
                if (health == null || !researcherControl.IsReachable || !string.IsNullOrEmpty(health.error)) { Note("RESEARCHER API UNREACHABLE"); return; }
                Action<DreamCodeVR2ResearcherControlClient.Response> ready = response =>
                {
                    if (!string.IsNullOrEmpty(response.error) || string.IsNullOrEmpty(response.session_id)) { Note("SESSION START FAILED: " + response.error); return; }
                    conditionManager.ResetPlaythrough(); conditionManager.sessionId = response.session_id; conditionManager.StartSession(false);
                    DreamCodeVR2ClientLogger.Correlate(peer, response.session_id, conditionManager.condition);
                    DreamCodeVR2ClientLogger.Event("session", restarting ? "SESSION_RESTARTED" : "SESSION_STARTED_LOCAL", null, new { session_id = response.session_id });
                    sceneContext?.SendSceneContextSnapshot("session_start");
                    researcherControl.GetStatus(peer, serverStatus =>
                    {
                        if (!string.IsNullOrEmpty(serverStatus.error) || serverStatus.session_id != conditionManager.sessionId || serverStatus.condition != DreamCodeVR2ResearcherControlClient.ServerCondition(conditionManager.condition))
                        {
                            DreamCodeVR2ClientLogger.Warn("session", "SESSION_STATUS_MISMATCH", serverStatus.error);
                            FindFirstObjectByType<DreamCodeVRSpeechStatusBridge>()?.CancelProcessing("Speech: Cancelled", "Session status mismatch.");
                            conditionManager.CompleteSession(); Note("SESSION STATUS MISMATCH"); return;
                        }
                        conditionManager.SetResearcherSessionReady();
                        DreamCodeVR2ClientLogger.Event("session", "SESSION_READY", null, new { session_id = conditionManager.sessionId, condition = serverStatus.condition });
                        Note("SESSION READY: " + conditionManager.sessionId);
                    });
                };
                if (restarting) researcherControl.RestartSession(conditionManager.selectedCondition, peer, ready); else researcherControl.StartSession(conditionManager.selectedCondition, peer, ready);
            });
        }

        private void EndServerSession()
        {
            var peer = protocol?.CurrentPeerUuid; if (string.IsNullOrEmpty(peer)) return;
            DreamCodeVR2ClientLogger.Event("session", "SESSION_END_REQUEST", null, new { session_id = conditionManager?.sessionId, peer_uuid = peer });
            researcherControl.EndSession(peer, response =>
            {
                if (!string.IsNullOrEmpty(response.error) || !response.ended) { Note("SESSION END FAILED: " + response.error); return; }
                DreamCodeVR2ClientLogger.Event("session", "SESSION_ENDED", null, new { session_id = conditionManager?.sessionId });
                FindFirstObjectByType<DreamCodeVRSpeechStatusBridge>()?.CancelProcessing("Speech: Cancelled", "Session ended.");
                DreamCodeVR2ClientLogger.Instance?.Flush(); conditionManager?.ResetPlaythrough(); Note("NO ACTIVE SESSION");
            });
        }

        private void ResetCurrentRun()
        {
            var peer = protocol?.CurrentPeerUuid; if (string.IsNullOrEmpty(peer)) return;
            DreamCodeVR2ClientLogger.Event("session", "SESSION_RESET_REQUEST", null, new { session_id = conditionManager?.sessionId, peer_uuid = peer });
            researcherControl.ResetSession(peer, response =>
            {
                if (!string.IsNullOrEmpty(response.error) || !response.reset) { Note("SERVER RESET FAILED: " + response.error); return; }
                DreamCodeVR2ClientLogger.Event("session", "SESSION_RESET", null, new { session_id = conditionManager?.sessionId });
                FindFirstObjectByType<DreamCodeVRSpeechStatusBridge>()?.CancelProcessing("Speech: Cancelled", "Session reset.");
                conditionManager?.ResetPlaythrough(); Note("RUN RESET");
            });
        }

        private void Refresh()
        {
            var session = conditionManager?.IsResearcherSessionReady == true ? "READY" : "NOT READY";
            var peer = string.IsNullOrEmpty(protocol?.CurrentPeerUuid) ? "NOT CONNECTED" : "CONNECTED";
            var api = researcherControl?.IsReachable == true ? "REACHABLE" : "UNVERIFIED / ERROR";
            status.text = $"Session: {session}\nActive condition: {conditionManager?.condition}\nSelected condition: {conditionManager?.selectedCondition}\nPeer: {peer}\nServer API: {api}\nLogging: {(DreamCodeVR2ClientLogger.Instance?.IsActive == true ? "ACTIVE" : "ERROR")}";
            if (!advancedPanel.activeSelf) return;
            var selected = interaction?.GetCurrentSelectedEditableObject()?.objectId ?? "none";
            var pointed = interaction?.GetCurrentPointedEditableObject()?.objectId ?? "none";
            advancedStatus.text = $"Session ID: {conditionManager?.sessionId ?? "none"}\nPeer UUID: {protocol?.CurrentPeerUuid ?? "none"}\nTask: {quest?.GetCurrentTask()?.step.ToString() ?? "none"}\nSelected: {selected}\nPointed: {pointed}\nLog: {DreamCodeVR2ClientLogger.Instance?.CurrentLogFilename ?? "none"}\nWarnings/errors: {DreamCodeVR2ClientLogger.Instance?.WarningCount ?? 0}/{DreamCodeVR2ClientLogger.Instance?.ErrorCount ?? 0}";
        }

        private void Select(string id)
        {
            var obj = AuthoringActionExecutor.FindEditable(id);
            var selector = FindFirstObjectByType<global::SelectObjectRay>();
            if (obj && selector) selector.selectedObject = obj.gameObject;
            sceneContext?.SendSceneContextSnapshot("researcher_selection");
            Note(obj ? "selected " + id : "object unavailable: " + id);
        }

        private void Predefined(string command)
        {
            var result = protocol?.ExecuteLocalPredefined(new PredefinedVoiceCommand { commandId = "local-" + Guid.NewGuid(), targetObjectId = "table_drawer_001", command = command });
            Note(command + ": " + result?.message);
        }

        private void Affordance(string operation, bool value)
        {
            var result = protocol?.ExecuteLocalSceneApi(new SceneApiCall { method = "SceneAPI.setAffordance", action = new AuthoringAction { actionId = "local-" + Guid.NewGuid(), targetObjectId = "table_drawer_001", operation = operation, value = value ? "true" : "false" } });
            Note("affordance: " + result?.message);
        }

        private void SimulateNextTask()
        {
            if (!dynamicStory) { Note("dynamic controller unavailable"); return; }
            var ok = dynamicStory.ActivateNextTask(new NextTaskSpec { taskId = "local-next", playerInstruction = "Pick up the key.", requiredObjects = new[] { "key_001" }, successConditions = new[] { new RuntimeSuccessCondition { type = "OBJECT_GRABBED", object_id = "key_001" } } }, out var error);
            Note(ok ? "next task activated" : "next task rejected: " + error);
        }

        private void Note(string value)
        {
            lines.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + value);
            while (lines.Count > 5) lines.Dequeue();
            if (log) log.text = string.Join("\n", lines);
        }

        private void RefreshConditionButtonVisuals()
        {
            var hasSelection = conditionManager != null;
            var selected = conditionManager ? conditionManager.selectedCondition : default;
            foreach (var pair in conditionButtons)
            {
                if (!pair.Value) continue;
                var color = pair.Value.colors;
                var isSelected = hasSelection && pair.Key == selected;
                color.normalColor = isSelected ? new Color(.13f, .62f, .34f, 1f) : new Color(.12f, .28f, .45f, 1f);
                color.highlightedColor = isSelected ? new Color(.20f, .78f, .43f, 1f) : new Color(.18f, .42f, .67f, 1f);
                color.pressedColor = isSelected ? new Color(.08f, .42f, .22f, 1f) : new Color(.07f, .18f, .30f, 1f);
                color.selectedColor = color.highlightedColor;
                pair.Value.colors = color;
                if (pair.Value.targetGraphic) pair.Value.targetGraphic.color = color.normalColor;
            }
        }

        private static void AddHeader(Transform parent, string text)
        {
            var label = AddText(parent, text, 17, FontStyles.Bold, 25);
            label.color = new Color(.42f, .78f, 1f);
        }

        private static TMP_Text AddText(Transform parent, string text, float size, FontStyles style, float height)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style; tmp.color = Color.white; tmp.textWrappingMode = TextWrappingModes.Normal; tmp.raycastTarget = false;
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return tmp;
        }

        private static GameObject CreateBox(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false); go.GetComponent<Image>().color = color;
            return go;
        }

        private static List<Button> AddButtons(Transform parent, (string, Action)[] specs, float height)
        {
            var buttons = new List<Button>();
            var row = new GameObject("ButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 9; rowLayout.childForceExpandWidth = true; rowLayout.childForceExpandHeight = true;
            row.GetComponent<LayoutElement>().preferredHeight = height;
            foreach (var spec in specs)
            {
                var captured = spec;
                var buttonObject = CreateBox("ResearcherButton_" + captured.Item1, row.transform, new Color(.12f, .28f, .45f, 1));
                var button = buttonObject.AddComponent<Button>(); button.targetGraphic = buttonObject.GetComponent<Image>();
                button.onClick.AddListener(() => { ResearcherPanelXrDiagnostics.NotifyButtonDispatch(buttonObject); DreamCodeVR2ClientLogger.Event("researcher", "RESEARCHER_BUTTON_CLICK", null, new { button_id = captured.Item1 }); captured.Item2(); });
                var label = AddText(button.transform, captured.Item1, 16, FontStyles.Bold, height);
                label.alignment = TextAlignmentOptions.Center;
                var labelRect = label.rectTransform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = Vector2.zero; labelRect.offsetMax = Vector2.zero;
                buttons.Add(button);
            }
            return buttons;
        }
    }

    // Logs only state transitions from the installed Ubiq XR raycaster for targets inside this panel.
    public class ResearcherPanelXrDiagnostics : MonoBehaviour
    {
        public Transform panelRoot;
        private readonly List<XRUIRaycaster> raycasters = new List<XRUIRaycaster>();
        private static readonly Dictionary<GameObject, int> dispatchedButtons = new Dictionary<GameObject, int>();

        public static void NotifyButtonDispatch(GameObject button)
        {
            if (button) dispatchedButtons[button] = Time.frameCount;
        }

        private void Start()
        {
            AttachAllRaycasters();
        }

        public void AttachAllRaycasters()
        {
            foreach (var raycaster in Resources.FindObjectsOfTypeAll<XRUIRaycaster>())
            {
                if (!raycaster || !raycaster.gameObject.scene.IsValid() || raycasters.Contains(raycaster)) continue;
                raycasters.Add(raycaster);
                raycaster.PointerHoverEnter += target => Log("XR_UI_HOVER_ENTER", raycaster, target);
                raycaster.PointerHoverExit += target => Log("XR_UI_HOVER_EXIT", raycaster, target);
                raycaster.PointerDown += target => Log("XR_UI_POINTER_DOWN", raycaster, target);
                raycaster.PointerUp += target => Log("XR_UI_POINTER_UP", raycaster, target);
                raycaster.PointerClick += target => { Log("XR_UI_CLICK", raycaster, target); VerifyButtonDispatch(target); };
            }
            if (raycasters.Count == 0) DreamCodeVR2ClientLogger.Error("researcher", "RESEARCHER_UI_INPUT_ERROR", "No Ubiq XRUIRaycaster was found.");
        }

        private void Log(string eventName, XRUIRaycaster raycaster, GameObject target)
        {
            if (!target || !panelRoot || !target.transform.IsChildOf(panelRoot)) return;
            var hand = raycaster.GetComponentInParent<HandController>();
            var manager = FindFirstObjectByType<ExperimentConditionManager>();
            DreamCodeVR2ClientLogger.Event("xr_ui", eventName, null, new { hand = hand && hand.Left ? "left" : hand && hand.Right ? "right" : "unknown", target = target.name, trigger = hand && hand.TriggerState ? "DOWN" : "UP", panel_visible = panelRoot.gameObject.activeInHierarchy, session_ready = manager?.IsResearcherSessionReady });
        }

        private void Update()
        {
            if (!panelRoot || !panelRoot.gameObject.activeInHierarchy) return;
            foreach (var raycaster in raycasters)
            {
                if (!raycaster) continue;
                var hand = raycaster.GetComponentInParent<HandController>();
                var triggerDown = hand && hand.TriggerState;
                var key = raycaster.GetInstanceID();
                if (!triggerStates.TryGetValue(key, out var previous)) { triggerStates[key] = triggerDown; continue; }
                if (previous == triggerDown) continue;
                triggerStates[key] = triggerDown;
                var target = raycaster.CurrentTarget;
                DreamCodeVR2ClientLogger.Event("xr_ui", triggerDown ? "XR_UI_TRIGGER_DOWN" : "XR_UI_TRIGGER_UP", null, new { hand = hand && hand.Left ? "left" : hand && hand.Right ? "right" : "unknown", target = target ? target.name : "NONE", trigger = triggerDown ? "DOWN" : "UP" });
            }
        }

        private readonly Dictionary<int, bool> triggerStates = new Dictionary<int, bool>();

        private void VerifyButtonDispatch(GameObject target)
        {
            if (!target || !target.transform.IsChildOf(panelRoot) || !target.name.StartsWith("ResearcherButton_")) return;
            if (!dispatchedButtons.TryGetValue(target, out var dispatchFrame) || dispatchFrame != Time.frameCount)
                DreamCodeVR2ClientLogger.Error("researcher", "RESEARCHER_UI_BUTTON_DISPATCH_ERROR", "XR_UI_CLICK reached " + Hierarchy(target.transform) + " but Button.onClick did not dispatch.");
            else
                dispatchedButtons.Remove(target);
        }

        private static string Hierarchy(Transform value) => value.parent ? Hierarchy(value.parent) + "/" + value.name : value.name;
    }
}
