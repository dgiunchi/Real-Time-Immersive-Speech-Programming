namespace DreamCodeVR2.ExperimentalAuthoring
{
    // Shared by the researcher canvas and PTT. While the console is visible, its
    // controller trigger is reserved for UI interaction and cannot open the microphone.
    public static class ResearcherUiInteractionState
    {
        public static bool IsResearcherUiInteractionActive { get; set; }
    }
}
