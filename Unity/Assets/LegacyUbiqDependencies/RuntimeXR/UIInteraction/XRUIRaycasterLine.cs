using UnityEngine;

namespace Ubiq.XR
{
    [RequireComponent(typeof(XRUIRaycaster))]
    [RequireComponent(typeof(LineRenderer))]
    public class XRUIRaycasterLine : MonoBehaviour
    {
        [Tooltip("Keep the controller ray visible at a fixed length when it is not currently over an XR UI canvas.")]
        public bool showRayOnMiss;
        [Min(0.05f)]
        public float missRayDistance = 2.0f;
        private XRUIRaycaster xruiRaycaster;
        private LineRenderer lineRenderer;

        private void Awake()
        {
            xruiRaycaster = GetComponent<XRUIRaycaster>();
            lineRenderer = GetComponent<LineRenderer>();
        }

        private void OnEnable()
        {
            xruiRaycaster.onRaycastHit.AddListener(XRUIRaycaster_OnRaycastHit);
            xruiRaycaster.onRaycastMiss.AddListener(XRUIRaycaster_OnRaycastMiss);
        }

        private void OnDisable()
        {
            if (xruiRaycaster)
            {
                xruiRaycaster.onRaycastHit.RemoveListener(XRUIRaycaster_OnRaycastHit);
                xruiRaycaster.onRaycastMiss.RemoveListener(XRUIRaycaster_OnRaycastMiss);
            }
        }

        private void XRUIRaycaster_OnRaycastHit (Vector3 hit, Vector3 normal)
        {
            lineRenderer.enabled = true;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, transform.position);
            lineRenderer.SetPosition(1, hit);
        }

        private void XRUIRaycaster_OnRaycastMiss ()
        {
            if (!showRayOnMiss)
            {
                lineRenderer.enabled = false;
                return;
            }

            lineRenderer.enabled = true;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, transform.position);
            lineRenderer.SetPosition(1, transform.position + transform.forward * missRayDistance);
        }

    }
}
