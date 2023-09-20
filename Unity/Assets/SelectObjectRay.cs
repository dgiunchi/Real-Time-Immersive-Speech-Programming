using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System;
using System.Linq;
using Ubiq.XR;
using Ubiq.Networking;
using Ubiq.Logging;
using Ubiq.Voip;
using Ubiq.Rooms;
using Ubiq.Messaging;
using Ubiq.Logging.Utf8Json;


[RequireComponent(typeof(LineRenderer))]
public class SelectObjectRay : MonoBehaviour
{
    private NetworkContext context;
    private RoomClient roomClient;
    public NetworkId networkId = new NetworkId(93);

    public bool isSelecting;
    public GameObject selectedObject;
    private GameObject lastSelectedObject = null;
    private Material lastMaterial = null;

    private HandController handController;
    public CodeGenerationManager codeGenerationManager;
    public MicrophoneCapture genieMicrophoneCapture;

    private new LineRenderer renderer;

    private readonly float range = 8f;
    private readonly float curve = 20f;
    private readonly int segments = 50;

    private Color validColour = new Color(0f, 1f, 0f, 0.4f);
    private Color collisionColour = new Color(1f, 1f, 0f, 0.4f);
    private Color invalidColour = new Color(1f, 0f, 0f, 0.4f);

    private void Awake()
    {
        renderer = GetComponent<LineRenderer>();
        renderer.useWorldSpace = true;
    }

    private void Start()
    {
        context = NetworkScene.Register(this, networkId);

        foreach (IPrimaryButtonProvider item in GetComponentsInParent<MonoBehaviour>().Where(c => c is IPrimaryButtonProvider))
        {
            item.PrimaryButtonPress.AddListener(UpdateSelect);
            item.PrimaryButtonPress.AddListener(listenForCommand);
        }

        // Get HandController in parent
        handController = GetComponentsInParent<MonoBehaviour>().Where(c => c is HandController).FirstOrDefault() as HandController;
        handController.TriggerPress.AddListener(listenForCommand);

        var roomClient = NetworkScene.Find(this).GetComponentInChildren<RoomClient>();
    }

    public void listenForCommand(bool listen)
    {
        genieMicrophoneCapture.gain = listen ? 1.0f : 0.0f;
    }

    public void UpdateSelect(bool selectActivation)
    {
        isSelecting = selectActivation;
    }

    private void SetCurrentSelection(GameObject obj)
    {
        
        Debug.Log("Sending current selection: " + obj.name);
        

        if (lastSelectedObject != null)
        {
            lastSelectedObject.GetComponent<Outline>().enabled = false; 
        }

        obj.GetComponent<Outline>().enabled = true;
        codeGenerationManager.targetObject = obj;
        lastSelectedObject = obj;
    }

    private void Update()
    {
        ComputeStraightRay();
        renderer.enabled = true;
        /*if (isSelecting)
        {
            ComputeStraightRay();
            renderer.enabled = true;
        }
        else
        {
            renderer.enabled = false;
        }*/

        if (!roomClient)
        {
            roomClient = NetworkScene.Find(this)?.GetComponentInChildren<RoomClient>();
            if (!roomClient)
            {
                return;
            }
        }
    }

    private void ComputeStraightRay()
    {
        renderer.sharedMaterial.color = validColour;

        var positions = new Vector3[2];

        RaycastHit raycasthitinfo;

        positions[0] = transform.position;
        positions[1] = transform.position + transform.forward * range;

        Physics.Linecast(positions[0], positions[1], out raycasthitinfo);

        renderer.positionCount = 2;
        renderer.SetPositions(positions);

        renderer.startWidth = 0.01f;
        renderer.endWidth = 0.01f;

        selectedObject = GetSelectedObject(raycasthitinfo);
        if (selectedObject != null && selectedObject.tag == "game")
        {
            Debug.Log("hit the box");
            SetCurrentSelection(selectedObject);
            lastSelectedObject = selectedObject;
        } else { 
            if (lastSelectedObject != null)
            {
                lastSelectedObject.GetComponent<Outline>().enabled = false;
                codeGenerationManager.targetObject = null;
            }
        }
    }

    private GameObject GetSelectedObject(RaycastHit raycasthitinfo)
    {
        if (raycasthitinfo.collider != null)
        {
            var meshCollider = raycasthitinfo.collider as MeshCollider;
            if (meshCollider != null)
            {
                Debug.Log("hit " + meshCollider.gameObject.name);
                return meshCollider.gameObject;
            }
        }
        return null;
    }

    // We only send messages to the server, so we don't need to implement this method
    public void ProcessMessage(ReferenceCountedSceneGraphMessage msg)
    {
    }
}
