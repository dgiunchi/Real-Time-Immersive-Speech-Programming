using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Events;

namespace Ubiq.XR
{
    /// <summary>
    /// A component that interacts with the standard Unity UI using a Hand Controller.
    /// This component operates outside of the Input Module, since the Input Module & Event System behaviour
    /// is limited to one module, and we do not want to re-create the mouse UI.
    /// </summary>
    /// <remarks>
    /// This code is based on the Unity Interactive 360 samples, but modified so it doesn't need physics collisions.
    /// </remarks>
    public class XRUIRaycaster : MonoBehaviour
    {
        [System.Serializable]
        public class RaycastHitEvent : UnityEvent<Vector3,Vector3> { };
        [System.Serializable]
        public class RaycastMissEvent : UnityEvent { };

        public RaycastHitEvent onRaycastHit;
        public RaycastMissEvent onRaycastMiss;
        [Tooltip("When enabled, world colliders do not truncate this controller ray before it reaches registered XR UI canvases.")]
        public bool ignorePhysicsOcclusion;
        [Tooltip("Keeps a UI target briefly during normal controller-tracking jitter, so a press/release remains a click.")]
        [Min(0f)]
        public float targetLossGraceSeconds = 0.12f;
        public event Action<GameObject> PointerHoverEnter;
        public event Action<GameObject> PointerHoverExit;
        public event Action<GameObject> PointerDown;
        public event Action<GameObject> PointerUp;
        public event Action<GameObject> PointerClick;

        private PointerEventData eventData;
        private List<RaycastResult> raycastResults;
        private HandController controller;
        private bool triggerWasPressed;
        private float targetLastSeenTime = float.NegativeInfinity;

        public GameObject CurrentTarget => eventData != null ? eventData.pointerEnter : null;

        private void Awake()
        {
            onRaycastHit ??= new RaycastHitEvent();
            onRaycastMiss ??= new RaycastMissEvent();
            raycastResults = new List<RaycastResult>();
            controller = GetComponentInParent<HandController>();
        }

        private void Start()
        {
            //Generate a new event data container
            eventData = new PointerEventData(EventSystem.current);
            eventData.pointerId = 0;
        }

        private void Update()
        {
            PerformRaycast();
            CheckInput();
        }

