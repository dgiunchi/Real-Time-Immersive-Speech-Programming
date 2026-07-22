using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Ubiq.Networking;
using Ubiq.Rooms;

/// <summary>
/// Zero-configuration LAN discovery for the study server.
///
/// The Mac's launcher (`npm run study`) broadcasts a small UDP beacon
///   "UBIQ_DISCOVERY:&lt;ip&gt;:&lt;port&gt;"
/// on the local network every second. This component listens for that beacon
/// and points the Ubiq RoomClient at whatever address it hears.
///
/// Effect: the headset finds the Mac automatically on ANY Wi-Fi the two share
/// — no baked IP, no rebuild when the network changes. Both devices just need
/// to be on the same network (which they always are for a co-located study).
///
/// The RoomClient re-reads its server list on every reconnect cycle, so once
/// we call SetDefaultServer with the discovered address, the next reconnect
/// (a few seconds at most) locks straight on.
///
/// Add this component to the StudyManager object (the StudyUIBootstrapper adds
/// it automatically if it is missing).
/// </summary>
[DefaultExecutionOrder(-200)]
public class ServerAutoDiscovery : MonoBehaviour
{
    [Tooltip("UDP port the Mac broadcasts the discovery beacon on. Must match BEACON_PORT in study.js.")]
    public int beaconPort = 8007;

    [Tooltip("Also run discovery in the Unity Editor. Off by default so the editor keeps using its known-good localhost connection.")]
    public bool runInEditor = false;

    private const string PREFIX = "UBIQ_DISCOVERY:";
    private const string QUERY  = "UBIQ_QUERY";

    private UdpClient udp;
    private Thread listenThread;
    private Thread queryThread;
    private volatile bool running;

    // Set by the listen thread, consumed on the main thread in Update().
    private volatile string pendingIp;
    private volatile string pendingPort;
    private string appliedIp;

    private AndroidJavaObject multicastLock;

    private void Start()
    {
#if UNITY_EDITOR
        if (!runInEditor)
        {
            enabled = false;
            return;
        }
#endif
        AcquireMulticastLock();
        StartListening();
    }

    private void Update()
    {
        // Apply a freshly discovered address on the main thread (Ubiq calls
        // must not happen off-thread). Only act when the address changes.
        var ip = pendingIp;
        var port = pendingPort;
        if (ip == null || ip == appliedIp) return;
        appliedIp = ip;
        ApplyServer(ip, port);
    }

    private void ApplyServer(string ip, string port)
    {
        var room = FindObjectOfType<RoomClient>(true);
        if (!room)
        {
            Debug.LogWarning("[ServerAutoDiscovery] No RoomClient found yet; will retry on next beacon.");
            appliedIp = null; // allow retry
            return;
        }

        var def = ScriptableObject.CreateInstance<ConnectionDefinition>();
        def.sendToIp = ip;
        def.sendToPort = port;
        def.type = ConnectionType.TcpClient;

        room.SetDefaultServer(def);
        Debug.Log($"[ServerAutoDiscovery] Discovered study server at {ip}:{port} — reconnecting.");

        // Force an immediate reconnect so we don't wait for the heartbeat timeout.
        try { room.Reconnect(); }
        catch (Exception e) { Debug.LogWarning("[ServerAutoDiscovery] Reconnect failed: " + e); }
    }

    private void StartListening()
    {
        running = true;
        try
        {
            udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, beaconPort));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ServerAutoDiscovery] Could not bind UDP " + beaconPort + ": " + e.Message);
            return;
        }

        udp.EnableBroadcast = true;

        listenThread = new Thread(ListenLoop) { IsBackground = true };
        listenThread.Start();

        // Actively ask for the server too — the Mac replies unicast, which needs
        // no special Android permission, so discovery works even when broadcast
        // reception is filtered by Wi-Fi power saving.
        queryThread = new Thread(QueryLoop) { IsBackground = true };
        queryThread.Start();

        Debug.Log("[ServerAutoDiscovery] Listening for study server beacon on UDP " + beaconPort);
    }

    private void QueryLoop()
    {
        var query = Encoding.UTF8.GetBytes(QUERY);
        var broadcast = new IPEndPoint(IPAddress.Broadcast, beaconPort);
        while (running && appliedIp == null)
        {
            try { udp.Send(query, query.Length, broadcast); }
            catch (Exception) { }
            Thread.Sleep(1500);
        }
    }

    private void ListenLoop()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                var bytes = udp.Receive(ref any);
                var msg = Encoding.UTF8.GetString(bytes);
                if (!msg.StartsWith(PREFIX)) continue;

                var parts = msg.Substring(PREFIX.Length).Split(':');
                if (parts.Length != 2) continue;

                pendingIp = parts[0].Trim();
                pendingPort = parts[1].Trim();
            }
            catch (SocketException) { /* socket closed on shutdown */ if (!running) break; }
            catch (Exception) { /* ignore malformed packets */ }
        }
    }

    // On Android/Quest, receiving broadcast UDP requires a held multicast lock.
    private void AcquireMulticastLock()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi"))
            {
                if (wifi != null)
                {
                    multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "ubiqDiscovery");
                    multicastLock.Call("setReferenceCounted", true);
                    multicastLock.Call("acquire");
                    Debug.Log("[ServerAutoDiscovery] Multicast lock acquired.");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ServerAutoDiscovery] Could not acquire multicast lock: " + e.Message);
        }
#endif
    }

    private void OnDestroy()
    {
        running = false;
        try { udp?.Close(); } catch (Exception) { }
        try { listenThread?.Join(200); } catch (Exception) { }
        try { queryThread?.Join(200); } catch (Exception) { }
#if UNITY_ANDROID && !UNITY_EDITOR
        try { multicastLock?.Call("release"); } catch (Exception) { }
#endif
    }
}
