namespace DreamCodeVR2.Quest
{
    public class QuestTaskValidator
    {
        public bool IsTaskComplete(QuestTaskSpec task, out string reason)
        {
            if (task == null)
            {
                reason = "Task is null.";
                return false;
            }

            switch (task.type)
            {
                case "ReadClue":
                    reason = "No inspection event state is wired yet; complete manually with F3.";
                    return false;
                case "StraightenAndMovePainting":
                case "CreateTextureAndPlaceObject":
                case "UnlockDoorWithKey":
                    reason = "Task validator v0 keeps this task manual for now; complete with F3.";
                    return false;
                default:
                    reason = "Task validator v0 does not auto-complete this task yet; complete with F3.";
                    return false;
            }
        }
    }
}
