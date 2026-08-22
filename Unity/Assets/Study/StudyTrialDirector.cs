using System;
using System.Collections.Generic;
using AgenticCache;
using UnityEngine;

namespace AgenticXR.Study
{
    [DisallowMultipleComponent]
    public sealed class StudyTrialDirector : MonoBehaviour
    {
        [Serializable]
        public sealed class TrialAssignment
        {
            public string participantId;
            public string sessionId;
            public string trialId;
            public string taskId;
            public string interactionMode;
            public string taskVariant;
            public string condition;
            public int candidateTarget;
        }

        [Serializable]
        public sealed class VariantBinding
        {
            public string taskId;
            public string interactionMode;
            public string taskVariant;
            public GameObject root;
            public float timeoutSeconds;
            public StudySuccessDetector detector;
            public Rigidbody resetBody;
            public Vector3 resetLocalPosition;
            public Quaternion resetLocalRotation = Quaternion.identity;
            public Transform[] resetTransforms;
            public Vector3[] resetTransformLocalPositions;
            public Quaternion[] resetTransformLocalRotations;
            public L4TrialDoor trialDoor;
            public AgenticRegionVolume l2Region;
        }

        [Tooltip("Server-side StudySessionMachine remains the sole transition authority. This client emits requests/evidence only.")]
        public string transitionAuthority = "StudySessionMachine";
        public CachePublisher publisher;
        public StudyTaskCardPresenter taskCardPresenter;
        public StudyQuestionnairePresenter questionnairePresenter;
        public VariantBinding[] variants;
        public bool trainingRequired = true;

        public bool TrainingUndoCriterionMet { get; private set; }
        public bool TrainingRejectCriterionMet { get; private set; }
        public bool TrainingCriteriaComplete => TrainingUndoCriterionMet && TrainingRejectCriterionMet;
        public TrialAssignment ActiveAssignment { get; private set; }

        private VariantBinding activeBinding;
        private bool t0Emitted;
        private bool t1Requested;
        private bool wasInsideL2Region;
        private float activatedAt;
        private float questionnairePauseStartedAt = -1f;
        private float questionnaireExcludedSeconds;
        private string pendingEndReason;
        private string pendingEndDetailJson;
        private bool taskCardDismissalHooked;

        public void ReceiveServerTrialJson(string assignmentJson)
        {
            var assignment = JsonUtility.FromJson<TrialAssignment>(assignmentJson);
            ApplyServerAssignment(assignment);
        }

        public void ApplyServerAssignment(TrialAssignment assignment)
        {
            if (trainingRequired && !TrainingCriteriaComplete)
                throw new InvalidOperationException("Training requires one demonstrated undo and one demonstrated rejection before study trials.");
            if (assignment == null || string.IsNullOrEmpty(assignment.participantId) || string.IsNullOrEmpty(assignment.sessionId) ||
                string.IsNullOrEmpty(assignment.trialId) ||
                string.IsNullOrEmpty(assignment.taskId) || string.IsNullOrEmpty(assignment.taskVariant) ||
                string.IsNullOrEmpty(assignment.condition))
                throw new InvalidOperationException("A complete server trial assignment is required.");

            DeactivateAllVariants();
            activeBinding = Array.Find(variants, binding => binding != null && binding.taskId == assignment.taskId &&
                binding.taskVariant == assignment.taskVariant && binding.interactionMode == assignment.interactionMode);
            if (activeBinding == null || activeBinding.root == null)
                throw new InvalidOperationException("Server assignment has no authored task-variant binding.");

            ActiveAssignment = assignment;
            questionnairePresenter?.BindTrial(assignment);
            ResetTrialLocalState(activeBinding);
            activeBinding.root.SetActive(true);
            if (CountActiveVariantRoots() != 1)
                throw new InvalidOperationException("Exactly one study variant root must be active.");
            activeBinding.detector.SuccessObserved += OnDetectorObserved;
            activeBinding.detector.Arm();
            if (!taskCardDismissalHooked && taskCardPresenter != null)
            {
                taskCardPresenter.Dismissed += OnTaskCardDismissed;
                taskCardDismissalHooked = true;
            }
            taskCardPresenter?.Configure(assignment.taskId, assignment.taskVariant);
            taskCardPresenter?.ShowCard();
            activatedAt = Time.unscaledTime;
            t0Emitted = false;
            t1Requested = false;
            questionnairePauseStartedAt = -1f;
            questionnaireExcludedSeconds = 0f;
            pendingEndReason = null;
            pendingEndDetailJson = null;
            wasInsideL2Region = activeBinding.l2Region != null && Camera.main != null &&
                activeBinding.l2Region.Contains(Camera.main.transform.position);
        }

