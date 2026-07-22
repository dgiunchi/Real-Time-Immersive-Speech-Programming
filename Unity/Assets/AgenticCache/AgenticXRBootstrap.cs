using UnityEngine;

namespace AgenticCache
{
    public static class AgenticXRBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Object.FindFirstObjectByType<CacheExchangeManager>() != null) return;

            var root = new GameObject("AgenticXR Runtime");
            Object.DontDestroyOnLoad(root);
            var registry = root.AddComponent<AgenticSceneRegistry>();
            var publisher = root.AddComponent<CachePublisher>();
            var panel = root.AddComponent<AgenticXRConsentPanel>();
            var watchdog = root.AddComponent<GeneratedBehaviourWatchdog>();
            var exchange = root.AddComponent<CacheExchangeManager>();

            exchange.sceneRegistry = registry;
            exchange.cachePublisher = publisher;
            exchange.compiler = Object.FindFirstObjectByType<TestRoslyn>();
            exchange.consentPanel = panel;
            exchange.executionWatchdog = watchdog;
            watchdog.manager = exchange;
            publisher.localCache = exchange.localCache;
            publisher.sceneRegistry = registry;
            publisher.sessionId = exchange.sessionId;
            panel.Initialize(exchange);

            if (exchange.compiler == null)
                Debug.LogError("[AgenticXR] TestRoslyn was not found. Open the DynamicCompiler scene or assign a runtime compiler.");
        }
    }
}
