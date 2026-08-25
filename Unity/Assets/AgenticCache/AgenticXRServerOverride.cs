using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ubiq.Networking;
using Ubiq.Rooms;
using UnityEngine;

namespace AgenticCache
{
    /// <summary>
    /// Points the Ubiq RoomClient at a host other than localhost when one is
    /// configured.
    ///
    /// The study scene is built with Assets/Demos/Localhost.asset as its default
    /// server, which is correct for the Windows path: the Editor and the server
    /// run on the same machine, so localhost resolves. A standalone headset build
    /// is a different machine, and localhost there means the headset, so the
    /// RoomClient loops on reconnect against a server that does not exist.
    ///
    /// The override is read at startup from, in order:
    ///   1. the AGENTICXR_UBIQ_HOST environment variable, for the Editor;
    ///   2. a "ubiq_server.txt" file in Application.persistentDataPath, which can
    ///      be pushed to a headset over adb without rebuilding.
    ///
    /// The file holds one line: "host" or "host:port". When neither is present
    /// nothing happens and the scene keeps whatever server it was built with, so
    /// the Windows workflow is unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    public class AgenticXRServerOverride : MonoBehaviour
    {
        private const string Tag = "[AgenticXRServerOverride]";
        private const string FileName = "ubiq_server.txt";
        private const int DefaultPort = 8009;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var host = ResolveOverride(out var source);
            if (string.IsNullOrWhiteSpace(host))
            {
                Debug.Log($"{Tag} no override configured; using the server the scene was built with");
                return;
            }

            var roomClient = FindAnyObjectByType<RoomClient>();
            if (roomClient == null)
            {
                Debug.LogWarning($"{Tag} an override is set ({host}) but no RoomClient exists in the scene");
                return;
            }

            if (!TryParse(host, out var address, out var port))
            {
                Debug.LogError($"{Tag} could not parse '{host}' from {source}; expected host or host:port");
                return;
            }

            var definition = new ConnectionDefinition
            {
                sendToIp = address,
                sendToPort = port.ToString(),
                type = ConnectionType.TcpClient,
            };

            Debug.Log($"{Tag} overriding the Ubiq server with tcp://{address}:{port} (from {source})");
            roomClient.SetDefaultServer(definition);
            roomClient.Connect(definition);
        }

        // A headset runs apps under a secondary Android user, so the app's
        // persistentDataPath is not the /sdcard path visible to adb, and a pushed
        // file is simply not found. The launch intent crosses that boundary
        // reliably:
        //   adb shell am start -e ubiqHost 192.168.1.5:8009 -n <pkg>/<activity>
        private const int DiscoveryPort = 8010;
        private const string DiscoveryProbe = "AGENTICXR_DISCOVER";
        private const string DiscoveryReplyPrefix = "AGENTICXR_HOST ";
        private const int DiscoveryTimeoutMs = 1200;

        // Broadcasts a probe and takes the first answer. Bounded by a short
        // timeout so a network without a host costs a moment at startup rather
        // than hanging the scene, and any failure is treated as "not found"
        // rather than propagated: the scene can still fall back to what it was
        // built with.
        private static string FromLanDiscovery()
        {
            UdpClient client = null;
            try
            {
                client = new UdpClient { EnableBroadcast = true };
                client.Client.ReceiveTimeout = DiscoveryTimeoutMs;
                var probe = Encoding.UTF8.GetBytes(DiscoveryProbe);
                client.Send(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

                var deadline = DateTime.UtcNow.AddMilliseconds(DiscoveryTimeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var response = Encoding.UTF8.GetString(client.Receive(ref remote)).Trim();
                    if (response.StartsWith(DiscoveryReplyPrefix, StringComparison.Ordinal))
                    {
                        return response.Substring(DiscoveryReplyPrefix.Length).Trim();
                    }
                }
                return null;
            }
            catch (Exception exception)
            {
                Debug.Log($"{Tag} no host answered on the local network ({exception.GetType().Name})");
                return null;
            }
            finally
            {
                try { client?.Close(); } catch { /* closing is best effort */ }
            }
        }

        private static string FromLaunchIntent()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    return intent.Call<string>("getStringExtra", "ubiqHost");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{Tag} could not read the launch intent: {exception.Message}");
                return null;
            }
#else
            return null;
#endif
        }

        private static string ResolveOverride(out string source)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("AGENTICXR_UBIQ_HOST");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                source = "AGENTICXR_UBIQ_HOST";
                return fromEnvironment.Trim();
            }

            var fromIntent = FromLaunchIntent();
            if (!string.IsNullOrWhiteSpace(fromIntent))
            {
                source = "launch intent extra 'ubiqHost'";
                return fromIntent.Trim();
            }

            // Last, and the one that needs no configuration: ask the network.
            // Baking an address in at build time works until the network changes
            // and then fails silently, so discovery is preferred over any stored
            // value and only an explicit override beats it.
            var discovered = FromLanDiscovery();
            if (!string.IsNullOrWhiteSpace(discovered))
            {
                source = "LAN discovery";
                return discovered.Trim();
            }

            try
            {
                var path = Path.Combine(Application.persistentDataPath, FileName);
                if (File.Exists(path))
                {
                    source = path;
                    return File.ReadAllText(path).Trim();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{Tag} could not read the override file: {exception.Message}");
            }

            source = null;
            return null;
        }

        private static bool TryParse(string value, out string address, out int port)
        {
            address = null;
            port = DefaultPort;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var trimmed = value.Trim();
            var separator = trimmed.LastIndexOf(':');
            if (separator > 0)
            {
                var portText = trimmed.Substring(separator + 1);
                if (!int.TryParse(portText, out port) || port <= 0 || port > 65535) return false;
                address = trimmed.Substring(0, separator);
            }
            else
            {
                address = trimmed;
            }
            return !string.IsNullOrWhiteSpace(address);
        }
    }
}