        public void NotifyTaskCardDismissed() => taskCardPresenter?.DismissCard();

        private void OnTaskCardDismissed()
        {
            EmitStudyEvidence("study_task_card_dismissed", "{}");
            TryEmitT0();
        }

        public void BeginQuestionnairePause()
        {
            if (!t0Emitted || t1Requested || questionnairePauseStartedAt >= 0f)
                throw new InvalidOperationException("Questionnaire pause requires a running t0-to-t1 clock.");
            questionnairePauseStartedAt = Time.unscaledTime;
            EmitStudyEvidence("study_task_clock_paused", "{\"reason\":\"questionnaire\"}");
        }

        public void EndQuestionnairePause()
        {
            if (questionnairePauseStartedAt < 0f) throw new InvalidOperationException("No questionnaire pause is active.");
            var duration = Time.unscaledTime - questionnairePauseStartedAt;
            questionnaireExcludedSeconds += duration;
            activatedAt += duration;
            questionnairePauseStartedAt = -1f;
            EmitStudyEvidence("study_task_clock_resumed", "{\"excludedQuestionnaireMs\":" +
                Mathf.RoundToInt(questionnaireExcludedSeconds * 1000f) + "}");
            if (!string.IsNullOrEmpty(pendingEndReason))
            {
                var reason = pendingEndReason;
                var detail = pendingEndDetailJson;
                pendingEndReason = null;
                pendingEndDetailJson = null;
                RequestTrialEnd(reason, detail);
            }
        }

        public void DeclareCompletion() => RequestTrialEnd("declared", "{}");

        public void RecordTrainingUndo()
        {
            if (TrainingUndoCriterionMet) return;
            TrainingUndoCriterionMet = true;
            EmitStudyEvidence("study_training_criterion_met", "{\"criterion\":\"undo\"}");
        }

        public void RecordTrainingReject()
        {
            if (TrainingRejectCriterionMet) return;
            TrainingRejectCriterionMet = true;
            EmitStudyEvidence("study_training_criterion_met", "{\"criterion\":\"reject\"}");
        }

        private void Update()
        {
            if (activeBinding == null || ActiveAssignment == null || questionnairePauseStartedAt >= 0f) return;
            TryEmitT0();
            if (!t1Requested && t0Emitted && Time.unscaledTime - activatedAt >= activeBinding.timeoutSeconds)
                RequestTrialEnd("timeout", "{}");
            if (ActiveAssignment.interactionMode == "L2" && activeBinding.l2Region != null && Camera.main != null)
            {
                var inside = activeBinding.l2Region.Contains(Camera.main.transform.position);
                if (inside && !wasInsideL2Region)
                    EmitStudyEvidence("study_l2_region_entered", "{\"regionId\":\"" +
                        AgenticSceneRegistry.Escape(activeBinding.l2Region.regionId) + "\"}");
                wasInsideL2Region = inside;
            }
        }

        private void TryEmitT0()
        {
            if (t0Emitted || activeBinding == null || !activeBinding.root.activeInHierarchy ||
                taskCardPresenter == null || !taskCardPresenter.IsDismissed) return;
            t0Emitted = true;
            activatedAt = Time.unscaledTime;
            EmitStudyEvidence("study_trial_t0", "{\"reason\":\"task_card_dismissed_and_root_active\"}");
        }

        private void OnDetectorObserved(StudySuccessDetector detector, string detailJson) =>
            RequestTrialEnd("detector", detailJson);

