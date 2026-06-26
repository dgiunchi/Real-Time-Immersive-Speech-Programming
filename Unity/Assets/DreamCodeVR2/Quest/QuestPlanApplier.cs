using System.Collections.Generic;
using System.Linq;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.UI;
using TMPro;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    public class QuestPlanApplier : MonoBehaviour
    {
        private static readonly string[] VariableSetupObjectIds = { "key_001", "key_002", "clue_note_002" };

        public DreamCodeVRAuthoringUIController authoringUiController;
        public RuntimeCreatableObjectCatalog runtimeCreatableObjectCatalog;
        public bool logQuestActions = true;

        private readonly Dictionary<string, AIEditableObject> editableObjectsById = new Dictionary<string, AIEditableObject>();
        private readonly Dictionary<string, List<Transform>> anchorsByName = new Dictionary<string, List<Transform>>();

        public QuestPlan DeserializePlan(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var plan = JsonUtility.FromJson<QuestPlan>(json);
            WarnOnLegacyObjectFields(plan);
            return plan;
        }

        public QuestValidationResult ValidatePlan(QuestPlan plan)
        {
            var result = new QuestValidationResult();
            if (plan == null)
            {
                result.AddError("QuestPlan is null or could not be deserialized.");
                return result;
            }

            RefreshSceneCaches();
            ValidateObjectReference(result, plan.final_key, "final_key");
            ValidateObjectReference(result, plan.drawer_key, "drawer_key");

            foreach (var task in plan.tasks ?? new List<QuestTaskSpec>())
            {
                ValidateObjectReference(result, task.target, $"task[{task.step}].target");
                ValidateObjectReference(result, task.key, $"task[{task.step}].key");
                ValidateObjectReference(result, task.@lock, $"task[{task.step}].lock");
                ValidateCreatableReference(result, task.object_to_create, $"task[{task.step}].object_to_create");
                ValidateAnchorReference(result, task.target_anchor, $"task[{task.step}].target_anchor");
            }

            foreach (var action in plan.initial_setup ?? new List<QuestInitialSetupAction>())
            {
                ValidateObjectReference(result, action.ObjectReference, $"initial_setup.{action.action}.object", true);
                ValidateObjectReference(result, action.parent, $"initial_setup.{action.action}.parent");
                ValidateAnchorReference(result, action.anchor, $"initial_setup.{action.action}.anchor", action.parent);
            }

            ValidateUniqueVariablePlacementAnchors(plan, result);

            foreach (var clue in plan.clues ?? new List<QuestClueSpec>())
            {
                ValidateObjectReference(result, clue.ObjectReference, "clues.object");
            }

            if (plan.error_risk != null)
            {
                ValidateObjectReference(result, plan.error_risk.correct_key, "error_risk.correct_key");
                ValidateObjectReference(result, plan.error_risk.wrong_key, "error_risk.wrong_key");
                ValidateRuntimeCreatableCapableReference(result, plan, plan.error_risk.target, "error_risk.target");
                ValidateAnchorOrObjectReference(result, plan, plan.error_risk.correct_target, "error_risk.correct_target");
                foreach (var distractor in plan.error_risk.distractor_targets ?? new List<string>())
                {
                    ValidateAnchorOrObjectReference(result, plan, distractor, "error_risk.distractor_targets");
                }
            }

            return result;
        }

        public bool ApplyQuestPlan(QuestPlan plan, out QuestValidationResult validation)
        {
            validation = ValidatePlan(plan);
            if (!validation.is_valid)
            {
                var firstError = validation.errors.FirstOrDefault() ?? "Unknown validation error.";
                authoringUiController?.SetQuestSetupStatus($"Quest plan validation failed: {firstError}");
                foreach (var error in validation.errors)
                {
                    Debug.LogWarning($"[QuestPlan] {error}");
                }

                return false;
            }

            RefreshSceneCaches();
            var canonicalClueTextByObject = BuildCanonicalClueTextMap(plan);

            foreach (var action in plan.initial_setup ?? new List<QuestInitialSetupAction>())
            {
                ApplySetupAction(action, canonicalClueTextByObject);
            }

            foreach (var clue in plan.clues ?? new List<QuestClueSpec>())
            {
                SetClueText(clue.ObjectReference, clue.text);
            }

            var previewSteps = (plan.tasks ?? new List<QuestTaskSpec>())
                .OrderBy(task => task.step)
                .Select(task => string.IsNullOrWhiteSpace(task.description)
                    ? $"{task.type} -> {task.target}"
                    : task.description)
                .ToList();

            authoringUiController?.SetQuestPreview(plan.title, previewSteps);
            authoringUiController?.SetQuestSetupStatus($"Applied quest setup: {plan.title}");

            if (logQuestActions)
            {
                Debug.Log($"[QuestPlan] Applied plan {plan.quest_id} ({plan.title})");
            }

            return true;
        }

        private void ApplySetupAction(QuestInitialSetupAction action, Dictionary<string, string> canonicalClueTextByObject)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.action))
            {
                return;
            }

            switch (action.action)
            {
                case "PlaceObject":
                    PlaceObject(action.ObjectReference, action.anchor, action.parent);
                    break;
                case "HideObject":
                    SetObjectActive(action.ObjectReference, false);
                    break;
                case "ShowObject":
                    SetObjectActive(action.ObjectReference, true);
                    break;
                case "SetClueText":
                    ApplyLegacyClueTextAction(action, canonicalClueTextByObject);
                    break;
                case "SetParent":
                    SetParent(action.ObjectReference, action.parent);
                    break;
                case "ResetCreatedObject":
                    runtimeCreatableObjectCatalog?.ResetCreatedObject(action.ObjectReference);
                    break;
                case "SetMaterial":
                    SetMaterial(action.ObjectReference, action.material);
                    break;
                default:
                    Debug.LogWarning($"[QuestPlan] Unsupported setup action {action.action}");
                    break;
            }
        }

        private void ApplyLegacyClueTextAction(QuestInitialSetupAction action, Dictionary<string, string> canonicalClueTextByObject)
        {
            var objectId = action.ObjectReference;
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return;
            }

            if (canonicalClueTextByObject.TryGetValue(objectId, out var canonicalText))
            {
                if (!string.Equals(canonicalText ?? string.Empty, action.text ?? string.Empty))
                {
                    Debug.LogWarning($"[QuestPlan] conflicting clue text definitions for {objectId}. Preferring clues[] over legacy initial_setup SetClueText.");
                }

                return;
            }

            SetClueText(objectId, action.text);
        }

        private void PlaceObject(string objectId, string anchorName, string parentObjectId)
        {
            var editable = ResolveOrCreateEditableObject(objectId);
            if (!editable)
            {
                Debug.LogWarning($"[QuestPlan] Could not resolve object for placement: {objectId}");
                return;
            }

            var anchor = ResolveAnchor(anchorName, parentObjectId);
            if (!anchor)
            {
                Debug.LogWarning($"[QuestPlan] Could not resolve anchor {anchorName} for {objectId}");
                return;
            }

            editable.gameObject.SetActive(true);
            editable.transform.SetParent(anchor, false);
            editable.transform.localPosition = Vector3.zero;
            editable.transform.localRotation = Quaternion.identity;

            var parentEditable = ResolveEditableObject(parentObjectId);
            if (parentEditable && parentEditable.transform != anchor)
            {
                editable.transform.SetParent(parentEditable.transform, true);
            }

            if (logQuestActions)
            {
                Debug.Log($"[QuestPlan] PlaceObject {objectId} -> {anchorName} parent={parentObjectId}");
            }
        }

        private void SetObjectActive(string objectId, bool active)
        {
            var editable = ResolveOrCreateEditableObject(objectId);
            if (editable)
            {
                editable.gameObject.SetActive(active);
            }
        }

        private void SetParent(string objectId, string parentObjectId)
        {
            var editable = ResolveOrCreateEditableObject(objectId);
            var parentEditable = ResolveEditableObject(parentObjectId);
            if (!editable || !parentEditable)
            {
                return;
            }

            editable.transform.SetParent(parentEditable.transform, true);
        }

        private void SetMaterial(string objectId, string materialName)
        {
            var editable = ResolveOrCreateEditableObject(objectId);
            if (!editable || runtimeCreatableObjectCatalog == null)
            {
                return;
            }

            if (!runtimeCreatableObjectCatalog.ApplyMaterial(editable.gameObject, materialName))
            {
                Debug.LogWarning($"[QuestPlan] Could not resolve material {materialName} for {objectId}");
            }
        }

        private void SetClueText(string objectId, string text)
        {
            var editable = ResolveEditableObject(objectId);
            if (!editable)
            {
                Debug.LogWarning($"[QuestPlan] Could not resolve clue object {objectId}");
                return;
            }

            var tmp = editable.GetComponentInChildren<TMP_Text>(true);
            if (!tmp)
            {
                Debug.LogWarning($"[QuestPlan] No TMP_Text found for clue object {objectId}");
                return;
            }

            tmp.text = text;
            editable.description = text;

            if (logQuestActions)
            {
                Debug.Log($"[QuestPlan] SetClueText {objectId} = \"{text}\"");
            }
        }

        private AIEditableObject ResolveOrCreateEditableObject(string objectId)
        {
            var editable = ResolveEditableObject(objectId);
            if (editable)
            {
                return editable;
            }

            if (!runtimeCreatableObjectCatalog || !runtimeCreatableObjectCatalog.IsSupportedObjectId(objectId))
            {
                return null;
            }

            var created = runtimeCreatableObjectCatalog.GetOrCreate(objectId);
            if (!created)
            {
                return null;
            }

            RefreshSceneCaches();
            return ResolveEditableObject(objectId);
        }

        private AIEditableObject ResolveEditableObject(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return null;
            }

            if (editableObjectsById.TryGetValue(objectId, out var editable) && editable)
            {
                return editable;
            }

            RefreshSceneCaches();
            editableObjectsById.TryGetValue(objectId, out editable);
            return editable;
        }

        private Transform ResolveAnchor(string anchorName, string preferredParentObjectId = null)
        {
            if (string.IsNullOrWhiteSpace(anchorName))
            {
                return null;
            }

            var normalizedAnchorName = NormalizeAnchorName(anchorName);

            var parentEditable = ResolveEditableObject(preferredParentObjectId);
            if (parentEditable)
            {
                var nested = parentEditable.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(child => child.name == normalizedAnchorName);
                if (nested)
                {
                    return nested;
                }
            }

            if (!anchorsByName.TryGetValue(normalizedAnchorName, out var anchors))
            {
                return null;
            }

            return anchors.FirstOrDefault(anchor => anchor);
        }

        private void RefreshSceneCaches()
        {
            editableObjectsById.Clear();
            foreach (var editable in FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!editable || string.IsNullOrWhiteSpace(editable.objectId))
                {
                    continue;
                }

                editableObjectsById[editable.objectId] = editable;
            }

            anchorsByName.Clear();
            foreach (var candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!candidate)
                {
                    continue;
                }

                if (!candidate.name.EndsWith("_anchor"))
                {
                    continue;
                }

                if (!anchorsByName.TryGetValue(candidate.name, out var anchors))
                {
                    anchors = new List<Transform>();
                    anchorsByName[candidate.name] = anchors;
                }

                anchors.Add(candidate);
            }
        }

        private void ValidateObjectReference(QuestValidationResult result, string objectId, string fieldName, bool allowCreatable = false)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return;
            }

            if (ResolveEditableObject(objectId))
            {
                return;
            }

            if (allowCreatable && runtimeCreatableObjectCatalog && runtimeCreatableObjectCatalog.IsSupportedObjectId(objectId))
            {
                return;
            }

            result.AddError($"Missing object reference for {fieldName}: {objectId}");
        }

        private void ValidateCreatableReference(QuestValidationResult result, string objectId, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return;
            }

            if (runtimeCreatableObjectCatalog && runtimeCreatableObjectCatalog.IsSupportedObjectId(objectId))
            {
                return;
            }

            result.AddError($"Unsupported creatable object for {fieldName}: {objectId}");
        }

        private void ValidateAnchorReference(QuestValidationResult result, string anchorName, string fieldName, string preferredParentObjectId = null)
        {
            if (string.IsNullOrWhiteSpace(anchorName))
            {
                return;
            }

            if (ResolveAnchor(anchorName, preferredParentObjectId))
            {
                return;
            }

            result.AddError($"Missing anchor reference for {fieldName}: {anchorName}");
        }

        private void ValidateRuntimeCreatableCapableReference(QuestValidationResult result, QuestPlan plan, string objectId, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return;
            }

            if (ResolveEditableObject(objectId))
            {
                return;
            }

            if (IsRuntimeCreatableReference(plan, objectId))
            {
                return;
            }

            result.AddError($"Missing object reference for {fieldName}: {objectId}");
        }

        private void ValidateAnchorOrObjectReference(QuestValidationResult result, QuestPlan plan, string reference, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return;
            }

            if (ResolveEditableObject(reference))
            {
                return;
            }

            if (IsRuntimeCreatableReference(plan, reference))
            {
                return;
            }

            if (ResolveAnchor(reference))
            {
                return;
            }

            result.AddError($"Missing anchor or object reference for {fieldName}: {reference}");
        }

        private void ValidateUniqueVariablePlacementAnchors(QuestPlan plan, QuestValidationResult result)
        {
            var variablePlacementsByLocation = new Dictionary<string, List<string>>();
            var variablePlaceActions = (plan.initial_setup ?? new List<QuestInitialSetupAction>())
                .Where(action => action != null
                    && action.action == "PlaceObject"
                    && !string.IsNullOrWhiteSpace(action.ObjectReference)
                    && VariableSetupObjectIds.Contains(action.ObjectReference))
                .ToList();

            foreach (var action in variablePlaceActions)
            {
                if (string.IsNullOrWhiteSpace(action.anchor))
                {
                    result.variable_anchors_unique = false;
                    result.AddError($"Variable setup object {action.ObjectReference} is placed without an anchor.");
                    continue;
                }

                var placementKey = BuildPlacementKey(action.parent, action.anchor);
                if (!variablePlacementsByLocation.TryGetValue(placementKey, out var occupants))
                {
                    occupants = new List<string>();
                    variablePlacementsByLocation[placementKey] = occupants;
                }

                occupants.Add(action.ObjectReference);
            }

            foreach (var placement in variablePlacementsByLocation)
            {
                if (placement.Value.Count < 2)
                {
                    continue;
                }

                result.variable_anchors_unique = false;
                result.AddError($"Variable setup objects must use unique placement anchors. Duplicate anchor '{placement.Key}' used by: {string.Join(", ", placement.Value)}.");
            }
        }

        private static string BuildPlacementKey(string parentObjectId, string anchorName)
        {
            if (string.IsNullOrWhiteSpace(anchorName))
            {
                return string.Empty;
            }

            var trimmedAnchor = anchorName.Trim();
            if (trimmedAnchor.Contains("."))
            {
                return trimmedAnchor;
            }

            return string.IsNullOrWhiteSpace(parentObjectId)
                ? trimmedAnchor
                : $"{parentObjectId.Trim()}.{trimmedAnchor}";
        }

        private void WarnOnLegacyObjectFields(QuestPlan plan)
        {
            if (plan == null)
            {
                return;
            }

            foreach (var action in plan.initial_setup ?? new List<QuestInitialSetupAction>())
            {
                if (action != null && action.UsesLegacyObjectId)
                {
                    Debug.LogWarning($"[QuestPlan] legacy field 'object_id' used in initial_setup for action {action.action}. Please switch to canonical field 'object'.");
                }
            }

            foreach (var clue in plan.clues ?? new List<QuestClueSpec>())
            {
                if (clue != null && clue.UsesLegacyObjectId)
                {
                    Debug.LogWarning("[QuestPlan] legacy field 'object_id' used in clues entry. Please switch to canonical field 'object'.");
                }
            }
        }

        private static string NormalizeAnchorName(string anchorName)
        {
            if (string.IsNullOrWhiteSpace(anchorName))
            {
                return anchorName;
            }

            var trimmed = anchorName.Trim();
            var lastDotIndex = trimmed.LastIndexOf('.');
            return lastDotIndex >= 0 && lastDotIndex < trimmed.Length - 1
                ? trimmed.Substring(lastDotIndex + 1)
                : trimmed;
        }

        private static Dictionary<string, string> BuildCanonicalClueTextMap(QuestPlan plan)
        {
            var clueTextByObject = new Dictionary<string, string>();
            foreach (var clue in plan.clues ?? new List<QuestClueSpec>())
            {
                if (clue == null || string.IsNullOrWhiteSpace(clue.ObjectReference))
                {
                    continue;
                }

                clueTextByObject[clue.ObjectReference] = clue.text;
            }

            return clueTextByObject;
        }

        private bool IsRuntimeCreatableReference(QuestPlan plan, string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return false;
            }

            if (runtimeCreatableObjectCatalog && runtimeCreatableObjectCatalog.IsSupportedObjectId(objectId))
            {
                return true;
            }

            foreach (var task in plan.tasks ?? new List<QuestTaskSpec>())
            {
                if (!string.IsNullOrWhiteSpace(task.object_to_create) && task.object_to_create == objectId)
                {
                    return true;
                }
            }

            foreach (var action in plan.initial_setup ?? new List<QuestInitialSetupAction>())
            {
                if (action == null || action.action != "ResetCreatedObject")
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(action.ObjectReference) && action.ObjectReference == objectId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
