using System;
using System.Collections.Generic;

namespace DreamCodeVR2.Quest
{
    [Serializable]
    public class QuestPlan
    {
        public string quest_id;
        public string title;
        public List<QuestTaskSpec> tasks = new List<QuestTaskSpec>();
    }
}
