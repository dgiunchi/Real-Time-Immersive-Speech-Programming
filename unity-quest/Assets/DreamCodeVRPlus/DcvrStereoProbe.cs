// DreamCodeVR+ — why does one object look like two?
//
// THE REPORT: closing either eye shows ONE object; both eyes open show TWO, and the thing
// appears to follow the wearer's gaze. An earlier automated test asserted that generated
// content does not move when the rig does, and it passed — so the TRANSFORMS are right and
// the fault is somewhere the transform test cannot see. The physical headset is the
// acceptance authority; this file exists to find out what it is actually seeing.
//
// The two symptoms are reported together but must not be assumed to share a cause, so this
// probe answers each of the candidate questions separately and prints the evidence:
//
//   1. Are there literally TWO GameObjects?      (instantiation/capture bug)
//   2. What is the FULL ancestor chain?          (head-locking via any ancestor)
//   3. How many cameras are rendering?           (a stray second camera)
//   4. What stereo mode is THIS process in?      (read from us, not the Horizon shell)
//   5. Do the eye view matrices differ?          (a real stereo rig, or one eye twice)
//   6. What SHADER is on each generated renderer? (the stereo-instancing question)
//
// Question 6 is the one the transform test structurally could not ask. Under
// SinglePassInstanced, both eyes are rendered in ONE draw into a texture array, and a
// shader participates only if it declares the stereo macros. A shader that does not
// declare them ignores the per-eye view matrix — so it draws the object at the SAME place
// in both eye images. That single fact produces both reported symptoms at once: the two
// images carry no binocular disparity so they never fuse, and an object pinned to the same
// screen position in both eyes is indistinguishable from one glued to your gaze.

using System.Text;
using UnityEngine;

namespace DreamCodeVRPlus
{
    public static class DcvrStereoProbe
    {
        /// <summary>Dump everything relevant to the doubling report. Called on demand, not
        /// per frame — it walks the scene and formats strings.</summary>
        public static void Run(string label)
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine($"===== [StereoProbe] {label} =====");

            ReportXrDisplay(sb);
            ReportCameras(sb);
            ReportEyeMatrices(sb);
            ReportGeneratedContent(sb);

            Debug.Log(sb.ToString());
        }

        // ---- 4. what stereo mode is THIS process in --------------------------------
        private static void ReportXrDisplay(StringBuilder sb)
        {
            var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
            UnityEngine.SubsystemManager.GetSubsystems(displays);
            sb.AppendLine($"-- XR display subsystems: {displays.Count}");
            foreach (UnityEngine.XR.XRDisplaySubsystem d in displays)
            {
                sb.AppendLine($"   running={d.running} passCount={d.GetRenderPassCount()} "
                              + $"textureLayout={d.textureLayout}");
            }
            sb.AppendLine($"-- XRSettings: enabled={UnityEngine.XR.XRSettings.enabled} "
                          + $"device='{UnityEngine.XR.XRSettings.loadedDeviceName}' "
                          + $"stereoMode={UnityEngine.XR.XRSettings.stereoRenderingMode} "
                          + $"eyeTex={UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight}");
        }

        // ---- 3. how many cameras are drawing ----------------------------------------
        private static void ReportCameras(StringBuilder sb)
        {
            Camera[] cams = Camera.allCameras;
            sb.AppendLine($"-- cameras rendering: {cams.Length} (expect 1 under OpenXR)");
            foreach (Camera c in cams)
            {
                sb.AppendLine($"   '{c.name}' enabled={c.enabled} tag={c.tag} "
                              + $"stereoEnabled={c.stereoEnabled} targetEye={c.stereoTargetEye} "
                              + $"depth={c.depth} mask=0x{c.cullingMask:X} "
                              + $"rt={(c.targetTexture == null ? "none" : c.targetTexture.name)} "
                              + $"parent='{(c.transform.parent == null ? "<root>" : c.transform.parent.name)}' "
                              + $"pos={c.transform.position}");
            }
        }

