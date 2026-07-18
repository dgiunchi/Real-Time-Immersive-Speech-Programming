using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AgenticCache
{
    public sealed class AgenticSceneRegistry : MonoBehaviour
    {
        public float haloRadius = 5f;
        public float refreshInterval = 0.25f;

        private readonly Dictionary<string, StableObjectId> byId = new Dictionary<string, StableObjectId>();
        private float nextRefresh;
        private string sceneEpoch;
        private string snapshotId;

        public string SceneEpoch => sceneEpoch;
        public string SnapshotId => snapshotId;

        private void Awake()
        {
            sceneEpoch = SceneManager.GetActiveScene().name + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            snapshotId = Guid.NewGuid().ToString();
            RefreshObjects();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            RefreshObjects();
        }

        public void RefreshObjects()
        {
            byId.Clear();
            GameObject[] objects;
            try { objects = GameObject.FindGameObjectsWithTag("game"); }
            catch (UnityException) { objects = Array.Empty<GameObject>(); }

            foreach (var obj in objects)
            {
                var stable = obj.GetComponent<StableObjectId>() ?? obj.AddComponent<StableObjectId>();
                stable.EnsureInitialized();
                stable.RefreshRevision();
                byId[stable.Value] = stable;
            }

            var selected = GetSelectedObject();
            if (selected != null && selected.scene.IsValid())
            {
                var stable = selected.GetComponent<StableObjectId>() ?? selected.AddComponent<StableObjectId>();
                stable.EnsureInitialized();
                stable.RefreshRevision();
                byId[stable.Value] = stable;
            }
        }

        public GameObject Find(string stableObjectId)
        {
            RefreshObjects();
            return !string.IsNullOrEmpty(stableObjectId) && byId.TryGetValue(stableObjectId, out var value)
                ? value.gameObject : null;
        }

        public string GetSelectedObjectId()
        {
            var selected = GetSelectedObject();
            if (selected == null) return null;
            var stable = selected.GetComponent<StableObjectId>() ?? selected.AddComponent<StableObjectId>();
            stable.EnsureInitialized();
            return stable.Value;
        }

        public long GetRevision(GameObject obj)
        {
            if (obj == null) return -1;
            var stable = obj.GetComponent<StableObjectId>() ?? obj.AddComponent<StableObjectId>();
            stable.EnsureInitialized();
            stable.RefreshRevision();
            return stable.Revision;
        }

        public string BuildFocusAndHaloJson(string requestedId)
        {
            RefreshObjects();
            var focus = Find(requestedId) ?? GetSelectedObject();
            if (focus == null && byId.Count > 0)
            {
                foreach (var item in byId.Values) { focus = item.gameObject; break; }
            }
            if (focus == null) return "{\"focus\":null,\"halo\":[],\"sensorEvents\":[]}";

            var focusStable = focus.GetComponent<StableObjectId>();
            var sb = new StringBuilder();
            sb.Append("{\"focus\":");
            AppendFullObject(sb, focus, focusStable);
            sb.Append(",\"halo\":[");
            var first = true;
            foreach (var entry in byId.Values)
            {
                if (entry.gameObject == focus || Vector3.Distance(entry.transform.position, focus.transform.position) > haloRadius) continue;
                if (!first) sb.Append(',');
                first = false;
                AppendHaloObject(sb, entry.gameObject, entry);
            }
            sb.Append("],\"sensorEvents\":[{\"sensorType\":\"gaze\",\"sourceObjectId\":\"xr-user-head\",\"targetObjectId\":\"")
                .Append(Escape(focusStable.Value)).Append("\",\"value\":true,\"confidence\":1.0}]}");
            return sb.ToString();
        }

        public string BuildSnapshotJson()
        {
            RefreshObjects();
            var sb = new StringBuilder("{\"objects\":[");
            var first = true;
            foreach (var entry in byId.Values)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"stableObjectId\":\"").Append(Escape(entry.Value))
                    .Append("\",\"objectRevision\":").Append(entry.Revision)
                    .Append(",\"tag\":\"").Append(Escape(entry.gameObject.tag))
                    .Append("\",\"region\":\"\",\"state\":");
                AppendTransform(sb, entry.transform);
                sb.Append('}');
            }
            return sb.Append("]}").ToString();
        }

        private static GameObject GetSelectedObject()
        {
            var manager = FindFirstObjectByType<CodeGenerationManager>();
            return manager != null ? manager.targetObject : null;
        }

        private static void AppendFullObject(StringBuilder sb, GameObject obj, StableObjectId stable)
        {
            sb.Append("{\"id\":\"").Append(Escape(stable.Value)).Append("\",\"name\":\"")
                .Append(Escape(obj.name)).Append("\",\"tag\":\"").Append(Escape(obj.tag))
                .Append("\",\"objectRevision\":").Append(stable.Revision).Append(",\"transform\":");
            AppendTransform(sb, obj.transform);
            sb.Append(",\"parentId\":");
            var parentStable = obj.transform.parent != null ? obj.transform.parent.GetComponent<StableObjectId>() : null;
            if (parentStable == null) sb.Append("null"); else sb.Append('"').Append(Escape(parentStable.Value)).Append('"');
            sb.Append(",\"components\":[");
            var first = true;
            foreach (var component in obj.GetComponents<Component>())
            {
                if (component == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"type\":\"").Append(Escape(component.GetType().Name)).Append("\",\"fields\":{");
                var firstField = true;
                foreach (var field in component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!IsSimple(field.FieldType)) continue;
                    object value;
                    try { value = field.GetValue(component); } catch { continue; }
                    if (!firstField) sb.Append(',');
                    firstField = false;
                    sb.Append('"').Append(Escape(field.Name)).Append("\":\"").Append(Escape(value != null ? value.ToString() : "null")).Append('"');
                }
                sb.Append("}}");
            }
            sb.Append("]}");
        }

        private static void AppendHaloObject(StringBuilder sb, GameObject obj, StableObjectId stable)
        {
            sb.Append("{\"id\":\"").Append(Escape(stable.Value)).Append("\",\"name\":\"")
                .Append(Escape(obj.name)).Append("\",\"tag\":\"").Append(Escape(obj.tag))
                .Append("\",\"type\":\"").Append(obj.isStatic ? "static" : "dynamic").Append("\"}");
        }

        private static void AppendTransform(StringBuilder sb, Transform t)
        {
            var p = t.position; var r = t.rotation; var s = t.lossyScale;
            sb.Append("{\"pos\":[").Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z))
                .Append("],\"rot\":[").Append(F(r.x)).Append(',').Append(F(r.y)).Append(',').Append(F(r.z)).Append(',').Append(F(r.w))
                .Append("],\"scale\":[").Append(F(s.x)).Append(',').Append(F(s.y)).Append(',').Append(F(s.z)).Append("]}");
        }

        private static bool IsSimple(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Color);
        private static string F(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        public static string Escape(string value) => string.IsNullOrEmpty(value) ? "" : value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }
}
