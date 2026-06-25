using System;

namespace DreamCodeVR2.Quest
{
    [Serializable]
    public class QuestInitialSetupAction
    {
        public string action;
        public string @object;
        public string object_id;
        public string anchor;
        public string parent;
        public bool active = true;
        public string material;
        public string text;

        public string ObjectReference
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(@object))
                {
                    return @object;
                }

                return object_id;
            }
        }

        public bool UsesLegacyObjectId
        {
            get
            {
                return string.IsNullOrWhiteSpace(@object) && !string.IsNullOrWhiteSpace(object_id);
            }
        }
    }
}
