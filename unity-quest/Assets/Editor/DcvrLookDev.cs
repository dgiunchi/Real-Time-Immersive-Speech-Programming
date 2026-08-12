// DreamCodeVR+ — offscreen look-dev renderer.
//
// Builds the environment in an empty scene, renders it from several viewpoints, and
// writes PNGs. This is how the visuals were actually iterated on: an IL2CPP build takes
// 6-8 minutes and needs the headset to be worn before anything can be seen, which is far
// too slow a loop for tuning colour, contrast and composition. This runs in seconds.
//
// It is NOT a substitute for wearing the headset. A flat render cannot tell you about
// stereo depth, world scale, text legibility at focal distance, or comfort. It tells you
// whether the composition and palette are right before spending a build on them.
//
//   Unity -batchmode -quit -projectPath unity-quest \
//         -executeMethod DcvrLookDev.RenderAndExit -logFile -
//
// Note: do NOT pass -nographics — there is no GPU to render with under that flag.

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DcvrLookDev
{
    private const string OutDir = "LookDev";
    private const int Width = 1280;
    private const int Height = 720;

    private struct Shot
    {
        public string Name;
        public Vector3 Pos;
        public Vector3 LookAt;
        public float Fov;
    }

    private static readonly Shot[] Shots =
    {
        // Standing wearer at the rig origin, looking at the platform 3.2 m ahead.
        new Shot { Name = "01-eye-level", Pos = new Vector3(0f, 1.6f, 0f),
                   LookAt = new Vector3(0f, 1.6f, 4.2f), Fov = 75f },
        // Wide, to judge the horizon, the monolith ring and the fog falloff.
        new Shot { Name = "02-wide", Pos = new Vector3(8f, 3.4f, -6f),
                   LookAt = new Vector3(0f, 1.2f, 4.2f), Fov = 70f },
        // Close on the platform: rings, grid density, target object.
        new Shot { Name = "03-platform", Pos = new Vector3(1.6f, 1.5f, 0.9f),
                   LookAt = new Vector3(0f, 1.1f, 4.2f), Fov = 62f },
        // The HUD read from where the wearer stands — the legibility check.
        new Shot { Name = "04-hud", Pos = new Vector3(0f, 1.65f, 0.2f),
                   LookAt = new Vector3(0f, 2.15f, 4.2f), Fov = 60f },
        // Looking down: how the grid and platform read in the lower field of view.
        new Shot { Name = "05-down", Pos = new Vector3(0f, 2.6f, -0.5f),
                   LookAt = new Vector3(0f, 0f, 4.0f), Fov = 80f },
        // BEHIND the wearer. If this is empty the world is a stage flat, and the
        // illusion dies the moment they turn around.
        new Shot { Name = "06-behind", Pos = new Vector3(0f, 1.6f, 0f),
                   LookAt = new Vector3(0f, 1.5f, -6f), Fov = 80f },
        // Straight up: is there anything worth seeing overhead?
        new Shot { Name = "07-up", Pos = new Vector3(0f, 1.6f, 0f),
                   LookAt = new Vector3(0.5f, 9f, 2f), Fov = 85f },
        // The whole composition: title, platform, panel and generation preview together.
        new Shot { Name = "08-composition", Pos = new Vector3(-0.6f, 1.7f, -1.4f),
                   LookAt = new Vector3(0f, 1.7f, 4.2f), Fov = 88f },
    };

    public static void RenderAndExit()
    {
        try
        {
            Render();
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[DcvrLookDev] " + e);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("DreamCodeVR+/Render look-dev stills")]
    public static void Render()
    {
        Directory.CreateDirectory(OutDir);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var world = DreamCodeVRPlus.DcvrWorld.Build();
        // Same placement as the device build: above the platform, unrotated. Yaw-flipping
        // the panel to "face back" mirrors every glyph, which is what the first look-dev
        // pass caught.
        DreamCodeVRPlus.DcvrHud hud = DreamCodeVRPlus.DcvrHud.Build(
            null, DreamCodeVRPlus.DcvrWorld.PlatformCenter + new Vector3(0f, 2.05f, 0f));

        // Show a representative mid-flight state rather than an empty panel, so the
        // stills exercise the stage lamps and verdict typography too.
        hud.SetHeard("make it bright green and spin it");
        hud.SetStage(DreamCodeVRPlus.DcvrStage.Validate);

        // Title piece and the privacy-preserving generation preview, so the stills show
        // the full composition rather than the platform alone.
        DreamCodeVRPlus.DcvrTitle.Build(
            DreamCodeVRPlus.DcvrWorld.PlatformCenter + new Vector3(-6.4f, 2.5f, 2.2f), -38f);
        var preview = DreamCodeVRPlus.DcvrCodePreview.Build(
            null, DreamCodeVRPlus.DcvrWorld.PlatformCenter + new Vector3(3.3f, 1.7f, -0.4f));
        preview.gameObject.SetActive(true);
        preview.SetStageProgress(DreamCodeVRPlus.DcvrStage.Validate);

        var camGo = new GameObject("DCVR_LookDevCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.backgroundColor = new Color(0.01f, 0.02f, 0.04f);
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 120f;
        cam.allowHDR = false;
        // Same path as the device: enables bloom / grading on the look-dev camera too,
        // so these stills show what the headset will actually show.
        DreamCodeVRPlus.DcvrRig.Configure(cam, world);

        foreach (Shot s in Shots)
        {
            cam.transform.position = s.Pos;
            cam.transform.LookAt(s.LookAt);
            cam.fieldOfView = s.Fov;
            string path = Path.Combine(OutDir, s.Name + ".png");
            Capture(cam, path);
            Debug.Log($"[DcvrLookDev] wrote {path}");
        }

        Debug.Log($"[DcvrLookDev] {Shots.Length} stills -> {Path.GetFullPath(OutDir)}");
        _ = scene;
    }

    private static void Capture(Camera cam, string path)
    {
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4,
        };
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);

        RenderTexture prevTarget = cam.targetTexture;
        RenderTexture prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
