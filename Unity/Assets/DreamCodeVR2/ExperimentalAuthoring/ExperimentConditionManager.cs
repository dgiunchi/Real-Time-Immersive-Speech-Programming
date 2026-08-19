using DreamCodeVR2.UI;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class ExperimentConditionManager : MonoBehaviour
    {
        public string participantCode; public string sessionId; public ExperimentCondition condition;
        public ExperimentCondition selectedCondition;
        public string questId; public string questVariant; public int conditionOrderIndex;
        public bool sessionStarted; public bool sessionCompleted; public StudyConfiguration studyConfiguration;
        public MicrophoneCapture microphoneCapture; public DreamCodeVRAuthoringUIController authoringUi;
        public AuthoringProtocolClient protocolClient;
        public bool IsAuthoringAvailable => sessionStarted && !sessionCompleted && condition != ExperimentCondition.VoiceCommandBaseline;
        public bool IsDynamicStorytelling => sessionStarted && !sessionCompleted && condition == ExperimentCondition.DynamicStorytelling;
        private ExperimentCondition frozenCondition;
        private void Awake() { DontDestroyOnLoad(gameObject); frozenCondition = condition; selectedCondition = condition; }
        private void Start() { Resolve(); ApplyConfiguration(); }
        public bool SetCondition(ExperimentCondition value)
        {
            if (sessionStarted && !sessionCompleted) { Debug.LogWarning("[Study] condition change rejected during playthrough"); return false; }
            condition = value; frozenCondition = value; ApplyConfiguration(); return true;
        }
        // Selection is intentionally separate from the active condition. The researcher panel
        // restarts the server session before applying this pending selection.
        public void PrepareResearcherCondition(ExperimentCondition value) { selectedCondition=value; }
        public void StartSession(bool useStudyDefault = true) { if (useStudyDefault && studyConfiguration) selectedCondition = studyConfiguration.condition; condition = selectedCondition; frozenCondition = condition; sessionStarted = true; sessionCompleted = false; ApplyConfiguration(); }
        public void CompleteSession() { sessionCompleted = true; ApplyConfiguration(); }
        public void SwitchResearcherCondition(ExperimentCondition value)
        {
            if (sessionStarted && !sessionCompleted) CompleteSession();
            FindFirstObjectByType<ExperimentalPlaythroughReset>()?.ResetExperimentalPlaythrough();
            selectedCondition = value; condition = value; frozenCondition = value; sessionStarted = false; sessionCompleted = false;
            ApplyConfiguration(); StartSession(false);
        }
        public void ResetPlaythrough() { FindFirstObjectByType<ExperimentalPlaythroughReset>()?.ResetExperimentalPlaythrough(); sessionStarted = false; sessionCompleted = false; participantCode = string.Empty; sessionId = string.Empty; questId = string.Empty; questVariant = string.Empty; condition = selectedCondition; frozenCondition = selectedCondition; ApplyConfiguration(); }
        private void Resolve() { if (!microphoneCapture) microphoneCapture = FindFirstObjectByType<MicrophoneCapture>(); if (!authoringUi) authoringUi = FindFirstObjectByType<DreamCodeVRAuthoringUIController>(); if (!protocolClient) protocolClient = FindFirstObjectByType<AuthoringProtocolClient>(); }
        private void ApplyConfiguration()
        {
            Resolve();
            // C1 remains a legitimate voice-interaction condition: never disable microphone/STT here.
            if (microphoneCapture) microphoneCapture.sendToServer = true;
            if (authoringUi) authoringUi.SetExperimentalAuthoringVisible(true);
        }
    }
}
