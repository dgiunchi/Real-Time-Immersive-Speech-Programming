using System.Collections.Generic;
using DreamCodeVR2.ContextBridge;
using UnityEngine;

namespace DreamCodeVR2.Quest
{
    public class RuntimeCreatableObjectCatalog : MonoBehaviour
    {
        private readonly Dictionary<string, GameObject> createdObjects = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Material> fallbackMaterials = new Dictionary<string, Material>();

        public bool IsSupportedObjectId(string objectId)
        {
            return objectId == "soccer_ball_001" || objectId == "colored_cube_001";
        }

        public GameObject GetOrCreate(string objectId, string primitiveHint = null, string materialName = null)
        {
            if (string.IsNullOrWhiteSpace(objectId) || !IsSupportedObjectId(objectId))
            {
                return null;
            }

            if (createdObjects.TryGetValue(objectId, out var existing) && existing)
            {
                existing.SetActive(true);
                if (!string.IsNullOrWhiteSpace(materialName))
                {
                    ApplyMaterial(existing, materialName);
                }

                return existing;
            }

            var primitiveType = ResolvePrimitiveType(objectId, primitiveHint);
            var created = GameObject.CreatePrimitive(primitiveType);
            created.name = objectId;
            created.tag = "game";

            var editable = created.AddComponent<AIEditableObject>();
            editable.objectId = objectId;
            editable.displayName = objectId == "soccer_ball_001" ? "Soccer Ball" : "Colored Cube";
            editable.description = objectId == "soccer_ball_001"
                ? "A runtime-created soccer ball used for constrained quest tasks."
                : "A runtime-created colored cube used for constrained quest tasks.";
            editable.labels = objectId == "soccer_ball_001"
                ? new[] { "ball", "soccer_ball", "created_object", "placeable", "puzzle_item", "interactive" }
                : new[] { "cube", "colored_cube", "created_object", "placeable", "puzzle_item", "interactive" };
            editable.editable = true;
            editable.includeRendererBounds = true;

            created.transform.localScale = objectId == "soccer_ball_001"
                ? Vector3.one * 0.15f
                : Vector3.one * 0.12f;

            ApplyMaterial(created, string.IsNullOrWhiteSpace(materialName) ? GetDefaultMaterialName(objectId) : materialName);
            createdObjects[objectId] = created;
            Debug.Log($"[QuestPlan] Created runtime object {objectId}");
            return created;
        }

        public void ResetCreatedObject(string objectId)
        {
            if (!createdObjects.TryGetValue(objectId, out var created) || !created)
            {
                return;
            }

            createdObjects.Remove(objectId);
            if (Application.isPlaying)
            {
                Destroy(created);
            }
            else
            {
                DestroyImmediate(created);
            }
        }

        public bool TryResolveMaterial(string materialName, out Material material)
        {
            material = null;
            if (string.IsNullOrWhiteSpace(materialName))
            {
                return false;
            }

            var normalized = NormalizeMaterialName(materialName);
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (!candidate)
                {
                    continue;
                }

                var candidateName = NormalizeMaterialName(candidate.name);
                if (candidateName == normalized)
                {
                    material = candidate;
                    return true;
                }
            }

            switch (normalized)
            {
                case "soccerballmaterial":
                    material = GetOrCreateFallbackMaterial(normalized, new Color(0.92f, 0.92f, 0.92f, 1f));
                    return true;
                case "redmaterial":
                    material = GetOrCreateFallbackMaterial(normalized, new Color(0.82f, 0.18f, 0.18f, 1f));
                    return true;
                case "bluematerial":
                    material = GetOrCreateFallbackMaterial(normalized, new Color(0.18f, 0.39f, 0.86f, 1f));
                    return true;
                case "greenmaterial":
                    material = GetOrCreateFallbackMaterial(normalized, new Color(0.16f, 0.68f, 0.27f, 1f));
                    return true;
                case "yellowmaterial":
                    material = GetOrCreateFallbackMaterial(normalized, new Color(0.94f, 0.78f, 0.15f, 1f));
                    return true;
                case "goldkey":
                    material = GetOrCreateFallbackMaterial(normalized, new Color(0.86f, 0.70f, 0.14f, 1f));
                    return true;
                case "silverkey":
                    material = GetOrCreateFallbackMaterial(normalized, new Color(0.74f, 0.77f, 0.81f, 1f));
                    return true;
            }

            return false;
        }

        public bool ApplyMaterial(GameObject target, string materialName)
        {
            if (!target || !TryResolveMaterial(materialName, out var material))
            {
                return false;
            }

            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
            }

            return true;
        }

        private Material GetOrCreateFallbackMaterial(string key, Color color)
        {
            if (fallbackMaterials.TryGetValue(key, out var cached) && cached)
            {
                return cached;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader)
            {
                shader = Shader.Find("Standard");
            }

            if (!shader)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader);
            material.name = key;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.color = color;
            }

            fallbackMaterials[key] = material;
            return material;
        }

        private static PrimitiveType ResolvePrimitiveType(string objectId, string primitiveHint)
        {
            if (!string.IsNullOrWhiteSpace(primitiveHint))
            {
                switch (primitiveHint.Trim().ToLowerInvariant())
                {
                    case "sphere":
                        return PrimitiveType.Sphere;
                    case "cube":
                        return PrimitiveType.Cube;
                }
            }

            return objectId == "soccer_ball_001" ? PrimitiveType.Sphere : PrimitiveType.Cube;
        }

        private static string GetDefaultMaterialName(string objectId)
        {
            return objectId == "soccer_ball_001" ? "soccer_ball_material" : "red_material";
        }

        private static string NormalizeMaterialName(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        }
    }
}
