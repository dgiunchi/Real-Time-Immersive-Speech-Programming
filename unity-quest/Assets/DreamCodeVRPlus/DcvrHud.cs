// DreamCodeVR+ — the world-space panel the wearer reads.
//
// Replaces the desktop OnGUI panel, which renders nothing in a stereo XR build: IMGUI
// draws to the screen, and in VR there is no screen. Everything here is world-space
// geometry the headset can actually show.
//
// The panel is the security story made legible. It does not just say "blocked" — it
// shows WHICH pipeline stage stopped the request, so the two-plane architecture
// (intent screen, then guardrail, then perceptual layer) is visible rather than
// asserted.
//
// Text: TextMeshPro when its font assets are present, legacy TextMesh otherwise. TMP
// essential resources are not always imported in a batchmode build and a missing font
// renders as nothing at all — a silent, invisible failure on the device. The fallback
// keeps the demo readable either way.

using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>Pipeline stages, in the order the backend runs them.</summary>
    public enum DcvrStage { Idle = -1, Intent = 0, Generate = 1, Validate = 2, Execute = 3 }

    public sealed class DcvrHud : MonoBehaviour
    {
        private const float PanelWidth = 2.4f;
        private static readonly string[] StageNames = { "INTENT", "GENERATE", "VALIDATE", "EXECUTE" };

        private readonly Renderer[] _stageLamps = new Renderer[4];
        private readonly Material[] _stageMats = new Material[4];
        private object _titleText, _statusText, _transcriptText, _verdictText, _reasonText;

        private DcvrStage _stage = DcvrStage.Idle;
        private float _verdictHold;

        public static DcvrHud Build(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("DCVR_Hud");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var hud = go.AddComponent<DcvrHud>();
            hud.Construct();
            return hud;
        }

        private void Construct()
        {
            // Backing plate — dark, slightly transparent, so text stays legible against
            // a bright horizon without becoming an opaque slab floating in the world.
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "DCVR_HudPlate";
            plate.transform.SetParent(transform, false);
            plate.transform.localScale = new Vector3(PanelWidth, 1.15f, 1f);
            Destroy(plate.GetComponent<Collider>());
            var plateMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                                        ?? Shader.Find("Unlit/Color"))
            { name = "DCVR_HudPlateMat" };
            SetColorSafe(plateMat, new Color(0.02f, 0.035f, 0.055f, 0.86f));
            EnableTransparency(plateMat);
            plate.GetComponent<Renderer>().sharedMaterial = plateMat;

            var border = GameObject.CreatePrimitive(PrimitiveType.Quad);
            border.name = "DCVR_HudBorder";
            border.transform.SetParent(transform, false);
            border.transform.localPosition = new Vector3(0f, 0f, 0.005f);
            border.transform.localScale = new Vector3(PanelWidth + 0.03f, 1.18f, 1f);
            Destroy(border.GetComponent<Collider>());
            Material borderMat = MakeHolo("DCVR_HudBorderMat", DcvrWorld.Cyan, 0.12f);
            if (borderMat != null) { border.GetComponent<Renderer>().sharedMaterial = borderMat; }

            _titleText = MakeText("DREAMCODEVR+", new Vector3(0f, 0.45f, -0.01f), 0.115f, DcvrWorld.Cyan);
            _statusText = MakeText("SPEAK TO CREATE", new Vector3(0f, 0.30f, -0.01f), 0.062f, DcvrWorld.Dim);
            _transcriptText = MakeText("", new Vector3(0f, 0.10f, -0.01f), 0.070f, Color.white);
            _verdictText = MakeText("", new Vector3(0f, -0.28f, -0.01f), 0.085f, DcvrWorld.Dim);
            _reasonText = MakeText("", new Vector3(0f, -0.42f, -0.01f), 0.048f, DcvrWorld.Dim);

            BuildStageLamps();
        }

        /// <summary>Four pills that light in sequence. The examiner can watch a request
        /// walk the pipeline and see exactly where a malicious one stops.</summary>
        private void BuildStageLamps()
        {
            const float spacing = 0.56f;
            float x0 = -spacing * 1.5f;
            for (int i = 0; i < 4; i++)
            {
                var pill = GameObject.CreatePrimitive(PrimitiveType.Quad);
                pill.name = "DCVR_Stage" + StageNames[i];
                pill.transform.SetParent(transform, false);
                pill.transform.localPosition = new Vector3(x0 + spacing * i, -0.10f, -0.01f);
                pill.transform.localScale = new Vector3(0.50f, 0.075f, 1f);
                Destroy(pill.GetComponent<Collider>());

                Material m = MakeHolo("DCVR_StageMat" + i, DcvrWorld.Dim, 0.10f);
                if (m != null)
                {
                    pill.GetComponent<Renderer>().sharedMaterial = m;
                    _stageMats[i] = m;
                }
                _stageLamps[i] = pill.GetComponent<Renderer>();

                MakeText(StageNames[i], new Vector3(x0 + spacing * i, -0.10f, -0.02f),
                         0.037f, Color.white);
            }
        }

        // ---- public state API ----------------------------------------------------
        public void SetHeard(string transcript)
        {
            SetText(_transcriptText, "“" + transcript + "”");
            SetText(_statusText, "PROCESSING");
            SetText(_verdictText, "");
            SetText(_reasonText, "");
            SetStage(DcvrStage.Intent);
        }

        public void SetListening(bool listening)
        {
            SetText(_statusText, listening ? "LISTENING…" : "SPEAK TO CREATE");
        }

        public void SetStage(DcvrStage stage)
        {
            _stage = stage;
            for (int i = 0; i < 4; i++)
            {
                if (_stageMats[i] == null) { continue; }
                bool lit = (int)stage >= i && stage != DcvrStage.Idle;
                _stageMats[i].SetColor("_Color", lit ? DcvrWorld.Cyan : DcvrWorld.Dim);
                _stageMats[i].SetFloat("_Alpha", lit ? 0.55f : 0.08f);
            }
        }

        public void SetAccepted(string what)
        {
            SetStage(DcvrStage.Execute);
            SetText(_statusText, "SPEAK TO CREATE");
            SetText(_verdictText, "✓ SAFE — EXECUTED");
            SetTextColor(_verdictText, DcvrWorld.Green);
            SetText(_reasonText, what ?? "");
            SetTextColor(_reasonText, DcvrWorld.Dim);
            _verdictHold = 6f;
            for (int i = 0; i < 4; i++)
            {
                if (_stageMats[i] != null) { _stageMats[i].SetColor("_Color", DcvrWorld.Green); }
            }
        }

        /// <summary><paramref name="stoppedAt"/> is the stage that actually refused the
        /// request — that lamp turns red and the ones after it stay dark, so the display
        /// shows where the defence engaged instead of a bare verdict.</summary>
        public void SetBlocked(string reason, DcvrStage stoppedAt)
        {
            SetText(_statusText, "SPEAK TO CREATE");
            SetText(_verdictText, "✕ BLOCKED");
            SetTextColor(_verdictText, DcvrWorld.Red);
            SetText(_reasonText, reason ?? "");
            SetTextColor(_reasonText, DcvrWorld.Red);
            _verdictHold = 8f;

            for (int i = 0; i < 4; i++)
            {
                if (_stageMats[i] == null) { continue; }
                bool reached = i < (int)stoppedAt;
                bool here = i == (int)stoppedAt;
                _stageMats[i].SetColor("_Color", here ? DcvrWorld.Red
                                                     : reached ? DcvrWorld.Cyan : DcvrWorld.Dim);
                _stageMats[i].SetFloat("_Alpha", here ? 0.75f : reached ? 0.45f : 0.08f);
            }
        }

        private void Update()
        {
            if (_verdictHold > 0f)
            {
                _verdictHold -= Time.deltaTime;
                if (_verdictHold <= 0f)
                {
                    SetText(_verdictText, "");
                    SetText(_reasonText, "");
                    SetStage(DcvrStage.Idle);
                }
            }
        }

        // ---- text helpers (TMP with a legacy fallback) ---------------------------
        private object MakeText(string content, Vector3 localPos, float size, Color color)
        {
            var go = new GameObject("DCVR_Text");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;

            System.Type tmpType = System.Type.GetType("TMPro.TextMeshPro, Unity.TextMeshPro");
            if (tmpType != null)
            {
                var comp = go.AddComponent(tmpType);
                if (comp != null && TrySetupTmp(comp, tmpType, content, size, color)) { return comp; }
                Destroy(comp);
            }

            var tm = go.AddComponent<TextMesh>();
            tm.text = content;
            tm.color = color;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            // Legacy TextMesh is raster: author at a large point size and scale down, or
            // it is unreadably blurry at VR viewing distance.
            tm.fontSize = 90;
            tm.characterSize = size * 0.30f;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return tm;
        }

        private static bool TrySetupTmp(Component comp, System.Type t, string content,
                                        float size, Color color)
        {
            try
            {
                t.GetProperty("text")?.SetValue(comp, content);
                t.GetProperty("fontSize")?.SetValue(comp, size * 40f);
                t.GetProperty("color")?.SetValue(comp, color);
                var align = System.Type.GetType("TMPro.TextAlignmentOptions, Unity.TextMeshPro");
                if (align != null)
                {
                    t.GetProperty("alignment")?.SetValue(comp, System.Enum.Parse(align, "Center"));
                }
                var rect = comp.GetComponent<RectTransform>();
                if (rect != null) { rect.sizeDelta = new Vector2(PanelWidth, 0.3f); }
                // A TMP component with no font asset renders nothing at all; treat that
                // as failure so the caller falls back rather than shipping blank text.
                object font = t.GetProperty("font")?.GetValue(comp);
                return font != null;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DcvrHud] TMP unavailable, using TextMesh: " + e.Message);
                return false;
            }
        }

        private static void SetText(object handle, string value)
        {
            if (handle is TextMesh tm) { tm.text = value; return; }
            if (handle is Component c)
            {
                c.GetType().GetProperty("text")?.SetValue(c, value);
            }
        }

        private static void SetTextColor(object handle, Color value)
        {
            if (handle is TextMesh tm) { tm.color = value; return; }
            if (handle is Component c)
            {
                c.GetType().GetProperty("color")?.SetValue(c, value);
            }
        }

        private static Material MakeHolo(string name, Color color, float alpha)
        {
            Shader s = Shader.Find("DreamCodeVRPlus/Holo");
            if (s == null) { return null; }
            var m = new Material(s) { name = name };
            m.SetColor("_Color", color);
            m.SetFloat("_Alpha", alpha);
            return m;
        }

        private static void SetColorSafe(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", c); }
            if (m.HasProperty("_Color")) { m.SetColor("_Color", c); }
        }

        private static void EnableTransparency(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}
