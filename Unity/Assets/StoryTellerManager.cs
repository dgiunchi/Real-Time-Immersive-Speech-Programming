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
using UnityEngine.Networking;
using TinyJson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

public class StoryTellerManager : MonoBehaviour
{
    private class AssistantSpeechUnit
    {
        public float startTime;
        public int samples;
        public string speechTargetName;

        public float endTime { get { return startTime + samples/(float)AudioSettings.outputSampleRate; } }
    }

    public NetworkId networkId = new NetworkId(95);
    private NetworkContext context;

    public InjectableAudioSource audioSource;
    public VirtualAssistantController assistantController;
    public AudioSourceVolume volume;

    private string speechTargetName;

    private List<AssistantSpeechUnit> speechUnits = new List<AssistantSpeechUnit>();

    [Serializable]
    private struct Message
    {
        public string type;
        public string targetPeer;
        //public string audiolength;
        public string data;
    }

    // from texture
    private RoomClient client;
    public string serverBaseUrl;

    public Texture2D tempTexture;
    public Dictionary<string, Texture2D> textureList = new Dictionary<string, Texture2D>();

    [Serializable]
    public struct MaterialKeywords
    {
        public Material material;
        public List<string> keywords;
    }
    public MaterialKeywords[] materialKeywords;

    [Serializable]
    public struct ObjectTargetKeywords
    {
        public GameObject targetObject;
        public int targetSubmeshIndex;
        public string targetMaterialName;
        public string[] targetKeywords;
    }
    public ObjectTargetKeywords[] targets;
    private List<Tuple<GameObject, int>> currentTargets;
    public GameObject target;
    private int currentSubmeshIndex;
    private string currentTargetMaterialName;

    // Start is called before the first frame update
    void Start()
    {
        context = NetworkScene.Register(this,networkId);
        client = GetComponentInParent<RoomClient>();
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
        

        if (data.ToString().Contains("StoryTelling")) {

            var parseddata = JObject.Parse(data.ToString())["data"];

            // Deserialize JSON array to list of string arrays
            List<string[]> tuples = JsonConvert.DeserializeObject<List<string[]>>(parseddata.ToString());

            // Convert to list of tuples
            List<Tuple<string, string>> tupleList = tuples.Select(x => Tuple.Create(x[0], x[1])).ToList();

            // Print tuples
            foreach (var tuple in tupleList)
            {
                LoadPNGFromURL(tuple.Item2, AddTextureToList);
            }

        } 
        else if (data.ToString().Contains("DisplayImage"))
        {
            Message message = data.FromJson<Message>();
            Debug.Log(message.data);
            SetTexture(textureList[message.data.ToString()]);
        }
        else if (data.ToString().Contains("BackgroundAudio"))
        {
            Message message = data.FromJson<Message>();
            Debug.Log(message.data);
            //SetAudio(textureList[message.data.ToString()]);
        }
        else // audio data or header
        {

            Debug.Assert(audioSource);
            // If the data is less than 100 bytes, then we have have received the audio info header
            if (data.data.Length < 100)
            {
                // Try to parse the data as a message, if it fails, then we have received the audio data
                Message message;
                try
                {
                    message = data.FromJson<Message>();
                    speechTargetName = message.targetPeer;
                    //Debug.Log("Received audio for peer: " + message.targetPeer + " with length: " + message.data);
                    return;
                }
                catch (Exception e)
                {
                    Debug.Log("Received audio data");
                }
            }

            if (data.data.Length < 200)
            {
                return;
            }

            var speechUnit = new AssistantSpeechUnit();
            var prevUnit = speechUnits.Count > 0 ? speechUnits[speechUnits.Count - 1] : null;
            speechUnit.startTime = prevUnit != null ? prevUnit.endTime : Time.time;
            speechUnit.samples = data.data.Length / 2;
            speechUnit.speechTargetName = speechTargetName;
            speechUnits.Add(speechUnit);

            audioSource.InjectPcm(data.data.ToArray());
        }


    }
    private void AddTextureToList(string name, Texture2D newTexture)
    {
        Debug.Log("Loaded..." + textureList.Count.ToString());
        textureList.Add(name, newTexture);
    }


    private void SetTexture(Texture2D newTexture)
    {
        target.GetComponent<Renderer>().material.mainTexture = newTexture;
    }

    void LoadPNGFromURL(string name, System.Action<string, Texture2D> onComplete)
    {
        UnityWebRequest www = UnityWebRequestTexture.GetTexture(serverBaseUrl + name);
        www.SendWebRequest().completed += operation =>
        {
            if (www.isNetworkError || www.isHttpError)
            {
                Debug.Log(www.error);
                onComplete(null,null);
            }
            else
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(www);
                var mmTexture = new Texture2D(texture.width, texture.height, texture.format, true);
                mmTexture.SetPixelData(texture.GetRawTextureData<byte>(), 0);
                mmTexture.Apply(true, true);
                onComplete(name, mmTexture);
            }
        };
    }

    // Find all submeshes of GameObjects in the scene with the given material name. Return a list of tuples of the GameObject and submesh index.
    private List<Tuple<GameObject, int>> FindTargets(string materialName)
    {
        List<Tuple<GameObject, int>> targets = new List<Tuple<GameObject, int>>();
        GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj.GetComponent<Renderer>() != null)
            {
                Material[] materials = obj.GetComponent<Renderer>().materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i].name == materialName)
                    {
                        targets.Add(new Tuple<GameObject, int>(obj, i));
                    }
                }
            }
        }
        return targets;
    }

    
}
