using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    public enum QuestTaskStatus
    {
        NotStarted,
        Active,
        Completed,
        Failed,
        Skipped
    }

    [Serializable]
    public class QuestTaskRuntimeEntry
    {
        public QuestTaskSpec task;
        public QuestTaskStatus status = QuestTaskStatus.NotStarted;
        public string lastReason;
    }

    public class QuestRuntimeState : MonoBehaviour
    {
        public QuestPlan ActiveQuestPlan { get; private set; }
        public int CurrentTaskIndex { get; private set; } = -1;
        public string LastTaskResult { get; private set; }

        public event Action<QuestTaskSpec> TaskCompleted;
        public event Action<string> ObjectInspected;
        public event Action<string> ObjectCreated;
        public event Action<string, string> ObjectPlaced;
        public event Action<string, string> ObjectUnlocked;
        public event Action<string> SceneActionApplied;

        private readonly List<QuestTaskRuntimeEntry> taskEntries = new List<QuestTaskRuntimeEntry>();

        public IReadOnlyList<QuestTaskRuntimeEntry> TaskEntries => taskEntries;
        public int CompletedTaskCount => taskEntries.Count(entry => entry.status == QuestTaskStatus.Completed);
        public int FailedTaskCount => taskEntries.Count(entry => entry.status == QuestTaskStatus.Failed);

        public bool IsQuestActive => ActiveQuestPlan != null && !IsQuestCompleted;

        public bool IsQuestCompleted => ActiveQuestPlan != null
            && taskEntries.Count > 0
            && taskEntries.All(entry => entry.status == QuestTaskStatus.Completed
                || entry.status == QuestTaskStatus.Skipped);

        public void StartQuest(QuestPlan plan)
        {
            ResetQuest();
            if (plan == null)
            {
                return;
            }

            ActiveQuestPlan = plan;
            foreach (var task in (plan.tasks ?? new List<QuestTaskSpec>()).OrderBy(task => task.step))
            {
                taskEntries.Add(new QuestTaskRuntimeEntry
                {
                    task = task,
                    status = QuestTaskStatus.NotStarted
                });
            }

            if (taskEntries.Count == 0)
            {
                LastTaskResult = "Quest started with no tasks.";
                return;
            }

            CurrentTaskIndex = 0;
            taskEntries[CurrentTaskIndex].status = QuestTaskStatus.Active;
            LastTaskResult = $"Started quest: {GetObjectiveLabel(taskEntries[CurrentTaskIndex].task)}";
        }

        public QuestTaskSpec GetCurrentTask()
        {
            if (CurrentTaskIndex < 0 || CurrentTaskIndex >= taskEntries.Count)
            {
                return null;
            }

            return taskEntries[CurrentTaskIndex].task;
        }

        public bool MarkCurrentTaskCompleted(string reason)
        {
            if (!TryGetCurrentEntry(out var entry))
            {
                return false;
            }

            entry.status = QuestTaskStatus.Completed;
            entry.lastReason = reason;
            LastTaskResult = BuildTaskResultLabel(entry.task, "completed", reason);
            TaskCompleted?.Invoke(entry.task);
            return true;
        }

        public bool MarkCurrentTaskFailed(string reason)
        {
            if (!TryGetCurrentEntry(out var entry))
            {
                return false;
            }

            entry.status = QuestTaskStatus.Failed;
            entry.lastReason = reason;
            LastTaskResult = BuildTaskResultLabel(entry.task, "failed", reason);
            return true;
        }

        public bool AdvanceToNextTask()
        {
            if (taskEntries.Count == 0)
            {
                CurrentTaskIndex = -1;
                return false;
            }

            var nextIndex = CurrentTaskIndex + 1;
            while (nextIndex < taskEntries.Count)
            {
                if (taskEntries[nextIndex].status == QuestTaskStatus.NotStarted)
                {
                    CurrentTaskIndex = nextIndex;
                    taskEntries[CurrentTaskIndex].status = QuestTaskStatus.Active;
                    LastTaskResult = $"Current objective: {GetObjectiveLabel(taskEntries[CurrentTaskIndex].task)}";
                    return true;
                }

                nextIndex++;
            }

            CurrentTaskIndex = taskEntries.Count;
            LastTaskResult = $"Quest completed: {GetQuestDisplayTitle()}";
            return false;
        }

        public void ResetQuest()
        {
            ActiveQuestPlan = null;
            CurrentTaskIndex = -1;
            LastTaskResult = null;
            taskEntries.Clear();
        }

        public string GetProgressSummary()
        {
            if (ActiveQuestPlan == null)
            {
                return "Quest inactive.";
            }

            var currentTask = GetCurrentTask();
            var currentObjective = currentTask != null ? GetObjectiveLabel(currentTask) : "none";
            return $"quest=\"{GetQuestDisplayTitle()}\" active={IsQuestActive} completed={IsQuestCompleted} progress={CompletedTaskCount}/{taskEntries.Count} failed={FailedTaskCount} current=\"{currentObjective}\" last=\"{LastTaskResult ?? "none"}\"";
        }

        public void OnSceneActionApplied(string actionResult)
        {
            SceneActionApplied?.Invoke(actionResult);
        }

        public void OnObjectInspected(string objectId)
        {
            ObjectInspected?.Invoke(objectId);
        }

        public void OnObjectCreated(string objectId)
        {
            ObjectCreated?.Invoke(objectId);
        }

        public void OnObjectPlaced(string objectId, string anchorId)
        {
            ObjectPlaced?.Invoke(objectId, anchorId);
        }

        public void OnObjectUnlocked(string targetId, string keyId)
        {
            ObjectUnlocked?.Invoke(targetId, keyId);
        }

        public string GetQuestDisplayTitle()
        {
            if (ActiveQuestPlan == null)
            {
                return "No Active Quest";
            }

            return string.IsNullOrWhiteSpace(ActiveQuestPlan.title)
                ? ActiveQuestPlan.quest_id
                : ActiveQuestPlan.title;
        }

        public string GetCurrentObjectiveText()
        {
            var currentTask = GetCurrentTask();
            return currentTask == null ? "No active objective." : GetObjectiveLabel(currentTask);
        }

        private bool TryGetCurrentEntry(out QuestTaskRuntimeEntry entry)
        {
            entry = null;
            if (CurrentTaskIndex < 0 || CurrentTaskIndex >= taskEntries.Count)
            {
                return false;
            }

            entry = taskEntries[CurrentTaskIndex];
            return entry != null;
        }

        private static string BuildTaskResultLabel(QuestTaskSpec task, string verb, string reason)
        {
            var label = GetObjectiveLabel(task);
            return string.IsNullOrWhiteSpace(reason)
                ? $"Task {verb}: {label}"
                : $"Task {verb}: {label} ({reason})";
        }

        private static string GetObjectiveLabel(QuestTaskSpec task)
        {
            if (task == null)
            {
                return "Unknown task";
            }

            if (!string.IsNullOrWhiteSpace(task.description))
            {
                return task.description.Trim();
            }

            var target = string.IsNullOrWhiteSpace(task.target) ? string.Empty : $" target={task.target.Trim()}";
            return $"{task.type}{target}";
        }
    }
}