        private void PerformRaycast()
        {
            // Generate a new ray at our input object facing forward
            var ray = new Ray(transform.position, transform.forward);

            // Check if there is a 3d object between us and the canvas.
            var distance = float.PositiveInfinity;
            RaycastHit rayHit;
            if(!ignorePhysicsOcclusion && Physics.Raycast(ray, out rayHit, distance,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                distance = rayHit.distance;
            }

            RaycastResult raycastResult = new RaycastResult();

            foreach (var canvas in XRUICanvas.Canvases)
            {
                // Raycast against the canvas
                var canvasTransform = canvas.GetComponent<RectTransform>();
                var graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();

                if (RayIntersectsRectTransform(canvasTransform, ray, ref distance))
                {
                    // Now use the Graphic Raycaster to perform a raycast into the canvas to get the actual control.
                    // The GraphicRaycaster expects the position to be in screenspace, of a particular event camera.
                    var screenPoint = graphicRaycaster.eventCamera.WorldToScreenPoint(ray.GetPoint(distance));

                    eventData.position = screenPoint;

                    raycastResults.Clear();
                    graphicRaycaster.Raycast(eventData, raycastResults);

                    if (raycastResults.Count > 0)
                    {
                        raycastResult = raycastResults[0];
                    }
                }
            }

            if(!raycastResult.isValid)
            {
                // Quest controller tracking can lose a small world-space button for a frame while
                // the trigger is being pressed. Retain the existing Ubiq target for this short grace
                // period rather than issuing a false exit/canceling the click.
                if (eventData.pointerEnter != null && Time.unscaledTime - targetLastSeenTime <= targetLossGraceSeconds)
                {
                    return;
                }
                LookAway();
                onRaycastMiss.Invoke();
                return;
            }

            targetLastSeenTime = Time.unscaledTime;

            onRaycastHit.Invoke(raycastResult.worldPosition,raycastResult.worldNormal);

            //If we are looking at the same object that we were looking at, we don't need to do anything and can exit
            if (eventData.pointerEnter == raycastResult.gameObject)
            {
                return;
            }

            //Otherwise we are looking at something new and should look away from the old object
            LookAway();

            //Record this data and tell the object that we are pointing at them (OnPointerEnter)
            eventData.pointerEnter = raycastResult.gameObject;
            eventData.pointerCurrentRaycast = raycastResult;

            ExecuteEvents.ExecuteHierarchy(eventData.pointerEnter, eventData, ExecuteEvents.pointerEnterHandler);
            PointerHoverEnter?.Invoke(eventData.pointerEnter);
        }

        void CheckInput()
        {
            if (!eventData.pointerEnter)
            {
                return;
            }

            if (controller.TriggerState && !triggerWasPressed && eventData.pointerEnter != null)
            {
                //...tell the object that we have pressed it (OnPointerDown)
                eventData.pointerPressRaycast = eventData.pointerCurrentRaycast;
                eventData.pressPosition = eventData.position;
                eventData.pointerPress = eventData.pointerEnter;
                ExecuteEvents.ExecuteHierarchy(eventData.pointerEnter, eventData, ExecuteEvents.pointerDownHandler);
                PointerDown?.Invoke(eventData.pointerEnter);
            }
            else if(!controller.TriggerState && triggerWasPressed)
            {
                //...tell the object than we have stopped pressing it (OnPointerUp)
                if (eventData.pointerPress != null)
                {
                    ExecuteEvents.ExecuteHierarchy(eventData.pointerPress, eventData, ExecuteEvents.pointerUpHandler);
                    PointerUp?.Invoke(eventData.pointerPress);
                }

                //...finally, if we pressed and released the same object, then we have clicked it (OnPointerClick)
                // Click the pressed button when a brief tracking loss cleared pointerEnter. Moving
                // deliberately onto a different UI target still cancels the original click.
                if (eventData.pointerPress == eventData.pointerEnter || eventData.pointerEnter == null)
                {
                    ExecuteEvents.ExecuteHierarchy(eventData.pointerPress, eventData, ExecuteEvents.pointerClickHandler);
                    PointerClick?.Invoke(eventData.pointerPress);
                }

                eventData.pointerPress = null;
            }

            triggerWasPressed = controller.TriggerState;
        }

        private void LookAway()
        {
            //If we are currently looking at something, stop looking at it and tell the object (OnPointerExit)
            if (eventData.pointerEnter != null)
            {
                ExecuteEvents.ExecuteHierarchy(eventData.pointerEnter, eventData, ExecuteEvents.pointerExitHandler);
                PointerHoverExit?.Invoke(eventData.pointerEnter);
                eventData.pointerEnter = null;
            }
        }

        /// <summary>
        /// Intersects the Ray with the RectTransform Rectangle in world space, and returns the distance, if it is closer
        /// than previous Raycasts.
        /// </summary>
        /// <remarks>
        /// Based on the Unity XR Interaction Toolkit function.
        /// </remarks>
        private bool RayIntersectsRectTransform(RectTransform transform, Ray ray, ref float distance)
        {
            Vector3[] corners = new Vector3[4];
            transform.GetWorldCorners(corners);
            var plane = new Plane(corners[0], corners[1], corners[2]);

            float enter;
            if (plane.Raycast(ray, out enter))
            {
                var intersection = ray.GetPoint(enter);

                var bottomEdge = corners[3] - corners[0];
                var leftEdge = corners[1] - corners[0];
                var bottomDot = Vector3.Dot(intersection - corners[0], bottomEdge);
                var leftDot = Vector3.Dot(intersection - corners[0], leftEdge);

                // If the intersection is right of the left edge and above the bottom edge.
                if (leftDot >= 0 && bottomDot >= 0)
                {
                    var topEdge = corners[1] - corners[2];
                    var rightEdge = corners[3] - corners[2];
                    var topDot = Vector3.Dot(intersection - corners[2], topEdge);
                    var rightDot = Vector3.Dot(intersection - corners[2], rightEdge);

                    //If the intersection is left of the right edge, and below the top edge
                    if (topDot >= 0 && rightDot >= 0)
                    {
                        if (enter < distance)
                        {
                            distance = enter;
                            return true;

                        }
                    }
                }
            }
            return false;
        }

    }
}
