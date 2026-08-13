using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.Quest;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DreamCodeVR2.UI
{
    public static class DreamCodeVRAuthoringUIBootstrap
    {
        private const string SceneName = "DreamCodeVR2_EscapeRoom_Testbed";
        private const string AuthoringUiRootName = "DreamCodeVR_AuthoringUI";
        private const string QuestRuntimeRootName = "DreamCodeVR_QuestRuntime";
        private static readonly string[] LegacyUiNames =
        {
            "Menu",
            "Keyboard",
            "Join Room Panel",
            "Menu Panel",
            "Join Room"
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad()
        {
            if (SceneManager.GetActiveScene().name != SceneName)
            {
                return;
            }

            var existingUiController = Object.FindFirstObjectByType<DreamCodeVRAuthoringUIController>();
            var existingQuestScenarioController = Object.FindFirstObjectByType<QuestScenarioController>();
            var existingQuestPlannerClient = Object.FindFirstObjectByType<QuestPlannerClient>();
            var existingQuestPlanApplier = Object.FindFirstObjectByType<QuestPlanApplier>();
            var existingQuestRuntimeState = Object.FindFirstObjectByType<QuestRuntimeState>();

            DreamCodeVRAuthoringUIController uiController = existingUiController;
            if (!uiController)
            {
                var legacyMenuHidden = HideLegacyMenuUi();
                uiController = CreateAuthoringUi(legacyMenuHidden);
            }

            EnsureQuestRuntime(
                uiController,
                existingQuestScenarioController,
                existingQuestPlannerClient,
                existingQuestPlanApplier,
                existingQuestRuntimeState);
        }

        private static bool HideLegacyMenuUi()
        {
            var hiddenAny = false;
            var disabledRoot = new GameObject("Legacy_Menu_Disabled");

            foreach (var candidate in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!candidate || !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                foreach (var legacyName in LegacyUiNames)
                {
                    if (candidate.name != legacyName)
                    {
                        continue;
                    }

                    if (candidate == disabledRoot.transform || candidate.IsChildOf(disabledRoot.transform))
                    {
                        continue;
                    }

                    candidate.SetParent(disabledRoot.transform, true);
                    hiddenAny = true;
                    break;
                }
            }

            disabledRoot.SetActive(false);
            return hiddenAny;
        }

        private static DreamCodeVRAuthoringUIController CreateAuthoringUi(bool legacyMenuHidden)
        {
            var mainCamera = Camera.main;
            if (!mainCamera)
            {
                Debug.LogWarning("[AuthoringUI] Main Camera not found; skipping bootstrap.");
                return null;
            }

            var root = new GameObject(AuthoringUiRootName);
            var controller = root.AddComponent<DreamCodeVRAuthoringUIController>();
            var speechStatusBridge = root.AddComponent<DreamCodeVRSpeechStatusBridge>();
            controller.enabled = false;
            controller.interactionContextProvider = Object.FindFirstObjectByType<InteractionContextProvider>();
            controller.selectObjectRay = Object.FindFirstObjectByType<SelectObjectRay>();
            controller.speechStatusBridge = speechStatusBridge;
            controller.followTarget = mainCamera.transform;
            controller.distanceFromCamera = 1.42f;
            controller.horizontalOffset = 0.58f;
            controller.verticalOffset = 0.04f;
            controller.uiScale = 0.00118f;
            controller.followSmoothing = 7f;
            speechStatusBridge.microphoneCapture = Object.FindFirstObjectByType<MicrophoneCapture>();

            var canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(root.transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = mainCamera;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            scaler.referencePixelsPerUnit = 100f;

            var canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(560f, 560f);
            canvasRect.localScale = Vector3.one;

            var layoutRoot = CreateCard("LayoutRoot", canvas.transform, new Color(0f, 0f, 0f, 0f));
            Stretch(layoutRoot);
            var layoutGroup = layoutRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layoutGroup.padding = new RectOffset(0, 0, 0, 0);
            layoutGroup.spacing = 12f;
            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.childForceExpandHeight = false;

            var compactCard = CreateCard("CompactCard", layoutRoot, new Color(0.06f, 0.08f, 0.11f, 0.94f));
            SetPreferredWidth(compactCard.gameObject, 360f);
            ConfigureCardLayout(compactCard.gameObject, 18, 18, 14, 14, 6f);
            CreateText("CompactTitle", compactCard, "DreamCodeVR Authoring", 22f, FontStyles.Bold, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.compactScenarioText = CreateText("CompactScenarioText", compactCard, "Scenario: Fixed Scenario", 16f, FontStyles.Normal, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.compactPointedText = CreateText("CompactPointedText", compactCard, "Pointed: none", 16f, FontStyles.Normal, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.compactSelectedText = CreateText("CompactSelectedText", compactCard, "Selected: none", 16f, FontStyles.Normal, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.compactQuestText = CreateText("CompactQuestText", compactCard, "Current task: 0 / 0", 16f, FontStyles.Normal, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.compactObjectiveText = CreateText("CompactObjectiveText", compactCard, "No active task.", 15f, FontStyles.Normal, TextAlignmentOptions.Left, 2, TextOverflowModes.Ellipsis);
            controller.compactSpeechText = CreateText("CompactSpeechText", compactCard, "Speech: Initializing...", 16f, FontStyles.Normal, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.compactFeedbackText = CreateText("CompactFeedbackText", compactCard, "Feedback: Ready.", 15f, FontStyles.Normal, TextAlignmentOptions.Left, 2, TextOverflowModes.Ellipsis);

            var inspectCard = CreateCard("InspectCard", layoutRoot, new Color(0.08f, 0.10f, 0.14f, 0.96f));
            SetPreferredWidth(inspectCard.gameObject, 380f);
            ConfigureCardLayout(inspectCard.gameObject, 18, 18, 14, 14, 5f);
            controller.inspectCardGroup = inspectCard.gameObject.AddComponent<CanvasGroup>();
            CreateText("InspectHeader", inspectCard, "Inspect", 18f, FontStyles.Bold, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.objectNameText = CreateText("ObjectNameText", inspectCard, "No object in focus.", 18f, FontStyles.Bold, TextAlignmentOptions.Left, 2, TextOverflowModes.Ellipsis);
            controller.objectIdText = CreateText("ObjectIdText", inspectCard, "none", 13f, FontStyles.Italic, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.objectDescriptionText = CreateText("ObjectDescriptionText", inspectCard, "Point at an interactive object to inspect its metadata.", 15f, FontStyles.Normal, TextAlignmentOptions.TopLeft, 4, TextOverflowModes.Ellipsis);

            var speechCard = CreateCard("SpeechCard", layoutRoot, new Color(0.07f, 0.09f, 0.13f, 0.96f));
            SetPreferredWidth(speechCard.gameObject, 420f);
            ConfigureCardLayout(speechCard.gameObject, 18, 18, 14, 14, 5f);
            controller.speechCardGroup = speechCard.gameObject.AddComponent<CanvasGroup>();
            CreateText("SpeechHeader", speechCard, "Speech", 18f, FontStyles.Bold, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.transcriptText = CreateText("TranscriptText", speechCard, "Waiting for speech...", 15f, FontStyles.Normal, TextAlignmentOptions.TopLeft, 4, TextOverflowModes.Ellipsis);
            controller.intentText = CreateText("IntentText", speechCard, "Intent: waiting for server-side interpretation...", 14f, FontStyles.Normal, TextAlignmentOptions.TopLeft, 3, TextOverflowModes.Ellipsis);

            var planCard = CreateCard("PlanCard", layoutRoot, new Color(0.08f, 0.11f, 0.16f, 0.97f));
            SetPreferredWidth(planCard.gameObject, 430f);
            ConfigureCardLayout(planCard.gameObject, 18, 18, 14, 14, 5f);
            controller.planCardGroup = planCard.gameObject.AddComponent<CanvasGroup>();
            CreateText("PlanHeader", planCard, "Quest Preview", 18f, FontStyles.Bold, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.planTitleText = CreateText("PlanTitleText", planCard, "Quest Preview", 16f, FontStyles.Bold, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.planStepsText = CreateText("PlanStepsText", planCard, "No pending plan.", 14f, FontStyles.Normal, TextAlignmentOptions.TopLeft, 6, TextOverflowModes.Ellipsis);

            var feedbackCard = CreateCard("FeedbackCard", layoutRoot, new Color(0.10f, 0.13f, 0.18f, 0.97f));
            SetPreferredWidth(feedbackCard.gameObject, 360f);
            ConfigureCardLayout(feedbackCard.gameObject, 18, 18, 12, 12, 4f);
            controller.feedbackCardGroup = feedbackCard.gameObject.AddComponent<CanvasGroup>();
            CreateText("FeedbackHeader", feedbackCard, "Feedback", 16f, FontStyles.Bold, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.statusText = CreateText("StatusText", feedbackCard, "Status: Ready.", 14f, FontStyles.Normal, TextAlignmentOptions.Left, 2, TextOverflowModes.Ellipsis);
            controller.undoHintText = CreateText("UndoHintText", feedbackCard, "Undo: No undoable action yet.", 14f, FontStyles.Normal, TextAlignmentOptions.Left, 2, TextOverflowModes.Ellipsis);

            var proposalCard = CreateCard("ProposalCard", layoutRoot, new Color(0.12f, 0.16f, 0.22f, 0.98f));
            SetPreferredWidth(proposalCard.gameObject, 430f);
            ConfigureCardLayout(proposalCard.gameObject, 18, 18, 12, 12, 5f);
            controller.proposalCardGroup = proposalCard.gameObject.AddComponent<CanvasGroup>();
            CreateText("ProposalHeader", proposalCard, "Change proposal", 17f, FontStyles.Bold, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.proposalText = CreateText("ProposalText", proposalCard, string.Empty, 15f, FontStyles.Normal, TextAlignmentOptions.TopLeft, 3, TextOverflowModes.Ellipsis);
            controller.proposalTargetText = CreateText("ProposalTargetText", proposalCard, string.Empty, 14f, FontStyles.Italic, TextAlignmentOptions.Left, 1, TextOverflowModes.Ellipsis);
            controller.proposalReasonText = CreateText("ProposalReasonText", proposalCard, string.Empty, 13f, FontStyles.Normal, TextAlignmentOptions.TopLeft, 2, TextOverflowModes.Ellipsis);
            proposalCard.gameObject.SetActive(false);

            controller.enabled = true;
            return controller;
        }

        private static void EnsureQuestRuntime(
            DreamCodeVRAuthoringUIController uiController,
            QuestScenarioController existingQuestScenarioController,
            QuestPlannerClient existingQuestPlannerClient,
            QuestPlanApplier existingQuestPlanApplier,
            QuestRuntimeState existingQuestRuntimeState)
        {
            var questRuntimeRoot = GameObject.Find(QuestRuntimeRootName);
            var createdRuntimeRoot = false;
            if (!questRuntimeRoot)
            {
                questRuntimeRoot = new GameObject(QuestRuntimeRootName);
                createdRuntimeRoot = true;
                Debug.Log("[QuestRuntimeBootstrap] Created DreamCodeVR_QuestRuntime");
            }

            var questScenarioController = existingQuestScenarioController;
            if (!questScenarioController)
            {
                questScenarioController = questRuntimeRoot.AddComponent<QuestScenarioController>();
            }
            else
            {
                Debug.Log("[QuestRuntimeBootstrap] Found existing QuestScenarioController");
                questRuntimeRoot = questScenarioController.gameObject;
            }

            var questPlannerClient = existingQuestPlannerClient;
            if (!questPlannerClient)
            {
                questPlannerClient = questRuntimeRoot.AddComponent<QuestPlannerClient>();
                Debug.Log("[QuestRuntimeBootstrap] Attached QuestPlannerClient");
            }

            var questPlanApplier = existingQuestPlanApplier;
            if (!questPlanApplier)
            {
                questPlanApplier = questRuntimeRoot.AddComponent<QuestPlanApplier>();
            }

            var questRuntimeState = existingQuestRuntimeState;
            if (!questRuntimeState)
            {
                questRuntimeState = questRuntimeRoot.AddComponent<QuestRuntimeState>();
            }

            var runtimeCreatableObjectCatalog = Object.FindFirstObjectByType<RuntimeCreatableObjectCatalog>();
            if (!runtimeCreatableObjectCatalog)
            {
                runtimeCreatableObjectCatalog = questRuntimeRoot.AddComponent<RuntimeCreatableObjectCatalog>();
            }

            if (uiController)
            {
                questPlanApplier.authoringUiController = uiController;
                questScenarioController.authoringUiController = uiController;
            }

            questPlanApplier.runtimeCreatableObjectCatalog = runtimeCreatableObjectCatalog;
            questScenarioController.questPlanApplier = questPlanApplier;
            questScenarioController.questPlannerClient = questPlannerClient;
            questScenarioController.questRuntimeState = questRuntimeState;

            if (createdRuntimeRoot || uiController || questScenarioController.questPlannerClient == questPlannerClient)
            {
                Debug.Log("[QuestRuntimeBootstrap] Wired QuestPlannerClient into QuestScenarioController");
            }
        }

        private static RectTransform CreateCard(string name, Transform parent, Color backgroundColor)
        {
            var card = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(UnityEngine.UI.Outline), typeof(LayoutElement), typeof(ContentSizeFitter));
            card.transform.SetParent(parent, false);

            var image = card.GetComponent<Image>();
            image.color = backgroundColor;

            var outline = card.GetComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new Color(0.38f, 0.48f, 0.60f, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            var fitter = card.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return card.GetComponent<RectTransform>();
        }

        private static void ConfigureCardLayout(GameObject card, int left, int right, int top, int bottom, float spacing)
        {
            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(left, right, top, bottom);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        private static void SetPreferredWidth(GameObject go, float preferredWidth)
        {
            var layoutElement = go.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = preferredWidth;
            layoutElement.minWidth = preferredWidth;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            string text,
            float fontSize,
            FontStyles fontStyle,
            TextAlignmentOptions alignment,
            int maxVisibleLines,
            TextOverflowModes overflowMode)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            var tmp = textObject.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = fontStyle;
            tmp.color = new Color(0.96f, 0.98f, 1f, 1f);
            tmp.alignment = alignment;
            tmp.textWrappingMode = maxVisibleLines > 1 ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = overflowMode;
            tmp.maxVisibleLines = maxVisibleLines;
            tmp.margin = new Vector4(0f, 0f, 4f, 0f);

            var layoutElement = textObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = EstimateHeight(fontSize, maxVisibleLines, fontStyle);
            layoutElement.minHeight = layoutElement.preferredHeight;

            return tmp;
        }

        private static float EstimateHeight(float fontSize, int lines, FontStyles fontStyle)
        {
            var boldBonus = fontStyle == FontStyles.Bold ? 4f : 0f;
            return (fontSize + 8f) * Mathf.Max(1, lines) + boldBonus;
        }

        private static void Stretch(RectTransform rectTransform)
        {
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(560f, 0f);
        }
    }
}
