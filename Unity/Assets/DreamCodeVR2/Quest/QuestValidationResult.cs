using System;
using System.Collections.Generic;

namespace DreamCodeVR2.Quest
{
    [Serializable]
    public class QuestValidationResult
    {
        public bool is_valid = true;
        public bool variable_anchors_unique = true;
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();

        public void AddError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            is_valid = false;
            errors.Add(message.Trim());
        }

        public void AddWarning(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            warnings.Add(message.Trim());
        }
    }
}
