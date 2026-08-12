// DreamCodeVR+ — objective 6DoF diagnostics.
//
// Written because "OpenXR started" was mistaken for "tracking reaches the scene". They
// are different claims and only the second one matters to a wearer. This measures the
// second one directly and reports it two ways: a world-space panel the wearer can read,
// and a low-frequency log line for THIS process.
//
// The decisive numbers are the head pose and the world anchors together. If the head pose
// changes while the anchors do not, tracking is correct and the world is stationary. If
// the head pose never changes, there is no tracking. If the anchors change with the head,
// something is parenting the world to the player.
//
// One line every two seconds — enough to see, not enough to bury the log.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace DreamCodeVRPlus
{
    public sealed class DcvrDiagnostics : MonoBehaviour
    {
        private const float LogInterval = 2f;

        private Transform _origin;
        private Transform _head;
        private Transform _leftHand, _rightHand;
        private Transform[] _worldAnchors;
        private string[] _anchorNames;

        private object _line1, _line2, _line3, _line4, _line5;
        private float _logTimer;
        private Vector3 _headMin = Vector3.positiveInfinity;
        private Vector3 _headMax = Vector3.negativeInfinity;

        public static DcvrDiagnostics Attach(DcvrXrRig.Rig rig, Transform panelParent,
                                             Vector3 panelPos,
                                             Transform[] anchors, string[] anchorNames)
        {
            var go = new GameObject("DCVR_Diagnostics");
            var d = go.AddComponent<DcvrDiagnostics>();
            d._origin = rig?.OriginTransform;
            d._head = rig?.Head != null ? rig.Head.transform : null;
            d._leftHand = rig?.LeftHand;
            d._rightHand = rig?.RightHand;
            d._worldAnchors = anchors;
            d._anchorNames = anchorNames;
            d.BuildPanel(panelParent, panelPos);
            return d;
        }

        private void BuildPanel(Transform parent, Vector3 pos)
        {
            var panel = new GameObject("DCVR_DiagPanel");
            panel.transform.SetParent(parent, false);
            panel.transform.position = pos;

            var plate = DcvrPrim.Create(PrimitiveType.Quad, "plate");
            plate.transform.SetParent(panel.transform, false);
            plate.transform.localScale = new Vector3(1.5f, 0.62f, 1f);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            Shader holo = Shader.Find("DreamCodeVRPlus/Holo");
            if (holo != null)
            {
                var m = new Material(holo) { name = "DCVR_DiagPlate" };
                m.SetColor("_Color", DcvrWorld.Cyan);
                m.SetFloat("_Alpha", 0.16f);
                plate.GetComponent<Renderer>().sharedMaterial = m;
            }

            DcvrText.Make(panel.transform, "6DoF DIAGNOSTIC", new Vector3(0f, 0.24f, 0f),
                          0.055f, DcvrWorld.Cyan);
            _line1 = DcvrText.Make(panel.transform, "", new Vector3(0f, 0.13f, 0f), 0.042f, Color.white);
            _line2 = DcvrText.Make(panel.transform, "", new Vector3(0f, 0.05f, 0f), 0.042f, Color.white);
            _line3 = DcvrText.Make(panel.transform, "", new Vector3(0f, -0.03f, 0f), 0.042f, Color.white);
            _line4 = DcvrText.Make(panel.transform, "", new Vector3(0f, -0.11f, 0f), 0.042f, DcvrWorld.Green);
            _line5 = DcvrText.Make(panel.transform, "", new Vector3(0f, -0.19f, 0f), 0.038f, DcvrWorld.Dim);
        }

        /// <summary>Report every renderer close enough to dominate the wearer's view, with
        /// its distance and on-screen size. Written because a large unexplained shape was
        /// filling a third of the field of view and guessing at candidates from the scene
        /// description was not converging — this names the object directly.</summary>
        private void ProbeNearGeometry(Camera cam)
        {
            if (cam == null) { return; }
            var hits = new List<(float dist, string name, float coverage, string path)>();

            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) { continue; }
                Bounds b = r.bounds;
                float d = Vector3.Distance(cam.transform.position, b.center);
                if (d > 8f) { continue; }

                // Rough screen coverage: project the bounds extents and compare with the
                // viewport. Anything over ~15% is dominating the view.
                Vector3 c = cam.WorldToViewportPoint(b.center);
                if (c.z <= 0f) { continue; }
                Vector3 e = cam.WorldToViewportPoint(b.center + new Vector3(b.extents.x, b.extents.y, 0f));
                float cov = Mathf.Abs(e.x - c.x) * Mathf.Abs(e.y - c.y) * 4f;

                string path = r.name;
                Transform t = r.transform.parent;
                int guard = 0;
                while (t != null && guard++ < 4) { path = t.name + "/" + path; t = t.parent; }
                hits.Add((d, r.name, cov, path));
            }

            hits.Sort((a, b2) => b2.coverage.CompareTo(a.coverage));
            int n = Mathf.Min(8, hits.Count);
            Debug.Log($"[DcvrProbe] {hits.Count} renderers within 8 m; top {n} by screen coverage:");
            for (int i = 0; i < n; i++)
            {
                Debug.Log($"[DcvrProbe]   cov={hits[i].coverage * 100f:F1}%  d={hits[i].dist:F2}m  {hits[i].path}");
            }
        }

        private void Update()
        {
            if (_head == null) { return; }

            Vector3 hl = _head.localPosition;
            Vector3 hw = _head.position;

            // Track the range the head has covered. A wearer who has leaned and stepped
            // should see this grow; if it stays at zero, no pose is arriving.
            _headMin = Vector3.Min(_headMin, hl);
            _headMax = Vector3.Max(_headMax, hl);
            Vector3 range = _headMax - _headMin;

            DcvrText.SetText(_line1, $"HEAD LOCAL   {hl.x:F2} {hl.y:F2} {hl.z:F2}");
            DcvrText.SetText(_line2, $"HEAD WORLD   {hw.x:F2} {hw.y:F2} {hw.z:F2}");
            DcvrText.SetText(_line3, _origin != null
                ? $"XR ORIGIN    {_origin.position.x:F2} {_origin.position.y:F2} {_origin.position.z:F2}"
                : "XR ORIGIN    (none)");

            bool moving = range.magnitude > 0.05f;
            DcvrText.SetText(_line4, moving
                ? $"TRACKING OK  moved {range.magnitude:F2} m"
                : "TRACKING ??  no head movement yet");
            DcvrText.SetColor(_line4, moving ? DcvrWorld.Green : DcvrWorld.Amber);

            DcvrText.SetText(_line5, $"MODE {TrackingMode()}   eye height {hl.y:F2} m");

            _logTimer += Time.deltaTime;
            if (_logTimer >= LogInterval)
            {
                _logTimer = 0f;
                LogState(hl, hw, range);
                ProbeNearGeometry(Camera.main);
            }
        }

        private void LogState(Vector3 headLocal, Vector3 headWorld, Vector3 range)
        {
            var sb = new StringBuilder(256);
            sb.Append("[DcvrDiag] head.local=").Append(Fmt(headLocal));
            sb.Append(" head.world=").Append(Fmt(headWorld));
            sb.Append(" origin=").Append(_origin != null ? Fmt(_origin.position) : "n/a");
            sb.Append(" range=").Append(range.magnitude.ToString("F3"));
            sb.Append(" mode=").Append(TrackingMode());
            if (_leftHand != null) { sb.Append(" L=").Append(Fmt(_leftHand.localPosition)); }
            if (_rightHand != null) { sb.Append(" R=").Append(Fmt(_rightHand.localPosition)); }

            // World anchors: these MUST NOT change. Logging them next to the head pose is
            // what turns "it feels like it follows me" into a checkable statement.
            if (_worldAnchors != null)
            {
                for (int i = 0; i < _worldAnchors.Length; i++)
                {
                    if (_worldAnchors[i] == null) { continue; }
                    string n = (_anchorNames != null && i < _anchorNames.Length)
                        ? _anchorNames[i] : ("anchor" + i);
                    sb.Append(' ').Append(n).Append('=').Append(Fmt(_worldAnchors[i].position));
                }
            }
            Debug.Log(sb.ToString());
        }

        private static string Fmt(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";

        private static string TrackingMode()
        {
            var subsystems = new System.Collections.Generic.List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (XRInputSubsystem s in subsystems)
            {
                return s.GetTrackingOriginMode().ToString();
            }
            return "none";
        }
    }
}
