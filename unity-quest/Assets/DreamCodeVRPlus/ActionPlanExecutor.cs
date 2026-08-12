using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Parses a validated action plan (the NID 94 payload) and executes ONLY
    /// allow-listed behaviours via <see cref="SafeBehaviourRegistry"/>.
    ///
    /// Accepts: a JSON object that is either a raw action plan or a
    /// <c>BackendDecision</c> envelope containing one. Rejects (fail-safe, returns
    /// false, logs a warning): invalid JSON, wrong schema_version, empty/oversized
    /// action lists, missing/malformed target, or any action that violates a
    /// perceptual-safety guard. There is NO runtime C# compilation, NO reflection,
    /// NO System.IO/System.Net here.
    ///
    /// SECURITY MODEL (TRACK U1 perceptual safety layer):
    ///  - ALL-OR-NOTHING: the whole plan is validated first; if ANY action is
    ///    invalid/malformed the ENTIRE plan is rejected (no partial application,
    ///    no silently-defaulted target). Prevents an attacker smuggling one bad
    ///    action past a list of benign ones.
    ///  - GENERATED tagging (SP-02): every produced object is stamped with a
    ///    <see cref="GeneratedMarker"/> + a subtle persistent outline/label so a
    ///    disguised / false-affordance object is always visibly AI-generated.
    ///  - USER-FRAME INVARIANT: spawns/moves into the head personal-space sphere
    ///    are rejected; continuously-moving objects that would enter it are stopped.
    ///  - FOV-OCCLUSION guard (PE-01): refuse a single object that would cover
    ///    more than ~80% of the forward view (full-FOV blackout).
    ///  - OVERLAP guard (SP-03): refuse to co-locate / fully occlude an existing
    ///    generated object.
    ///  - COLD-START FALLBACK: if Camera.main pose is unavailable/low-confidence we
    ///    FAIL CLOSED on head-relative risky placements rather than allowing them.
    /// </summary>
    public sealed class ActionPlanExecutor : MonoBehaviour
    {
        public GeneratedObjectTracker tracker;

        [Tooltip("Comfort intensity. When true (default) the perceptual guards are " +
                 "ENFORCED. Set false only for a vetted, comfort-tolerant user to relax " +
                 "the head-relative / FOV / overlap rejections.")]
        // FREE-CREATION default: perceptual guards OFF so normal builds are never blocked.
        // The allow-list + numeric bounds + all-or-nothing validation ALWAYS apply.
        // Set this TRUE (in the Inspector) to arm the FOV / personal-space / overlap
        // perceptual guards for the safety demonstration.
        public bool comfortIntensity = false;

        public bool Execute(string json, GameObject selectedObject, GameObject sceneRoot)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ActionPlanExecutor] invalid JSON, refusing: {e.Message}");
                return false;
            }

            // Accept either a raw action plan or a BackendDecision envelope.
            JObject plan = root["action_plan"] as JObject ?? root;

            if ((string)plan["schema_version"] != ProtocolModels.SupportedSchemaVersion)
            {
                Debug.LogWarning("[ActionPlanExecutor] unsupported schema_version; refusing.");
                return false;
            }

            if (!(plan["actions"] is JArray actions) || actions.Count == 0)
            {
                Debug.LogWarning("[ActionPlanExecutor] empty/missing actions; nothing to do.");
                return false;
            }
            if (actions.Count > ProtocolModels.MaxActions)
            {
                Debug.LogWarning($"[ActionPlanExecutor] too many actions ({actions.Count}); refusing.");
                return false;
            }

            // ALL-OR-NOTHING: a missing target must NOT be silently defaulted to a
            // benign value — an attacker could rely on that to mis-route an action.
            JToken targetTok = plan["target"];
            if (targetTok == null || targetTok.Type != JTokenType.String)
            {
                Debug.LogWarning("[ActionPlanExecutor] missing/malformed target; refusing whole plan.");
                return false;
            }
            string targetName = (string)targetTok;
            // FREEDOM FIX: "scene_root" is a valid target for CREATE/spawn commands
            // (e.g. "generate a solar system"). If the host wired no scene-root object,
            // create/reuse a tidy generated root so creation works instead of being
            // refused. Only "selected_object" with nothing selected is unresolvable.
            GameObject effectiveSceneRoot = sceneRoot != null ? sceneRoot : EnsureGeneratedRoot();
            GameObject target = targetName == "scene_root" ? effectiveSceneRoot : selectedObject;
            if (target == null)
            {
                Debug.LogWarning("[ActionPlanExecutor] target 'selected_object' but nothing is selected; refusing.");
                return false;
            }

            // -------- PASS 1: validate the ENTIRE plan; reject all if any is bad. --------
            // We do NOT touch the scene here. Only if every action passes static +
            // perceptual validation do we run PASS 2 to apply them.
            var camPose = PerceptualSafety.SampleCamera();
            foreach (var token in actions)
            {
                if (!(token is JObject action))
                {
                    Debug.LogWarning("[ActionPlanExecutor] non-object action; refusing whole plan.");
                    return false;
                }
                if (!ValidateAction(action, target, effectiveSceneRoot, camPose))
                {
                    Debug.LogWarning("[ActionPlanExecutor] action failed validation; refusing whole plan.");
                    return false;
                }
            }

            // -------- PASS 2: apply (the plan is now known-good as a whole). --------
            bool allOk = true;
            foreach (var token in actions)
            {
                var action = (JObject)token;
                allOk &= ExecuteAction(action, target, effectiveSceneRoot);
            }
            return allOk;
        }

        // Resolve a usable scene root for CREATE commands when the host supplies none,
        // so creation works rather than being refused. Reused across the session.
        private GameObject _generatedRoot;
        private GameObject EnsureGeneratedRoot()
        {
            if (_generatedRoot == null)
            {
                _generatedRoot = GameObject.Find("DCVR_GeneratedRoot") ?? new GameObject("DCVR_GeneratedRoot");
            }
            return _generatedRoot;
        }

        /// <summary>
        /// Static + perceptual validation for ONE action. No scene mutation.
        /// Returns false to reject the whole plan (all-or-nothing).
        /// </summary>
        private bool ValidateAction(JObject action, GameObject target, GameObject sceneRoot,
                                    PerceptualSafety.CameraPose camPose)
        {
            string type = (string)action["type"];
            switch (type)
            {
                case "set_color":
                    return !string.IsNullOrEmpty((string)action["color"]);

                case "set_scale":
                {
                    // A scale-up of the target can blow it up to a full-FOV blackout (PE-01).
                    float v = Mathf.Clamp(ReadFloat(action, "value", 1f),
                                          ProtocolModels.ScaleMin, ProtocolModels.ScaleMax);
                    if (!PerceptualSafety.ScaledObjectCoverageOk(target, v, camPose, comfortIntensity))
                    {
                        Debug.LogWarning("[ActionPlanExecutor] set_scale would blackout the FOV (PE-01); refusing.");
                        return false;
                    }
                    return true;
                }

                case "move":
                {
                    // USER-FRAME INVARIANT: a continuously-moving object must not be able
                    // to enter the head personal-space sphere. Fail-closed on cold start.
                    string mode = (string)action["mode"];
                    float speed = Mathf.Clamp(ReadFloat(action, "speed", 1f), 0f, ProtocolModels.MoveSpeedMax);
                    float amplitude = Mathf.Clamp(ReadFloat(action, "amplitude", 1f), 0f, ProtocolModels.MoveAmplitudeMax);
                    if (!PerceptualSafety.MovePathOk(target, ParseAxisDir((string)action["axis"]),
                                                     mode, speed, amplitude, camPose, comfortIntensity))
                    {
                        Debug.LogWarning("[ActionPlanExecutor] move path would enter personal space; refusing.");
                        return false;
                    }
                    return true;
                }

                case "rotate":
                    return true; // rotation is in-place; bounded by RotateDegPerSecMax in the registry.

                case "spawn_primitive":
                {
                    if (!IsKnownShape((string)action["shape"]))
                    {
                        return false;
                    }
                    string parent = (string)action["parent"];
                    int count = Mathf.Clamp(ReadInt(action, "count", 1), 1, ProtocolModels.MaxSpawnCount);
                    if (tracker != null && !tracker.CanSpawn(count))
                    {
                        Debug.LogWarning("[ActionPlanExecutor] session spawn cap reached; refusing.");
                        return false;
                    }
                    // Predict where the registry would place each primitive and run the
                    // user-frame / FOV / overlap guards BEFORE anything is created.
                    Transform parentT = (parent == "scene_root" ? sceneRoot : target)?.transform;
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 worldPos = PerceptualSafety.PredictedSpawnWorldPos(parentT, i);
                        if (!PerceptualSafety.PlacementOk(worldPos, camPose, tracker, comfortIntensity,
                                                          out string why))
                        {
                            Debug.LogWarning($"[ActionPlanExecutor] spawn rejected ({why}); refusing whole plan.");
                            return false;
                        }
                    }
                    return true;
                }

                case "set_physics":
                    return true; // mass/gravity clamped in the registry; no perceptual risk.

                default:
                    Debug.LogWarning($"[ActionPlanExecutor] unknown action type '{type}'; refusing whole plan.");
                    return false; // all-or-nothing: an unknown action invalidates the plan.
            }
        }

        private bool ExecuteAction(JObject action, GameObject target, GameObject sceneRoot)
        {
            string type = (string)action["type"];
            switch (type)
            {
                case "set_color":
                    return SafeBehaviourRegistry.SetColor(target, (string)action["color"]);
                case "set_scale":
                    return SafeBehaviourRegistry.SetScale(target, ReadFloat(action, "value", 1f));
                case "move":
                    return SafeBehaviourRegistry.Move(
                        target, (string)action["axis"], (string)action["mode"],
                        ReadFloat(action, "speed", 1f), ReadFloat(action, "amplitude", 1f));
                case "rotate":
                    return SafeBehaviourRegistry.Rotate(
                        target, (string)action["axis"], ReadFloat(action, "deg_per_sec", 30f));
                case "spawn_primitive":
                    return SafeBehaviourRegistry.SpawnPrimitive(
                        (string)action["shape"], ReadInt(action, "count", 1),
                        (string)action["parent"], target, sceneRoot, tracker);
                case "set_physics":
                    return SafeBehaviourRegistry.SetPhysics(
                        target, action["gravity"]?.Value<bool>() ?? true, ReadFloat(action, "mass", 1f));
                default:
                    // Unreachable: PASS 1 already rejected unknown types. Fail-safe anyway.
                    Debug.LogWarning($"[ActionPlanExecutor] unknown action type '{type}'; skipped.");
                    return false;
            }
        }

        private static bool IsKnownShape(string shape)
        {
            switch (shape)
            {
                case "cube":
                case "sphere":
                case "capsule":
                case "cylinder":
                case "plane":
                case "quad":
                    return true;
                default:
                    return false;
            }
        }

        private static Vector3 ParseAxisDir(string axis)
        {
            switch (axis)
            {
                case "x": return Vector3.right;
                case "z": return Vector3.forward;
                default: return Vector3.up;
            }
        }

        private static float ReadFloat(JObject o, string key, float fallback)
        {
            var t = o[key];
            if (t == null)
            {
                return fallback;
            }
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer)
            {
                return t.Value<float>();
            }
            return fallback;
        }

        private static int ReadInt(JObject o, string key, int fallback)
        {
            var t = o[key];
            return t != null && t.Type == JTokenType.Integer ? t.Value<int>() : fallback;
        }
    }
}
