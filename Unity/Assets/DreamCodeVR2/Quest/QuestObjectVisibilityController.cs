using System;
using System.Collections.Generic;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.ExperimentalAuthoring;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    // Reversible gating for fixed plans. Furniture, anchors, exit door and locks are never hidden.
    public class QuestObjectVisibilityController : MonoBehaviour
    {
        private readonly Dictionary<AIEditableObject,bool> originalActive=new Dictionary<AIEditableObject,bool>();
        private static readonly HashSet<string> PuzzleIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "key_001","key_002","clue_note_001","clue_note_002","painting_001","sphere_001","lamp_001","lamp_002","lamp_003","lamp_004" };

        public void ApplyFixedInstance(QuestInstance instance)
        {
            RestoreAll();
            if(instance?.relevantObjectIds==null||instance.relevantObjectIds.Length==0)return;
            var keep=new HashSet<string>(instance.relevantObjectIds,StringComparer.OrdinalIgnoreCase);
            foreach(var p in instance.placements??Array.Empty<QuestPlacementBinding>())keep.Add(p?.objectId);
            foreach(var n in instance.notes??Array.Empty<QuestNoteBinding>())keep.Add(n?.noteId);
            foreach(var b in instance.lockBindings??Array.Empty<QuestLockBinding>()){keep.Add(b?.requiredKeyId);keep.Add(b?.lockId);keep.Add(b?.targetObjectId);}
            keep.Add(instance.targetDrawerId);keep.Add(instance.selectedLampId);keep.Add(instance.c1SphereId);
            foreach(var task in instance.plan?.tasks??new List<QuestTaskSpec>()){keep.Add(task?.target);foreach(var condition in task?.successConditions??Array.Empty<RuntimeSuccessCondition>())keep.Add(condition?.object_id);}
            HideOutside(keep);
        }
        public void ApplyDynamicCandidatePool(string[] candidateIds)
        {
            RestoreAll(); if(candidateIds==null||candidateIds.Length==0)return;
            HideOutside(new HashSet<string>(candidateIds,StringComparer.OrdinalIgnoreCase));
        }
        public void RestoreAll(){foreach(var pair in originalActive)if(pair.Key)pair.Key.gameObject.SetActive(pair.Value);originalActive.Clear();}
        private void HideOutside(HashSet<string> keep)
        {
            foreach(var item in FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(!item||string.IsNullOrWhiteSpace(item.objectId)||!PuzzleIds.Contains(item.objectId)||keep.Contains(item.objectId))continue;
                originalActive[item]=item.gameObject.activeSelf;item.gameObject.SetActive(false);
                DreamCodeVR2ClientLogger.Event("quest","QUEST_OBJECT_HIDDEN",null,new { object_id=item.objectId });
            }
        }
    }
}
