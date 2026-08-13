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
        public float CurrentTaskStartTime { get; private set; }
        public int IncorrectAttempts { get; private set; }
        public int HintCount { get; private set; }
        public string LastIncorrectAttempt { get; private set; }
        private readonly HashSet<string> discoveredClues = new HashSet<string>();
        private readonly List<string> recentlyInteractedObjectIds = new List<string>();
        public IReadOnlyCollection<string> DiscoveredClues => discoveredClues;
        public IReadOnlyList<string> RecentlyInteractedObjectIds => recentlyInteractedObjectIds;
        public QuestEventBus eventBus;

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
            CurrentTaskStartTime = Time.unscaledTime;
            EnsureEventBus(); eventBus?.Publish(QuestEventType.TaskStarted, taskEntries[CurrentTaskIndex].task.target);
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
            EnsureEventBus(); eventBus?.Publish(QuestEventType.TaskCompleted, entry.task.target);
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
                    CurrentTaskStartTime = Time.unscaledTime;
                    EnsureEventBus(); eventBus?.Publish(QuestEventType.TaskStarted, taskEntries[CurrentTaskIndex].task.target);
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
            IncorrectAttempts = 0; HintCount = 0; LastIncorrectAttempt = null; discoveredClues.Clear(); recentlyInteractedObjectIds.Clear();
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
            RegisterInteraction(objectId); discoveredClues.Add(objectId);
            ObjectInspected?.Invoke(objectId);
        }

        public void OnObjectCreated(string objectId)
        {
            RegisterInteraction(objectId); EnsureEventBus(); eventBus?.Publish(QuestEventType.ObjectCreated, objectId);
            ObjectCreated?.Invoke(objectId);
        }

        public void OnObjectPlaced(string objectId, string anchorId)
        {
            RegisterInteraction(objectId); EnsureEventBus(); eventBus?.Publish(QuestEventType.ObjectPlacedInZone, objectId, anchorId);
            ObjectPlaced?.Invoke(objectId, anchorId);
        }

        public void OnObjectUnlocked(string targetId, string keyId)
        {
            RegisterInteraction(targetId); EnsureEventBus(); eventBus?.Publish(QuestEventType.LockOpened, targetId, keyId);
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

        public void ActivateDynamicTask(QuestTaskSpec task)
        {
            if (task == null) return;
            ActiveQuestPlan ??= new QuestPlan { quest_id = "dynamic_story" };
            taskEntries.Clear(); taskEntries.Add(new QuestTaskRuntimeEntry { task = task, status = QuestTaskStatus.Active });
            CurrentTaskIndex = 0; CurrentTaskStartTime = Time.unscaledTime;
            EnsureEventBus(); eventBus?.Publish(QuestEventType.TaskStarted, task.target);
            LastTaskResult = $"Current objective: {GetObjectiveLabel(task)}";
        }

        public void RecordIncorrectAttempt(string objectId, string reason) { IncorrectAttempts++; LastIncorrectAttempt = reason; RegisterInteraction(objectId); EnsureEventBus(); eventBus?.Publish(QuestEventType.IncorrectAttempt, objectId, null, reason); }
        public void RecordHintRequested() { HintCount++; EnsureEventBus(); eventBus?.Publish(QuestEventType.HintRequested); }
        private void RegisterInteraction(string objectId) { if (string.IsNullOrWhiteSpace(objectId)) return; recentlyInteractedObjectIds.Remove(objectId); recentlyInteractedObjectIds.Insert(0, objectId); if (recentlyInteractedObjectIds.Count > 8) recentlyInteractedObjectIds.RemoveAt(recentlyInteractedObjectIds.Count - 1); }
        private void EnsureEventBus() { if (!eventBus) eventBus = QuestEventBus.Instance ? QuestEventBus.Instance : FindFirstObjectByType<QuestEventBus>(); }

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
