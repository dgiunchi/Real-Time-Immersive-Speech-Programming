using System;
using System.Collections.Generic;
using System.Linq;
using AgenticCache;
using Ubiq.XR;
using UnityEngine;
using UnityEngine.UI;

namespace AgenticXR.Study
{
    [DisallowMultipleComponent]
    public sealed class StudyQuestionnairePresenter : MonoBehaviour
    {
        [Serializable] private sealed class QuestionnaireDefinition
        {
            public string questionnaireVersion;
            public string protocolId;
            public string methodVersion;
            public QuestionnaireItem[] studySpecificItemSlots;
        }

        [Serializable] private sealed class QuestionnaireItem
        {
            public string itemId;
            public string responseType;
            public string timing;
            public int presentationOrder;
            public string wording;
            public QuestionnaireAnchor[] anchors;
            public string[] interactionModes;
            public string[] conditions;
        }

        [Serializable] private sealed class QuestionnaireAnchor
        {
            public string label;
            public string responseCode;
        }

        public TextAsset questionnaireDefinition;
        public CachePublisher publisher;
        public StudyTrialDirector trialDirector;
        public bool IsRunning { get; private set; }
        public bool DecisionAffordanceLocked { get; private set; }
        public event Action ImmediateItemCompleted;
        public event Action BatteryCompleted;
        public event Action Started;

        private QuestionnaireDefinition definition;
        private StudyTrialDirector.TrialAssignment trial;
        private readonly List<QuestionnaireItem> queue = new List<QuestionnaireItem>();
        private int itemIndex;
        private float itemShownAt;
        private bool immediateFlow;
        private Canvas canvas;
        private Text itemText;
        private RectTransform responseRoot;

        private void Awake()
        {
            LoadDefinition();
            BuildWorldSpacePanel();
            SetPanelVisible(false);
        }

        public void BindTrial(StudyTrialDirector.TrialAssignment assignment) => trial = assignment;

        public bool RequiresImmediateProposalItem()
        {
            return trial != null && trial.condition == "agenticxr_verification" &&
                (trial.interactionMode == "L4" || trial.interactionMode == "L5");
        }

        public void BeginImmediateProposalItem()
        {
            if (!RequiresImmediateProposalItem()) return;
            StartQueue(definition.studySpecificItemSlots.Where(item =>
                item.itemId == "immediateProposalPerceivedLatency"), true);
        }

        public void BeginAfterTrialBattery()
        {
            if (trial == null) throw new InvalidOperationException("Questionnaire trial identity is missing.");
            StartQueue(definition.studySpecificItemSlots.Where(item =>
                item.itemId != "immediateProposalPerceivedLatency" && Applies(item)), false);
        }

        public void SelectLikert(int response)
        {
            if (!IsRunning || response < 1 || response > 7 || Current.responseType != "likert7") return;
            RecordResponse(response.ToString(), false);
        }

        public void SelectForcedChoice(string response)
        {
            if (!IsRunning || Current.responseType != "forcedChoice" || string.IsNullOrEmpty(response)) return;
            RecordResponse(response, false);
        }

        public void DeclineCurrent()
        {
            if (IsRunning) RecordResponse("declined", true);
        }

        private QuestionnaireItem Current => queue[itemIndex];

        private void LoadDefinition()
        {
            if (questionnaireDefinition == null) throw new InvalidOperationException("Questionnaire JSON TextAsset is required.");
            definition = JsonUtility.FromJson<QuestionnaireDefinition>(questionnaireDefinition.text);
            if (definition == null || definition.studySpecificItemSlots == null || definition.studySpecificItemSlots.Length != 12)
                throw new InvalidOperationException("Questionnaire JSON must contain exactly twelve study-specific items.");
        }

        private bool Applies(QuestionnaireItem item)
        {
            if (item.interactionModes != null && item.interactionModes.Length > 0 && !item.interactionModes.Contains(trial.interactionMode)) return false;
            if (item.conditions != null && item.conditions.Length > 0 && !item.conditions.Contains(trial.condition)) return false;
            return item.timing.StartsWith("after-", StringComparison.Ordinal);
        }

        private void StartQueue(IEnumerable<QuestionnaireItem> items, bool immediate)
        {
            if (IsRunning) throw new InvalidOperationException("A questionnaire item is already running.");
            queue.Clear();
            queue.AddRange(items.OrderBy(item => item.presentationOrder));
            if (queue.Count == 0) return;
            itemIndex = 0;
            immediateFlow = immediate;
            IsRunning = true;
            DecisionAffordanceLocked = immediate;
            if (immediate) trialDirector.BeginQuestionnairePause();
            Started?.Invoke();
            SetPanelVisible(true);
            RenderCurrent();
        }

