using DreamCodeVR2.UI;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class ExperimentConditionManager : MonoBehaviour
    {
        public string participantCode; public string sessionId; public ExperimentCondition condition;
        public string questId; public string questVariant; public int conditionOrderIndex;
        public bool sessionStarted; public bool sessionCompleted; public StudyConfiguration studyConfiguration;
        public KeyCode debugStartKey = KeyCode.F1; public bool researcherDebugKeys = true;
        public MicrophoneCapture microphoneCapture; public DreamCodeVRAuthoringUIController authoringUi;
        public AuthoringProtocolClient protocolClient;
        public bool IsAuthoringAvailable => sessionStarted && !sessionCompleted && condition != ExperimentCondition.VoiceCommandBaseline;
        public bool IsDynamicStorytelling => sessionStarted && !sessionCompleted && condition == ExperimentCondition.DynamicStorytelling;
        private ExperimentCondition frozenCondition;
        private void Awake() { DontDestroyOnLoad(gameObject); frozenCondition = condition; }
        private void Start() { Resolve(); ApplyConfiguration(); }
        private void Update() { if (researcherDebugKeys && Debug.isDebugBuild && Input.GetKeyDown(debugStartKey)) { if (!sessionStarted) StartSession(); else CompleteSession(); } }
        public bool SetCondition(ExperimentCondition value)
        {
            if (sessionStarted && !sessionCompleted) { Debug.LogWarning("[Study] condition change rejected during playthrough"); return false; }
            condition = value; frozenCondition = value; ApplyConfiguration(); return true;
        }
        public void StartSession() { if (studyConfiguration) condition = studyConfiguration.condition; frozenCondition = condition; sessionStarted = true; sessionCompleted = false; ApplyConfiguration(); protocolClient?.SendSessionConfiguration(this); }
        public void CompleteSession() { sessionCompleted = true; ApplyConfiguration(); }
        public void ResetPlaythrough() { FindFirstObjectByType<ExperimentalPlaythroughReset>()?.ResetExperimentalPlaythrough(); sessionStarted = false; sessionCompleted = false; participantCode = string.Empty; sessionId = string.Empty; questId = string.Empty; questVariant = string.Empty; condition = frozenCondition; ApplyConfiguration(); }
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
