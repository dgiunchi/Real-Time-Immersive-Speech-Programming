using NUnit.Framework;
using UnityEngine;

namespace DreamCodeVRPlus.EditorTests
{
    /// <summary>
    /// EditMode tests for the client-side executor. Run inside Unity's Test
    /// Runner (they are not part of the Rust `cargo test` gate).
    /// </summary>
    public sealed class ActionPlanExecutorTests
    {
        private static (ActionPlanExecutor exec, GameObject target) Make()
        {
            var host = new GameObject("exec");
            var exec = host.AddComponent<ActionPlanExecutor>();
            exec.tracker = host.AddComponent<GeneratedObjectTracker>();
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            return (exec, target);
        }

        [Test]
        public void RejectsInvalidJson()
        {
            var (exec, target) = Make();
            Assert.IsFalse(exec.Execute("{ not json", target, target));
        }

        [Test]
        public void RejectsEmptyActions()
        {
            var (exec, target) = Make();
            const string json =
                "{\"schema_version\":\"1.0\",\"request_id\":\"r\",\"target\":\"selected_object\",\"actions\":[]}";
            Assert.IsFalse(exec.Execute(json, target, target));
        }

        [Test]
        public void AcceptsSetColor()
        {
            var (exec, target) = Make();
            const string json =
                "{\"schema_version\":\"1.0\",\"request_id\":\"r\",\"target\":\"selected_object\",\"actions\":[{\"type\":\"set_color\",\"color\":\"#FF0000\"}]}";
            Assert.IsTrue(exec.Execute(json, target, target));
            Assert.AreEqual(Color.red, target.GetComponent<Renderer>().material.color);
        }

        [Test]
        public void ClampsOversizeScaleDefensively()
        {
            var (exec, target) = Make();
            // The Rust backend would REJECT this outright; the client only re-clamps
            // as defence-in-depth if a bad plan ever reaches it.
            const string json =
                "{\"schema_version\":\"1.0\",\"request_id\":\"r\",\"target\":\"selected_object\",\"actions\":[{\"type\":\"set_scale\",\"value\":1000.0}]}";
            exec.Execute(json, target, target);
            Assert.LessOrEqual(target.transform.localScale.x, ProtocolModels.ScaleMax);
        }
    }
}
