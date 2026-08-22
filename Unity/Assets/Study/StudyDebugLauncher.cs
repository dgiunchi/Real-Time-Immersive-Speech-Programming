using System;
using AgenticCache;
using Ubiq.Rooms;
using Ubiq.XR;
using UnityEngine;
using UnityEngine.UI;

namespace AgenticXR.Study
{
    /// <summary>
    /// Researcher-only live acceptance launcher. Node must acknowledge the dry-run
    /// assignment before the matching authored task root is activated in Unity.
    /// </summary>
    public sealed class StudyDebugLauncher : MonoBehaviour
    {
        private const string FullCondition = "agenticxr_verification";
        private const string NoVerificationCondition = "agenticxr_no_verification";
        private const string BaselineCondition = "baseline";
        private const string RuntimeRoomId = "6765c52b-3ad6-4fb0-9030-2c9a05dc4731";

        private StudyTrialDirector director;
        private CacheExchangeManager exchange;
        private AgenticXRConsentPanel consentPanel;
        private RoomClient roomClient;
        private Canvas canvas;
        private Text summaryText;
        private Text statusText;
        private Text comparisonLabel;
        private Button comparisonButton;
        private Button candidateOneButton;
        private Button candidateThreeButton;
        private readonly Button[] modeButtons = new Button[5];
        private Button variantAButton;
        private Button variantBButton;
        private Button fullButton;

        private string selectedMode = "L4";
        private string selectedVariant = "A";
        private bool comparisonSelected;
        private int candidateTarget = 1;
        private string pendingCorrelationId;
        private StudyTrialDirector.TrialAssignment pendingAssignment;
        private float requestStartedAt;
        private bool initialized;

        public void Initialize(StudyTrialDirector owner, CacheExchangeManager transport,
            AgenticXRConsentPanel runtimePanel)
        {
            if (initialized) return;
            director = owner;
            exchange = transport;
            consentPanel = runtimePanel;
            roomClient = exchange.GetComponent<RoomClient>();
            if (director == null || exchange == null || consentPanel == null)
                throw new InvalidOperationException("Study debug launcher references are incomplete.");

            BuildPanel();
            exchange.DebugStudyTrialConfigured += OnDebugStudyTrialConfigured;
            if (director.taskCardPresenter != null)
                director.taskCardPresenter.Dismissed += OnTaskCardDismissed;
            consentPanel.SetPanelVisible(false);
            RefreshSelection();
            initialized = true;
        }

        private void OnDestroy()
        {
            if (exchange != null) exchange.DebugStudyTrialConfigured -= OnDebugStudyTrialConfigured;
            if (director != null && director.taskCardPresenter != null)
                director.taskCardPresenter.Dismissed -= OnTaskCardDismissed;
        }

        private void OnTaskCardDismissed()
        {
            consentPanel?.SetPanelVisible(true);
            var assignment = director != null ? director.ActiveAssignment : null;
            if (assignment == null || consentPanel == null) return;
            if (assignment.interactionMode == "L1")
                consentPanel.ShowStatus("L1 task", "Grab a YELLOW TOOL with the RIGHT TRIGGER, place it inside a CYAN TRAY, then release it.");
            else if (assignment.interactionMode == "L2")
                consentPanel.ShowStatus("L2 task", "Grab a COLOURED BOX with the RIGHT TRIGGER, place it on the SAME-COLOUR SOCKET, then release it.");
        }

        private void Update()
        {
            if (!initialized) return;
            if (Input.GetKeyDown(KeyCode.F1) && string.IsNullOrEmpty(pendingCorrelationId))
            {
                consentPanel.SetPanelVisible(false);
                canvas.gameObject.SetActive(true);
                RefreshSelection();
            }
            if (!string.IsNullOrEmpty(pendingCorrelationId) && Time.unscaledTime - requestStartedAt > 10f)
            {
                pendingCorrelationId = null;
                pendingAssignment = null;
                statusText.text = "Server did not acknowledge the trial within 10 seconds. Check localhost:8009 and retry.";
            }
        }

        private void SelectMode(string mode)
        {
            if (!string.IsNullOrEmpty(pendingCorrelationId)) return;
            selectedMode = mode;
            comparisonSelected = false;
            candidateTarget = 1;
            RefreshSelection();
        }

        private void SelectVariant(string variant)
        {
            if (!string.IsNullOrEmpty(pendingCorrelationId)) return;
            selectedVariant = variant;
            RefreshSelection();
        }

        private void SelectComparison(bool comparison)
        {
            if (!string.IsNullOrEmpty(pendingCorrelationId)) return;
            comparisonSelected = comparison;
            RefreshSelection();
        }

