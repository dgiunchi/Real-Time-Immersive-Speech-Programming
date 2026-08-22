using System;
using AgenticCache;
using UnityEngine;

namespace AgenticXR.Study
{
    public abstract class StudySuccessDetector : MonoBehaviour
    {
        public CachePublisher publisher;
        public string taskId;
        public string variant;
        public string targetObjectId;
        public float settleWindowSeconds = 0.5f;
        public float restSpeedThreshold = 0.05f;

        public bool IsArmed { get; private set; }
        public bool HasFired { get; private set; }
        public long FiredAtUnixMs { get; private set; }

        public event Action<StudySuccessDetector, string> SuccessObserved;

        public virtual void Arm()
        {
            IsArmed = true;
            HasFired = false;
            FiredAtUnixMs = 0;
            OnArmed();
        }

        public virtual void Disarm()
        {
            IsArmed = false;
            OnDisarmed();
        }

        protected virtual void OnArmed() { }
        protected virtual void OnDisarmed() { }

        protected void FireOnce(string detailJson)
        {
            if (!IsArmed || HasFired) return;
            HasFired = true;
            FiredAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var stable = GetComponent<StableObjectId>();
            var objectId = !string.IsNullOrEmpty(targetObjectId) ? targetObjectId :
                stable != null ? stable.Value : gameObject.name;
            var json = "[{\"sensorType\":\"study_task_completion_observed\",\"sourceObjectId\":\"" +
                AgenticSceneRegistry.Escape(gameObject.name) + "\",\"targetObjectId\":\"" +
                AgenticSceneRegistry.Escape(objectId) + "\",\"value\":{\"taskId\":\"" +
                AgenticSceneRegistry.Escape(taskId) + "\",\"variant\":\"" +
                AgenticSceneRegistry.Escape(variant) + "\",\"observedAtUnixMs\":" + FiredAtUnixMs +
                ",\"detail\":" + (string.IsNullOrEmpty(detailJson) ? "{}" : detailJson) +
                "},\"confidence\":1.0}]";
            publisher?.PublishSensorEvent(objectId, stable != null ? stable.Revision : 1,
                gameObject.tag, string.Empty, json);
            SuccessObserved?.Invoke(this, detailJson ?? "{}");
        }

        protected bool IsAtRest(Rigidbody body) => body != null &&
            body.linearVelocity.sqrMagnitude <= restSpeedThreshold * restSpeedThreshold &&
            body.angularVelocity.sqrMagnitude <= restSpeedThreshold * restSpeedThreshold;
    }
}
