#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

public static class CreateSoccerBallMaterial
{
    private const string TexturePath = "Assets/Resources/SoccerBall.png";
    private const string MaterialPath = "Assets/Resources/SoccerBall.mat";

    [MenuItem("DreamCodeVR2/Setup/Create Soccer Ball Material")]
    public static void CreateMaterial()
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);

        if (texture == null)
        {
            Debug.LogError(
                $"[DreamCodeVR2] Soccer-ball texture not found at '{TexturePath}'. " +
                "Add SoccerBall.png to Assets/Resources first."
            );
            return;
        }

        Material existingMaterial =
            AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (existingMaterial != null)
        {
            Debug.LogWarning(
                $"[DreamCodeVR2] Material already exists at '{MaterialPath}'. " +
                "It will not be overwritten."
            );
            Selection.activeObject = existingMaterial;
            EditorGUIUtility.PingObject(existingMaterial);
            return;
        }

        Shader shader = FindCompatibleShader();

        if (shader == null)
        {
            Debug.LogError(
                "[DreamCodeVR2] Could not find a compatible shader. " +
                "Checked URP/Lit and Standard."
            );
            return;
        }

        Material material = new Material(shader)
        {
            name = "SoccerBall"
        };

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }
        else if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }
        else
        {
            Debug.LogWarning(
                $"[DreamCodeVR2] Shader '{shader.name}' has neither _BaseMap nor _MainTex."
            );
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }

        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = material;
        EditorGUIUtility.PingObject(material);

        Debug.Log(
            $"[DreamCodeVR2] Created SoccerBall material.\n" +
            $"Shader: {shader.name}\n" +
            $"Texture: {TexturePath}\n" +
            $"Material: {MaterialPath}"
        );
    }

    private static Shader FindCompatibleShader()
    {
        // Prefer URP because Quest projects commonly use it.
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader != null)
        {
            return shader;
        }

        shader = Shader.Find("Standard");

        if (shader != null)
        {
            return shader;
        }

        return null;
    }
}

#endif