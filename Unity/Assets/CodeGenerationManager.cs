using System.Collections;
using System.Collections.Generic;
using Ubiq.Networking;
using UnityEngine;
using Ubiq.Dictionaries;
using Ubiq.Messaging;
using Ubiq.Logging.Utf8Json;
using Ubiq.Rooms;
using System;
using System.Text;
using Ubiq.Samples;
using Ubiq.Voip;
using Ubiq.Voip.Implementations;

public class CodeGenerationManager : MonoBehaviour
{
    private class AssistantSpeechUnit
    {
        public float startTime;
        public int samples;
        public string speechTargetName;

        public float endTime { get { return startTime + samples/(float)AudioSettings.outputSampleRate; } }
    }
    public SelectRay selectRay;
    public NetworkId networkId = new NetworkId(94);
    private NetworkContext context;

    public InjectableAudioSource audioSource;
    public VirtualAssistantController assistantController;
    public AudioSourceVolume volume;

    private string speechTargetName;

    private List<AssistantSpeechUnit> speechUnits = new List<AssistantSpeechUnit>();
    public TestRoslyn testRoslyn;

    public GameObject targetObject;
    public GameObject sceneController;

    [Serializable]
    private struct Message
    {
        public string type;
        public string peer;
        public string data;
    }

    // Start is called before the first frame update
    void Start()
    {
        context = NetworkScene.Register(this,networkId);
    }

    // Update is called once per frame
    void Update()
    {
        while(speechUnits.Count > 0)
        {
            if (Time.time > speechUnits[0].endTime)
            {
                speechUnits.RemoveAt(0);
            }
            else
            {
                break;
            }
        }

        if (assistantController)
        {
            var speechTarget = null as string;
            if (speechUnits.Count > 0)
            {
                speechTarget = speechUnits[0].speechTargetName;
            }

            assistantController.UpdateAssistantSpeechStatus(speechTarget,volume.volume);
        }
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage data)
    {
        Message message = data.FromJson<Message>();
        // Unity now also SENDS CodeAttachResult on this channel - only act on the
        // server's CodeGenerated messages so replies can never re-trigger an attach.
        if (message.type != "CodeGenerated") return;
        Debug.Log("Res: " + message.data.ToString());
        testRoslyn.SetCodeString(message.data.ToString());

        // Baseline attach acknowledgement (study measure): the legacy direct-apply
        // path reports whether the generated behavior actually compiled/attached,
        // and how long that took, so baseline trials get a validated-execution
        // timestamp comparable to the agentic path's ArtifactResult.
        var target = targetObject != null ? targetObject : sceneController;
        var startedAt = Time.realtimeSinceStartupAsDouble;
        var attached = testRoslyn.TryCompileAndAttach(target, message.data.ToString(), out _, out var error);
        var durationMs = (Time.realtimeSinceStartupAsDouble - startedAt) * 1000.0;
        if (!attached) Debug.LogError("[Baseline] attach failed: " + error);
        SendAttachResult(message.peer, attached, durationMs, error);
    }

    private void SendAttachResult(string peer, bool attached, double durationMs, string error)
    {
        var payload = attached
            ? "{\"status\":\"attached\",\"commitAttachDurationMs\":" + durationMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "}"
            : "{\"status\":\"error\",\"error\":\"" + EscapeJson(error) + "\"}";
        var result = new Message { type = "CodeAttachResult", peer = peer, data = payload };
        var json = JsonUtility.ToJson(result);
        var bytes = Encoding.UTF8.GetBytes(json);
        var outgoing = ReferenceCountedSceneGraphMessage.Rent(bytes.Length);
        bytes.CopyTo(new Span<byte>(outgoing.bytes, outgoing.start, bytes.Length));
        context.Send(outgoing);
    }

    private static string EscapeJson(string value)
    {
        return string.IsNullOrEmpty(value) ? "" : value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
    }
}
