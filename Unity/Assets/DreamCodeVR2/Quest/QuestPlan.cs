using System;
using System.Collections.Generic;

namespace DreamCodeVR2.Quest
{
    [Serializable]
    public class QuestPlan
    {
        public string quest_id;
        public string mode;
        public string title;
        public string summary;
        public string final_key;
        public string drawer_key;
        public List<QuestTaskSpec> tasks = new List<QuestTaskSpec>();
        public List<QuestInitialSetupAction> initial_setup = new List<QuestInitialSetupAction>();
        public List<QuestClueSpec> clues = new List<QuestClueSpec>();
        public QuestErrorRiskSpec error_risk;
        public QuestValidationFlags validation_flags;
    }

    [Serializable]
    public class QuestErrorRiskSpec
    {
        public string type;
        public string correct_key;
        public string wrong_key;
        public string target;
        public string correct_target;
        public List<string> distractor_targets = new List<string>();
        public string repair_hint;
    }

    [Serializable]
    public class QuestValidationFlags
    {
        public bool has_fixed_first_task;
        public bool has_fixed_final_task;
        public bool has_planning_task;
        public bool has_error_risk_task;
        public bool anchor_only_placement = true;
        public bool clue_text_limited = true;
    }
}