        private void SelectCandidateCount(int count)
        {
            if (!string.IsNullOrEmpty(pendingCorrelationId)) return;
            candidateTarget = count;
            RefreshSelection();
        }

        private void StartSelectedTrial()
        {
            if (!string.IsNullOrEmpty(pendingCorrelationId)) return;
            if (roomClient == null || !roomClient.JoinedRoom ||
                !string.Equals(roomClient.Room.UUID, RuntimeRoomId, StringComparison.OrdinalIgnoreCase))
            {
                statusText.text = "Still joining the AgenticXR room on localhost:8009. Wait a moment and retry.";
                return;
            }
            var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
            var condition = SelectedCondition();
            var candidate = condition == FullCondition && (selectedMode == "L4" || selectedMode == "L5")
                ? candidateTarget : 0;
            pendingAssignment = new StudyTrialDirector.TrialAssignment
            {
                participantId = "DEBUG",
                sessionId = "debug-" + selectedMode + "-" + stamp,
                trialId = "debug-" + selectedMode + "-" + selectedVariant + "-" + stamp,
                taskId = TaskIdFor(selectedMode),
                interactionMode = selectedMode,
                taskVariant = selectedVariant,
                condition = condition,
                candidateTarget = candidate,
            };
            pendingCorrelationId = "debug-config-" + stamp;
            var payload = new DebugTrialRequest
            {
                participantId = pendingAssignment.participantId,
                sessionId = pendingAssignment.sessionId,
                trialId = pendingAssignment.trialId,
                taskId = pendingAssignment.taskId,
                interactionMode = pendingAssignment.interactionMode,
                taskVariant = pendingAssignment.taskVariant,
                condition = pendingAssignment.condition,
                conditionAlias = ConditionAlias(condition),
                candidateTarget = candidate,
            };

            // Ordered messages on channel 100: remove old artifacts, then register.
            exchange.ResetTrialState();
            if (!exchange.RequestDebugStudyTrial(JsonUtility.ToJson(payload), pendingCorrelationId))
            {
                pendingCorrelationId = null;
                pendingAssignment = null;
                statusText.text = "XR network transport is not ready yet. Wait for the room connection and retry.";
                return;
            }
            requestStartedAt = Time.unscaledTime;
            statusText.text = "Registering the selected trial with the server...";
        }

        private void OnDebugStudyTrialConfigured(string correlationId, string status, string detail)
        {
            if (string.IsNullOrEmpty(pendingCorrelationId) || correlationId != pendingCorrelationId) return;
            pendingCorrelationId = null;
            if (!string.Equals(status, "configured", StringComparison.Ordinal))
            {
                pendingAssignment = null;
                statusText.text = "Server rejected the trial: " + (detail ?? "unknown error");
                return;
            }

            try
            {
                director.trainingRequired = false;
                director.ApplyServerAssignment(pendingAssignment);
                pendingAssignment = null;
                canvas.gameObject.SetActive(false);
                // The task card owns the foreground until the participant presses
                // Begin. Its Dismissed event reveals the runtime consent panel.
                consentPanel.SetPanelVisible(false);
            }
            catch (Exception error)
            {
                pendingAssignment = null;
                statusText.text = "Unity could not activate the task: " + error.Message;
                canvas.gameObject.SetActive(true);
                consentPanel.SetPanelVisible(false);
            }
        }

        private string SelectedCondition()
        {
            if (!comparisonSelected) return FullCondition;
            return selectedMode == "L1" || selectedMode == "L2"
                ? NoVerificationCondition : BaselineCondition;
        }

        private void RefreshSelection()
        {
            for (var index = 0; index < modeButtons.Length; index++)
                SetSelected(modeButtons[index], selectedMode == "L" + (index + 1));
            SetSelected(variantAButton, selectedVariant == "A");
            SetSelected(variantBButton, selectedVariant == "B");
            SetSelected(fullButton, !comparisonSelected);
            SetSelected(comparisonButton, comparisonSelected);
            comparisonLabel.text = selectedMode == "L1" || selectedMode == "L2" ? "No dry-run" : "Baseline";

            var h4Choice = !comparisonSelected && (selectedMode == "L4" || selectedMode == "L5");
            candidateOneButton.interactable = h4Choice;
            candidateThreeButton.interactable = h4Choice;
            SetSelected(candidateOneButton, h4Choice && candidateTarget == 1);
            SetSelected(candidateThreeButton, h4Choice && candidateTarget == 3);

            var condition = SelectedCondition();
            var pipeline = condition == BaselineCondition
                ? "DreamCodeVR direct apply"
                : condition == NoVerificationCondition
                    ? "AgenticXR, Verification Space bypassed"
                    : "AgenticXR with verification";
            var candidate = h4Choice ? ", candidates N=" + candidateTarget : string.Empty;
            summaryText.text = selectedMode + " / Variant " + selectedVariant + "\n" + pipeline + candidate +
                "\n\nDry-run acceptance launcher only; randomized participant assignment remains server-authoritative.";
            statusText.text = selectedMode == "L1" || selectedMode == "L2"
                ? "Start, dismiss the task card, then interact with/approach the station. Speech does not start L1/L2."
                : "Start, dismiss the task card, point at an authorable object, then hold the left trigger to speak.";
        }

