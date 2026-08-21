using System;
using System.Text;
using Ubiq.Logging.Utf8Json;
using Ubiq.Messaging;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
                var json = Encoding.UTF8.GetString(data.bytes, data.start, data.length);
                var root = JObject.Parse(json);
                // Node represents optional numeric envelope fields as JSON null,
                // while CacheEnvelope uses -1 sentinels because Unity's
                // JsonUtility does not reliably support nullable value types.
                // Ignoring null values preserves those field initializers instead
                // of throwing while converting null to long/float/double.
                var serializer = Newtonsoft.Json.JsonSerializer.Create(new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
                envelope = root.ToObject<CacheEnvelope>(serializer);
                var payload = root["payload"];
                if (payload != null)
                {
                    envelope.payload = payload.Type == Newtonsoft.Json.Linq.JTokenType.String
                        ? payload.Value<string>()
                        : payload.ToString(Newtonsoft.Json.Formatting.None);
                }
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

        public void Send(CacheEnvelope envelope)
        {
            var json = JsonUtility.ToJson(envelope);
            var bytes = Encoding.UTF8.GetBytes(json);
            var message = ReferenceCountedSceneGraphMessage.Rent(bytes.Length);
            bytes.CopyTo(new Span<byte>(message.bytes, message.start, bytes.Length));
            context.Send(message);
        }
    }
}
