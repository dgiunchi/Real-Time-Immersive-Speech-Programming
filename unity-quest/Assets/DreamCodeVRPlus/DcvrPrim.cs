// DreamCodeVR+ — collider-free primitives.
//
// GameObject.CreatePrimitive always attaches a Collider. Nothing in this scene is
// simulated, so every one of them was created only to be destroyed on the next line —
// and on the Quest build the physics collider classes are stripped out entirely, so each
// call logged "Can't add component because class 'MeshCollider' doesn't exist!" and threw.
// A hundred of those at startup buries the log lines that actually matter.
//
// Building straight from the builtin meshes skips the collider, the exception and the
// immediate destroy.

using UnityEngine;

namespace DreamCodeVRPlus
{
    public static class DcvrPrim
    {
        public static GameObject Create(PrimitiveType type, string name = null)
        {
            var go = new GameObject(name ?? type.ToString());
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = MeshFor(type);
            var mr = go.AddComponent<MeshRenderer>();
            // Nothing here casts or receives shadows: the scene is lit by emission and a
            // single shadowless directional, so shadow work would be pure cost.
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        private static Mesh MeshFor(PrimitiveType type)
        {
            string res = type switch
            {
                PrimitiveType.Cube => "Cube.fbx",
                PrimitiveType.Sphere => "Sphere.fbx",
                PrimitiveType.Capsule => "Capsule.fbx",
                PrimitiveType.Cylinder => "Cylinder.fbx",
                PrimitiveType.Plane => "Plane.fbx",
                _ => "Quad.fbx",
            };
            return Resources.GetBuiltinResource<Mesh>(res);
        }
    }
}
