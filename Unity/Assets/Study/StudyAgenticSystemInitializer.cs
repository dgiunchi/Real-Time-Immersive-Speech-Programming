using AgenticCache;
using UnityEngine;

namespace AgenticXR.Study
{
    [DefaultExecutionOrder(-100)]
    public sealed class StudyAgenticSystemInitializer : MonoBehaviour
    {
        public CacheExchangeManager exchange;
        public CachePublisher publisher;
        public AgenticSceneRegistry sceneRegistry;
        public AgenticXRConsentPanel consentPanel;
        public GeneratedBehaviourWatchdog watchdog;
        public ImplicitTriggerSensors implicitSensors;
        public AgenticRuntimeCompiler compiler;

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
        }
    }
}
