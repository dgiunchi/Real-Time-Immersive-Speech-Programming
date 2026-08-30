using System;
using System.Collections.Generic;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    // The sole client-side translation point for participant-facing C1 language.
    // Canonical IDs remain intentionally available only to logs/researcher tooling.
    public static class ParticipantFacingText
    {
        private static readonly Dictionary<string,string> Names = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            { "painting_001", "the painting" }, { "sphere_001", "the sphere" },
            { "key_001", "the Golden Key" }, { "key_002", "the Silver Key" },
            { "table_drawer_001", "the desk drawer" }, { "table_drawer_002", "the second desk drawer" }, { "table_drawer_003", "the third desk drawer" },
            { "cabinet_drawer_001", "the cabinet drawer" }, { "cabinet_drawer_002", "the second cabinet drawer" }, { "cabinet_drawer_003", "the third cabinet drawer" },
            { "lock_001", "the desk drawer lock" }, { "lock_002", "the second desk drawer lock" }, { "lock_003", "the exit lock" },
            { "basket_001", "the basket" }, { "door_001", "the exit door" }
        };

        public static string ObjectName(string objectId)
        {
            var editable=AuthoringActionExecutor.FindEditable(objectId);
            if(editable&&!string.IsNullOrWhiteSpace(editable.displayName) && !LooksTechnical(editable.displayName)) return WithArticle(editable.displayName);
            if(!string.IsNullOrWhiteSpace(objectId)&&Names.TryGetValue(objectId,out var known)) return known;
            return "the object";
        }

        public static string Describe(PredefinedVoiceCommand command)
        {
            var operation=(command?.command??string.Empty).Trim().ToUpperInvariant();
            var target=ObjectName(command?.targetObjectId);
            switch(operation)
            {
                case "MOVE_TO_PRESET":
                    if(string.Equals(command?.targetObjectId,"painting_001",StringComparison.OrdinalIgnoreCase)&&string.Equals(command?.preset,"aligned",StringComparison.OrdinalIgnoreCase)) return "Straighten the painting";
                    if(string.Equals(command?.targetObjectId,"sphere_001",StringComparison.OrdinalIgnoreCase)&&string.Equals(command?.preset,"soccer_ball",StringComparison.OrdinalIgnoreCase)) return "Turn the sphere into a soccer ball";
                    return "Move "+target+" to the selected position";
                case "USE_WITH": return "Use "+target+" with "+ObjectName(command?.secondaryObjectId);
                case "PLACE_IN": return "Place "+target+" in "+ObjectName(command?.secondaryObjectId);
                case "OPEN": return "Open "+target;
                case "CLOSE": return "Close "+target;
                case "ACTIVATE": return "Turn on "+target;
                case "DEACTIVATE": return "Turn off "+target;
                default: return "Confirm this action";
            }
        }

        private static bool LooksTechnical(string value) => value.IndexOf('_')>=0 || value.IndexOf("lock_",StringComparison.OrdinalIgnoreCase)>=0;
        private static string WithArticle(string value) => value.StartsWith("the ",StringComparison.OrdinalIgnoreCase) ? value : "the "+value;
    }
}
