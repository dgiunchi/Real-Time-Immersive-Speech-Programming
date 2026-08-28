using System;
using System.Collections;
using DreamCodeVR2.ContextBridge;
using DreamCodeVR2.Quest;
using DreamCodeVR2.SceneContext;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class ExperimentalDrawerController : MonoBehaviour
    {
        [Header("Scene-authored poses")]
        public Transform closedAnchor;
        public Transform openAnchor;
        [Tooltip("Translation is normally sufficient for a drawer. Enable only if the authored anchors also define a desired rotation.")]
        public bool applyAnchorRotation;
        [Header("Motion")]
        [Range(0f, 3f)] public float duration = .5f;

        public bool IsOpen { get; private set; }
        public bool IsMoving => motion != null;
        public QuestEventBus eventBus;
        public SceneContextTransmitter sceneContext;
        public event Action<bool> MotionCompleted;
        public event Action<bool> MotionInterrupted;

        private Coroutine motion;
        private Rigidbody body;

        private void Awake() { body = GetComponent<Rigidbody>(); }
        public bool TryOpen(out string error) => TryMove(true, out error);
        public bool TryClose(out string error) => TryMove(false, out error);
        public void Open() { TryOpen(out _); }
        public void Close() { TryClose(out _); }

        public void ResetClosed()
        {
            if (motion != null) StopCoroutine(motion);
            motion = null;
            if (!TryGetTarget(false, out var target, out _)) return;
            SetPose(target);
            IsOpen = false;
        }

        private bool TryMove(bool requestedOpen, out string error)
        {
            if (!TryGetTarget(requestedOpen, out var target, out error))
            {
                Log("DRAWER_MOTION_CONFIGURATION_ERROR", requestedOpen, error, transform.position, null);
                return false;
            }
            if (motion != null)
            {
                StopCoroutine(motion);
                motion = null;
                MotionInterrupted?.Invoke(requestedOpen);
                Log("DRAWER_MOTION_INTERRUPTED", requestedOpen, null, transform.position, target.position);
            }
            if (IsOpen == requestedOpen && IsAtTarget(target)) { PublishState(requestedOpen); return true; }
            if (duration <= 0f) { SetPose(target); Complete(requestedOpen, target); return true; }
            Log("DRAWER_MOTION_START", requestedOpen, null, transform.position, target.position);
            motion = StartCoroutine(MoveRoutine(requestedOpen, target));
            return true;
        }

        private bool TryGetTarget(bool requestedOpen, out Transform target, out string error)
        {
            target = requestedOpen ? openAnchor : closedAnchor;
            if (!closedAnchor || !openAnchor) { error = "Drawer motion anchors are not configured."; return false; }
            if (Vector3.Distance(closedAnchor.position, openAnchor.position) < .001f) { error = "Drawer Open Anchor must be placed away from Closed Anchor."; return false; }
            error = null;
            return true;
        }

        private IEnumerator MoveRoutine(bool requestedOpen, Transform target)
        {
            var fromPosition = transform.position;
            var fromRotation = transform.rotation;
            var safeDuration = Mathf.Max(.01f, duration);
            var elapsed = 0f;
            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / safeDuration));
                SetPose(Vector3.LerpUnclamped(fromPosition, target.position, t), Quaternion.Slerp(fromRotation, target.rotation, t));
                yield return null;
            }
            SetPose(target);
            Complete(requestedOpen, target);
        }

        private void Complete(bool requestedOpen, Transform target)
        {
            motion = null;
            IsOpen = requestedOpen;
            PublishState(requestedOpen);
            Log("DRAWER_MOTION_COMPLETE", requestedOpen, null, transform.position, target.position);
            MotionCompleted?.Invoke(requestedOpen);
        }

        private void PublishState(bool open)
        {
            var id = GetComponent<AIEditableObject>()?.objectId;
            eventBus?.Publish(QuestEventType.ObjectStateChanged, id, null, open ? "open" : "closed");
            sceneContext?.SendSceneContextSnapshot("drawer state");
        }

        private bool IsAtTarget(Transform target) => Vector3.Distance(transform.position, target.position) < .001f && (!applyAnchorRotation || Quaternion.Angle(transform.rotation, target.rotation) < .1f);
        private void SetPose(Transform target) => SetPose(target.position, target.rotation);

        // Anchors are always read in world space: they may have different stable parents.
        private void SetPose(Vector3 position, Quaternion rotation)
        {
            if (!body) body = GetComponent<Rigidbody>();
            if (body && !body.isKinematic) { body.MovePosition(position); if (applyAnchorRotation) body.MoveRotation(rotation); return; }
            transform.position = position;
            if (applyAnchorRotation) transform.rotation = rotation;
        }

        private void Log(string eventName, bool requestedOpen, string error, Vector3 source, Vector3? target)
        {
            DreamCodeVR2ClientLogger.Event("drawer", eventName, error, new { object_id = GetComponent<AIEditableObject>()?.objectId, source_position = source, target_position = target, motion_duration = duration, requested_state = requestedOpen ? "open" : "closed" });
        }

        private void OnDrawGizmosSelected()
        {
            if (!closedAnchor || !openAnchor) return;
            Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(closedAnchor.position, .035f);
            Gizmos.color = Color.green; Gizmos.DrawWireSphere(openAnchor.position, .035f);
            Gizmos.color = Color.yellow; Gizmos.DrawLine(closedAnchor.position, openAnchor.position);
        }
    }
}
