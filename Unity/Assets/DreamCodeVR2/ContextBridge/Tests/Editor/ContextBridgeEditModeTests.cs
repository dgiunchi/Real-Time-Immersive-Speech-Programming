using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace DreamCodeVR2.ContextBridge.Tests
{
    public class ContextBridgeEditModeTests
    {
        [Test]
        public void SnapshotSerializationIncludesNullActionFields()
        {
            var snapshot = new InteractionContextSnapshot
            {
                peer = "00000000-0000-0000-0000-000000000000",
                timestamp_unix_ms = 1,
                scene_version = 0,
                active_selection = null,
                pointed_object = null,
                pointed_world_position = null,
                last_action = null,
                pending_confirmation = null
            };

            var json = JsonConvert.SerializeObject(snapshot, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include
            });

            Assert.That(json, Does.Contain("\"type\":\"InteractionContextUpdate\""));
            Assert.That(json, Does.Contain("\"last_action\":null"));
            Assert.That(json, Does.Contain("\"pending_confirmation\":null"));
        }

        [Test]
        public void SceneRegistryResolvesEditableObjectSummary()
        {
            var registryObject = new GameObject("Registry");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

            try
            {
                var registry = registryObject.AddComponent<SceneRegistry>();
                registry.allowFallbackSummaries = false;

                var editable = cube.AddComponent<AIEditableObject>();
                editable.objectId = "cube_001";
                editable.displayName = "Demo Cube";
                editable.labels = new[] { "cube", "demo_object" };
                editable.editable = true;

                registry.Register(editable);

                Assert.That(registry.TryGetSummary(cube, out var summary), Is.True);
                Assert.That(summary.id, Is.EqualTo("cube_001"));
                Assert.That(summary.display_name, Is.EqualTo("Demo Cube"));
                Assert.That(summary.editable, Is.True);
                Assert.That(summary.source, Is.EqualTo("AIEditableObject"));
            }
            finally
            {
                Object.DestroyImmediate(cube);
                Object.DestroyImmediate(registryObject);
            }
        }
    }
}
