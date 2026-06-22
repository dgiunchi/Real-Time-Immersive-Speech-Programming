using System.Collections.Generic;
using System.Linq;
using DreamCodeVR2.ContextBridge;
using TMPro;
using UnityEngine;

namespace DreamCodeVR2.UI
{
    public class DreamCodeVRAuthoringUIController : MonoBehaviour
    {
        [Header("Compact")]
        public TMP_Text compactPointedText;
        public TMP_Text compactSelectedText;
        public TMP_Text compactSpeechText;

        [Header("Inspect")]
        public CanvasGroup inspectCardGroup;
        public TMP_Text objectNameText;
        public TMP_Text objectIdText;
        public TMP_Text objectDescriptionText;
        public TMP_Text objectLabelsText;
        public TMP_Text possibleActionsText;

        [Header("Speech")]
        public CanvasGroup speechCardGroup;
        public TMP_Text transcriptText;
        public TMP_Text intentText;

        [Header("Plan")]
        public CanvasGroup planCardGroup;
        public TMP_Text planTitleText;
        public TMP_Text planStepsText;

        [Header("Feedback")]
        public CanvasGroup feedbackCardGroup;
        public TMP_Text statusText;
        public TMP_Text undoHintText;

        [Header("Data Sources")]
        public InteractionContextProvider interactionContextProvider;
        public SelectObjectRay selectObjectRay;
        public Transform followTarget;

        [Header("Layout")]
        public float distanceFromCamera = 1.42f;
        public float horizontalOffset = 0.58f;
        public float verticalOffset = 0.04f;
        public float uiScale = 0.00118f;
        public float followSmoothing = 7f;

        [Header("Behavior")]
        public bool debugAlwaysShowAllPanels = false;
        public float pollIntervalSeconds = 0.15f;
        public float speechCardHideDelay = 6f;
        public float feedbackHideDelay = 5f;

        private AIEditableObject lastPointedObject;
        private AIEditableObject lastSelectedObject;
        private AIEditableObject lastInspectObject;
        private string lastTranscript;
        private string lastStatusMessage;
        private string lastUndoHint;
        private bool hasUndoAvailable;
        private bool hasPlanPreview;
        private bool hasPlacedUi;
        private float nextPollTime;
        private float lastTranscriptTime = float.NegativeInfinity;
        private float lastFeedbackTime = float.NegativeInfinity;

        private void OnEnable()
        {
            TranscriptionCollector.TranscriptReceived += OnTranscriptReceived;
            ApplyDefaults();
        }

        private void OnDisable()
        {
            TranscriptionCollector.TranscriptReceived -= OnTranscriptReceived;
        }

        private void Start()
        {
            ResolveSources();
            ApplyDefaults();
            UpdateTransform(true);

            if (!string.IsNullOrWhiteSpace(TranscriptionCollector.LatestTranscript))
            {
                SetTranscript(TranscriptionCollector.LatestTranscript);
            }
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextPollTime)
            {
                nextPollTime = Time.unscaledTime + pollIntervalSeconds;
                ResolveSources();
                UpdateSelectionState();
            }

            UpdatePanelVisibility();
        }

        private void LateUpdate()
        {
            UpdateTransform(false);
        }

        public void SetPointedObject(AIEditableObject obj)
        {
            var objectId = obj ? obj.objectId : "none";
            compactPointedText.text = obj
                ? $"Pointed: {DisplayNameOrObjectId(obj)}"
                : "Pointed: none";

            if (lastPointedObject != obj)
            {
                Debug.Log($"[AuthoringUI] pointed={objectId}");
                lastPointedObject = obj;
            }
        }

        public void SetSelectedObject(AIEditableObject obj)
        {
            var objectId = obj ? obj.objectId : "none";
            compactSelectedText.text = obj
                ? $"Selected: {DisplayNameOrObjectId(obj)}"
                : "Selected: none";

            if (lastSelectedObject != obj)
            {
                Debug.Log($"[AuthoringUI] selected={objectId}");
                lastSelectedObject = obj;
            }
        }

