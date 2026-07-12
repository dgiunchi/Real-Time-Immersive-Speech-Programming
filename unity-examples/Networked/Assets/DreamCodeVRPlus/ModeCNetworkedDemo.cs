using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// DreamCodeVR+ **Path B — networked Mode-C demo**.
    ///
    /// A minimal, self-contained Ubiq TCP client (NO external Ubiq package, so no
    /// Unity-version compatibility risk). It joins the SAME Ubiq room as the running
    /// Rust backend, sends a selection (NID 93) + push-to-talk command (NID 98), and
    /// applies the backend's reply (NID 94 action-plan) to a cube via the safe
    /// <see cref="ActionPlanExecutor"/> — i.e. the object changes from a LIVE backend
    /// message, with no runtime code compilation.
    ///
    /// Run order (see README):
    ///   1) scripts/run-roomserver.sh                                   (RoomServer :8009)
    ///   2) DCVR_UBIQ_ADDR=127.0.0.1:8009 ./target/debug/dreamcodevr-server
    ///   3) press Play here.
    /// </summary>
    public sealed class ModeCNetworkedDemo : MonoBehaviour
    {
        // ---- backend address ----
        // Host is RUNTIME-CONFIGURABLE so you never rebuild the APK when the laptop's IP
        // changes (e.g. a fresh iPhone-hotspot 172.20.10.x each session). Resolved at
        // Start() from, in order: a pushed config file (persistentDataPath/dcvr_server.txt),
        // then the last in-app value (PlayerPrefs), then this loopback default. It can also
        // be changed live in the on-screen panel ("Apply & Reconnect").
        private volatile string _host = "127.0.0.1";
        private volatile bool _forceReconnect;   // set when the IP changes -> drop & reconnect
        private string _hostEdit = "127.0.0.1";  // text-field buffer (main thread only)
        private TouchScreenKeyboard _keyboard;   // Quest system keyboard for standalone typing
        // LAN auto-discovery: the backend answers "DCVR_DISCOVER" UDP probes (:8987) and
        // broadcasts a beacon (:8988) carrying {"dcvr":1,"tcp":"ip:8009",...}. A background
        // thread fills _discoveredHost; Update() applies it (PlayerPrefs is main-thread-only).
        private volatile string _discoveredHost;
        private Thread _discover;
        private const string HostPrefKey = "dcvr_host";
        private static string HostConfigPath =>
            System.IO.Path.Combine(Application.persistentDataPath, "dcvr_server.txt");
        private const int Port = 8009;
        private const string RoomGuid = "6765c52b-3ad6-4fb0-9030-2c9a05dc4731";
        // 36-char ASCII peer id that prefixes NID 93/98 payloads (identifies us to the backend)
        private const string PeerUuid = "00000000-0000-4000-8000-0000000000ab";

        private const uint NID_ROOMSERVER_B = 1;
        private const uint NID_SELECTION_B = 93;
        private const uint NID_AUDIO_B = 98;
        private const uint NID_OUTPUT_B = 94;
        private const uint NID_FEEDBACK_B = 95;  // 👍/👎 to the backend (RAG learning)
        private const uint NID_COMPILE_B = 96;   // Mode-A compile result -> admin panel

        private GameObject _cube;
        private ActionPlanExecutor _exec;
        private GeneratedObjectTracker _tracker;
        // Mode-agnostic perceptual monitor (TRACK U2). Monitor-only: disclosures only,
        // never blocks. Off by default (discloseEnabled=false) so free creation is quiet.
        private GeneratedContentMonitor _monitor;

        private TcpClient _tcp;
        private NetworkStream _stream;
        private Thread _net;
        private volatile bool _running;
        private volatile bool _joinedOnce;  // auto-send the hello only on the FIRST join
        private volatile string _status = "connecting…";
        private readonly ConcurrentQueue<string> _inbound = new ConcurrentQueue<string>();   // NID 94 JSON
        private readonly ConcurrentQueue<string> _outbound = new ConcurrentQueue<string>();  // command text
        // Control messages to send: (networkId, body) — 👍/👎 feedback (95) + compile result (96).
        private readonly ConcurrentQueue<(uint nid, string body)> _ctrlOut =
            new ConcurrentQueue<(uint, string)>();
        private volatile bool _hasResult;   // a generation has arrived -> enable 👍/👎
        private uint _clientA, _clientB;
        private string _typed = "make it look like a calm ocean";   // free-text command box

        // --- desktop push-to-talk mic (click to record, click to send) ---
        // Captures the system mic, resamples to 16 kHz mono s16le, and sends it as a
        // push-to-talk utterance on NID 98 (start / PCM chunks / stop). The backend
        // transcribes it with Whisper when started with DCVR_STT_OPENAI=true.
        private const int MicSeconds = 15;          // max hold per utterance
        private const int TargetRate = 16000;       // backend expects 16 kHz mono
        private string _micDevice;
        private AudioClip _micClip;
        private volatile bool _micRecording;
        private readonly ConcurrentQueue<byte[]> _audioOut = new ConcurrentQueue<byte[]>(); // raw NID 98 bodies

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (UnityEngine.Object.FindAnyObjectByType<ModeCNetworkedDemo>() == null)
            {
                new GameObject("ModeCNetworkedDemo").AddComponent<ModeCNetworkedDemo>();
            }
        }

        private void Start()
        {
            // Resolve the backend IP (pushed file > saved value > loopback default) so a
            // standalone Quest can be pointed at the laptop with no rebuild and no typing
            // (adb push … dcvr_server.txt), while the in-app panel can still change it live.
            _host = LoadHost();
            _hostEdit = _host;

            // Start CLEAN: no default cube. _cube is an INVISIBLE anchor at the origin
            // that generated content (Mode A/B C# or Mode-C spawns) attaches/parents to,
            // so on Play you see just the ground plane and only what you ask for is built.
            _cube = new GameObject("DCVR_Target");
            if (Camera.main != null)
            {
                Camera.main.transform.position = new Vector3(0f, 1.2f, -4f);
                Camera.main.transform.LookAt(_cube.transform);
            }
            _tracker = gameObject.AddComponent<GeneratedObjectTracker>();
            _exec = gameObject.AddComponent<ActionPlanExecutor>();
            _exec.tracker = _tracker;
            // Mode-agnostic perceptual monitor: scans generated content (Mode A/B/C)
            // and DISCLOSES concerns. Stays silent unless armed in the Inspector
            // (discloseEnabled), so it never restricts free creation.
            _monitor = gameObject.AddComponent<GeneratedContentMonitor>();
            _monitor.confinementRoot = _cube.transform;
            _monitor.drift = gameObject.AddComponent<UserDisplacementTracker>();

            if (Microphone.devices != null && Microphone.devices.Length > 0)
            {
                _micDevice = Microphone.devices[0];
            }

            var rnd = new System.Random();
            _clientA = (uint)rnd.Next();
            _clientB = (uint)rnd.Next();

            _running = true;
            _net = new Thread(NetworkLoop) { IsBackground = true };
            _net.Start();
            _discover = new Thread(DiscoveryLoop) { IsBackground = true };
            _discover.Start();
        }

        // Probe the LAN for the backend (works on hotspot OR uni wifi, no typing/rebuild):
        // send "DCVR_DISCOVER" to broadcast:8987 and listen for the beacon. Both the unicast
        // reply and the periodic :8988 broadcast land on this socket. Background thread only
        // WRITES _discoveredHost; Update() applies it on the main thread.
        private void DiscoveryLoop()
        {
            System.Net.Sockets.UdpClient udp = null;
            try
            {
                try
                {
                    udp = new System.Net.Sockets.UdpClient();
                    udp.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket,
                        System.Net.Sockets.SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, 8988));
                }
                catch
                {
                    // 8988 taken (e.g. Editor + player on one machine): ephemeral port still
                    // receives the unicast probe reply, just not the broadcast beacon.
                    udp = new System.Net.Sockets.UdpClient(0);
                }
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = 2000;
                byte[] probe = Encoding.UTF8.GetBytes("DCVR_DISCOVER");
                var bcast = new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 8987);
                while (_running)
                {
                    try { udp.Send(probe, probe.Length, bcast); } catch { }
                    try
                    {
                        var from = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref from);
                        string s = Encoding.UTF8.GetString(data);
                        // beacon: {"dcvr":1,"tcp":"172.20.10.2:8009",...} -> host part only
                        int i = s.IndexOf("\"tcp\":\"", StringComparison.Ordinal);
                        if (s.Contains("\"dcvr\"") && i >= 0)
                        {
                            int start = i + 7;
                            int end = s.IndexOf('"', start);
                            int colon = s.LastIndexOf(':', end);
                            if (end > start && colon > start)
                            {
                                _discoveredHost = s.Substring(start, colon - start);
                            }
                        }
                    }
                    catch { /* timeout: probe again */ }
                    Thread.Sleep(2000);
                }
            }
            catch { /* discovery is best-effort; manual IP entry still works */ }
            finally { try { udp?.Close(); } catch { } }
        }

        private void Update()
        {
            // Auto-discovered backend on this LAN: apply it once if it differs from the
            // current target (main thread — ApplyHost touches PlayerPrefs).
            string found = _discoveredHost;
            if (!string.IsNullOrEmpty(found) && found != _host)
            {
                Debug.Log("[ModeC-Net] auto-discovered backend at " + found + ":" + Port);
                _hostEdit = found;
                ApplyHost(found);
            }

            // Pump the Quest system keyboard (standalone text entry for the server IP).
            if (_keyboard != null)
            {
                var st = _keyboard.status;
                if (st == TouchScreenKeyboard.Status.Done)
                {
                    _hostEdit = (_keyboard.text ?? "").Trim();
                    _keyboard = null;
                }
                else if (st == TouchScreenKeyboard.Status.Canceled
                      || st == TouchScreenKeyboard.Status.LostFocus)
                {
                    _keyboard = null;
                }
            }

            // Apply backend replies on the MAIN thread (Unity API is main-thread only).
            while (_inbound.TryDequeue(out string json))
            {
                // SOC-01 / NET-03 (global-broadcast cross-peer attack): NID 94 reaches
                // EVERY peer in the room. Apply a decision ONLY if its optional
                // "target_peer" is null/empty (broadcast) OR equals our own PeerUuid.
                // Otherwise another peer's decision could drive our scene.
                if (!IsForThisPeer(json))
                {
                    Debug.Log("[ModeC-Net] NID 94 decision targeted another peer; ignoring.");
                    continue;
                }

                // Mode A (DCVR_MODE_A=true): backend sends VALIDATED C# as {type:"code",data:...}.
                // Compile it at runtime (Roslyn) and attach it to the cube — the original
                // DreamCodeVR path, but the C# was already vetted by the Rust gate.
                // Otherwise (Mode C, the default): apply the safe action plan, no compilation.
                string mtype = TryGetType(json);
                if (mtype == "code")
                {
                    string code = TryGetData(json);
                    // Only wipe the scene for a NEW build (one that creates primitives).
                    // Modifier commands (spin / scale / colour) attach on top so they act on the
                    // EXISTING structure — e.g. "spin this house" spins the house, not a fresh cube.
                    bool isBuild = !string.IsNullOrEmpty(code) && code.Contains("CreatePrimitive");
                    if (isBuild) { ResetTarget(); }
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    bool ok = RuntimeCSharpCompiler.CompileAndAttach(code, _cube, out string err);
                    sw.Stop();
                    long compileMs = sw.ElapsedMilliseconds;
                    Debug.Log(ok
                        ? "[Mode-A] compiled + attached generated C# ✓"
                        : "[Mode-A] compile/attach FAILED: " + err);
                    _status = ok
                        ? "Mode A: compiled C# at runtime in Unity ✓ (cube changed)"
                        : "Mode A FAILED — " + err;
                    // Report the compile outcome + duration to the admin panel (NID 96).
                    _ctrlOut.Enqueue((NID_COMPILE_B, ok
                        ? "{\"ok\":true,\"ms\":" + compileMs + "}"
                        : "{\"ok\":false,\"ms\":" + compileMs + ",\"error\":\"" + JsonEscape(err) + "\"}"));
                    if (ok)
                    {
                        // Mode-A/B provenance: stamp the visible synthetic marker on every
                        // object the generated C# produced (SP-02; defeats disguise/false-
                        // affordance regardless of mode). Then run the mode-agnostic monitor
                        // over the result (disclosures only; never blocks).
                        SafeBehaviourRegistry.MarkGeneratedHierarchy(_cube);
                        _monitor.ScanGenerated(contentInMotion: !isBuild || code.Contains("Update"));
                    }
                    _hasResult = true;
                }
                else if (mtype == "undo")
                {
                    ResetTarget();
                    _status = "reset by admin panel";
                }
                else
                {
                    bool ok = _exec.Execute(json, _cube, null);
                    Debug.Log($"[ModeC-Net] applied backend NID 94 -> {(ok ? "applied" : "rejected")}");
                    if (ok)
                    {
                        _monitor.ScanGenerated(contentInMotion: json.Contains("move") || json.Contains("rotate"));
                    }
                    _hasResult = true;
                }
            }
        }

        // Reset the target cube to a clean state before each Mode-A command, so
        // generated behaviours and built structures don't pile up across commands.
        private void ResetTarget()
        {
            if (_cube == null) { return; }
            // Remove every previously-compiled behaviour (the cube has no MonoBehaviours of ours).
            foreach (var mb in _cube.GetComponents<MonoBehaviour>())
            {
                if (mb != null) { Destroy(mb); }
            }
            // Remove anything a previous command built as children of the cube.
            for (int i = _cube.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(_cube.transform.GetChild(i).gameObject);
            }
            // Restore default transform + appearance.
            _cube.transform.localScale = Vector3.one;
            _cube.transform.localRotation = Quaternion.identity;
            var r = _cube.GetComponent<Renderer>();
            if (r != null)
            {
                r.enabled = true;
                r.material.color = Color.white;
            }
        }

        // SOC-01 / NET-03 (global-broadcast cross-peer attack): true if this NID 94
        // decision is addressed to us. Optional "target_peer" (envelope or action_plan)
        // that is null/empty => broadcast-to-all (apply). A targeted decision is applied
        // ONLY when target_peer equals our own PeerUuid. Unparseable JSON returns true so
        // the safe ActionPlanExecutor can reject it via its own fail-safe.
        private bool IsForThisPeer(string json)
        {
            string targetPeer;
            try
            {
                JObject root = JObject.Parse(json);
                JToken tok = root["target_peer"] ?? (root["action_plan"] as JObject)?["target_peer"];
                targetPeer = tok != null && tok.Type == JTokenType.String ? (string)tok : null;
            }
            catch { return true; }

            if (string.IsNullOrEmpty(targetPeer)) { return true; } // broadcast-to-all.
            return targetPeer == PeerUuid;                         // targeted: only for us.
        }

        private static string TryGetType(string json)
        {
            try { return (string)JObject.Parse(json)["type"]; } catch { return null; }
        }

        private static string TryGetData(string json)
        {
            try { return (string)JObject.Parse(json)["data"]; } catch { return null; }
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
        }

        // --- the background socket thread: connect, join, send, read, AUTO-RECONNECT ---
        // Graceful: if the RoomServer/backend goes away (socket shut down), we do NOT
        // throw a raw exception or die — we show a clean status and reconnect, so
        // restarting the servers recovers the live link WITHOUT re-entering Play.
        private void NetworkLoop()
        {
            while (_running)
            {
                try
                {
                    RunSession();
                }
                catch (System.Threading.ThreadAbortException)
                {
                    return; // Play stopped; thread aborted. Not an error.
                }
                catch (Exception e)
                {
                    // Clean, non-spammy status; one info line (not a scary error dump).
                    _status = "disconnected — reconnecting…  (start the RoomServer + backend; it auto-recovers)";
                    Debug.Log("[ModeC-Net] link dropped (" + e.Message + ") — reconnecting");
                }
                CloseSocket();
                // Backoff before reconnecting; wake promptly if Play stops.
                for (int i = 0; i < 20 && _running; i++) { Thread.Sleep(100); }
            }
        }

        // One connect -> join -> pump session. Returns (or throws) when the link drops;
        // NetworkLoop then reconnects.
        private void RunSession()
        {
            _tcp = new TcpClient();
            _tcp.Connect(_host, Port);
            _stream = _tcp.GetStream();

            SendFrame(0, NID_ROOMSERVER_B, Encoding.UTF8.GetBytes(BuildJoin()));
            if (!WaitForSetRoom())
            {
                _status = "join failed (no SetRoom) — retrying…";
                return;
            }
            _status = _joinedOnce ? "reconnected to room ✓" : "JOINED room. Sending a command to the backend…";

            // tell the backend which object is selected; auto-send one hello on FIRST join only
            SendApp(NID_SELECTION_B, "Cube:DefaultMaterial");
            if (!_joinedOnce)
            {
                _outbound.Enqueue("make this cube red");
                _joinedOnce = true;
            }

            var buf = new List<byte>();
            var tmp = new byte[8192];
            _stream.ReadTimeout = 800; // ms — also paces the ping
            DateTime lastPing = DateTime.UtcNow;

            while (_running)
            {
                // Live IP change: drop this session so NetworkLoop reconnects to the new host.
                if (_forceReconnect) { _forceReconnect = false; return; }

                // 1) flush any user/auto commands as push-to-talk (NID 98 start/text/stop)
                while (_outbound.TryDequeue(out string text))
                {
                    SendApp(NID_AUDIO_B, "__STT_CONTROL__:start");
                    SendApp(NID_AUDIO_B, text);
                    SendApp(NID_AUDIO_B, "__STT_CONTROL__:stop");
                    _status = $"sent: \"{text}\" — waiting for backend…";
                }

                // 1b) flush recorded mic audio (already framed: start / PCM chunks / stop)
                while (_audioOut.TryDequeue(out byte[] body))
                {
                    SendAppBytes(NID_AUDIO_B, body);
                }

                // 1c) flush control messages (👍/👎 feedback on NID 95, compile result on NID 96)
                while (_ctrlOut.TryDequeue(out var ctl))
                {
                    SendApp(ctl.nid, ctl.body);
                }

                // 2) read whatever arrived. A read TIMEOUT (IOException) is normal pacing;
                // a 0-byte RETURN means the peer closed the connection -> reconnect.
                int n = 0;
                bool readTimedOut = false;
                try { n = _stream.Read(tmp, 0, tmp.Length); }
                catch (System.IO.IOException) { readTimedOut = true; }
                if (n > 0) { for (int i = 0; i < n; i++) buf.Add(tmp[i]); }
                else if (!readTimedOut) { _status = "disconnected — reconnecting…"; return; } // peer closed

                while (TryDecode(buf, out uint b, out byte[] payload, out int consumed))
                {
                    buf.RemoveRange(0, consumed);
                    if (b == NID_OUTPUT_B)
                    {
                        _inbound.Enqueue(Encoding.UTF8.GetString(payload));
                        _status = "backend replied (NID 94) — applied to the cube ✓";
                    }
                }

                // 3) keepalive ping (~1s). A failed write here throws -> NetworkLoop reconnects.
                if ((DateTime.UtcNow - lastPing).TotalSeconds >= 1.0)
                {
                    SendFrame(0, NID_ROOMSERVER_B, Encoding.UTF8.GetBytes(BuildPing()));
                    lastPing = DateTime.UtcNow;
                }
            }
        }

        private void CloseSocket()
        {
            try { _stream?.Close(); } catch { }
            try { _tcp?.Close(); } catch { }
            _stream = null;
            _tcp = null;
        }

        // Resolve the backend host at startup: a pushed config file wins (set on a standalone
        // Quest via `adb push dcvr_server.txt`), then the last in-app value, then loopback.
        private string LoadHost()
        {
            try
            {
                if (System.IO.File.Exists(HostConfigPath))
                {
                    foreach (var line in System.IO.File.ReadAllLines(HostConfigPath))
                    {
                        string t = (line ?? "").Trim();
                        if (t.Length > 0 && !t.StartsWith("#")) { return t; }
                    }
                }
            }
            catch { }
            try
            {
                string p = PlayerPrefs.GetString(HostPrefKey, "");
                if (!string.IsNullOrEmpty(p)) { return p.Trim(); }
            }
            catch { }
            return "127.0.0.1";
        }

        // Commit a new backend IP from the panel: persist it (PlayerPrefs + config file so it
        // survives restarts and can be re-pushed), then force a clean reconnect.
        private void ApplyHost(string raw)
        {
            string h = (raw ?? "").Trim();
            if (h.Length == 0) { _status = "server IP is empty"; return; }
            _host = h;
            try { PlayerPrefs.SetString(HostPrefKey, h); PlayerPrefs.Save(); } catch { }
            try { System.IO.File.WriteAllText(HostConfigPath, h + "\n"); } catch { }
            _status = "server set to " + _host + ":" + Port + " — reconnecting…";
            _forceReconnect = true;
        }

        private bool WaitForSetRoom()
        {
            var buf = new List<byte>();
            var tmp = new byte[8192];
            _stream.ReadTimeout = 10000;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                while (TryDecode(buf, out uint _, out byte[] payload, out int consumed))
                {
                    buf.RemoveRange(0, consumed);
                    if (IsType(payload, "SetRoom")) { return true; }
                }
                int n;
                try { n = _stream.Read(tmp, 0, tmp.Length); }
                catch (System.IO.IOException) { return false; }
                if (n <= 0) { return false; }
                for (int i = 0; i < n; i++) buf.Add(tmp[i]);
            }
            return false;
        }

        // --- wire helpers (exact match to crates/protocol/src/frame.rs) ---
        // frame = [u32 LE body_len][u32 LE a][u32 LE b][payload], body_len = 8 + payload.len()
        private void SendFrame(uint a, uint b, byte[] payload)
        {
            uint bodyLen = (uint)(8 + payload.Length);
            var outBuf = new byte[4 + bodyLen];
            WriteU32(outBuf, 0, bodyLen);
            WriteU32(outBuf, 4, a);
            WriteU32(outBuf, 8, b);
            Array.Copy(payload, 0, outBuf, 12, payload.Length);
            _stream.Write(outBuf, 0, outBuf.Length);
            _stream.Flush();
        }

        // app frame (NID 93/98): payload = 36-char peer uuid prefix + body text
        private void SendApp(uint b, string text)
        {
            SendFrame(0, b, Encoding.UTF8.GetBytes(PeerUuid + text));
        }

        // app frame for raw bytes (NID 98 PCM/control): peer uuid prefix + raw body
        private void SendAppBytes(uint b, byte[] body)
        {
            byte[] prefix = Encoding.UTF8.GetBytes(PeerUuid);
            var payload = new byte[prefix.Length + body.Length];
            Array.Copy(prefix, 0, payload, 0, prefix.Length);
            Array.Copy(body, 0, payload, prefix.Length, body.Length);
            SendFrame(0, b, payload);
        }

        // ---- desktop mic: capture -> mono -> 16 kHz -> s16le -> NID 98 push-to-talk ----
        private void StartMic()
        {
            if (_micRecording) { return; }
            if (string.IsNullOrEmpty(_micDevice))
            {
                _status = "no microphone detected on this machine";
                return;
            }
            _micClip = Microphone.Start(_micDevice, false, MicSeconds, TargetRate);
            _micRecording = true;
            _status = "● recording… click again to STOP & send (speak now)";
        }

        private void StopMic()
        {
            if (!_micRecording) { return; }
            _micRecording = false;
            int pos = Microphone.GetPosition(_micDevice);
            Microphone.End(_micDevice);
            if (_micClip == null) { _status = "mic error (no clip)"; return; }

            int channels = _micClip.channels;
            int srcRate = _micClip.frequency;
            if (pos <= 0) { pos = _micClip.samples; }
            if (pos <= 0) { _status = "no audio captured"; return; }

            var raw = new float[pos * channels];
            _micClip.GetData(raw, 0);
            float[] mono = ToMono(raw, channels);
            float[] rs = Resample(mono, srcRate, TargetRate);
            byte[] pcm = FloatToPcm16(rs);

            // frame it as a push-to-talk utterance the backend understands
            _audioOut.Enqueue(Encoding.UTF8.GetBytes("__STT_CONTROL__:start"));
            const int chunk = 32000; // ~1 s of 16 kHz mono s16le; each chunk > 64 B => treated as PCM
            for (int off = 0; off < pcm.Length; off += chunk)
            {
                int len = Math.Min(chunk, pcm.Length - off);
                var c = new byte[len];
                Array.Copy(pcm, off, c, 0, len);
                _audioOut.Enqueue(c);
            }
            _audioOut.Enqueue(Encoding.UTF8.GetBytes("__STT_CONTROL__:stop"));
            _status = $"sent {pcm.Length / 2} samples (~{(float)rs.Length / TargetRate:0.0}s) — Whisper + backend working…";
        }

        private static float[] ToMono(float[] interleaved, int channels)
        {
            if (channels <= 1) { return interleaved; }
            int frames = interleaved.Length / channels;
            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) { sum += interleaved[i * channels + c]; }
                mono[i] = sum / channels;
            }
            return mono;
        }

        private static float[] Resample(float[] src, int srcRate, int dstRate)
        {
            if (srcRate == dstRate || src.Length == 0) { return src; }
            int dstLen = (int)((long)src.Length * dstRate / srcRate);
            if (dstLen <= 0) { return src; }
            var dst = new float[dstLen];
            double ratio = (double)srcRate / dstRate;
            for (int i = 0; i < dstLen; i++)
            {
                double srcPos = i * ratio;
                int i0 = (int)srcPos;
                int i1 = Math.Min(i0 + 1, src.Length - 1);
                double frac = srcPos - i0;
                dst[i] = (float)(src[i0] * (1.0 - frac) + src[i1] * frac);
            }
            return dst;
        }

        private static byte[] FloatToPcm16(float[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                float f = samples[i];
                if (f > 1f) { f = 1f; }
                else if (f < -1f) { f = -1f; }
                short s = (short)Math.Round(f * 32767f);
                bytes[i * 2] = (byte)s;
                bytes[i * 2 + 1] = (byte)(s >> 8);
            }
            return bytes;
        }

        private static bool TryDecode(List<byte> buf, out uint b, out byte[] payload, out int consumed)
        {
            b = 0; payload = null; consumed = 0;
            if (buf.Count < 4) { return false; }
            uint bodyLen = ReadU32(buf, 0);
            if (bodyLen < 8) { consumed = 4; return false; } // malformed; skip prefix next call
            int total = 4 + (int)bodyLen;
            if (buf.Count < total) { return false; }
            b = ReadU32(buf, 8);
            int payloadLen = (int)bodyLen - 8;
            payload = new byte[payloadLen];
            for (int i = 0; i < payloadLen; i++) { payload[i] = buf[12 + i]; }
            consumed = total;
            return true;
        }

        private static void WriteU32(byte[] dst, int off, uint v)
        {
            dst[off] = (byte)v; dst[off + 1] = (byte)(v >> 8);
            dst[off + 2] = (byte)(v >> 16); dst[off + 3] = (byte)(v >> 24);
        }
        private static uint ReadU32(List<byte> b, int off)
        {
            return (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
        }

        private static bool IsType(byte[] payload, string type)
        {
            try { return (string)JObject.Parse(Encoding.UTF8.GetString(payload))["type"] == type; }
            catch { return false; }
        }

        private string BuildJoin()
        {
            var peer = new JObject
            {
                ["uuid"] = Guid.NewGuid().ToString(),
                ["sceneid"] = new JObject { ["a"] = (long)(uint)new System.Random().Next(), ["b"] = (long)(uint)new System.Random().Next() },
                ["clientid"] = new JObject { ["a"] = (long)_clientA, ["b"] = (long)_clientB },
                ["keys"] = new JArray("ubiq.samples.social.name"),
                ["values"] = new JArray("UnityQuest"),
            };
            string args = new JObject { ["uuid"] = RoomGuid, ["peer"] = peer }.ToString(Newtonsoft.Json.Formatting.None);
            return new JObject { ["type"] = "Join", ["args"] = args }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private string BuildPing()
        {
            string args = new JObject { ["clientid"] = new JObject { ["a"] = (long)_clientA, ["b"] = (long)_clientB } }
                .ToString(Newtonsoft.Json.Formatting.None);
            return new JObject { ["type"] = "Ping", ["args"] = args }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private void OnGUI()
        {
            var label = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true };
            GUI.Label(new Rect(14, 10, Screen.width - 28, 60),
                "<b>DreamCodeVR+ — Path B (networked Mode-C)</b>\n" + _status, label);

            float y = 78f;

            // --- SERVER IP (laptop): change at runtime so the APK never needs rebuilding
            //     when the iPhone-hotspot IP (172.20.10.x) changes between sessions ---
            GUI.Label(new Rect(14, y, 460, 22),
                "<b>Laptop server IP</b>  (iPhone hotspot is usually 172.20.10.x):",
                new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true });
            y += 24f;
            _hostEdit = GUI.TextField(new Rect(14, y, 190, 28), _hostEdit ?? "", 64);
            if (GUI.Button(new Rect(210, y, 96, 28), "Keyboard"))
            {
                _keyboard = TouchScreenKeyboard.Open(_hostEdit ?? "",
                    TouchScreenKeyboardType.NumbersAndPunctuation);
            }
            if (GUI.Button(new Rect(310, y, 156, 28), "Apply & Reconnect"))
            {
                ApplyHost(_hostEdit);
            }
            y += 34f;
            GUI.Label(new Rect(14, y, 460, 20),
                "target: " + _host + ":" + Port,
                new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true });
            y += 26f;

            // FREE-TEXT command box: type anything and Send it to the backend (real GPT).
            GUI.Label(new Rect(14, y, 420, 22),
                "<b>Type a command and press Send (or Enter):</b>",
                new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true });
            y += 24f;
            GUI.SetNextControlName("cmd");
            _typed = GUI.TextField(new Rect(14, y, 300, 28), _typed, 200);
            bool enterPressed = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == "cmd";
            if (GUI.Button(new Rect(320, y, 92, 28), "Send") || enterPressed)
            {
                if (!string.IsNullOrWhiteSpace(_typed)) { _outbound.Enqueue(_typed.Trim()); }
            }
            y += 38f;

            // Quick preset buttons (no Input System needed). Each sends a live command.
            if (Button(ref y, "Send: make it red")) _outbound.Enqueue("make this cube red");
            if (Button(ref y, "Send: make it green")) _outbound.Enqueue("make it green");
            if (Button(ref y, "Send: make it blue")) _outbound.Enqueue("make it blue");
            if (Button(ref y, "Send: make it bigger")) _outbound.Enqueue("make it bigger");
            if (Button(ref y, "Send: spin it")) _outbound.Enqueue("spin it");

            // --- SPEAK to it (push-to-talk mic): click to record, click to send ---
            y += 10f;
            Color prev = GUI.color;
            GUI.color = _micRecording ? new Color(1f, 0.35f, 0.35f) : prev;
            string micLabel = _micRecording ? "■  STOP & send (recording — speak now)" : "🎤  Click to record a spoken command";
            if (GUI.Button(new Rect(14, y, 300, 34), micLabel))
            {
                if (_micRecording) { StopMic(); } else { StartMic(); }
            }
            GUI.color = prev;
            y += 38f;
            GUI.Label(new Rect(14, y, 460, 22),
                string.IsNullOrEmpty(_micDevice)
                    ? "(no microphone detected — typing still works)"
                    : "mic: " + _micDevice + "   — backend needs DCVR_STT_OPENAI=true",
                new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true });

            // --- rate the last result (teaches the AI via RAG) ---
            y += 28f;
            if (_hasResult)
            {
                GUI.Label(new Rect(14, y, 320, 22), "<b>Rate the last result (teaches the AI):</b>",
                    new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true });
                y += 24f;
                if (GUI.Button(new Rect(14, y, 105, 30), "👍 Like"))
                {
                    _ctrlOut.Enqueue((NID_FEEDBACK_B, "{\"liked\":true}"));
                    _status = "👍 sent — the AI will favour this style";
                }
                if (GUI.Button(new Rect(125, y, 105, 30), "👎 Dislike"))
                {
                    _ctrlOut.Enqueue((NID_FEEDBACK_B, "{\"liked\":false}"));
                    _status = "👎 sent";
                }
                if (GUI.Button(new Rect(236, y, 95, 30), "↺ Reset"))
                {
                    ResetTarget();
                    _status = "cube reset";
                }
            }
        }

        private static bool Button(ref float y, string text)
        {
            bool clicked = GUI.Button(new Rect(14, y, 220, 28), text);
            y += 32f;
            return clicked;
        }

        private void OnDestroy()
        {
            _running = false;
            try { _stream?.Close(); } catch { }
            try { _tcp?.Close(); } catch { }
        }
    }
}
