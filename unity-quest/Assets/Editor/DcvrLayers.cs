// DreamCodeVR+ — the layer that lets generated content be lit on its own terms.
//
// The accepted environment is lit by a deliberately blue key at low intensity over a nearly
// black ambient, because almost everything in it is emissive and only needs a rim of light
// to sit in the scene. Generated content is NOT emissive — it is ordinary URP/Lit geometry
// carrying the colours the model chose — so that same rig renders a warm grey stone wall as
// dark blue-grey and a brown gate as very nearly black.
//
// Rather than relight the world (which would change a look already signed off), generated
// content gets its own layer and its own neutral light. The environment keeps exactly the
// lighting it has; creations get lighting suited to what they actually are.
//
// Configured from source rather than by hand in the Inspector, for the same reason the rest
// of this project's build settings are: a layer that exists only in someone's local
// TagManager is a build that only works on one machine.

using UnityEditor;
using UnityEngine;

public static class DcvrLayers
{
    public const string GeneratedLayerName = "DCVR_Generated";

    /// <summary>Ensure the layer exists, returning its index (-1 if none could be added).
    ///
    /// Idempotent: re-running finds the existing entry rather than consuming another slot.
    /// Unity reserves 0-7 for its own layers, so the search starts at 8.</summary>
    public static int EnsureGeneratedLayer()
    {
        var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (asset == null || asset.Length == 0)
        {
            Debug.LogWarning("[DcvrLayers] cannot open TagManager.asset");
            return -1;
        }

        var tagManager = new SerializedObject(asset[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        if (layers == null || !layers.isArray)
        {
            Debug.LogWarning("[DcvrLayers] TagManager has no layers array");
            return -1;
        }

        for (int i = 0; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == GeneratedLayerName)
            {
                return i;
            }
        }

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = GeneratedLayerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log($"[DcvrLayers] added layer '{GeneratedLayerName}' at index {i}");
                return i;
            }
        }

        Debug.LogWarning("[DcvrLayers] no free user layer slot");
        return -1;
    }

    [MenuItem("DreamCodeVR+/Ensure Generated Layer")]
    public static void Run() => EnsureGeneratedLayer();
}
