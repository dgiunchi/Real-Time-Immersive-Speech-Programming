using System;

namespace DreamCodeVR2.Quest
{
    [Serializable]
    public class QuestClueSpec
    {
        public string @object;
        public string object_id;
        public string text;
        public string style;
        public string text_target;

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
