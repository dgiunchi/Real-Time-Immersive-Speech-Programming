using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public abstract class AuthoringRuntimeBehavior : MonoBehaviour { public string behaviorId; public bool IsActive => enabled; public virtual void Configure(AuthoringAction action) { behaviorId = action.behaviorId; } }
    public class AuthoringRotateBehavior : AuthoringRuntimeBehavior { public float degreesPerSecond = 45f; private void Update() { transform.Rotate(Vector3.up, Mathf.Clamp(degreesPerSecond, -180f, 180f) * Time.deltaTime, Space.World); } }
    public class AuthoringBlinkBehavior : AuthoringRuntimeBehavior { public float interval = .5f; private Renderer[] renderers; private float next; private void Awake() { renderers = GetComponentsInChildren<Renderer>(true); } private void Update() { if (Time.time < next) return; next = Time.time + Mathf.Clamp(interval, .1f, 5f); foreach (var r in renderers) if (r) r.enabled = !r.enabled; } }
    public class AuthoringObjectLink : MonoBehaviour { public string linkId; public string sourceObjectId; public string targetObjectId; public string linkOperation; public string propertyValue; public void Activate() { var target = AuthoringActionExecutor.FindEditable(targetObjectId); if (!target) return; if (linkOperation == "activate") target.gameObject.SetActive(true); } }
}
