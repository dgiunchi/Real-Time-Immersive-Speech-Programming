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
using Ubiq.Voip.Implementations.Dotnet;

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
        Debug.Log("Res: " + message.data.ToString());
        testRoslyn.SetCodeString(message.data.ToString());

        if (targetObject != null)
        {
            testRoslyn.RunCode(targetObject);
        }
        else {
            testRoslyn.RunCode(sceneController);
        }
        
    }
}
