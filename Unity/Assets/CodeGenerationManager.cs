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
using TMPro;

public class CodeGenerationManager : MonoBehaviour
{
    private class AssistantSpeechUnit
    {
        public float startTime;
        public int samples;
        public string speechTargetName;
        public float endTime => startTime + samples / (float)AudioSettings.outputSampleRate;
    }

    public SelectRay selectRay;
    public NetworkId networkId = new NetworkId(94);
    private NetworkContext context;

    public InjectableAudioSource audioSource;
    public VirtualAssistantController assistantController;
    public AudioSourceVolume volume;

    private string speechTargetName;
    private readonly List<AssistantSpeechUnit> speechUnits = new List<AssistantSpeechUnit>();

    public TestRoslyn testRoslyn;
    public GameObject targetObject;
    public GameObject sceneController;

    [Header("Study feedback (optional)")]
    public FeedbackPanelController feedbackPanel;

    [Header("VR code-result display (optional)")]
    public TextMeshProUGUI codeResultText;
    public GameObject codeResultPanel;

    [Serializable]
    private struct Message
    {
        public string type;
        public string peer;
        public string data;
    }

    void Start()
    {
        context = NetworkScene.Register(this, networkId);
    }

    void Update()
    {
        while (speechUnits.Count > 0)
        {
            if (Time.time > speechUnits[0].endTime) speechUnits.RemoveAt(0);
            else break;
        }

        if (assistantController)
        {
            var speechTarget = speechUnits.Count > 0 ? speechUnits[0].speechTargetName : null;
            assistantController.UpdateAssistantSpeechStatus(speechTarget, volume.volume);
        }
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage data)
    {
        Message message = data.FromJson<Message>();
        var code = message.data;
        Debug.Log("[CodeGen] Received code:\n" + code);

        // Show the generated code to the participant so they can see what the
        // system produced (matches supervisor requirement to always show the code).
        DisplayCodeResult(code);

        testRoslyn.SetCodeString(code);
        try
        {
            var runTarget = targetObject != null ? targetObject : sceneController;
            testRoslyn.RunCode(runTarget);
            feedbackPanel?.ShowSuccess("Code executed successfully.");
        }
        catch (Exception ex)
        {
            var errorMsg = "Execution error: " + ex.Message;
            Debug.LogError("[CodeGen] " + errorMsg);
            feedbackPanel?.ShowError("Execution failed.", errorMsg);
        }
    }

    private void DisplayCodeResult(string code)
    {
        if (codeResultText) codeResultText.text = code;
        if (codeResultPanel) codeResultPanel.SetActive(true);
    }
}