        private void RequestTrialEnd(string reason, string detailJson)
        {
            if (t1Requested || activeBinding == null || ActiveAssignment == null) return;
            if (questionnairePauseStartedAt >= 0f)
            {
                if (string.IsNullOrEmpty(pendingEndReason)) { pendingEndReason = reason; pendingEndDetailJson = detailJson; }
                return;
            }
            t1Requested = true;
            EmitStudyEvidence("study_trial_t1", "{\"arbitrationReason\":\"" +
                AgenticSceneRegistry.Escape(reason) + "\",\"excludedQuestionnaireMs\":" +
                Mathf.RoundToInt(questionnaireExcludedSeconds * 1000f) + ",\"detail\":" + (detailJson ?? "{}") + "}");
            activeBinding.detector.Disarm();
            activeBinding.detector.SuccessObserved -= OnDetectorObserved;
            activeBinding.root.SetActive(false);
            questionnairePresenter?.BeginAfterTrialBattery();
            activeBinding = null;
            ActiveAssignment = null;
            if (CountActiveVariantRoots() != 0)
                throw new InvalidOperationException("Study variant roots must be inactive after t1.");
        }

        private void ResetTrialLocalState(VariantBinding binding)
        {
            if (binding.resetBody != null)
            {
                binding.resetBody.transform.localPosition = binding.resetLocalPosition;
                binding.resetBody.transform.localRotation = binding.resetLocalRotation;
                binding.resetBody.linearVelocity = Vector3.zero;
                binding.resetBody.angularVelocity = Vector3.zero;
            }
            if (binding.resetTransforms != null)
            {
                if (binding.resetTransformLocalPositions == null || binding.resetTransformLocalRotations == null ||
                    binding.resetTransforms.Length != binding.resetTransformLocalPositions.Length ||
                    binding.resetTransforms.Length != binding.resetTransformLocalRotations.Length)
                    throw new InvalidOperationException("Trial-local reset transform arrays are inconsistent.");
                for (var index = 0; index < binding.resetTransforms.Length; index++)
                {
                    if (binding.resetTransforms[index] == null) continue;
                    binding.resetTransforms[index].localPosition = binding.resetTransformLocalPositions[index];
                    binding.resetTransforms[index].localRotation = binding.resetTransformLocalRotations[index];
                }
            }
            binding.trialDoor?.ResetClosed();
            if (binding.detector is L3MarkerPlacedDetector l3 && l3.doneButton != null) l3.doneButton.ResetButton();
        }

        private void DeactivateAllVariants()
        {
            if (variants == null) return;
            foreach (var binding in variants)
            {
                if (binding == null || binding.root == null) continue;
                binding.detector?.Disarm();
                binding.root.SetActive(false);
            }
        }

        private int CountActiveVariantRoots()
        {
            var count = 0;
            if (variants != null) foreach (var binding in variants)
                if (binding != null && binding.root != null && binding.root.activeSelf) count += 1;
            return count;
        }

        private void EmitStudyEvidence(string eventType, string detailJson)
        {
            if (publisher == null) return;
            var trial = ActiveAssignment;
            var json = "[{\"sensorType\":\"" + AgenticSceneRegistry.Escape(eventType) +
                "\",\"sourceObjectId\":\"study-trial-director\",\"value\":{\"transitionAuthority\":\"StudySessionMachine\"" +
                (trial == null ? string.Empty : ",\"trialId\":\"" + AgenticSceneRegistry.Escape(trial.trialId) +
                    "\",\"participantId\":\"" + AgenticSceneRegistry.Escape(trial.participantId) +
                    "\",\"sessionId\":\"" + AgenticSceneRegistry.Escape(trial.sessionId) +
                    "\",\"taskId\":\"" + AgenticSceneRegistry.Escape(trial.taskId) +
                    "\",\"interactionMode\":\"" + AgenticSceneRegistry.Escape(trial.interactionMode) +
                    "\",\"taskVariant\":\"" + AgenticSceneRegistry.Escape(trial.taskVariant) +
                    "\",\"condition\":\"" + AgenticSceneRegistry.Escape(trial.condition) + "\"") +
                ",\"detail\":" + (detailJson ?? "{}") + "},\"confidence\":1.0}]";
            publisher.PublishSensorEvent("study-trial-director", 1, string.Empty, string.Empty, json);
        }
    }
}
