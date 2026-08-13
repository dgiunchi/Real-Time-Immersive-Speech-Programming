using UnityEngine;

namespace AgenticCache
{
    // Authorable "inert anchor" for the implicit/proactive study tasks
    // (docs/code-implicit-proactive-showcase-2026-08-13.md §3): an object that
    // exists in the scene from load - with a stable ID - but visibly does
    // NOTHING until the agentic system decides, from context, what function it
    // should gain (or what child object should appear on it).
    //
    // The public fields below are serialized into the focus/halo scene state by
    // AgenticSceneRegistry (public simple fields of every component are
    // published), so the backend's Scene Analyst SEES the role and description
    // as raw environmental context. Keep them descriptive of what the object
    // IS, never of what function to add - the agents choosing the function is
    // the study's variability requirement.
    [DisallowMultipleComponent]
    public sealed class AgenticInertAnchor : MonoBehaviour
    {
        [Tooltip("What this object is, e.g. 'unlit-lamp', 'empty-pedestal', 'dormant-guide-marker'. Describes the object, never the function to add.")]
        public string anchorRole = "inert-anchor";

        [Tooltip("Optional free-text description of the object and its surroundings, published to Shared XR Memory as context.")]
        public string description = "";

        private void Awake()
        {
            // Anchors must be discoverable by AgenticSceneRegistry (tag scan) and
            // carry a stable ID from scene load, so implicit triggers and
            // speculative preparation can reference them before any interaction.
            if (!gameObject.CompareTag("game"))
            {
                try { gameObject.tag = "game"; }
                catch (UnityException) { Debug.LogWarning("[AgenticXR] 'game' tag is missing from the project; AgenticInertAnchor '" + name + "' will not be scene-registered."); }
            }
            var stable = GetComponent<StableObjectId>() ?? gameObject.AddComponent<StableObjectId>();
            stable.EnsureInitialized();
        }
    }
}
