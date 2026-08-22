using System;
using System.Linq;
using Ubiq.XR;
using UnityEngine;
using UnityEngine.UI;

namespace AgenticXR.Study
{
    public sealed class StudyTaskCardPresenter : MonoBehaviour
    {
        [Serializable] private sealed class Definition { public Card[] cards; }
        [Serializable] private sealed class Card
        {
            public string taskId;
            public string taskVariant;
            public string goal;
            public string scriptedFirstUtterance;
            public string goalCardDetail;
            public string[] revisionOrder;
        }

        public TextAsset taskCardDefinition;
        public bool IsDismissed { get; private set; }
        public event Action Dismissed;
        private Definition definition;
        private Card selected;
        private Canvas canvas;
        private Text cardText;

        private void Awake()
        {
            if (taskCardDefinition == null) throw new InvalidOperationException("Task-card JSON TextAsset is required.");
            definition = JsonUtility.FromJson<Definition>(taskCardDefinition.text);
            if (definition == null || definition.cards == null || definition.cards.Length != 10)
                throw new InvalidOperationException("Task-card JSON must contain exactly ten condition-independent cards.");
            BuildPanel();
            canvas.gameObject.SetActive(false);
        }

        public void Configure(string taskId, string taskVariant)
        {
            selected = definition.cards.SingleOrDefault(card => card.taskId == taskId && card.taskVariant == taskVariant);
            if (selected == null) throw new InvalidOperationException("No task card matches the server assignment.");
        }

        public void ShowCard()
        {
            if (selected == null) throw new InvalidOperationException("Configure the assigned task card before showing it.");
            IsDismissed = false;
            cardText.text = string.Join("\n\n", new[] { selected.goal, selected.scriptedFirstUtterance,
                selected.goalCardDetail, selected.revisionOrder == null ? null : string.Join("\n", selected.revisionOrder) }
                .Where(value => !string.IsNullOrEmpty(value)));
            canvas.gameObject.SetActive(true);
        }

        public void DismissCard()
        {
            if (IsDismissed) return;
            IsDismissed = true;
            if (canvas != null) canvas.gameObject.SetActive(false);
            Dismissed?.Invoke();
        }

        private void BuildPanel()
        {
            var root = new GameObject("Study Task Card", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(XRUICanvas), typeof(Image));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(850, 620);
            root.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f, 0.97f);
            if (Camera.main != null)
            {
                root.transform.SetParent(Camera.main.transform, false);
                root.transform.localPosition = new Vector3(0f, 0f, 1.4f);
                root.transform.localScale = Vector3.one * 0.0012f;
            }
            cardText = CreateText(root.transform, "Card", new Vector2(0, 50), new Vector2(790, 470), 29);
            var button = new GameObject("Dismiss", typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(root.transform, false);
            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchoredPosition = new Vector2(0, -245);
            buttonRect.sizeDelta = new Vector2(220, 65);
            button.GetComponent<Image>().color = new Color(0.13f, 0.45f, 0.3f, 1f);
            button.GetComponent<Button>().onClick.AddListener(DismissCard);
            var label = CreateText(button.transform, "Label", Vector2.zero, buttonRect.sizeDelta, 28);
            label.text = "Begin";
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
    }
}
