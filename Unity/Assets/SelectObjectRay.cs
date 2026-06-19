using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System;
using System.Linq;
using DreamCodeVR2.ContextBridge;
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
    private RoomClient roomClient;
    public NetworkId networkId = new NetworkId(93);
    [SerializeField] private bool cloneForOtherHands = true;

    public bool isSelecting;
    public GameObject selectedObject;
    private GameObject lastSelectedObject = null;
    //private Material lastMaterial = null;

    private HandController handController;
    public CodeGenerationManager codeGenerationManager;
    public MicrophoneCapture genieMicrophoneCapture;
    public bool controlMicrophoneGain = false;
    [SerializeField] private bool logSelectionDebug = true;

    private new LineRenderer renderer;

    private readonly float range = 8f;
    //private readonly float curve = 20f;
    //private readonly int segments = 50;

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
        if (cloneForOtherHands)
        {
            CreateMissingHandSelectors();
        }

        foreach (IPrimaryButtonProvider item in GetComponentsInParent<MonoBehaviour>(true).Where(c => c is IPrimaryButtonProvider))
        {
            item.PrimaryButtonPress.AddListener(UpdateSelect);
            item.PrimaryButtonPress.AddListener(listenForCommand);
        }

        // Get HandController in parent
        handController = GetComponentsInParent<MonoBehaviour>(true).Where(c => c is HandController).FirstOrDefault() as HandController;
        if (handController)
        {
            handController.TriggerPress.AddListener(listenForCommand);
        }
        else
        {
            Debug.LogWarning("SelectObjectRay could not find HandController parent.", this);
        }

        roomClient = NetworkScene.Find(this)?.GetComponentInChildren<RoomClient>();
    }

    private void CreateMissingHandSelectors()
    {
        foreach (var hand in FindObjectsByType<HandController>(FindObjectsSortMode.None))
        {
            if (hand.GetComponentInChildren<SelectObjectRay>(true))
            {
                continue;
            }

            var selector = new GameObject("Selector");
            selector.transform.SetParent(hand.transform, false);

            var lineRenderer = selector.AddComponent<LineRenderer>();
            CopyLineRenderer(renderer, lineRenderer);

            var ray = selector.AddComponent<SelectObjectRay>();
            ray.cloneForOtherHands = false;
            ray.networkId = networkId;
            ray.codeGenerationManager = codeGenerationManager;
            ray.genieMicrophoneCapture = genieMicrophoneCapture;
            ray.controlMicrophoneGain = controlMicrophoneGain;
        }
    }

    private void CopyLineRenderer(LineRenderer source, LineRenderer target)
    {
        target.sharedMaterial = source.sharedMaterial;
        target.useWorldSpace = true;
        target.startWidth = source.startWidth;
        target.endWidth = source.endWidth;
        target.startColor = source.startColor;
        target.endColor = source.endColor;
        target.numCornerVertices = source.numCornerVertices;
        target.numCapVertices = source.numCapVertices;
        target.textureMode = source.textureMode;
        target.alignment = source.alignment;
        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
    }

    public void listenForCommand(bool listen)
    {
        if (controlMicrophoneGain && genieMicrophoneCapture)
        {
            genieMicrophoneCapture.gain = listen ? 1.0f : 0.0f;
        }
    }

    public void UpdateSelect(bool selectActivation)
    {
        isSelecting = selectActivation;
    }

    private struct SelectionHit
    {
        public RaycastHit hitInfo;
        public GameObject hitObject;
        public GameObject resolvedObject;
        public AIEditableObject editableObject;
    }

    private void SetCurrentSelection(SelectionHit selection)
    {
        if (lastSelectedObject != null)
        {
            //lastSelectedObject.GetComponent<Outline>().enabled = false; 
            renderer.material.color = Color.green;
        }

        //obj.GetComponent<Outline>().enabled = true;
        renderer.material.color = Color.red;
        if (codeGenerationManager)
        {
            codeGenerationManager.targetObject = selection.resolvedObject;
        }

        if (logSelectionDebug && lastSelectedObject != selection.resolvedObject)
        {
            Debug.Log($"[Selection] hit={selection.hitObject.name} resolved={selection.editableObject.objectId} display={selection.editableObject.displayName}");
            Debug.Log($"[Selection] hitTag={selection.hitObject.tag} resolvedTag={selection.resolvedObject.tag}");
        }

        lastSelectedObject = selection.resolvedObject;
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

        positions[0] = transform.position;
        positions[1] = transform.position + transform.forward * range;

        renderer.positionCount = 2;
        renderer.SetPositions(positions);

        renderer.startWidth = 0.01f;
        renderer.endWidth = 0.01f;

        var selection = GetSelectionHit(positions[0], transform.forward);
        selectedObject = selection?.resolvedObject;

        if (selection.HasValue)
        {
            SetCurrentSelection(selection.Value);
        }
        else
        {
            if (lastSelectedObject != null)
            {
                //lastSelectedObject.GetComponent<Outline>().enabled = false;
                renderer.material.color = Color.green;
                if (codeGenerationManager)
                {
                    codeGenerationManager.targetObject = null;
                }
                lastSelectedObject = null;
            }
        }
    }

    private SelectionHit? GetSelectionHit(Vector3 origin, Vector3 direction)
    {
        var hits = Physics.RaycastAll(origin, direction, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            .OrderBy(hit => hit.distance);

        foreach (var hit in hits)
        {
            var hitObject = hit.collider ? hit.collider.gameObject : null;
            if (hitObject == null)
            {
                continue;
            }

            var editableObject = hitObject.GetComponentInParent<AIEditableObject>();
            if (editableObject == null)
            {
                continue;
            }

            var resolvedObject = editableObject.gameObject;
            if (!hitObject.CompareTag("game") && !resolvedObject.CompareTag("game"))
            {
                continue;
            }

            return new SelectionHit
            {
                hitInfo = hit,
                hitObject = hitObject,
                resolvedObject = resolvedObject,
                editableObject = editableObject
            };
        }

        return null;
    }

    // We only send messages to the server, so we don't need to implement this method
    public void ProcessMessage(ReferenceCountedSceneGraphMessage msg)
    {
    }
}
