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
        public TMP_Text compactScenarioText;
        public TMP_Text compactPointedText;
        public TMP_Text compactSelectedText;
        public TMP_Text compactSpeechText;
        public TMP_Text compactQuestText;
        public TMP_Text compactObjectiveText;
        public TMP_Text compactFeedbackText;

        [Header("Inspect")]
        public CanvasGroup inspectCardGroup;
        public TMP_Text objectNameText;
        public TMP_Text objectIdText;
        public TMP_Text objectDescriptionText;

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
        public DreamCodeVRSpeechStatusBridge speechStatusBridge;
        public Transform followTarget;

        [Header("Layout")]
        public float distanceFromCamera = 1.42f;
        public float horizontalOffset = 0.58f;
        public float verticalOffset = 0.04f;
        public float uiScale = 0.00118f;
        public float followSmoothing = 7f;

        [Header("Behavior")]
        public bool debugAlwaysShowAllPanels = false;
        public bool showDebugQuestDetails = false;
        public bool showInspectPanel = false;
        public float pollIntervalSeconds = 0.15f;
        public float feedbackHideDelay = 5f;

        private AIEditableObject lastPointedObject;
        private AIEditableObject lastSelectedObject;
        private AIEditableObject lastInspectObject;
        private string lastScenarioMode = "Fixed Scenario";
        private string lastStatusMessage;
        private string lastUndoHint;
        private bool hasUndoAvailable;
        private bool hasPlanPreview;
        private bool hasPlacedUi;
        private float nextPollTime;
        private float lastFeedbackTime = float.NegativeInfinity;

        private void OnEnable()
        {
            SubscribeSpeechBridge();
            ApplyDefaults();
        }

        private void OnDisable()
        {
            UnsubscribeSpeechBridge();
        }

        private void Start()
        {
            ResolveSources();
            SubscribeSpeechBridge();
            ApplyDefaults();
            UpdateTransform(true);
            RefreshSpeechUi();
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
            if (compactPointedText)
            {
                compactPointedText.text = obj
                    ? $"Pointed: {DisplayNameOrObjectId(obj)}"
                    : "Pointed: none";
            }

            if (lastPointedObject != obj)
            {
                Debug.Log($"[AuthoringUI] pointed={objectId}");
                lastPointedObject = obj;
            }
        }

        public void SetSelectedObject(AIEditableObject obj)
        {
            var objectId = obj ? obj.objectId : "none";
            if (compactSelectedText)
            {
                compactSelectedText.text = obj
                    ? $"Selected: {DisplayNameOrObjectId(obj)}"
                    : "Selected: none";
            }

            if (lastSelectedObject != obj)
            {
                Debug.Log($"[AuthoringUI] selected={objectId}");
                lastSelectedObject = obj;
            }
        }

        public void SetInspectInfo(AIEditableObject obj)
        {
            if (objectNameText)
            {
                objectNameText.text = obj
                    ? DisplayNameOrObjectId(obj)
                    : "No object in focus.";
            }

            if (objectIdText)
            {
                objectIdText.text = obj
                    ? obj.objectId
                    : "none";
            }

            if (objectDescriptionText)
            {
                objectDescriptionText.text = obj && !string.IsNullOrWhiteSpace(obj.description)
                    ? TruncateMultiline(obj.description, 180)
                    : "Point at an interactive object to inspect its metadata.";
            }

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
            if (planTitleText)
            {
                planTitleText.text = string.IsNullOrWhiteSpace(title) ? "Plan Preview" : title;
            }

            if (planStepsText)
            {
                planStepsText.text = hasPlanPreview
                    ? string.Join("\n", filteredSteps.Select((step, index) => $"{index + 1}. {step}"))
                    : "No pending plan.";
            }
        }

        public void SetScenarioMode(string mode)
        {
            lastScenarioMode = string.IsNullOrWhiteSpace(mode) ? "Fixed Scenario" : mode.Trim();
            if (compactScenarioText)
            {
                compactScenarioText.text = $"Scenario: {lastScenarioMode}";
            }
        }

        public void SetQuestPreview(string title, IEnumerable<string> steps)
        {
            SetPlanPreview(title, steps);
        }

        public void SetQuestSetupStatus(string message)
        {
            SetStatus(message);
        }

        public void SetStatus(string message)
        {
            lastStatusMessage = SimplifyStatusMessage(message);
            if (statusText)
            {
                statusText.text = string.IsNullOrWhiteSpace(lastStatusMessage)
                    ? "Feedback: Ready."
                    : $"Feedback: {lastStatusMessage}";
            }

            if (compactFeedbackText)
            {
                compactFeedbackText.text = string.IsNullOrWhiteSpace(lastStatusMessage)
                    ? "Feedback: Ready."
                    : $"Feedback: {TruncateMultiline(lastStatusMessage, 52)}";
            }

            if (!string.IsNullOrWhiteSpace(lastStatusMessage))
            {
                lastFeedbackTime = Time.unscaledTime;
            }
        }

        public void SetUndoAvailable(bool available, string hint)
        {
            hasUndoAvailable = available;
            lastUndoHint = string.IsNullOrWhiteSpace(hint) ? null : hint.Trim();
            if (undoHintText)
            {
                undoHintText.text = available
                    ? $"Undo: {lastUndoHint ?? "Undo available."}"
                    : string.IsNullOrWhiteSpace(lastUndoHint)
                        ? "Undo: No undoable action yet."
                        : $"Undo: {lastUndoHint}";
            }

            if (available || !string.IsNullOrWhiteSpace(lastUndoHint))
            {
                lastFeedbackTime = Time.unscaledTime;
            }
        }

        public void SetQuestRuntimeInfo(int currentTaskNumber, int totalTasks, string instruction, string lastResult)
        {
            var displayInstruction = string.IsNullOrWhiteSpace(instruction) ? "No active task." : instruction.Trim();
            var safeTotalTasks = Mathf.Max(0, totalTasks);
            var safeCurrentTask = Mathf.Clamp(currentTaskNumber, 0, safeTotalTasks);

            if (compactQuestText)
            {
                compactQuestText.text = $"Current task: {safeCurrentTask} / {safeTotalTasks}";
            }

            if (compactObjectiveText)
            {
                compactObjectiveText.text = TruncateMultiline(displayInstruction, 90);
            }

            if (!string.IsNullOrWhiteSpace(lastResult))
            {
                SetStatus(lastResult);
            }
        }

        public void ClearQuestRuntimeInfo()
        {
            SetQuestRuntimeInfo(0, 0, "No active task.", null);
        }

        private void ApplyDefaults()
        {
            transform.localScale = Vector3.one * uiScale;
            SetPointedObject(null);
            SetSelectedObject(null);
            SetInspectInfo(null);
            SetScenarioMode(lastScenarioMode);
            SetPlanPreview(null, null);
            SetStatus(null);
            SetUndoAvailable(false, null);
            ClearQuestRuntimeInfo();

            if (compactSpeechText)
            {
                compactSpeechText.text = "Speech: Initializing...";
            }

            if (transcriptText)
            {
                transcriptText.text = "Speech: Initializing...";
            }

            if (intentText)
            {
                intentText.text = "Waiting for microphone initialization.";
            }

            if (compactFeedbackText)
            {
                compactFeedbackText.text = "Feedback: Ready.";
            }

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

            if (!speechStatusBridge)
            {
                speechStatusBridge = FindFirstObjectByType<DreamCodeVRSpeechStatusBridge>();
            }
        }

        private void SubscribeSpeechBridge()
        {
            ResolveSources();
            if (speechStatusBridge)
            {
                speechStatusBridge.StateChanged -= OnSpeechStateChanged;
                speechStatusBridge.StateChanged += OnSpeechStateChanged;
            }
        }

        private void UnsubscribeSpeechBridge()
        {
            if (speechStatusBridge)
            {
                speechStatusBridge.StateChanged -= OnSpeechStateChanged;
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

        private void RefreshSpeechUi()
        {
            if (!speechStatusBridge)
            {
                return;
            }

            if (compactSpeechText)
            {
                compactSpeechText.text = speechStatusBridge.CompactSpeechText;
            }

            if (transcriptText)
            {
                transcriptText.text = speechStatusBridge.DetailedSpeechText;
            }

            if (intentText)
            {
                intentText.text = speechStatusBridge.DiagnosticsSummaryText;
            }
            UpdatePanelVisibility();
        }

        private void OnSpeechStateChanged()
        {
            RefreshSpeechUi();
        }

        private void UpdatePanelVisibility()
        {
            var inspectVisible = debugAlwaysShowAllPanels || (showInspectPanel && (lastPointedObject || lastSelectedObject));
            var speechVisible = debugAlwaysShowAllPanels || showDebugQuestDetails;
            var planVisible = debugAlwaysShowAllPanels || (showDebugQuestDetails && hasPlanPreview);
            var feedbackVisible = debugAlwaysShowAllPanels || showDebugQuestDetails;

            SetCanvasGroupVisible(inspectCardGroup, inspectVisible);
            SetCanvasGroupVisible(speechCardGroup, speechVisible);
            SetCanvasGroupVisible(planCardGroup, planVisible);
            SetCanvasGroupVisible(feedbackCardGroup, feedbackVisible);
            SetTextVisible(compactScenarioText, showDebugQuestDetails || debugAlwaysShowAllPanels);
            SetTextVisible(compactSelectedText, showDebugQuestDetails || debugAlwaysShowAllPanels);
        }

        private bool ShouldShowSpeechCard()
        {
            if (!speechStatusBridge)
            {
                return false;
            }

            return speechStatusBridge.CurrentState != SpeechUiState.Ready
                && speechStatusBridge.CurrentState != SpeechUiState.Initializing;
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

        private static void SetTextVisible(TMP_Text text, bool visible)
        {
            if (!text)
            {
                return;
            }

            if (text.gameObject.activeSelf != visible)
            {
                text.gameObject.SetActive(visible);
            }
        }

        private static string SimplifyStatusMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var trimmed = message.Trim();
            if (trimmed.StartsWith("Quest preview ready:"))
            {
                return "Quest received.";
            }

            if (trimmed.StartsWith("Quest received:"))
            {
                return "Quest received.";
            }

            if (trimmed.StartsWith("Quest applied with warnings:"))
            {
                return "Quest applied.";
            }

            if (trimmed.StartsWith("Quest applied:"))
            {
                return "Quest applied.";
            }

            if (trimmed.StartsWith("Applied with warnings:"))
            {
                return "Quest applied.";
            }

            if (trimmed.StartsWith("Applied quest setup:"))
            {
                return "Quest applied.";
            }

            if (trimmed.StartsWith("Task completed:"))
            {
                return "Task completed.";
            }

            if (trimmed.StartsWith("Started quest:"))
            {
                return "Quest applied.";
            }

            if (trimmed.StartsWith("Current objective:"))
            {
                return "Task updated.";
            }

            if (trimmed.StartsWith("Quest completed:"))
            {
                return "Quest completed.";
            }

            if (trimmed.StartsWith("Quest request failed:"))
            {
                return trimmed.Contains("status=0") ? "Server unavailable." : "Try again.";
            }

            if (trimmed.StartsWith("Quest apply failed:"))
            {
                return "Try again.";
            }

            return TruncateMultiline(trimmed, 120);
        }

        private static string DisplayNameOrObjectId(AIEditableObject obj)
        {
            return string.IsNullOrWhiteSpace(obj.displayName) ? obj.objectId : obj.displayName;
        }

        private static string TruncateMultiline(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var normalized = value.Replace('\n', ' ').Replace('\r', ' ');
            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            return normalized.Substring(0, maxLength - 3) + "...";
        }
    }
}
