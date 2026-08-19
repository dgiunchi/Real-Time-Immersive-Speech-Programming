using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DreamCodeVR2.ContextBridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DreamCodeVR2.ContextBridge.Editor
{
    public static class EscapeRoomSceneRepairTool
    {
        private const string ScenePath = "Assets/DreamCodeVR2/EscapeRoomTestbed/DreamCodeVR2_EscapeRoom_Testbed.unity";
        private const string ReportPath = "Assets/DreamCodeVR2/EscapeRoomTestbed/EscapeRoomSceneSnapshot.generated.md";
        private const string SelectionProxyColliderName = "selection_proxy_collider";
        private const string RequiredTag = "game";

        private static readonly string[] IntendedObjectIds =
        {
            "door_001",
            "lock_001",
            "table_001",
            "table_drawer_001",
            "table_drawer_002",
            "table_drawer_003",
            "lock_002",
            "cabinet_001",
            "cabinet_drawer_001",
            "cabinet_drawer_002",
            "cabinet_drawer_003",
            "lock_003",
            "key_001",
            "key_002",
            "painting_001",
            "clue_note_001",
            "clue_note_002",
            "basket_001",
            "lamp_001"
        };

        private static readonly HashSet<string> ProxyPreferredObjectIds = new HashSet<string>
        {
            "lock_001",
            "lock_002",
            "lock_003",
            "key_001",
            "key_002",
            "clue_note_001",
            "clue_note_002"
        };

        private static readonly Dictionary<string, MetadataSpec> MetadataByObjectId =
            new Dictionary<string, MetadataSpec>(StringComparer.Ordinal)
            {
                ["door_001"] = new MetadataSpec(
                    "Exit Door",
                    "Main exit door of the escape room. It can be unlocked with the correct key and opened.",
                    "door", "exit", "openable", "lockable", "interactive", "final_goal"),
                ["lock_001"] = new MetadataSpec(
                    "Door Lock",
                    "Lock attached to the exit door. It should be unlocked with the silver key.",
                    "lock", "door_lock", "exit_lock", "puzzle_mechanism", "interactive"),
                ["table_001"] = new MetadataSpec(
                    "Desk",
                    "Desk with drawers. It can hold keys, notes, and other puzzle objects.",
                    "desk", "table", "furniture", "surface", "container_parent", "interactive"),
                ["table_drawer_001"] = new MetadataSpec(
                    "Desk Drawer 1",
                    "First drawer of the desk. It can be made openable and may contain puzzle objects.",
                    "drawer", "desk_drawer", "table_drawer", "container", "openable", "unlocked", "interactive"),
                ["table_drawer_002"] = new MetadataSpec(
                    "Locked Desk Drawer",
                    "Locked drawer of the desk. It should be unlocked using the golden key.",
                    "drawer", "desk_drawer", "table_drawer", "container", "locked", "lockable", "golden_key_target", "interactive"),
                ["table_drawer_003"] = new MetadataSpec(
                    "Desk Drawer 3",
                    "Third drawer of the desk. It can be made openable and searched.",
                    "drawer", "desk_drawer", "table_drawer", "container", "openable", "unlocked", "interactive"),
                ["lock_002"] = new MetadataSpec(
                    "Desk Drawer Lock",
                    "Lock attached to the locked desk drawer. It should be unlocked with the golden key.",
                    "lock", "drawer_lock", "desk_drawer_lock", "table_drawer_lock", "golden_key_target", "puzzle_mechanism", "interactive"),
                ["cabinet_001"] = new MetadataSpec(
                    "Cabinet",
                    "Cabinet with drawers that may contain hidden puzzle objects such as the silver key or notes.",
                    "cabinet", "dresser", "furniture", "container_parent", "interactive"),
                ["cabinet_drawer_001"] = new MetadataSpec(
                    "Cabinet Drawer 1",
                    "First cabinet drawer. It contains or hides the silver key.",
                    "drawer", "cabinet_drawer", "container", "openable", "unlocked", "contains_silver_key", "interactive"),
                ["cabinet_drawer_002"] = new MetadataSpec(
                    "Locked Cabinet Drawer",
                    "Locked drawer of the cabinet. It should be unlocked using the golden key.",
                    "drawer", "cabinet_drawer", "container", "locked", "lockable", "golden_key_target", "interactive"),
                ["cabinet_drawer_003"] = new MetadataSpec(
                    "Cabinet Drawer 3",
                    "Third cabinet drawer. It can be made openable and searched.",
                    "drawer", "cabinet_drawer", "container", "openable", "unlocked", "interactive"),
                ["lock_003"] = new MetadataSpec(
                    "Cabinet Drawer Lock",
                    "Lock attached to the locked cabinet drawer. It should be unlocked with the golden key.",
                    "lock", "drawer_lock", "cabinet_drawer_lock", "golden_key_target", "puzzle_mechanism", "interactive"),
                ["key_001"] = new MetadataSpec(
                    "Golden Key",
                    "Visible key used to unlock locked drawers and discover further instructions.",
                    "key", "golden_key", "drawer_key", "puzzle_item", "unlock_item", "visible", "interactive"),
                ["key_002"] = new MetadataSpec(
                    "Silver Key",
                    "Hidden key used to unlock and open the exit door.",
                    "key", "silver_key", "exit_key", "puzzle_item", "unlock_item", "hidden", "interactive"),
                ["painting_001"] = new MetadataSpec(
                    "Crooked Painting",
                    "A crooked wall painting that can be straightened and moved to reveal or contextualize clues.",
                    "painting", "wall_object", "decoration", "movable", "rotatable", "clue_context", "interactive"),
                ["clue_note_001"] = new MetadataSpec(
                    "First Clue Note",
                    "First note explaining that the golden key opens locked drawers.",
                    "clue", "note", "readable", "puzzle_instruction", "first_clue", "interactive"),
                ["clue_note_002"] = new MetadataSpec(
                    "Second Clue Note",
                    "Second note instructing the user to create a soccer ball and place it in the basket.",
                    "clue", "note", "readable", "puzzle_instruction", "ball_task_clue", "interactive"),
                ["basket_001"] = new MetadataSpec(
                    "Basket",
                    "Basket where the created soccer ball must be placed.",
                    "basket", "container", "receptacle", "placement_target", "ball_target", "puzzle_mechanism", "interactive"),
                ["lamp_001"] = new MetadataSpec(
                    "Puzzle Lamp",
                    "Lamp that can provide visual feedback for puzzle events.",
                    "lamp", "light", "feedback_object", "interactive")
            };

        [MenuItem("Tools/DreamCodeVR2/Escape Room/Apply Safe Scene Fixes")]
        public static void ApplySafeSceneFixes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var changeLog = new List<string>();
            var editables = GetSceneEditableObjects(scene).ToList();
            var byId = BuildEditableLookup(editables);

            var changed = false;
            changed |= ApplyMetadata(byId, changeLog);
            changed |= EnsureSafeSemanticNames(byId, changeLog);
            changed |= EnsureHierarchy(byId, changeLog);
            changed |= EnsureTags(byId, changeLog);
            changed |= EnsureSelectionColliders(byId, changeLog);
            changed |= EnsurePlacementAnchors(byId, changeLog);

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            var report = BuildReport(byId);
            File.WriteAllText(ToAbsoluteProjectPath(ReportPath), report, new UTF8Encoding(false));
            AssetDatabase.Refresh();

            var summary = changeLog.Count == 0 ? "no scene object changes were required" : string.Join("; ", changeLog);
            Debug.Log($"[EscapeRoomRepair] Completed safe scene repair. Summary: {summary}\nReport: {ReportPath}");
        }

        [MenuItem("Tools/DreamCodeVR2/Escape Room/Generate Snapshot Report")]
        public static void GenerateSnapshotReport()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var report = BuildReport(BuildEditableLookup(GetSceneEditableObjects(scene)));
            File.WriteAllText(ToAbsoluteProjectPath(ReportPath), report, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log($"[EscapeRoomRepair] Snapshot report regenerated at {ReportPath}");
        }

        private static IEnumerable<AIEditableObject> GetSceneEditableObjects(Scene scene)
        {
            return UnityEngine.Object.FindObjectsByType<AIEditableObject>(FindObjectsSortMode.None)
                .Where(editable => editable && editable.gameObject.scene == scene);
        }

        private static Dictionary<string, AIEditableObject> BuildEditableLookup(IEnumerable<AIEditableObject> editables)
        {
            var lookup = new Dictionary<string, AIEditableObject>(StringComparer.Ordinal);
            foreach (var editable in editables)
            {
                if (!editable || string.IsNullOrWhiteSpace(editable.objectId))
                {
                    continue;
                }

                if (!lookup.ContainsKey(editable.objectId))
                {
                    lookup.Add(editable.objectId, editable);
                }
            }

            return lookup;
        }

        private static bool ApplyMetadata(Dictionary<string, AIEditableObject> byId, List<string> changeLog)
        {
            var changed = false;
            foreach (var pair in MetadataByObjectId)
            {
                var editable = FindEditableByObjectIdOrName(byId, pair.Key);
                if (!editable)
                {
                    continue;
                }

                var spec = pair.Value;
                var itemChanged = false;
                if (editable.displayName != spec.DisplayName)
                {
                    editable.displayName = spec.DisplayName;
                    itemChanged = true;
                }

                if (editable.description != spec.Description)
                {
                    editable.description = spec.Description;
                    itemChanged = true;
                }

                if (!LabelsMatch(editable.labels, spec.Labels))
                {
                    editable.labels = spec.Labels.ToArray();
                    itemChanged = true;
                }

                if (!editable.editable)
                {
                    editable.editable = true;
                    itemChanged = true;
                }

                if (!editable.includeRendererBounds)
                {
                    editable.includeRendererBounds = true;
                    itemChanged = true;
                }

                if (itemChanged)
                {
                    EditorUtility.SetDirty(editable);
                    changed = true;
                }
            }

            if (changed)
            {
                changeLog.Add("updated AIEditableObject display names, descriptions, and labels");
            }

            return changed;
        }

        private static bool EnsureSafeSemanticNames(Dictionary<string, AIEditableObject> byId, List<string> changeLog)
        {
            var changed = false;
            foreach (var objectId in IntendedObjectIds)
            {
                var editable = FindEditableByObjectIdOrName(byId, objectId);
                if (!editable)
                {
                    continue;
                }

                if (!editable.name.StartsWith("GameObject_", StringComparison.Ordinal))
                {
                    continue;
                }

                editable.gameObject.name = objectId;
                EditorUtility.SetDirty(editable.gameObject);
                changed = true;
            }

            if (changed)
            {
                changeLog.Add("renamed generic GameObject_* roots to semantic names");
            }

            return changed;
        }

        private static bool EnsureHierarchy(Dictionary<string, AIEditableObject> byId, List<string> changeLog)
        {
            var changed = false;
            changed |= ReparentIfNeeded(FindEditableByObjectIdOrName(byId, "lock_002"), FindEditableByObjectIdOrName(byId, "table_drawer_002"), changeLog);
            changed |= ReparentIfNeeded(FindEditableByObjectIdOrName(byId, "lock_003"), FindEditableByObjectIdOrName(byId, "cabinet_drawer_002"), changeLog);
            changed |= ReparentIfNeeded(FindEditableByObjectIdOrName(byId, "key_002"), FindEditableByObjectIdOrName(byId, "cabinet_drawer_001"), changeLog);
            return changed;
        }

        private static bool ReparentIfNeeded(AIEditableObject child, AIEditableObject parent, List<string> changeLog)
        {
            if (!child || !parent || child.transform.parent == parent.transform)
            {
                return false;
            }

            child.transform.SetParent(parent.transform, true);
            EditorUtility.SetDirty(child.transform);
            changeLog.Add($"reparented {child.objectId} under {parent.objectId}");
            return true;
        }

        private static bool EnsureTags(Dictionary<string, AIEditableObject> byId, List<string> changeLog)
        {
            var changed = false;
            foreach (var objectId in IntendedObjectIds)
            {
                var editable = FindEditableByObjectIdOrName(byId, objectId);
                if (!editable)
                {
                    continue;
                }

                if (editable.gameObject.tag != RequiredTag)
                {
                    editable.gameObject.tag = RequiredTag;
                    EditorUtility.SetDirty(editable.gameObject);
                    changed = true;
                }
            }

            if (changed)
            {
                changeLog.Add("tagged intended semantic targets with game");
            }

            return changed;
        }

        private static bool EnsureSelectionColliders(Dictionary<string, AIEditableObject> byId, List<string> changeLog)
        {
            var changed = false;
            foreach (var objectId in IntendedObjectIds)
            {
                var editable = FindEditableByObjectIdOrName(byId, objectId);
                if (!editable)
                {
                    continue;
                }

                var preferProxy = ProxyPreferredObjectIds.Contains(objectId);
                if (EnsureSelectionCollider(editable, preferProxy))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                changeLog.Add("added or normalized colliders for selectable scene targets");
            }

            return changed;
        }

        private static bool EnsureSelectionCollider(AIEditableObject editable, bool preferProxy)
        {
            if (!editable || !TryCalculateLocalBounds(editable.transform, out var localBounds))
            {
                return false;
            }

            var changed = false;
            var expandedSize = ExpandSize(localBounds.size, preferProxy ? 0.08f : 0.03f);

            if (preferProxy)
            {
                var proxy = editable.transform.Find(SelectionProxyColliderName);
                if (!proxy)
                {
                    var proxyObject = new GameObject(SelectionProxyColliderName);
                    proxyObject.transform.SetParent(editable.transform, false);
                    proxy = proxyObject.transform;
                    changed = true;
                }

                proxy.localPosition = localBounds.center;
                proxy.localRotation = Quaternion.identity;
                proxy.localScale = Vector3.one;
                proxy.gameObject.tag = RequiredTag;
                proxy.gameObject.layer = editable.gameObject.layer;

                var collider = proxy.GetComponent<BoxCollider>();
                if (!collider)
                {
                    collider = proxy.gameObject.AddComponent<BoxCollider>();
                    changed = true;
                }

                if (collider.center != Vector3.zero || collider.size != expandedSize)
                {
                    collider.center = Vector3.zero;
                    collider.size = expandedSize;
                    changed = true;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(proxy.gameObject);
                    EditorUtility.SetDirty(collider);
                }

                return changed;
            }

            var existingCollider = editable.GetComponent<Collider>();
            if (!existingCollider && editable.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                existingCollider = editable.gameObject.AddComponent<BoxCollider>();
                changed = true;
            }

            if (existingCollider is BoxCollider boxCollider)
            {
                if (boxCollider.center != localBounds.center || boxCollider.size != expandedSize)
                {
                    boxCollider.center = localBounds.center;
                    boxCollider.size = expandedSize;
                    EditorUtility.SetDirty(boxCollider);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool EnsurePlacementAnchors(Dictionary<string, AIEditableObject> byId, List<string> changeLog)
        {
            var changed = false;
            changed |= EnsureSurfaceAnchor(FindEditableByObjectIdOrName(byId, "basket_001"), "basket_inside_anchor", BoundsPlacement.Center);
            changed |= EnsureSurfaceAnchor(FindEditableByObjectIdOrName(byId, "table_001"), "desk_surface_anchor", BoundsPlacement.Top);
            changed |= EnsureSurfaceAnchor(FindEditableByObjectIdOrName(byId, "cabinet_001"), "cabinet_top_anchor", BoundsPlacement.Top);
            changed |= EnsureSurfaceAnchor(FindEditableByObjectIdOrName(byId, "table_drawer_001"), "drawer_inside_anchor", BoundsPlacement.Center);
            changed |= EnsureSurfaceAnchor(FindEditableByObjectIdOrName(byId, "table_drawer_002"), "drawer_inside_anchor", BoundsPlacement.Center);
            changed |= EnsureSurfaceAnchor(FindEditableByObjectIdOrName(byId, "table_drawer_003"), "drawer_inside_anchor", BoundsPlacement.Center);
            changed |= EnsureSurfaceAnchor(FindEditableByObjectIdOrName(byId, "cabinet_drawer_001"), "drawer_inside_anchor", BoundsPlacement.Center);
            changed |= EnsureSurfaceAnchor(FindEditableByObjectIdOrName(byId, "cabinet_drawer_002"), "drawer_inside_anchor", BoundsPlacement.Center);
            changed |= EnsureSurfaceAnchor(FindEditableByObjectIdOrName(byId, "cabinet_drawer_003"), "drawer_inside_anchor", BoundsPlacement.Center);

            if (changed)
            {
                changeLog.Add("added placement anchors for basket, desk, cabinet, and drawers");
            }

            return changed;
        }

        private static bool EnsureSurfaceAnchor(AIEditableObject editable, string anchorName, BoundsPlacement placement)
        {
            if (!editable || !TryCalculateLocalBounds(editable.transform, out var localBounds))
            {
                return false;
            }

            var anchor = editable.transform.Find(anchorName);
            var changed = false;
            if (!anchor)
            {
                var anchorObject = new GameObject(anchorName);
                anchorObject.transform.SetParent(editable.transform, false);
                anchor = anchorObject.transform;
                changed = true;
            }

            var localPosition = placement == BoundsPlacement.Top
                ? localBounds.center + new Vector3(0f, localBounds.extents.y + 0.02f, 0f)
                : localBounds.center;

            if (anchor.localPosition != localPosition || anchor.localRotation != Quaternion.identity || anchor.localScale != Vector3.one)
            {
                anchor.localPosition = localPosition;
                anchor.localRotation = Quaternion.identity;
                anchor.localScale = Vector3.one;
                EditorUtility.SetDirty(anchor.gameObject);
                changed = true;
            }

            return changed;
        }

        private static string BuildReport(Dictionary<string, AIEditableObject> byId)
        {
            var sb = new StringBuilder();
            var editables = byId.Values.OrderBy(editable => editable.objectId, StringComparer.Ordinal).ToList();
            var missingTargets = IntendedObjectIds.Where(id => FindEditableByObjectIdOrName(byId, id) == null).ToList();
            var missingGameTag = new List<string>();
            var colliderTagIssues = new List<string>();
            var selectionIssues = new List<string>();

            sb.AppendLine("# Escape Room Scene Snapshot After Fixes");
            sb.AppendLine();
            sb.AppendLine($"Scene: `{ScenePath}`");
            sb.AppendLine("Selection convention: `SelectObjectRay` and `InteractionContextProvider` now resolve `GetComponentInParent<AIEditableObject>()` and require `game` on either the hit collider object or the resolved semantic root.");
            sb.AppendLine("Current selection rule requires `game`: yes.");
            sb.AppendLine();

            sb.AppendLine("## AIEditableObject Inventory");
            sb.AppendLine();
            foreach (var editable in editables)
            {
                var colliders = editable.GetComponentsInChildren<Collider>(true);
                var rootHasGameTag = editable.gameObject.CompareTag(RequiredTag);
                if (!rootHasGameTag && IntendedObjectIds.Contains(editable.objectId))
                {
                    missingGameTag.Add(editable.objectId);
                }

                var hasSelectableCollider = colliders.Any(c => c && (c.gameObject.CompareTag(RequiredTag) || rootHasGameTag));
                if (IntendedObjectIds.Contains(editable.objectId) && !hasSelectableCollider)
                {
                    selectionIssues.Add($"{editable.objectId}: no collider path compatible with current game-tag selection rule");
                }

                foreach (var collider in colliders)
                {
                    if (!collider)
                    {
                        continue;
                    }

                    if (!collider.gameObject.CompareTag(RequiredTag) && !rootHasGameTag)
                    {
                        colliderTagIssues.Add($"{editable.objectId}: collider `{collider.gameObject.name}` is not tagged game and root is not tagged game");
                    }
                }

                sb.AppendLine($"### `{editable.objectId}`");
                sb.AppendLine($"- Unity name: `{editable.gameObject.name}`");
                sb.AppendLine($"- Display name: `{editable.displayName}`");
                sb.AppendLine($"- Root tag: `{editable.gameObject.tag}`");
                sb.AppendLine($"- Parent: `{(editable.transform.parent ? editable.transform.parent.name : "<scene-root>")}`");
                sb.AppendLine($"- Colliders: {(colliders.Length == 0 ? "none" : string.Join(", ", colliders.Select(ColliderSummary)))}");
                sb.AppendLine($"- Labels: {(editable.labels == null || editable.labels.Length == 0 ? "(none)" : string.Join(", ", editable.labels))}");
                sb.AppendLine($"- Description: {editable.description}");
                sb.AppendLine($"- Selectable under current rules: {(hasSelectableCollider ? "yes" : "no")}");
                sb.AppendLine();
            }

            sb.AppendLine("## Intended Hierarchy");
            sb.AppendLine();
            AppendHierarchyLine(sb, byId, "door_001");
            AppendHierarchyLine(sb, byId, "lock_001");
            AppendHierarchyLine(sb, byId, "table_001");
            AppendHierarchyLine(sb, byId, "table_drawer_001");
            AppendHierarchyLine(sb, byId, "table_drawer_002");
            AppendHierarchyLine(sb, byId, "lock_002");
            AppendHierarchyLine(sb, byId, "table_drawer_003");
            AppendHierarchyLine(sb, byId, "cabinet_001");
            AppendHierarchyLine(sb, byId, "cabinet_drawer_001");
            AppendHierarchyLine(sb, byId, "key_002");
            AppendHierarchyLine(sb, byId, "cabinet_drawer_002");
            AppendHierarchyLine(sb, byId, "lock_003");
            AppendHierarchyLine(sb, byId, "cabinet_drawer_003");
            AppendHierarchyLine(sb, byId, "key_001");
            AppendHierarchyLine(sb, byId, "clue_note_001");
            AppendHierarchyLine(sb, byId, "clue_note_002");
            AppendHierarchyLine(sb, byId, "basket_001");
            AppendHierarchyLine(sb, byId, "painting_001");
            sb.AppendLine();

            sb.AppendLine("## Selection Readiness Issues Remaining");
            sb.AppendLine();
            if (missingTargets.Count == 0 && missingGameTag.Count == 0 && colliderTagIssues.Count == 0 && selectionIssues.Count == 0)
            {
                sb.AppendLine("- None detected by the repair tool.");
            }
            else
            {
                foreach (var line in missingTargets.Select(id => $"- Missing intended AIEditableObject: `{id}`"))
                {
                    sb.AppendLine(line);
                }

                foreach (var line in missingGameTag.Select(id => $"- Missing tag game on intended root: `{id}`"))
                {
                    sb.AppendLine(line);
                }

                foreach (var line in colliderTagIssues.Select(issue => $"- {issue}"))
                {
                    sb.AppendLine(line);
                }

                foreach (var line in selectionIssues.Select(issue => $"- {issue}"))
                {
                    sb.AppendLine(line);
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Runtime Setup Status");
            sb.AppendLine();
            sb.AppendLine("- `SelectObjectRay`: resolves semantic parents with `GetComponentInParent<AIEditableObject>()`, sorts `RaycastAll` hits by distance, and accepts the hit if either the collider or the resolved semantic root is tagged `game`.");
            sb.AppendLine("- `InteractionContextProvider`: mirrors the same semantic resolution rule for pointer snapshots.");
            sb.AppendLine("- `SceneRegistry`: resolves collider hits to semantic parents with `GetComponentInParent<AIEditableObject>()`.");

            return sb.ToString();
        }

        private static void AppendHierarchyLine(StringBuilder sb, Dictionary<string, AIEditableObject> byId, string objectId)
        {
            var editable = FindEditableByObjectIdOrName(byId, objectId);
            if (!editable)
            {
                sb.AppendLine($"- `{objectId}`: missing");
                return;
            }

            sb.AppendLine($"- `{objectId}` -> parent `{(editable.transform.parent ? editable.transform.parent.name : "<scene-root>")}`");
        }

        private static string ColliderSummary(Collider collider)
        {
            return $"`{collider.gameObject.name}` ({collider.GetType().Name}, tag={collider.gameObject.tag})";
        }

        private static AIEditableObject FindEditableByObjectIdOrName(Dictionary<string, AIEditableObject> byId, string key)
        {
            if (byId.TryGetValue(key, out var editable))
            {
                return editable;
            }

            return byId.Values.FirstOrDefault(item => item && string.Equals(item.gameObject.name, key, StringComparison.Ordinal));
        }

        private static bool LabelsMatch(string[] current, IReadOnlyList<string> expected)
        {
            if (current == null)
            {
                return expected.Count == 0;
            }

            if (current.Length != expected.Count)
            {
                return false;
            }

            for (var i = 0; i < current.Length; i++)
            {
                if (!string.Equals(current[i], expected[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryCalculateLocalBounds(Transform root, out Bounds localBounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                localBounds = default;
                return false;
            }

            var firstBounds = TransformBoundsToLocal(root, renderers[0].bounds);
            localBounds = firstBounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                localBounds.Encapsulate(TransformBoundsToLocal(root, renderers[i].bounds));
            }

            return true;
        }

        private static Bounds TransformBoundsToLocal(Transform root, Bounds worldBounds)
        {
            var corners = new[]
            {
                new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.min.z),
                new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.max.z),
                new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.min.z),
                new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.max.z),
                new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.min.z),
                new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.max.z),
                new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.min.z),
                new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.max.z)
            };

            var localBounds = new Bounds(root.InverseTransformPoint(corners[0]), Vector3.zero);
            for (var i = 1; i < corners.Length; i++)
            {
                localBounds.Encapsulate(root.InverseTransformPoint(corners[i]));
            }

            return localBounds;
        }

        private static Vector3 ExpandSize(Vector3 size, float margin)
        {
            return new Vector3(
                Mathf.Max(size.x + margin, 0.05f),
                Mathf.Max(size.y + margin, 0.05f),
                Mathf.Max(size.z + margin, 0.05f));
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return Path.Combine(projectRoot ?? string.Empty, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private sealed class MetadataSpec
        {
            public MetadataSpec(string displayName, string description, params string[] labels)
            {
                DisplayName = displayName;
                Description = description;
                Labels = labels;
            }

            public string DisplayName { get; }
            public string Description { get; }
            public IReadOnlyList<string> Labels { get; }
        }

        private enum BoundsPlacement
        {
            Center,
            Top
        }
    }
}