        private void BuildPanel()
        {
            var root = new GameObject("Study Debug Launcher", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(XRUICanvas), typeof(Image));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1000, 720);
            root.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.065f, 0.97f);
            if (Camera.main != null)
            {
                root.transform.SetParent(Camera.main.transform, false);
                root.transform.localPosition = new Vector3(0f, -0.05f, 1.25f);
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one * 0.0011f;
            }

            var title = CreateText(root.transform, "Title", new Vector2(0, 305), new Vector2(930, 60), 36);
            title.text = "AgenticXR Study - Acceptance Trial Setup";
            for (var index = 0; index < 5; index++)
            {
                var mode = "L" + (index + 1);
                modeButtons[index] = CreateButton(root.transform, mode, new Vector2(-360 + index * 180, 230),
                    new Vector2(145, 60), () => SelectMode(mode));
            }

            variantAButton = CreateButton(root.transform, "Variant A", new Vector2(-115, 150), new Vector2(200, 55),
                () => SelectVariant("A"));
            variantBButton = CreateButton(root.transform, "Variant B", new Vector2(115, 150), new Vector2(200, 55),
                () => SelectVariant("B"));
            fullButton = CreateButton(root.transform, "Full AgenticXR", new Vector2(-140, 80), new Vector2(250, 55),
                () => SelectComparison(false));
            comparisonButton = CreateButton(root.transform, "Comparison", new Vector2(140, 80), new Vector2(250, 55),
                () => SelectComparison(true));
            comparisonLabel = comparisonButton.GetComponentInChildren<Text>();
            candidateOneButton = CreateButton(root.transform, "N=1", new Vector2(-90, 10), new Vector2(150, 52),
                () => SelectCandidateCount(1));
            candidateThreeButton = CreateButton(root.transform, "N=3", new Vector2(90, 10), new Vector2(150, 52),
                () => SelectCandidateCount(3));
            summaryText = CreateText(root.transform, "Summary", new Vector2(0, -105), new Vector2(900, 170), 25);
            statusText = CreateText(root.transform, "Status", new Vector2(0, -225), new Vector2(900, 80), 22);
            statusText.color = new Color(0.8f, 0.9f, 1f);
            CreateButton(root.transform, "Start Trial", new Vector2(0, -315), new Vector2(280, 70), StartSelectedTrial,
                new Color(0.12f, 0.55f, 0.3f));
        }

        private static Button CreateButton(Transform parent, string label, Vector2 position, Vector2 size,
            UnityEngine.Events.UnityAction action, Color? color = null)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = color ?? new Color(0.16f, 0.22f, 0.34f, 1f);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(action);
            var text = CreateText(go.transform, "Label", Vector2.zero, size, 24);
            text.text = label;
            return button;
        }

        private static Text CreateText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            return text;
        }

        private static void SetSelected(Button button, bool selected)
        {
            if (button == null) return;
            button.GetComponent<Image>().color = selected
                ? new Color(0.12f, 0.55f, 0.72f, 1f)
                : new Color(0.16f, 0.22f, 0.34f, button.interactable ? 1f : 0.45f);
        }

        private static string TaskIdFor(string mode)
        {
            switch (mode)
            {
                case "L1": return "L1-proactive";
                case "L2": return "L2-context";
                case "L3": return "L3-clarify";
                case "L4": return "L4-confirm";
                case "L5": return "L5-converse";
                default: throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown interaction mode.");
            }
        }

        private static string ConditionAlias(string condition)
        {
            if (condition == NoVerificationCondition) return "noDryRun";
            if (condition == BaselineCondition) return "baseline";
            return "full";
        }

        [Serializable]
        private sealed class DebugTrialRequest
        {
            public string participantId;
            public string sessionId;
            public string trialId;
            public string taskId;
            public string interactionMode;
            public string taskVariant;
            public string condition;
            public string conditionAlias;
            public int candidateTarget;
        }
    }
}
