using System;
using System.Linq;
using DreamCodeVR2.ContextBridge;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    // Thin, front-facing ray target for a drawer. It is a child of the semantic drawer,
    // so it never becomes a separate SceneContext object.
    public sealed class DrawerSelectionHandle : MonoBehaviour
    {
        // Percentage inset is deliberately small: this is a front-panel target, not a handle strip.
        public const float FrontInsetFraction = .04f;
        public const float FrontDepth = .018f;
        public AIEditableObject Drawer => drawer ? drawer : (drawer = GetComponentInParent<AIEditableObject>());
        private AIEditableObject drawer;

        public static DrawerSelectionHandle Ensure(AIEditableObject item, ExperimentalDrawerController controller)
        {
            if (!item || !controller) return null;
            var handle = item.GetComponentInChildren<DrawerSelectionHandle>(true);
            if (!handle)
            {
                var go = new GameObject("DrawerSelectionHandle");
                go.transform.SetParent(item.transform, false);
                handle = go.AddComponent<DrawerSelectionHandle>();
                var collider = go.AddComponent<BoxCollider>();
                collider.isTrigger = false; // Pointer rays intentionally ignore triggers.
            }

            handle.drawer = item;
            var colliderHandle = handle.GetComponent<BoxCollider>() ?? handle.gameObject.AddComponent<BoxCollider>();
            colliderHandle.isTrigger = false;
            var bounds = RendererBounds(item.transform);
            var front = DrawerFront(item.transform, controller);
            var up = item.transform.up;
            if (Mathf.Abs(Vector3.Dot(front, up)) > .95f) up = Vector3.up;
            var right = Vector3.Cross(up, front).normalized;
            var frontWidth = Mathf.Max(.08f, ProjectedExtent(bounds, right) * 2f);
            var frontHeight = Mathf.Max(.045f, ProjectedExtent(bounds, up) * 2f);
            var width = frontWidth * (1f - FrontInsetFraction * 2f);
            var height = frontHeight * (1f - FrontInsetFraction * 2f);
            var depth = FrontDepth;
            var position = bounds.center + front * (ProjectedExtent(bounds, front) + depth * .75f);
            handle.transform.SetPositionAndRotation(position, Quaternion.LookRotation(front, up));
            handle.transform.localScale = Vector3.one;
            colliderHandle.size = new Vector3(width, height, depth);
            DreamCodeVR2ClientLogger.Event("drawer", "DRAWER_SELECTION_HANDLE_CREATED", null, new { drawer_id = item.objectId, handle_gameobject = handle.gameObject.name, front_width = frontWidth, front_height = frontHeight, collider_size = colliderHandle.size, local_position = handle.transform.localPosition, front_normal = front, margin = FrontInsetFraction });
            return handle;
        }

        private static Bounds RendererBounds(Transform root)
        {
            var owner = root.GetComponent<AIEditableObject>();
            var renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => r && r.enabled && r.GetComponentInParent<AIEditableObject>() == owner).ToArray();
            if (renderers.Length == 0) return new Bounds(root.position, Vector3.one * .1f);
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static Vector3 DrawerFront(Transform drawer, ExperimentalDrawerController controller)
        {
            var delta = controller.openAnchor && controller.closedAnchor ? controller.openAnchor.position - controller.closedAnchor.position : Vector3.zero;
            return delta.sqrMagnitude > .000001f ? delta.normalized : drawer.forward;
        }

        private static float ProjectedExtent(Bounds bounds, Vector3 axis)
        {
            axis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            return Vector3.Dot(bounds.extents, axis);
        }
    }

    public struct SemanticPointerHit
    {
        public RaycastHit hit;
        public AIEditableObject editable;
        public string reason;
        public Collider internalCandidate;
        public Collider drawerHandleCandidate;
    }

    // Shared by the visible controller ray and InteractionContext, preventing them from
    // disagreeing about the canonical object behind proxy colliders.
    public static class SemanticPointerRaycast
    {
        public static bool TryResolve(Vector3 origin, Vector3 direction, float range, int layers, out SemanticPointerHit result)
        {
            result = default;
            SemanticPointerHit? fallback = null;
            foreach (var hit in Physics.RaycastAll(origin, direction, range, layers, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                var collider = hit.collider;
                var editable = collider ? collider.GetComponentInParent<AIEditableObject>() : null;
                if (!editable || (!collider.gameObject.CompareTag("game") && !editable.gameObject.CompareTag("game"))) continue;
                var handle = collider.GetComponent<DrawerSelectionHandle>();
                if (handle && handle.Drawer)
                {
                    result = new SemanticPointerHit { hit = hit, editable = handle.Drawer, reason = "drawer_selection_handle", drawerHandleCandidate = collider };
                    return true;
                }
                if (ExperimentalDrawerController.ShouldIgnoreColliderForOpenDrawerContents(collider))
                {
                    if (!fallback.HasValue) fallback = new SemanticPointerHit { hit = hit, editable = editable, reason = "open_drawer_body_fallback" };
                    continue;
                }
                var ownerDrawer = collider.GetComponentInParent<ExperimentalDrawerController>();
                result = new SemanticPointerHit { hit = hit, editable = editable, reason = ownerDrawer && ownerDrawer.GetComponent<AIEditableObject>() != editable ? "internal_object" : "semantic_collider", internalCandidate = ownerDrawer ? collider : null };
                return true;
            }
            if (!fallback.HasValue) return false;
            result = fallback.Value;
            return true;
        }
    }
}