        // ---- 5. do the two eyes actually differ --------------------------------------
        private static void ReportEyeMatrices(StringBuilder sb)
        {
            Camera cam = Camera.main;
            if (cam == null) { sb.AppendLine("-- no main camera"); return; }

            Matrix4x4 vl = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 vr = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
            Matrix4x4 pl = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 pr = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);

            // The IPD shows up as the x difference between the two view matrices. If this
            // is genuinely zero the rig is rendering the same eye twice and nothing can
            // fuse — but a zero reading is ALSO what you get by asking before the first
            // stereo frame has been submitted, so the timing is reported alongside it. A
            // measurement taken at the wrong moment is not evidence of anything.
            float dx = Mathf.Abs(vl.m03 - vr.m03);
            string verdict = dx < 0.01f
                ? (Time.frameCount < 5
                    ? "<<< zero, but frame " + Time.frameCount + " — too early to conclude"
                    : "<<< SUSPECT: eyes are not separated after " + Time.frameCount + " frames")
                : "(normal IPD)";
            sb.AppendLine($"-- eye separation from view matrices: {dx:F4} m {verdict}");
            sb.AppendLine($"   projL.m00={pl.m00:F4} projR.m00={pr.m00:F4} "
                          + $"projL.m02={pl.m02:F4} projR.m02={pr.m02:F4}");
        }

        // ---- 1, 2, 6. the objects themselves ----------------------------------------
        private static void ReportGeneratedContent(StringBuilder sb)
        {
            DcvrGeneratedContent content = DcvrGeneratedContent.Instance;
            if (content == null) { sb.AppendLine("-- no GeneratedContent yet"); return; }

            sb.AppendLine($"-- GeneratedContent world={content.transform.position} "
                          + $"path={PathOf(content.transform)}");

            int n = 0;
            foreach (GameObject go in content.AllObjects)
            {
                if (n++ >= 6) { sb.AppendLine("   … (truncated)"); break; }
                var r = go.GetComponent<Renderer>();
                Material m = r != null ? r.sharedMaterial : null;
                Shader sh = m != null ? m.shader : null;

                sb.AppendLine($"   [{n}] '{go.name}' world={go.transform.position} "
                              + $"scale={go.transform.lossyScale}");
                sb.AppendLine($"        path={PathOf(go.transform)}");
                sb.AppendLine($"        renderers-in-subtree={go.GetComponentsInChildren<Renderer>(true).Length}");
                sb.AppendLine($"        material='{(m == null ? "<none>" : m.name)}' "
                              + $"shader='{(sh == null ? "<none>" : sh.name)}' "
                              + $"instancing={(m != null && m.enableInstancing)} "
                              + $"stereoOk={StereoVerdict(sh)}");
            }
            sb.AppendLine($"-- registry entries: {content.ObjectCount}, groups: {content.Groups.Count}");
        }

        /// <summary>Is this shader one we KNOW handles single-pass-instanced stereo?
        ///
        /// URP's own shaders do. The project's custom shaders declare the macros (they were
        /// written for this build). The built-in `Standard` / `Diffuse` that
        /// `GameObject.CreatePrimitive` attaches by default do NOT participate in URP's
        /// stereo path at all — and generated code calls `CreatePrimitive` constantly.</summary>
        private static string StereoVerdict(Shader sh)
        {
            if (sh == null) { return "NO-SHADER"; }
            string n = sh.name;
            if (n.StartsWith("Universal Render Pipeline/")) { return "URP-ok"; }
            if (n.StartsWith("DreamCodeVRPlus/")) { return "project-shader"; }
            return "*** NOT-URP: " + n + " ***";
        }

        private static string PathOf(Transform t)
        {
            var sb = new StringBuilder();
            for (Transform p = t; p != null; p = p.parent)
            {
                sb.Insert(0, "/" + p.name);
            }
            return sb.ToString();
        }
    }
}
