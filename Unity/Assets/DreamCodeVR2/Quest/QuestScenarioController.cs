using System.Collections.Generic;
using System.Linq;
using DreamCodeVR2.UI;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    public class QuestScenarioController : MonoBehaviour
    {
        public DreamCodeVRAuthoringUIController authoringUiController;
        public QuestPlanApplier questPlanApplier;
        public QuestPlannerClient questPlannerClient;
        public QuestRuntimeState questRuntimeState;
        public QuestScenarioMode currentMode = QuestScenarioMode.FixedScenario;
        public KeyCode printQuestProgressKey = KeyCode.F2;
        public KeyCode completeCurrentTaskKey = KeyCode.F3;
        public KeyCode resetQuestProgressKey = KeyCode.F4;
        public bool previewOnStart;
        public KeyCode cycleModeKey = KeyCode.F6;
        public KeyCode previewModeKey = KeyCode.F7;
        public KeyCode applyModeKey = KeyCode.F8;
        public KeyCode applyServerContractKey = KeyCode.F9;
        public KeyCode requestServerPreviewKey = KeyCode.F10;
        public KeyCode applyLastServerQuestKey = KeyCode.F11;
        public KeyCode requestAndApplyServerQuestKey = KeyCode.F12;
        public TextAsset fixedScenarioPlan;
        public TextAsset llmGeneratedScenarioPlan;
        public TextAsset manualDebugScenarioPlan;
        public TextAsset serverContractPlan;

        [Header("Server Quest Request")]
        public string serverQuestMode = "llm_generated_v1";
        public string serverQuestTemplate = string.Empty;

        private QuestPlan lastServerQuestPlan;
        private readonly QuestTaskValidator questTaskValidator = new QuestTaskValidator();

        private void Start()
        {
            ResolveReferences();
            LoadDefaultMockAssets();
            RefreshUi(clearPreview: !previewOnStart);

            if (previewOnStart)
            {
                PreviewCurrentScenario();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(printQuestProgressKey))
            {
                PrintQuestProgress();
            }

            if (Input.GetKeyDown(completeCurrentTaskKey))
            {
                CompleteCurrentTaskAndAdvance();
            }

            if (Input.GetKeyDown(resetQuestProgressKey))
            {
                ResetActiveQuestProgress();
            }

            if (Input.GetKeyDown(cycleModeKey))
            {
                CycleScenarioMode();
            }

            if (Input.GetKeyDown(previewModeKey))
            {
                PreviewCurrentScenario();
            }

            if (Input.GetKeyDown(applyModeKey))
            {
                ApplyCurrentScenario();
            }

            if (Input.GetKeyDown(applyServerContractKey))
            {
                ApplyServerContractScenario();
            }

            if (Input.GetKeyDown(requestServerPreviewKey))
            {
                RequestServerQuestPreview();
            }

            if (Input.GetKeyDown(applyLastServerQuestKey))
            {
                ApplyLastServerQuest();
            }

            if (Input.GetKeyDown(requestAndApplyServerQuestKey))
            {
                RequestAndApplyServerQuest();
            }
        }

        public void CycleScenarioMode()
        {
            switch (currentMode)
            {
                case QuestScenarioMode.FixedScenario:
                    currentMode = QuestScenarioMode.LlmGeneratedScenario;
                    break;
                case QuestScenarioMode.LlmGeneratedScenario:
                    currentMode = QuestScenarioMode.ManualDebugScenario;
                    break;
                default:
                    currentMode = QuestScenarioMode.FixedScenario;
                    break;
            }

            RefreshUi(clearPreview: true);
            authoringUiController?.SetQuestSetupStatus($"Scenario mode: {GetScenarioModeLabel(currentMode)}");
        }

        public void PreviewCurrentScenario()
        {
            ResolveReferences();
            var asset = GetCurrentPlanAsset();
            if (!asset || questPlanApplier == null)
            {
                authoringUiController?.SetQuestSetupStatus("No mock quest plan available for preview.");
                return;
            }

            var plan = questPlanApplier.DeserializePlan(asset.text);
            if (plan == null)
            {
                authoringUiController?.SetQuestSetupStatus("Mock quest plan could not be deserialized.");
                return;
            }

            var previewSteps = (plan.tasks ?? new List<QuestTaskSpec>())
                .OrderBy(task => task.step)
                .Select(task => string.IsNullOrWhiteSpace(task.description)
                    ? $"{task.step}. {task.type}"
                    : task.description);

            authoringUiController?.SetScenarioMode(GetScenarioModeLabel(currentMode));
            authoringUiController?.SetQuestPreview(plan.title, previewSteps);
            authoringUiController?.SetQuestSetupStatus(GetPreviewStatusLabel());
        }

        public void ApplyCurrentScenario()
        {
            ResolveReferences();
            var asset = GetCurrentPlanAsset();
            if (!asset || questPlanApplier == null)
            {
                authoringUiController?.SetQuestSetupStatus("No mock quest plan available to apply.");
                return;
            }

            var plan = questPlanApplier.DeserializePlan(asset.text);
            if (plan == null)
            {
                authoringUiController?.SetQuestSetupStatus("Mock quest plan could not be deserialized.");
                return;
            }

            authoringUiController?.SetScenarioMode(GetScenarioModeLabel(currentMode));
            if (!questPlanApplier.ApplyQuestPlan(plan, out var validation))
            {
                var firstError = validation.errors.FirstOrDefault() ?? "Unknown validation error.";
                authoringUiController?.SetQuestSetupStatus($"Quest apply failed: {firstError}");
                return;
            }

            if (validation.warnings.Count > 0)
            {
                authoringUiController?.SetQuestSetupStatus($"Applied with warnings: {validation.warnings[0]}");
            }

            StartQuestRuntime(plan);
        }

        public void RequestServerQuestPreview()
        {
            ResolveReferences();
            if (!questPlannerClient)
            {
                authoringUiController?.SetQuestSetupStatus("QuestPlannerClient not available.");
                return;
            }

            authoringUiController?.SetQuestSetupStatus("Requesting quest...");
            questPlannerClient.RequestQuestPlan(serverQuestMode, serverQuestTemplate, (success, plan, error) =>
            {
                if (!success || plan == null)
                {
                    authoringUiController?.SetQuestSetupStatus($"Quest request failed: {error}");
                    return;
                }

                lastServerQuestPlan = plan;
                authoringUiController?.SetScenarioMode("Server Quest Preview");
                authoringUiController?.SetQuestPreview(plan.title, BuildPreviewSteps(plan));
                authoringUiController?.SetQuestSetupStatus($"Quest preview ready: {GetDisplayTitle(plan)}");
            });
        }

        public void ApplyLastServerQuest()
        {
            ResolveReferences();
            if (lastServerQuestPlan == null)
            {
                authoringUiController?.SetQuestSetupStatus("No server quest available. Press F10 first.");
                return;
            }

            ApplyServerQuestPlan(lastServerQuestPlan, "last server quest");
        }

        public void RequestAndApplyServerQuest()
        {
            ResolveReferences();
            if (!questPlannerClient)
            {
                authoringUiController?.SetQuestSetupStatus("QuestPlannerClient not available.");
                return;
            }

            authoringUiController?.SetQuestSetupStatus("Requesting quest...");
            questPlannerClient.RequestQuestPlan(serverQuestMode, serverQuestTemplate, (success, plan, error) =>
            {
                if (!success || plan == null)
                {
                    authoringUiController?.SetQuestSetupStatus($"Quest request failed: {error}");
                    return;
                }

                lastServerQuestPlan = plan;
                authoringUiController?.SetScenarioMode("Server Quest Apply");
                authoringUiController?.SetQuestPreview(plan.title, BuildPreviewSteps(plan));
                authoringUiController?.SetQuestSetupStatus($"Quest received: {GetDisplayTitle(plan)}");
                ApplyServerQuestPlan(plan, "server request");
            });
        }

        private void RefreshUi(bool clearPreview)
        {
            authoringUiController?.SetScenarioMode(GetScenarioModeLabel(currentMode));
            if (clearPreview)
            {
                authoringUiController?.SetQuestPreview(null, null);
            }
        }

        private void ResolveReferences()
        {
            if (!authoringUiController)
            {
                authoringUiController = FindFirstObjectByType<DreamCodeVRAuthoringUIController>();
            }

            if (!questPlanApplier)
            {
                questPlanApplier = FindFirstObjectByType<QuestPlanApplier>();
            }

            if (!questPlannerClient)
            {
                questPlannerClient = FindFirstObjectByType<QuestPlannerClient>();
            }

            if (!questRuntimeState)
            {
                questRuntimeState = FindFirstObjectByType<QuestRuntimeState>();
            }
        }

        private void LoadDefaultMockAssets()
        {
            if (!fixedScenarioPlan)
            {
                fixedScenarioPlan = Resources.Load<TextAsset>("MockQuestPlans/MockQuestA_Ball");
            }

            if (!llmGeneratedScenarioPlan)
            {
                llmGeneratedScenarioPlan = Resources.Load<TextAsset>("MockQuestPlans/MockQuestB_Cube");
            }

            if (!manualDebugScenarioPlan)
            {
                manualDebugScenarioPlan = Resources.Load<TextAsset>("MockQuestPlans/MockQuestDebug");
            }

            if (!serverContractPlan)
            {
                serverContractPlan = Resources.Load<TextAsset>("MockQuestPlans/MockQuest_ServerContract");
            }
        }

        public void ApplyServerContractScenario()
        {
            ResolveReferences();
            if (!serverContractPlan || questPlanApplier == null)
            {
                authoringUiController?.SetQuestSetupStatus("Server contract mock quest not available.");
                return;
            }

            var plan = questPlanApplier.DeserializePlan(serverContractPlan.text);
            if (plan == null)
            {
                authoringUiController?.SetQuestSetupStatus("Server contract mock quest could not be deserialized.");
                return;
            }

            authoringUiController?.SetScenarioMode("Server Contract Debug");
            if (!questPlanApplier.ApplyQuestPlan(plan, out var validation))
            {
                var firstError = validation.errors.FirstOrDefault() ?? "Unknown validation error.";
                authoringUiController?.SetQuestSetupStatus($"Server contract apply failed: {firstError}");
                return;
            }

            StartQuestRuntime(plan);
        }

        private void ApplyServerQuestPlan(QuestPlan plan, string sourceLabel)
        {
            if (plan == null)
            {
                authoringUiController?.SetQuestSetupStatus("Server quest is null.");
                return;
            }

            if (!questPlanApplier)
            {
                authoringUiController?.SetQuestSetupStatus("QuestPlanApplier not available.");
                return;
            }

            var validation = questPlanApplier.ValidatePlan(plan);
            if (!validation.is_valid)
            {
                var firstError = validation.errors.FirstOrDefault() ?? "Unknown validation error.";
                Debug.LogWarning($"[QuestScenarioController] Server quest validation failed: {firstError}");
                authoringUiController?.SetQuestSetupStatus($"Quest apply failed: {firstError}");
                return;
            }

            Debug.Log($"[QuestScenarioController] Applying server quest {plan.quest_id}");
            if (!questPlanApplier.ApplyQuestPlan(plan, out validation))
            {
                var firstError = validation.errors.FirstOrDefault() ?? "Unknown validation error.";
                authoringUiController?.SetQuestSetupStatus($"Quest apply failed: {firstError}");
                return;
            }

            if (validation.warnings.Count > 0)
            {
                authoringUiController?.SetQuestSetupStatus($"Quest applied with warnings: {validation.warnings[0]}");
                StartQuestRuntime(plan);
                return;
            }

            authoringUiController?.SetQuestSetupStatus($"Quest applied: {GetDisplayTitle(plan)} ({sourceLabel})");
            StartQuestRuntime(plan);
        }

        private void StartQuestRuntime(QuestPlan plan)
        {
            if (plan == null)
            {
                return;
            }

            if (!questRuntimeState)
            {
                Debug.LogWarning("[QuestRuntime] QuestRuntimeState not available; runtime progress tracking skipped.");
                return;
            }

            questRuntimeState.StartQuest(plan);
            var currentTask = questRuntimeState.GetCurrentTask();

            Debug.Log($"[QuestRuntime] Started quest {plan.quest_id} title=\"{GetDisplayTitle(plan)}\"");
            if (currentTask != null)
            {
                Debug.Log($"[QuestRuntime] Current task {questRuntimeState.CurrentTaskIndex + 1}/{questRuntimeState.TaskEntries.Count}: {FormatTaskObjective(currentTask)}");
            }

            RefreshQuestRuntimeUi();
        }

        private void PrintQuestProgress()
        {
            ResolveReferences();
            if (!questRuntimeState || questRuntimeState.ActiveQuestPlan == null)
            {
                Debug.Log("[QuestRuntime] Quest inactive.");
                authoringUiController?.SetQuestSetupStatus("Quest inactive.");
                return;
            }

            var validatorReason = string.Empty;
            var currentTask = questRuntimeState.GetCurrentTask();
            if (currentTask != null && questTaskValidator.IsTaskComplete(currentTask, out validatorReason))
            {
                Debug.Log($"[QuestRuntime] Current task validator reports complete: {validatorReason}");
            }
            else if (!string.IsNullOrWhiteSpace(validatorReason))
            {
                Debug.Log($"[QuestRuntime] Validator: {validatorReason}");
            }

            var summary = questRuntimeState.GetProgressSummary();
            Debug.Log($"[QuestRuntime] {summary}");
            authoringUiController?.SetQuestSetupStatus(summary);
            RefreshQuestRuntimeUi();
        }

        private void CompleteCurrentTaskAndAdvance()
        {
            ResolveReferences();
            if (!questRuntimeState || questRuntimeState.ActiveQuestPlan == null)
            {
                authoringUiController?.SetQuestSetupStatus("No active quest to advance.");
                return;
            }

            var currentTask = questRuntimeState.GetCurrentTask();
            if (currentTask == null)
            {
                authoringUiController?.SetQuestSetupStatus("Quest has no active task.");
                RefreshQuestRuntimeUi();
                return;
            }

            if (!questRuntimeState.MarkCurrentTaskCompleted("Manual debug completion (F3)"))
            {
                authoringUiController?.SetQuestSetupStatus("Could not complete current task.");
                return;
            }

            Debug.Log($"[QuestRuntime] Completed task {questRuntimeState.CompletedTaskCount}/{questRuntimeState.TaskEntries.Count}: {FormatTaskObjective(currentTask)}");
            var advanced = questRuntimeState.AdvanceToNextTask();
            if (advanced)
            {
                var nextTask = questRuntimeState.GetCurrentTask();
                Debug.Log($"[QuestRuntime] Current task {questRuntimeState.CurrentTaskIndex + 1}/{questRuntimeState.TaskEntries.Count}: {FormatTaskObjective(nextTask)}");
            }
            else
            {
                Debug.Log($"[QuestRuntime] Quest completed: {questRuntimeState.GetQuestDisplayTitle()}");
            }

            RefreshQuestRuntimeUi();
        }

        private void ResetActiveQuestProgress()
        {
            ResolveReferences();
            if (!questRuntimeState)
            {
                authoringUiController?.SetQuestSetupStatus("QuestRuntimeState not available.");
                return;
            }

            questRuntimeState.ResetQuest();
            Debug.Log("[QuestRuntime] Quest progress reset.");
            authoringUiController?.SetQuestSetupStatus("Quest progress reset.");
            authoringUiController?.ClearQuestRuntimeInfo();
        }

        private void RefreshQuestRuntimeUi()
        {
            if (!authoringUiController)
            {
                return;
            }

            if (!questRuntimeState || questRuntimeState.ActiveQuestPlan == null)
            {
                authoringUiController.ClearQuestRuntimeInfo();
                return;
            }

            var totalTasks = questRuntimeState.TaskEntries.Count;
            var currentTaskNumber = totalTasks == 0
                ? 0
                : Mathf.Clamp(questRuntimeState.CurrentTaskIndex + 1, 1, totalTasks);

            if (questRuntimeState.IsQuestCompleted)
            {
                currentTaskNumber = totalTasks;
            }

            var instruction = questRuntimeState.IsQuestCompleted
                ? "Quest completed."
                : GetUserFacingTaskInstruction(questRuntimeState.GetCurrentTask());

            authoringUiController.SetQuestRuntimeInfo(
                currentTaskNumber,
                totalTasks,
                instruction,
                questRuntimeState.LastTaskResult);
        }

        private static IEnumerable<string> BuildPreviewSteps(QuestPlan plan)
        {
            return (plan.tasks ?? new List<QuestTaskSpec>())
                .OrderBy(task => task.step)
                .Select(task => string.IsNullOrWhiteSpace(task.description)
                    ? $"{task.step}. {task.type}"
                    : task.description)
                .ToList();
        }

        private static string GetDisplayTitle(QuestPlan plan)
        {
            if (plan == null)
            {
                return "Untitled Quest";
            }

            return string.IsNullOrWhiteSpace(plan.title) ? plan.quest_id : plan.title;
        }

        private TextAsset GetCurrentPlanAsset()
        {
            switch (currentMode)
            {
                case QuestScenarioMode.FixedScenario:
                    return fixedScenarioPlan;
                case QuestScenarioMode.LlmGeneratedScenario:
                    return llmGeneratedScenarioPlan;
                default:
                    return manualDebugScenarioPlan;
            }
        }

        private string GetPreviewStatusLabel()
        {
            return currentMode == QuestScenarioMode.LlmGeneratedScenario
                ? "LLM Generated Scenario placeholder: previewing local mock quest."
                : $"Previewing {GetScenarioModeLabel(currentMode)}.";
        }

        private static string GetScenarioModeLabel(QuestScenarioMode mode)
        {
            switch (mode)
            {
                case QuestScenarioMode.FixedScenario:
                    return "Fixed Scenario";
                case QuestScenarioMode.LlmGeneratedScenario:
                    return "LLM Generated Scenario";
                default:
                    return "Manual Debug Scenario";
            }
        }

        private static string FormatTaskObjective(QuestTaskSpec task)
        {
            if (task == null)
            {
                return "Unknown task";
            }

            if (!string.IsNullOrWhiteSpace(task.description))
            {
                return task.description.Trim();
            }

            return string.IsNullOrWhiteSpace(task.target)
                ? task.type
                : $"{task.type} target={task.target}";
        }

        private static string GetUserFacingTaskInstruction(QuestTaskSpec task)
        {
            if (task == null)
            {
                return "No active task.";
            }

            if (!string.IsNullOrWhiteSpace(task.description))
            {
                return task.description.Trim();
            }

            switch (task.type)
            {
                case "StraightenAndMovePainting":
                    return "Straighten the painting and move it into place.";
                case "ReadClue":
                    return "Read the clue note.";
                case "CreateTextureAndPlaceObject":
                    return "Create the required object and place it in the target area.";
                case "UnlockDoorWithKey":
                    return "Use the correct key on the exit door.";
                default:
                    return "Complete the current task.";
            }
        }
    }
}
