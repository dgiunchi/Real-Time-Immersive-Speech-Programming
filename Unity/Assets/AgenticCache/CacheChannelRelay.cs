using System;
using Ubiq.Logging.Utf8Json;
using Ubiq.Messaging;
using UnityEngine;

namespace AgenticCache
{
    // Every existing networked MonoBehaviour in this codebase (CodeGenerationManager,
    // MicrophoneCapture, TextGenerationCollector, StoryTellerManager, ...) registers
    // itself on exactly ONE NetworkId. CacheExchangeManager needs to listen on FOUR
    // (96, 97, 99, 101), so it creates one of these tiny relay components per
    // channel instead - each is its own networked component (its own
    // NetworkScene.Register call) that just forwards to a shared callback, mirroring
    // Server/mcp/unity_scene_bridge/scene_bridge_client.js's EnvelopeListener
    // pattern on the Node side.
    //
    // NOT compiled/verified in this environment - see docs/cache-exchange-layer.md.
    public class CacheChannelRelay : MonoBehaviour
    {
        public NetworkId networkId;
        private Action<CacheEnvelope, ReferenceCountedSceneGraphMessage> onEnvelope;
        private NetworkContext context;

        public void Init(NetworkId id, Action<CacheEnvelope, ReferenceCountedSceneGraphMessage> handler)
        {
            networkId = id;
            onEnvelope = handler;
            context = NetworkScene.Register(this, networkId);
        }

        public void ProcessMessage(ReferenceCountedSceneGraphMessage data)
        {
            CacheEnvelope envelope;
            try
            {
                // Matches CodeGenerationManager.cs's exact parsing call
                // (data.FromJson<T>(), from Ubiq.Logging.Utf8Json) rather than
                // JsonUtility.FromJson(data.ToString()) directly, for consistency
                // with the one proven-working pattern in this codebase. Whether
                // this extension actually handles nested payload objects better
                // than plain JsonUtility was not verified against source in this
                // environment - see CacheEnvelope.cs's class comment; `payload` is
                // still declared as `string` defensively either way.
                envelope = data.FromJson<CacheEnvelope>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CacheChannelRelay] dropped non-JSON message on channel {networkId}: {e.Message}");
                return;
            }

            if (envelope == null)
            {
                Debug.LogWarning($"[CacheChannelRelay] failed to parse envelope on channel {networkId}");
                return;
            }

            onEnvelope?.Invoke(envelope, data);
        }
    }
}
