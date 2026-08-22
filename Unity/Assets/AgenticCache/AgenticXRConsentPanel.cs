using AgenticXR.Study;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Ubiq.XR;

namespace AgenticCache
{
    public sealed class AgenticXRConsentPanel : MonoBehaviour
    {
        private CacheExchangeManager manager;
        private Canvas canvas;
        private Text statusText;
        private Text transcriptText;
        private Text proposalText;
        private MicrophoneCapture microphoneCapture;
        private string pendingCorrelationId;
        private bool leftPrimaryWasPressed;
        private bool decisionLocked;
        public StudyQuestionnairePresenter questionnairePresenter;

        public void Initialize(CacheExchangeManager owner)
        {
            manager = owner;
            if (questionnairePresenter == null) questionnairePresenter = FindFirstObjectByType<StudyQuestionnairePresenter>();
            if (questionnairePresenter != null) questionnairePresenter.ImmediateItemCompleted += UnlockDecision;
            BuildWorldSpacePanel();
            ShowStatus("ready", "Point at an object and hold the left trigger to speak.");
        }

        public void ShowStatus(string state, string detail)
        {
            if (statusText != null) statusText.text = "AgentiXR: " + (state ?? "") + "\n" + (detail ?? "");
            if (state == "listening" && transcriptText != null) transcriptText.text = "";
        }

        public void ShowTranscript(string transcript)
        {
            if (transcriptText != null)
                transcriptText.text = string.IsNullOrWhiteSpace(transcript) ? "Heard: (no speech recognized)" : "Heard: " + transcript;
        }

        public void ShowProposal(string correlationId, string targetName, string intent, string validationSummary,
            float riskScore, string[] requiredPermissions, string expectedSideEffects, int candidateCount,
            string candidateComparison)
        {
            pendingCorrelationId = correlationId;
            decisionLocked = questionnairePresenter != null && questionnairePresenter.RequiresImmediateProposalItem();
            if (decisionLocked) questionnairePresenter.BeginImmediateProposalItem();
            if (proposalText != null)
                proposalText.text = "Claude proposes a behaviour for " + targetName + ":\n" + intent +
                    "\n\nVerification: " + (string.IsNullOrEmpty(validationSummary) ? "compiled on staging clone" : validationSummary) +
                    "\nRisk: " + (riskScore >= 0f ? riskScore.ToString("0.00") : "not supplied") +
                    "\nPermissions: " + (requiredPermissions != null ? string.Join(", ", requiredPermissions) : "attach_component") +
                    "\nExpected effects: " + (string.IsNullOrEmpty(expectedSideEffects) ? "not supplied" : expectedSideEffects) +
                    (candidateCount > 1 ? "\n\nCompared " + candidateCount + " candidates:\n" +
                        (string.IsNullOrEmpty(candidateComparison) ? "Best eligible candidate selected." : candidateComparison) : "") +
                    "\n\nApprove or reject?";
            if (canvas != null) canvas.gameObject.SetActive(true);
        }

        public void HideProposal()
        {
            pendingCorrelationId = null;
            decisionLocked = false;
            if (proposalText != null) proposalText.text = "";
        }

        private void Update()
        {
            // Editor/desktop fallback; the buttons remain available to the XR UI ray.
            if (!string.IsNullOrEmpty(pendingCorrelationId) && Input.GetKeyDown(KeyCode.Return)) Approve();
            if (Input.GetKeyDown(KeyCode.Escape)) Reject();
            if (!string.IsNullOrEmpty(pendingCorrelationId) && Input.GetKeyDown(KeyCode.R)) Revise();
            if (Input.GetKeyDown(KeyCode.U)) Undo();

            // Oculus/Meta left-controller X button maps to the XR primaryButton.
            // Use the rising edge so holding X sends only one cancel/reject request.
            var leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var leftPrimaryPressed = leftController.isValid &&
                leftController.TryGetFeatureValue(CommonUsages.primaryButton, out var pressed) && pressed;
            if (leftPrimaryPressed && !leftPrimaryWasPressed) Reject();
            leftPrimaryWasPressed = leftPrimaryPressed;
        }

        private void Approve()
        {
            if (decisionLocked) return;
            if (!string.IsNullOrEmpty(pendingCorrelationId)) manager.ApprovePending(pendingCorrelationId);
        }

        private void Reject()
        {
            if (decisionLocked) return;
            if (!string.IsNullOrEmpty(pendingCorrelationId)) manager.RejectPending(pendingCorrelationId, "user_rejected");
            else manager.CancelActiveRequest();
        }

        private void Revise()
        {
            if (decisionLocked) return;
            if (!string.IsNullOrEmpty(pendingCorrelationId)) manager.RevisePending(pendingCorrelationId);
        }

        private void UnlockDecision() => decisionLocked = false;

        private void Undo() => manager.UndoLatest();

        private void ResetTrial() => manager.ResetTrialState();

        private void ListenToLastRecording()
        {
            if (microphoneCapture == null) microphoneCapture = FindFirstObjectByType<MicrophoneCapture>();
            if (microphoneCapture == null || !microphoneCapture.PlayLastRecording())
                ShowStatus("listen", "No completed microphone recording is available yet.");
        }

        private void BuildWorldSpacePanel()
        {
            var root = new GameObject("AgenticXR Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(XRUICanvas), typeof(Image));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(760, 590);
            root.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.075f, 0.94f);

            var cameraTransform = Camera.main != null ? Camera.main.transform : null;
            if (cameraTransform != null)
            {
                root.transform.SetParent(cameraTransform, false);
                root.transform.localPosition = new Vector3(0, -0.15f, 1.35f);
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one * 0.0012f;
            }

            statusText = CreateText(root.transform, "Status", new Vector2(0, 200), new Vector2(700, 70), 26, TextAnchor.MiddleLeft);
            transcriptText = CreateText(root.transform, "Transcript", new Vector2(-85, 135), new Vector2(530, 55), 24, TextAnchor.MiddleLeft);
            CreateButton(root.transform, "Listen", new Vector2(275, 135), new Color(0.2f, 0.48f, 0.65f), ListenToLastRecording, new Vector2(145, 55));
            proposalText = CreateText(root.transform, "Proposal", new Vector2(0, -20), new Vector2(700, 250), 20, TextAnchor.UpperLeft);
            CreateButton(root.transform, "Approve", new Vector2(-270, -205), new Color(0.15f, 0.55f, 0.3f), Approve, new Vector2(150, 65));
            CreateButton(root.transform, "Revise", new Vector2(-90, -205), new Color(0.7f, 0.45f, 0.12f), Revise, new Vector2(150, 65));
            CreateButton(root.transform, "Reject", new Vector2(90, -205), new Color(0.65f, 0.2f, 0.2f), Reject, new Vector2(150, 65));
            CreateButton(root.transform, "Undo", new Vector2(270, -205), new Color(0.25f, 0.35f, 0.65f), Undo, new Vector2(150, 65));
            CreateButton(root.transform, "Reset Trial", new Vector2(0, -280), new Color(0.45f, 0.3f, 0.55f), ResetTrial, new Vector2(240, 55));
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

        private static void CreateButton(Transform parent, string label, Vector2 position, Color color,
            UnityEngine.Events.UnityAction action, Vector2? size = null)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size ?? new Vector2(180, 65);
            go.GetComponent<Image>().color = color;
            go.GetComponent<Button>().onClick.AddListener(action);
            var text = CreateText(go.transform, "Label", Vector2.zero, rect.sizeDelta, 25, TextAnchor.MiddleCenter);
            text.text = label;
        }
    }
}
