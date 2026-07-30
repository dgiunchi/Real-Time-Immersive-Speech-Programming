using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Ubiq.Networking;
using Ubiq.Rooms;

/// <summary>
/// Zero-configuration discovery for the study server. Two independent paths, so
/// a session is never lost to whatever network the room happens to have.
///
/// 1. HANDOFF FILE (primary — fast, works across subnets).
///    While the headset is on the USB cable, `npm run study` pushes the Mac's
///    current IP to a small file in this app's storage. We read it at startup and
///    connect straight away — no waiting, no broadcast involved.
///
///    This matters because the UDP beacon below only works when the Mac and the
///    headset share a subnet. In a lab that is frequently false: the headset sits
///    on the lab AP while the Mac is on the institutional network. Broadcasts do
///    not route between them, so the beacon never arrives and the app falls back
///    to localhost — which on the headset is itself, hence "connection lost".
///    Ordinary TCP between the two still works fine; only the *discovery* was
///    broken, which is exactly what this file supplies.
///
///    The cable is needed only for that one-off write at launch. Once connected,
///    the session runs over Wi-Fi and the headset can be unplugged.
///
/// 2. UDP BEACON (fallback). The launcher broadcasts
///    "UBIQ_DISCOVERY:&lt;ip&gt;:&lt;port&gt;" every second; we listen and point the
///    RoomClient at whatever we hear. Requires a shared subnet, so it is the
///    backup rather than the primary.
///
/// The file is re-read while we are still unconnected, so starting the app before
/// the server also works — it locks on within a second of the server coming up.
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

    [Tooltip("File the launcher writes the Mac's IP into over USB. Relative to this app's persistent data path.")]
    public string handoffFileName = "study_server.txt";

    private const string PREFIX = "UBIQ_DISCOVERY:";
    private const string QUERY  = "UBIQ_QUERY";

    private UdpClient udp;
    private Thread listenThread;
    private Thread queryThread;
    private Thread handoffThread;
    private volatile bool running;

    // Resolved on the main thread in Start(); persistentDataPath is not safe to
    // touch from a worker thread.
    private string handoffPath;

    // Set by the listen thread, consumed on the main thread in Update().
    private volatile string pendingIp;
    private volatile string pendingPort;
    // Read by the query and handoff threads to know when to stop polling.
    private volatile string appliedIp;

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
        handoffPath = System.IO.Path.Combine(Application.persistentDataPath, handoffFileName);

        // Try the handoff file first and synchronously — when the launcher has
        // already pushed it, this connects on frame one with nothing to wait for.
        ReadHandoffFile();

        AcquireMulticastLock();
        StartListening();
        StartHandoffWatch();
    }

    /// <summary>
    /// Reads "ip:port" from the handoff file, if present. Stages the address for
    /// Update() rather than applying it here so both discovery paths converge on
    /// the same main-thread code.
    /// </summary>
    private void ReadHandoffFile()
    {
        try
        {
            if (string.IsNullOrEmpty(handoffPath) || !System.IO.File.Exists(handoffPath)) return;
            var text = System.IO.File.ReadAllText(handoffPath).Trim();
            if (text.Length == 0) return;

            var parts = text.Split(':');
            if (parts.Length != 2) return;

            var ip = parts[0].Trim();
            if (ip.Length == 0 || ip == "127.0.0.1" || ip == "localhost") return;

            pendingIp = ip;
            pendingPort = parts[1].Trim();
        }
        catch (Exception) { /* unreadable or mid-write — the watch thread retries */ }
    }

    /// <summary>
    /// Keeps re-reading the handoff file until we have applied an address, so
    /// launching the app before the server still connects promptly.
    /// </summary>
    private void StartHandoffWatch()
    {
        handoffThread = new Thread(() =>
        {
            while (running && appliedIp == null)
            {
                ReadHandoffFile();
                Thread.Sleep(1000);
            }
        })
        { IsBackground = true };
        handoffThread.Start();
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
        Debug.Log($"[ServerAutoDiscovery] Study server at {ip}:{port} — reconnecting. " +
                  $"(source: {(System.IO.File.Exists(handoffPath) ? "USB handoff file" : "UDP beacon")})");

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
        try { handoffThread?.Join(1200); } catch (Exception) { }
#if UNITY_ANDROID && !UNITY_EDITOR
        try { multicastLock?.Call("release"); } catch (Exception) { }
#endif
    }
}
