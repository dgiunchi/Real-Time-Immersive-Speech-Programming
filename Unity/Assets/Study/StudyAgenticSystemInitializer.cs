using AgenticCache;
using Ubiq.Rooms;
using UnityEngine;

namespace AgenticXR.Study
{
    [DefaultExecutionOrder(-100)]
    public sealed class StudyAgenticSystemInitializer : MonoBehaviour
    {
        private static readonly System.Guid RuntimeRoomId =
            new System.Guid("6765c52b-3ad6-4fb0-9030-2c9a05dc4731");

        public CacheExchangeManager exchange;
        public CachePublisher publisher;
        public AgenticSceneRegistry sceneRegistry;
        public AgenticXRConsentPanel consentPanel;
        public GeneratedBehaviourWatchdog watchdog;
        public ImplicitTriggerSensors implicitSensors;
        public AgenticRuntimeCompiler compiler;
        public StudyTrialDirector trialDirector;
        public bool enableDebugStudyLauncher = true;

        private void Awake()
        {
            if (exchange == null || publisher == null || sceneRegistry == null || consentPanel == null ||
                watchdog == null || implicitSensors == null)
                throw new System.InvalidOperationException("AgenticXR study system references are incomplete.");
            exchange.cachePublisher = publisher;
            exchange.sceneRegistry = sceneRegistry;
            exchange.consentPanel = consentPanel;
            exchange.executionWatchdog = watchdog;
            exchange.compiler = compiler;
            publisher.localCache = exchange.localCache;
            publisher.sceneRegistry = sceneRegistry;
            publisher.sessionId = exchange.sessionId;
            implicitSensors.publisher = publisher;
            implicitSensors.sceneRegistry = sceneRegistry;
            watchdog.manager = exchange;
            consentPanel.Initialize(exchange);
            // AgenticXRStudy is a standalone authored scene and does not inherit
            // the MicrophoneCapture component from DynamicCompiler. Ensure the
            // spoken L3-L5 modes always have the left-trigger recorder available.
            var microphoneCapture = GetComponent<MicrophoneCapture>();
            if (microphoneCapture == null) microphoneCapture = gameObject.AddComponent<MicrophoneCapture>();
            microphoneCapture.sendToServer = true;
            var roomClient = GetComponent<RoomClient>();
            if (roomClient == null)
                throw new System.InvalidOperationException("AgenticXR study system requires a RoomClient.");
            // The hand-authored study bootstrap does not include Ubiq's Social
            // join UI. Join the fixed room UUID from the Node app config; joining
            // by the display name would create a different room named My Room.
            // Join queues safely before RoomClient.Start establishes the socket.
            roomClient.Join(RuntimeRoomId);
            var baseline = GetComponent<CodeGenerationManager>();
            if (baseline == null) baseline = gameObject.AddComponent<CodeGenerationManager>();
            baseline.runtimeCompiler = compiler;
            baseline.sceneRegistry = sceneRegistry;
            var localAvatar = GetComponent<StudyXRLocalAvatar>();
            if (localAvatar == null) localAvatar = gameObject.AddComponent<StudyXRLocalAvatar>();
            localAvatar.Initialize(baseline);
            if (enableDebugStudyLauncher)
            {
                if (trialDirector == null) trialDirector = FindFirstObjectByType<StudyTrialDirector>();
                var launcher = GetComponent<StudyDebugLauncher>();
                if (launcher == null) launcher = gameObject.AddComponent<StudyDebugLauncher>();
                launcher.Initialize(trialDirector, exchange, consentPanel);
            }
        }
    }
}