        public void SetTranscript(string transcript)
        {
            lastTranscript = transcript;
            lastTranscriptTime = Time.unscaledTime;

            var hasTranscript = !string.IsNullOrWhiteSpace(transcript);
            compactSpeechText.text = hasTranscript
                ? $"Speech: {TruncateInline(transcript, 28)}"
                : "Speech: waiting";

            transcriptText.text = hasTranscript
                ? transcript
                : "Waiting for speech...";
        }

        public void SetIntentDebug(string intent, string policy, float confidence)
        {
            intentText.text = string.IsNullOrWhiteSpace(intent)
                ? "Intent: waiting for server-side interpretation..."
                : $"Intent: {intent}\nPolicy: {policy}\nConfidence: {confidence:0.00}";
        }

        public void SetInspectInfo(AIEditableObject obj)
        {
            objectNameText.text = obj
                ? DisplayNameOrObjectId(obj)
                : "No object in focus.";

            objectIdText.text = obj
                ? $"ID: {obj.objectId}"
                : "ID: none";

            objectDescriptionText.text = obj && !string.IsNullOrWhiteSpace(obj.description)
                ? obj.description
                : "Point at an interactive object to inspect its metadata.";

            objectLabelsText.text = obj && obj.labels != null && obj.labels.Length > 0
                ? $"Labels: {TruncateInline(string.Join(", ", obj.labels), 54)}"
                : "Labels: none";

            possibleActionsText.text = obj
                ? $"Possible actions: inspect, reference, describe{BuildActionSuffix(obj)}"
                : "Possible actions: inspect";

            if (lastInspectObject != obj)
            {
                Debug.Log($"[AuthoringUI] inspect={(obj ? obj.objectId : "none")}");
                lastInspectObject = obj;
            }
        }

        public void SetPlanPreview(string title, IEnumerable<string> steps)
        {
            var filteredSteps = steps == null
                ? new List<string>()
                : steps.Where(step => !string.IsNullOrWhiteSpace(step)).ToList();

            hasPlanPreview = filteredSteps.Count > 0;
            planTitleText.text = string.IsNullOrWhiteSpace(title) ? "Plan Preview" : title;
            planStepsText.text = hasPlanPreview
                ? string.Join("\n", filteredSteps.Select((step, index) => $"{index + 1}. {step}"))
                : "No pending plan.";
        }

        public void SetStatus(string message)
        {
            lastStatusMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
            statusText.text = string.IsNullOrWhiteSpace(lastStatusMessage)
                ? "Status: Ready."
                : $"Status: {lastStatusMessage}";

            if (!string.IsNullOrWhiteSpace(lastStatusMessage))
            {
                lastFeedbackTime = Time.unscaledTime;
            }
        }

        public void SetUndoAvailable(bool available, string hint)
        {
            hasUndoAvailable = available;
            lastUndoHint = string.IsNullOrWhiteSpace(hint) ? null : hint.Trim();
            undoHintText.text = available
                ? $"Undo: {lastUndoHint ?? "Undo available."}"
                : string.IsNullOrWhiteSpace(lastUndoHint)
                    ? "Undo: No undoable action yet."
                    : $"Undo: {lastUndoHint}";

            if (available || !string.IsNullOrWhiteSpace(lastUndoHint))
            {
                lastFeedbackTime = Time.unscaledTime;
            }
        }

        private void ApplyDefaults()
        {
            transform.localScale = Vector3.one * uiScale;
            SetPointedObject(null);
            SetSelectedObject(null);
            SetInspectInfo(null);
            SetTranscript(string.IsNullOrWhiteSpace(lastTranscript) ? null : lastTranscript);
            SetIntentDebug(null, null, 0f);
            SetPlanPreview(null, null);
            SetStatus(null);
            SetUndoAvailable(false, null);
            UpdatePanelVisibility();
        }

        private void ResolveSources()
        {
            if (!followTarget && Camera.main)
            {
                followTarget = Camera.main.transform;
            }

            if (!interactionContextProvider)
            {
                interactionContextProvider = FindFirstObjectByType<InteractionContextProvider>();
            }

            if (!selectObjectRay)
            {
                selectObjectRay = FindFirstObjectByType<SelectObjectRay>();
            }
        }