        private void RenderCurrent()
        {
            itemShownAt = Time.unscaledTime;
            itemText.text = Current.wording;
            ClearResponses();
            if (Current.responseType == "likert7")
            {
                for (var value = 1; value <= 7; value++)
                {
                    var captured = value;
                    var anchor = Current.anchors == null ? null : Current.anchors.FirstOrDefault(candidate =>
                        (value == 1 && candidate == Current.anchors.FirstOrDefault()) ||
                        (value == 4 && candidate == Current.anchors.Skip(1).FirstOrDefault()) ||
                        (value == 7 && candidate == Current.anchors.LastOrDefault()));
                    var label = value + (anchor == null || string.IsNullOrEmpty(anchor.label) ? string.Empty : " " + anchor.label);
                    CreateButton(label, () => SelectLikert(captured));
                }
            }
            else
            {
                foreach (var anchor in Current.anchors ?? Array.Empty<QuestionnaireAnchor>())
                {
                    var response = anchor.responseCode;
                    CreateButton(anchor.label, () => SelectForcedChoice(response));
                }
            }
            CreateButton("Decline", DeclineCurrent);
        }

        private void RecordResponse(string response, bool declined)
        {
            var latencyMs = Mathf.Max(0, Mathf.RoundToInt((Time.unscaledTime - itemShownAt) * 1000f));
            var respondedAtUtc = DateTime.UtcNow.ToString("o");
            var value = "{\"protocolId\":\"" + Escape(definition.protocolId) + "\",\"methodVersion\":\"" +
                Escape(definition.methodVersion) + "\",\"questionnaireVersion\":\"" + Escape(definition.questionnaireVersion) +
                "\",\"participantId\":\"" + Escape(trial.participantId) + "\",\"sessionId\":\"" + Escape(trial.sessionId) +
                "\",\"trialId\":\"" + Escape(trial.trialId) + "\",\"taskId\":\"" + Escape(trial.taskId) +
                "\",\"taskVariant\":\"" + Escape(trial.taskVariant) + "\",\"condition\":\"" + Escape(trial.condition) +
                "\",\"interactionMode\":\"" + Escape(trial.interactionMode) + "\",\"itemId\":\"" + Escape(Current.itemId) +
                "\",\"response\":\"" + Escape(response) + "\",\"declined\":" + (declined ? "true" : "false") +
                ",\"answeredAtUtc\":\"" + respondedAtUtc + "\",\"respondedAtUtc\":\"" + respondedAtUtc +
                "\",\"responseLatencyMs\":" + latencyMs + "}";
            publisher?.PublishSensorEvent("study-questionnaire-presenter", 1, "study", string.Empty,
                "[{\"sensorType\":\"study_questionnaire_response\",\"sourceObjectId\":\"study-questionnaire-presenter\",\"value\":" + value + ",\"confidence\":1.0}]");
            itemIndex += 1;
            if (itemIndex < queue.Count) { RenderCurrent(); return; }
            IsRunning = false;
            SetPanelVisible(false);
            if (immediateFlow)
            {
                DecisionAffordanceLocked = false;
                trialDirector.EndQuestionnairePause();
                ImmediateItemCompleted?.Invoke();
            }
            else
            {
                PublishBatteryComplete();
                BatteryCompleted?.Invoke();
            }
        }

        private void PublishBatteryComplete()
        {
            var value = "{\"participantId\":\"" + Escape(trial.participantId) + "\",\"sessionId\":\"" +
                Escape(trial.sessionId) + "\",\"trialId\":\"" + Escape(trial.trialId) + "\"}";
            publisher?.PublishSensorEvent("study-questionnaire-presenter", 1, "study", string.Empty,
                "[{\"sensorType\":\"study_questionnaire_battery_complete\",\"sourceObjectId\":\"study-questionnaire-presenter\",\"value\":" +
                value + ",\"confidence\":1.0}]");
        }

        private static string Escape(string value) => AgenticSceneRegistry.Escape(value ?? string.Empty);

        private void BuildWorldSpacePanel()
        {
            var root = new GameObject("Study Questionnaire Panel", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(XRUICanvas), typeof(Image));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(900, 700);
            root.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 0.97f);
            if (Camera.main != null)
            {
                root.transform.SetParent(Camera.main.transform, false);
                root.transform.localPosition = new Vector3(0f, 0f, 1.4f);
                root.transform.localScale = Vector3.one * 0.0012f;
            }
            itemText = CreateText(root.transform, "Item", new Vector2(0, 210), new Vector2(820, 180), 32);
            var responses = new GameObject("Responses", typeof(RectTransform), typeof(VerticalLayoutGroup));
            responses.transform.SetParent(root.transform, false);
            responseRoot = responses.GetComponent<RectTransform>();
            responseRoot.anchoredPosition = new Vector2(0, -70);
            responseRoot.sizeDelta = new Vector2(820, 430);
            var layout = responses.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
        }

        private void ClearResponses()
        {
            for (var index = responseRoot.childCount - 1; index >= 0; index--)
                Destroy(responseRoot.GetChild(index).gameObject);
        }

        private void CreateButton(string label, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject("Response", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(responseRoot, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 46);
            go.GetComponent<LayoutElement>().preferredHeight = 46;
            go.GetComponent<Image>().color = new Color(0.13f, 0.25f, 0.42f, 1f);
            go.GetComponent<Button>().onClick.AddListener(action);
            var text = CreateText(go.transform, "Label", Vector2.zero, new Vector2(790, 44), 24);
            text.text = label;
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

        private void SetPanelVisible(bool visible)
        {
            if (canvas != null) canvas.gameObject.SetActive(visible);
        }
    }
}
