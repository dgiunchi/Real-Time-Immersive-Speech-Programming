using UnityEngine;
using UnityEngine.UI;

namespace AgenticCache
{
    public sealed class AgenticXRConsentPanel : MonoBehaviour
    {
        private CacheExchangeManager manager;
        private Canvas canvas;
        private Text statusText;
        private Text proposalText;
        private string pendingCorrelationId;

        public void Initialize(CacheExchangeManager owner)
        {
            manager = owner;
            BuildWorldSpacePanel();
            ShowStatus("ready", "Point at an object and hold the left trigger to speak.");
        }

        public void ShowStatus(string state, string detail)
        {
            if (statusText != null) statusText.text = "AgentiXR: " + (state ?? "") + "\n" + (detail ?? "");
        }

        public void ShowProposal(string correlationId, string targetName, string intent)
        {
            pendingCorrelationId = correlationId;
            if (proposalText != null)
                proposalText.text = "Claude proposes a behaviour for " + targetName + ":\n" + intent + "\n\nApprove or reject?";
            if (canvas != null) canvas.gameObject.SetActive(true);
        }

        public void HideProposal()
        {
            pendingCorrelationId = null;
            if (proposalText != null) proposalText.text = "";
        }

        private void Update()
        {
            // Editor/desktop fallback; the buttons remain available to the XR UI ray.
            if (!string.IsNullOrEmpty(pendingCorrelationId) && Input.GetKeyDown(KeyCode.Return)) Approve();
            if (!string.IsNullOrEmpty(pendingCorrelationId) && Input.GetKeyDown(KeyCode.Escape)) Reject();
            if (Input.GetKeyDown(KeyCode.U)) Undo();
        }

        private void Approve()
        {
            if (!string.IsNullOrEmpty(pendingCorrelationId)) manager.ApprovePending(pendingCorrelationId);
        }

        private void Reject()
        {
            if (!string.IsNullOrEmpty(pendingCorrelationId)) manager.RejectPending(pendingCorrelationId, "user_rejected");
        }

        private void Undo() => manager.UndoLatest();

        private void BuildWorldSpacePanel()
        {
            var root = new GameObject("AgenticXR Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(760, 430);
            root.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.075f, 0.94f);

            var cameraTransform = Camera.main != null ? Camera.main.transform : null;
            if (cameraTransform != null)
            {
                root.transform.SetParent(cameraTransform, false);
                root.transform.localPosition = new Vector3(0, -0.15f, 1.35f);
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one * 0.0012f;
            }

            statusText = CreateText(root.transform, "Status", new Vector2(0, 150), new Vector2(700, 80), 26, TextAnchor.MiddleLeft);
            proposalText = CreateText(root.transform, "Proposal", new Vector2(0, 25), new Vector2(700, 160), 24, TextAnchor.UpperLeft);
            CreateButton(root.transform, "Approve", new Vector2(-210, -150), new Color(0.15f, 0.55f, 0.3f), Approve);
            CreateButton(root.transform, "Reject", new Vector2(0, -150), new Color(0.65f, 0.2f, 0.2f), Reject);
            CreateButton(root.transform, "Undo", new Vector2(210, -150), new Color(0.25f, 0.35f, 0.65f), Undo);
        }

        private static Text CreateText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
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
            text.alignment = alignment;
            return text;
        }

        private static void CreateButton(Transform parent, string label, Vector2 position, Color color, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(180, 65);
            go.GetComponent<Image>().color = color;
            go.GetComponent<Button>().onClick.AddListener(action);
            var text = CreateText(go.transform, "Label", Vector2.zero, rect.sizeDelta, 25, TextAnchor.MiddleCenter);
            text.text = label;
        }
    }
}