        private void UpdateSelectionState()
        {
            var pointedObject = interactionContextProvider ? interactionContextProvider.GetCurrentPointedEditableObject() : null;
            var selectedObject = interactionContextProvider ? interactionContextProvider.GetCurrentSelectedEditableObject() : null;

            if (!selectedObject && selectObjectRay && selectObjectRay.selectedObject)
            {
                selectedObject = selectObjectRay.selectedObject.GetComponentInParent<AIEditableObject>();
            }

            SetPointedObject(pointedObject);
            SetSelectedObject(selectedObject);
            SetInspectInfo(selectedObject ? selectedObject : pointedObject);
        }

        private void UpdatePanelVisibility()
        {
            var inspectVisible = debugAlwaysShowAllPanels || lastPointedObject || lastSelectedObject;
            var speechVisible = debugAlwaysShowAllPanels || ShouldShowSpeechCard();
            var planVisible = debugAlwaysShowAllPanels || hasPlanPreview;
            var feedbackVisible = debugAlwaysShowAllPanels || ShouldShowFeedbackCard();

            SetCanvasGroupVisible(inspectCardGroup, inspectVisible);
            SetCanvasGroupVisible(speechCardGroup, speechVisible);
            SetCanvasGroupVisible(planCardGroup, planVisible);
            SetCanvasGroupVisible(feedbackCardGroup, feedbackVisible);
        }

        private bool ShouldShowSpeechCard()
        {
            if (string.IsNullOrWhiteSpace(lastTranscript))
            {
                return false;
            }

            return Time.unscaledTime - lastTranscriptTime <= speechCardHideDelay;
        }

        private bool ShouldShowFeedbackCard()
        {
            if (hasUndoAvailable)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(lastStatusMessage))
            {
                return Time.unscaledTime - lastFeedbackTime <= feedbackHideDelay;
            }

            if (!string.IsNullOrWhiteSpace(lastUndoHint))
            {
                return Time.unscaledTime - lastFeedbackTime <= feedbackHideDelay;
            }

            return false;
        }

        private void UpdateTransform(bool snapToTarget)
        {
            if (!followTarget)
            {
                return;
            }

            var targetPosition = followTarget.position
                + followTarget.forward * distanceFromCamera
                + followTarget.right * horizontalOffset
                + followTarget.up * verticalOffset;

            var facingDirection = targetPosition - followTarget.position;
            if (facingDirection.sqrMagnitude < 0.001f)
            {
                facingDirection = -followTarget.forward;
            }

            var targetRotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);
            var smoothing = Mathf.Max(0f, followSmoothing);
            var blend = smoothing <= 0.01f ? 1f : 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);

            if (snapToTarget || !hasPlacedUi)
            {
                transform.position = targetPosition;
                transform.rotation = targetRotation;
                hasPlacedUi = true;
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, targetPosition, blend);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, blend);
            }

            transform.localScale = Vector3.one * uiScale;
        }

        private void OnTranscriptReceived(string transcript)
        {
            SetTranscript(transcript);
        }

        private static void SetCanvasGroupVisible(CanvasGroup group, bool visible)
        {
            if (!group)
            {
                return;
            }

            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
            if (group.gameObject.activeSelf != visible)
            {
                group.gameObject.SetActive(visible);
            }
        }

        private static string DisplayNameOrObjectId(AIEditableObject obj)
        {
            return string.IsNullOrWhiteSpace(obj.displayName) ? obj.objectId : obj.displayName;
        }

        private static string TruncateInline(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }

        private static string BuildActionSuffix(AIEditableObject obj)
        {
            if (obj.labels == null || obj.labels.Length == 0)
            {
                return string.Empty;
            }

            if (obj.labels.Contains("openable") || obj.labels.Contains("container"))
            {
                return ", open";
            }

            if (obj.labels.Contains("readable"))
            {
                return ", read";
            }

            if (obj.labels.Contains("lock") || obj.labels.Contains("lockable"))
            {
                return ", unlock";
            }

            return string.Empty;
        }
    }
}
