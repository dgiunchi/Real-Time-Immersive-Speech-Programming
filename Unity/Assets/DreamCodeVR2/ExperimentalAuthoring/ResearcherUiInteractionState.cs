namespace DreamCodeVR2.ExperimentalAuthoring
{
    // Shared by the researcher canvas and PTT. While the console is visible, its
    // controller trigger is reserved for UI interaction and cannot open the microphone.
    public static class ResearcherUiInteractionState
    {
        public static bool IsResearcherUiInteractionActive { get; set; }
        private static float gainBeforeResearcherUi = 1.0f;

        public static void Open(MicrophoneCapture microphone)
        {
            if (!IsResearcherUiInteractionActive && microphone) gainBeforeResearcherUi = microphone.gain;
            IsResearcherUiInteractionActive = true;
            if (microphone)
            {
                if (microphone.IsRecording) DreamCodeVR2ClientLogger.Event("stt", "PTT_FORCED_STOP_PANEL_OPEN");
                microphone.SetRecording(false);
                microphone.gain = 0.0f;
            }
        }

        public static void Close(MicrophoneCapture microphone)
        {
            IsResearcherUiInteractionActive = false;
            if (microphone) microphone.gain = gainBeforeResearcherUi;
        }
    }
}
