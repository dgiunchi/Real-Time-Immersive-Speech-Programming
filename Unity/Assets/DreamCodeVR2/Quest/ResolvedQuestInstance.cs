using System;
using System.Collections.Generic;
using DreamCodeVR2.ExperimentalAuthoring;

namespace DreamCodeVR2.Quest
{
    // Single client-side interpretation of server identifiers and physical bindings.
    // Server payloads remain untouched; all world systems consume these canonical IDs.
    public sealed class ResolvedQuestInstance
    {
        public QuestInstance source;
        public string questId;
        public string targetDrawerId;
        public QuestLockBinding[] lockBindings=Array.Empty<QuestLockBinding>();
        public QuestPlacementBinding[] placements=Array.Empty<QuestPlacementBinding>();
        public QuestInitialStateBinding[] initialStates=Array.Empty<QuestInitialStateBinding>();
        public QuestRuntimeObjectSpec[] requiredRuntimeObjects=Array.Empty<QuestRuntimeObjectSpec>();
        public string[] relevantObjectIds=Array.Empty<string>();
    }

    public static class QuestCanonicalIds
    {
        public static string Normalize(string id)
        {
            if(string.Equals(id,"lock_drawer_001",StringComparison.OrdinalIgnoreCase)||string.Equals(id,"lock_drawer_002",StringComparison.OrdinalIgnoreCase))return "lock_002";
            if(string.Equals(id,"lock_drawer_003",StringComparison.OrdinalIgnoreCase))return "lock_003";
            return id;
        }
        public static string NormalizeTaskObject(string taskId,string objectId)
        {
            return Normalize(objectId);
        }
    }

    public static class QuestInstanceResolver
    {
        public static ResolvedQuestInstance Resolve(QuestInstance instance)
        {
            var resolved=new ResolvedQuestInstance{source=instance,questId=instance?.questId,targetDrawerId=instance?.targetDrawerId,placements=instance?.placements??Array.Empty<QuestPlacementBinding>(),initialStates=instance?.initialStates??Array.Empty<QuestInitialStateBinding>(),relevantObjectIds=instance?.relevantObjectIds??Array.Empty<string>()};
            var runtime=new List<QuestRuntimeObjectSpec>();foreach(var spec in instance?.requiredRuntimeObjects??Array.Empty<QuestRuntimeObjectSpec>())if(spec!=null&&!string.IsNullOrWhiteSpace(spec.objectId))runtime.Add(new QuestRuntimeObjectSpec{objectId=QuestCanonicalIds.Normalize(spec.objectId),primitive=spec.primitive,semanticProfile=spec.semanticProfile,presetId=spec.presetId,materialProfile=spec.materialProfile,initialAnchorId=spec.initialAnchorId,initialSemanticState=spec.initialSemanticState,initialGrabbable=spec.initialGrabbable,canonicalSizeMeters=spec.canonicalSizeMeters,canonicalScale=spec.canonicalScale,source=spec.source});resolved.requiredRuntimeObjects=runtime.ToArray();
            var locks=new List<QuestLockBinding>();var legacyConversionUsed=false;
            foreach(var binding in instance?.lockBindings??Array.Empty<QuestLockBinding>())
            {
                if(binding==null)continue;
                var lockId=QuestCanonicalIds.Normalize(binding.lockId);
                legacyConversionUsed|=!string.Equals(lockId,binding.lockId,StringComparison.Ordinal);
                var target=binding.targetObjectId;
                locks.Add(new QuestLockBinding{lockId=lockId,requiredKeyId=QuestCanonicalIds.Normalize(binding.requiredKeyId),targetObjectId=target});
                if(string.Equals(binding.targetObjectId,instance.targetDrawerId,StringComparison.OrdinalIgnoreCase)||string.Equals(target,resolved.targetDrawerId,StringComparison.OrdinalIgnoreCase))resolved.targetDrawerId=target;
            }
            resolved.lockBindings=locks.ToArray();
            var primary=locks.Count>0?locks[0]:null;
            DreamCodeVR2ClientLogger.Event("quest","QUEST_CANONICAL_INSTANCE_RESOLVED",null,new { quest_instance_id=resolved.questId,drawer_id=resolved.targetDrawerId,lock_id=primary?.lockId,required_key_id=primary?.requiredKeyId,legacy_conversion_used=legacyConversionUsed });
            return resolved;
        }
    }
}
