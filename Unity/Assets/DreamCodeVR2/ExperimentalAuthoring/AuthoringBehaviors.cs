using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public abstract class AuthoringRuntimeBehavior : MonoBehaviour { public string behaviorId; public bool IsActive => enabled; public virtual void Configure(AuthoringAction action) { behaviorId = action.behaviorId; } }
    public class AuthoringRotateBehavior : AuthoringRuntimeBehavior { public float degreesPerSecond = 45f; private void Update() { transform.Rotate(Vector3.up, Mathf.Clamp(degreesPerSecond, -180f, 180f) * Time.deltaTime, Space.World); } }
    public class AuthoringBlinkBehavior : AuthoringRuntimeBehavior { public float interval = .5f; private Renderer[] renderers; private float next; private void Awake() { renderers = GetComponentsInChildren<Renderer>(true); } private void Update() { if (Time.time < next) return; next = Time.time + Mathf.Clamp(interval, .1f, 5f); foreach (var r in renderers) if (r) r.enabled = !r.enabled; } }
    public class AuthoringFollowTargetBehavior : AuthoringRuntimeBehavior { public Transform target; public float speed = 1f; private void Update() { if (target) transform.position = Vector3.MoveTowards(transform.position, target.position, Mathf.Clamp(speed, .05f, 5f) * Time.deltaTime); } }
    public class AuthoringMoveBetweenAnchorsBehavior : AuthoringRuntimeBehavior { public Transform first; public Transform second; public float speed = 1f; private bool toSecond = true; private void Update() { var goal = toSecond ? second : first; if (!goal) return; transform.position = Vector3.MoveTowards(transform.position, goal.position, Mathf.Clamp(speed,.05f,5f)*Time.deltaTime); if ((transform.position-goal.position).sqrMagnitude < .0001f) toSecond = !toSecond; } }
    public class AuthoringContactTrigger : AuthoringRuntimeBehavior { public string targetObjectId; private void OnTriggerEnter(Collider other) { if (other.GetComponentInParent<DreamCodeVR2.ContextBridge.AIEditableObject>()?.objectId == targetObjectId) gameObject.SetActive(true); } }
    public class AuthoringProximityTrigger : AuthoringRuntimeBehavior { public Transform target; public float radius = 1f; private void Update() { if (target && (target.position-transform.position).sqrMagnitude <= radius*radius) gameObject.SetActive(true); } }
    public class AuthoringTaskCompletionTrigger : AuthoringRuntimeBehavior { public bool triggered; public void Trigger() { triggered = true; gameObject.SetActive(true); } }
    public class AuthoringObjectLink : MonoBehaviour { public string linkId; public string sourceObjectId; public string targetObjectId; public string linkOperation; public string propertyValue; public void Activate() { var target = AuthoringActionExecutor.FindEditable(targetObjectId); if (!target) return; if (linkOperation == "activate") target.gameObject.SetActive(true); } }
}
