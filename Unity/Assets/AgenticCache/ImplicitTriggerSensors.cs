using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace AgenticCache
{
    // Authorable in-scene trigger volume for the L2 "context" trigger path
    // (docs/code-study-readiness-2026-08-11.md §1). Place one on a doorway, station,
    // or target region, set regionId, and ImplicitTriggerSensors will emit discrete
    // `locomotion` sensor events (regionId + entering/exiting) when the user's head
    // crosses it. Uses the attached Collider's bounds when one exists, otherwise the
    // authored `size` box centred on this transform - no Rigidbody on the XR rig is
    // required because membership is polled, not physics-triggered.
    public sealed class AgenticRegionVolume : MonoBehaviour
    {
        [Tooltip("Stable region identifier reported to Shared XR Memory, e.g. 'workshop-entrance'.")]
        public string regionId = "region";

        [Tooltip("Fallback box extents (local, metres) when no Collider is attached.")]
        public Vector3 size = new Vector3(4f, 3f, 4f);

        public bool Contains(Vector3 worldPoint)
        {
            var attached = GetComponent<Collider>();
            if (attached != null) return attached.bounds.Contains(worldPoint);
            var local = transform.InverseTransformPoint(worldPoint);
            var half = size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
    }

    // Unity-side implicit-trigger emitters for L1/L2 (study readiness item 1): region
    // entry/exit (`locomotion`), user-to-object proximity enter/exit (`proximity`),
    // and head-ray gaze dwell (`gaze`). All three publish DISCRETE events onto the
    // existing SceneDelta sensorEvents path via CachePublisher.PublishSensorEvent -
    // the server's SensorRegistry/ActivityMonitor/RegionStore already ingest that
    // stream, so a threshold crossing reaches the continuous monitor and can start an
    // L2 authoring iteration gated by mode_policy exactly like an explicit request.
    //
    // Deliberately out of scope (privacy/IRB, per the study-readiness prompt):
    // continuous position-trajectory logging, eye tracking, and camera/passthrough
    // capture. Only enter/exit/dwell transitions are ever emitted. "Gaze" here is a
    // HEAD-direction ray with an angular tolerance and dwell threshold, not eye
    // tracking - say so in the paper if L2's trigger list keeps the word "gaze".
    public sealed class ImplicitTriggerSensors : MonoBehaviour
    {
        public CachePublisher publisher;
        public AgenticSceneRegistry sceneRegistry;

        public float scanIntervalSeconds = 0.25f;

        [Tooltip("Head-to-object distance (metres) that raises a proximity 'entering' event.")]
        public float proximityEnterMeters = 1.5f;

        [Tooltip("Head-to-object distance (metres) that raises the matching 'exiting' event. Kept above the enter radius so jitter at the boundary cannot flap.")]
        public float proximityExitMeters = 2.0f;

        [Tooltip("Half-angle (degrees) of the head-forward cone used as the gaze ray tolerance.")]
        public float gazeConeDegrees = 10f;

        public float gazeMaxDistanceMeters = 10f;

        [Tooltip("Seconds the same object must stay in the gaze cone before a discrete gaze event is emitted.")]
        public float gazeDwellSeconds = 0.8f;

        private readonly Dictionary<AgenticRegionVolume, bool> insideByVolume = new Dictionary<AgenticRegionVolume, bool>();
        private readonly HashSet<string> nearObjectIds = new HashSet<string>();
        private string gazeCandidateId;
        private float gazeCandidateSince;
        private string gazeEmittedId;
        private float nextScanAt;
        private string observedSceneEpoch;

        private void Update()
        {
            if (publisher == null || sceneRegistry == null || Time.unscaledTime < nextScanAt) return;
            nextScanAt = Time.unscaledTime + scanIntervalSeconds;

            if (observedSceneEpoch != sceneRegistry.SceneEpoch)
            {
                // New scene: forget membership rather than emitting cross-scene exits.
                observedSceneEpoch = sceneRegistry.SceneEpoch;
                insideByVolume.Clear();
                nearObjectIds.Clear();
                gazeCandidateId = null;
                gazeEmittedId = null;
            }

            var head = Camera.main;
            if (head == null) return;
            var headPosition = head.transform.position;
            var headForward = head.transform.forward;

            ScanRegionVolumes(headPosition);
            ScanProximityAndGaze(headPosition, headForward);
        }

        private void ScanRegionVolumes(Vector3 headPosition)
        {
            var volumes = FindObjectsByType<AgenticRegionVolume>(FindObjectsSortMode.None);
            foreach (var volume in volumes)
            {
                if (volume == null || string.IsNullOrEmpty(volume.regionId)) continue;
                var inside = volume.Contains(headPosition);
                insideByVolume.TryGetValue(volume, out var wasInside);
                if (inside == wasInside) continue;
                insideByVolume[volume] = inside;
                EmitLocomotion(volume.regionId, inside);
            }
        }

        private void ScanProximityAndGaze(Vector3 headPosition, Vector3 headForward)
        {
            string bestGazeId = null;
            GameObject bestGazeObject = null;
            var bestGazeAngle = gazeConeDegrees;

            var seenThisScan = new HashSet<string>();
            foreach (var tracked in sceneRegistry.GetTrackedObjects())
            {
                if (tracked == null || tracked.gameObject == null) continue;
                var id = tracked.Value;
                seenThisScan.Add(id);
                var toObject = tracked.transform.position - headPosition;
                var distance = toObject.magnitude;

                if (!nearObjectIds.Contains(id) && distance <= proximityEnterMeters)
                {
                    nearObjectIds.Add(id);
                    EmitProximity(tracked, distance, true);
                }
                else if (nearObjectIds.Contains(id) && distance >= proximityExitMeters)
                {
                    nearObjectIds.Remove(id);
                    EmitProximity(tracked, distance, false);
                }

                if (distance > 0.05f && distance <= gazeMaxDistanceMeters)
                {
                    var angle = Vector3.Angle(headForward, toObject);
                    if (angle < bestGazeAngle)
                    {
                        bestGazeAngle = angle;
                        bestGazeId = id;
                        bestGazeObject = tracked.gameObject;
                    }
                }
            }
            nearObjectIds.RemoveWhere((id) => !seenThisScan.Contains(id));

            if (bestGazeId != gazeCandidateId)
            {
                gazeCandidateId = bestGazeId;
                gazeCandidateSince = Time.unscaledTime;
            }
            if (gazeEmittedId != null && gazeEmittedId != gazeCandidateId)
            {
                EmitGaze(gazeEmittedId, null, false);
                gazeEmittedId = null;
            }
            if (gazeCandidateId != null && gazeEmittedId == null &&
                Time.unscaledTime - gazeCandidateSince >= gazeDwellSeconds)
            {
                gazeEmittedId = gazeCandidateId;
                EmitGaze(gazeEmittedId, bestGazeObject, true);
            }
        }

        private void EmitLocomotion(string regionId, bool entering)
        {
            // RegionStore keys region context by sourceObjectId, so the sessionId is
            // the source - the same convention CachePublisher's scene-entry event uses.
            var json = "[{\"sensorType\":\"locomotion\",\"sourceObjectId\":\"" + AgenticSceneRegistry.Escape(publisher.sessionId) +
                "\",\"value\":{\"regionId\":\"" + AgenticSceneRegistry.Escape(regionId) +
                "\",\"entering\":" + (entering ? "true" : "false") + "},\"confidence\":1.0}]";
            publisher.PublishSensorEvent("xr-user-head", 1, string.Empty, regionId, json);
        }

        private void EmitProximity(StableObjectId target, float distance, bool entering)
        {
            var json = "[{\"sensorType\":\"proximity\",\"sourceObjectId\":\"xr-user-head\",\"targetObjectId\":\"" +
                AgenticSceneRegistry.Escape(target.Value) +
                "\",\"value\":{\"entering\":" + (entering ? "true" : "false") +
                ",\"distanceMeters\":" + distance.ToString("0.##", CultureInfo.InvariantCulture) + "},\"confidence\":1.0}]";
            publisher.PublishSensorEvent(target.Value, target.Revision, target.gameObject.tag, string.Empty, json);
        }

        private void EmitGaze(string targetId, GameObject target, bool focused)
        {
            var json = "[{\"sensorType\":\"gaze\",\"sourceObjectId\":\"xr-user-head\",\"targetObjectId\":\"" +
                AgenticSceneRegistry.Escape(targetId) + "\",\"value\":" + (focused ? "true" : "false") + ",\"confidence\":1.0}]";
            publisher.PublishSensorEvent(targetId, 1, target != null ? target.tag : string.Empty, string.Empty, json);
        }
    }
}
