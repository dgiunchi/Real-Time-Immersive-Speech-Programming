using System.Collections.Generic;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.Quest;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class ExperimentalPlaythroughReset : MonoBehaviour
    {
        private class Snapshot { public AIEditableObject editable; public Vector3 position; public Quaternion rotation; public Vector3 scale; public bool active; public bool kinematic; public bool gravity; public bool[] colliderEnabled; public Color[] colors; public string semanticState; public bool grabbable; public bool movable; public bool interactable; public bool adapterGrabbable; }
        public QuestRuntimeState runtimeState; public AuthoringUndoManager undoManager; public AuthoringActionExecutor executor; public AuthoringProtocolClient protocol; public DynamicStoryTaskController dynamicStory;
        private readonly List<Snapshot> snapshots=new List<Snapshot>();
        private void Start(){CaptureInitialState();}
        public void CaptureInitialState()
        { snapshots.Clear(); foreach(var e in FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include,FindObjectsSortMode.None)){var body=e.GetComponent<Rigidbody>();var cs=e.GetComponentsInChildren<Collider>(true);var rs=e.GetComponentsInChildren<Renderer>(true);var afford=e.GetComponent<AuthoringAffordanceState>();var adapter=e.GetComponent<ExperimentalGrabbableAdapter>();var s=new Snapshot{editable=e,position=e.transform.position,rotation=e.transform.rotation,scale=e.transform.localScale,active=e.gameObject.activeSelf,kinematic=body&&body.isKinematic,gravity=body&&body.useGravity,colliderEnabled=new bool[cs.Length],colors=new Color[rs.Length],semanticState=e.GetComponent<AuthoringSemanticState>()?.state,grabbable=afford&&afford.grabbable,movable=afford&&afford.movable,interactable=afford&&afford.interactable,adapterGrabbable=adapter&&adapter.grabbable};for(int i=0;i<cs.Length;i++)s.colliderEnabled[i]=cs[i].enabled;for(int i=0;i<rs.Length;i++)s.colors[i]=ReadColor(rs[i].material);snapshots.Add(s);} }
        public void ResetExperimentalPlaythrough()
        {
            foreach(var runtime in FindObjectsByType<AIEditableObject>(FindObjectsInactive.Include,FindObjectsSortMode.None)) if(runtime.labels!=null&&System.Array.Exists(runtime.labels,x=>x=="runtime_created")) Destroy(runtime.gameObject);
            foreach(var s in snapshots){if(!s.editable)continue;var e=s.editable;e.gameObject.SetActive(s.active);e.transform.SetPositionAndRotation(s.position,s.rotation);e.transform.localScale=s.scale;var body=e.GetComponent<Rigidbody>();if(body){body.isKinematic=s.kinematic;body.useGravity=s.gravity;}var cs=e.GetComponentsInChildren<Collider>(true);for(int i=0;i<cs.Length&&i<s.colliderEnabled.Length;i++)cs[i].enabled=s.colliderEnabled[i];var rs=e.GetComponentsInChildren<Renderer>(true);for(int i=0;i<rs.Length&&i<s.colors.Length;i++)SetColor(rs[i].material,s.colors[i]);var afford=e.GetComponent<AuthoringAffordanceState>();if(afford){afford.grabbable=s.grabbable;afford.movable=s.movable;afford.interactable=s.interactable;}var adapter=e.GetComponent<ExperimentalGrabbableAdapter>();if(adapter)adapter.SetGrabbable(s.adapterGrabbable);var semantic=e.GetComponent<AuthoringSemanticState>();if(semantic)semantic.state=s.semanticState;foreach(var behavior in e.GetComponents<AuthoringRuntimeBehavior>())Destroy(behavior);foreach(var link in e.GetComponents<AuthoringObjectLink>())Destroy(link);}
            foreach(var drawer in FindObjectsByType<ExperimentalDrawerController>(FindObjectsInactive.Include,FindObjectsSortMode.None)) drawer.ResetClosed();
            foreach(var anchor in FindObjectsByType<AuthoringAnchor>(FindObjectsInactive.Include,FindObjectsSortMode.None)) anchor.SetOccupied(anchor.GetComponentsInChildren<AIEditableObject>(true).Length>0);
            runtimeState?.ResetQuest();dynamicStory?.ResetDynamicState();undoManager?.Clear();executor?.ClearProcessedActions();protocol?.ClearPendingProtocolState();
        }
        private static Color ReadColor(Material m)=>m&&m.HasProperty("_BaseColor")?m.GetColor("_BaseColor"):m?m.color:Color.white;
        private static void SetColor(Material m,Color c){if(!m)return;if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",c);if(m.HasProperty("_Color"))m.color=c;}
    }
}
