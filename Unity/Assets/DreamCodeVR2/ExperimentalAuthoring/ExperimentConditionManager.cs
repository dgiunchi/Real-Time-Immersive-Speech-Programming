using DreamCodeVR2.UI;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class ExperimentConditionManager : MonoBehaviour
    {
        public string participantCode; public string sessionId; public ExperimentCondition condition;
        public ExperimentCondition selectedCondition; public bool hasPendingResearcherConditionSelection;
        // Researcher choices are pending until the server start/restart succeeds.
        public string selectedQuestSetId; public string selectedQuestInstanceId;
        public string activeQuestSetId; public string activeQuestInstanceId;
        public string questId; public string questVariant; public int conditionOrderIndex;
        public bool sessionStarted; public bool sessionCompleted; public StudyConfiguration studyConfiguration;
        [SerializeField] private bool researcherSessionReady;
        public MicrophoneCapture microphoneCapture; public DreamCodeVRAuthoringUIController authoringUi;
        public AuthoringProtocolClient protocolClient;
        public bool IsAuthoringAvailable => sessionStarted && !sessionCompleted && condition != ExperimentCondition.VoiceCommandBaseline;
        public bool IsDynamicStorytelling => sessionStarted && !sessionCompleted && condition == ExperimentCondition.DynamicStorytelling;
        public bool IsResearcherSessionReady => sessionStarted && !sessionCompleted && researcherSessionReady && !string.IsNullOrWhiteSpace(sessionId);
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
        public void PrepareResearcherCondition(ExperimentCondition value) { selectedCondition=value;hasPendingResearcherConditionSelection=true; }
        public void PrepareResearcherQuestSet(string value) { selectedQuestSetId=value; selectedQuestInstanceId=null; }
        public void PrepareResearcherQuestInstance(string value) { selectedQuestInstanceId=value; }
        public void ActivateResearcherQuestSelection()
        {
            if(selectedCondition==ExperimentCondition.DynamicStorytelling){activeQuestSetId=null;activeQuestInstanceId=null;return;}
            activeQuestSetId=selectedQuestSetId;activeQuestInstanceId=selectedQuestInstanceId;
        }
        public void StartSession(bool useStudyDefault = true) { if (useStudyDefault && studyConfiguration) selectedCondition = studyConfiguration.condition; condition = selectedCondition; frozenCondition = condition; sessionStarted = true; sessionCompleted = false; researcherSessionReady = false; ApplyConfiguration(); }
        public void CompleteSession() { sessionCompleted = true; researcherSessionReady = false; ApplyConfiguration(); }
        public void SetResearcherSessionReady() { if (sessionStarted && !sessionCompleted && !string.IsNullOrWhiteSpace(sessionId)) researcherSessionReady = true; }
        public void InvalidateResearcherSessionReady() { researcherSessionReady = false; }
        public void SwitchResearcherCondition(ExperimentCondition value)
        {
            if (sessionStarted && !sessionCompleted) CompleteSession();
            FindFirstObjectByType<ExperimentalPlaythroughReset>()?.ResetExperimentalPlaythrough();
            selectedCondition = value; condition = value; frozenCondition = value; sessionStarted = false; sessionCompleted = false; researcherSessionReady = false;
            ApplyConfiguration(); StartSession(false);
        }
        public void ResetPlaythrough() { FindFirstObjectByType<ExperimentalPlaythroughReset>()?.ResetExperimentalPlaythrough(); sessionStarted = false; sessionCompleted = false; researcherSessionReady = false; participantCode = string.Empty; sessionId = string.Empty; questId = string.Empty; questVariant = string.Empty; activeQuestSetId=null;activeQuestInstanceId=null;condition = selectedCondition; frozenCondition = selectedCondition; ApplyConfiguration(); }
        private void Resolve() { if (!microphoneCapture) microphoneCapture = FindFirstObjectByType<MicrophoneCapture>(); if (!authoringUi) authoringUi = FindFirstObjectByType<DreamCodeVRAuthoringUIController>(); if (!protocolClient) protocolClient = FindFirstObjectByType<AuthoringProtocolClient>(); }
        private void ApplyConfiguration()
        {
            Resolve();
            authoringUi?.ClearParticipantCommandFeedback();
            // C1 remains a legitimate voice-interaction condition: never disable microphone/STT here.
            if (microphoneCapture) { microphoneCapture.sendToServer = true; microphoneCapture.pttMicGain = Mathf.Clamp(studyConfiguration ? studyConfiguration.pttMicGain : microphoneCapture.pttMicGain,1f,4f); }
            if (authoringUi) authoringUi.SetExperimentalAuthoringVisible(true);
        }
    }
}
