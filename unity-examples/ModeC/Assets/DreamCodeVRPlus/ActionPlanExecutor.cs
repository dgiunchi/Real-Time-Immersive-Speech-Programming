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
    /// action lists, missing target. There is NO runtime C# compilation, NO
    /// reflection, NO System.IO/System.Net here.
    /// </summary>
    public sealed class ActionPlanExecutor : MonoBehaviour
    {
        public GeneratedObjectTracker tracker;

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

            string targetName = (string)plan["target"] ?? "selected_object";
            GameObject target = targetName == "scene_root" ? sceneRoot : selectedObject;
            if (target == null)
            {
                Debug.LogWarning("[ActionPlanExecutor] no target object; refusing.");
                return false;
            }

            bool allOk = true;
            foreach (var token in actions)
            {
                if (token is JObject action)
                {
                    allOk &= ExecuteAction(action, target, sceneRoot);
                }
                else
                {
                    allOk = false;
                }
            }
            return allOk;
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
                    Debug.LogWarning($"[ActionPlanExecutor] unknown action type '{type}'; skipped.");
                    return false;
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
