using Newtonsoft.Json.Linq;
using Ubiq.Messaging;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Drop-in replacement for the original CodeGenerationManager on NetworkId 94.
    /// Receives the backend's BackendDecision JSON and routes it to the SAFE
    /// <see cref="ActionPlanExecutor"/> (allow-listed behaviours) instead of
    /// runtime-compiling C#. Registration mirrors CodeGenerationManager exactly so
    /// it slots into the existing scene.
    ///
    /// IMPORTANT: do not have BOTH CodeGenerationManager and this bridge registered
    /// on NID 94 at once — disable/remove CodeGenerationManager and add this.
    /// </summary>
    public class ActionPlanNetworkBridge : MonoBehaviour
    {
        // Identical to CodeGenerationManager.networkId (NID 94 backend output).
        public NetworkId networkId = new NetworkId(94);
        private NetworkContext context;

        [Tooltip("The safe executor (allow-listed actions). Required.")]
        public ActionPlanExecutor executor;

        [Tooltip("Live selected object source (the existing SelectRay). Optional.")]
        public SelectRay selectRay;

        [Tooltip("Explicit target; falls back to selection when null.")]
        public GameObject targetObject;

        [Tooltip("scene_root target (mirrors CodeGenerationManager.sceneController).")]
        public GameObject sceneController;

        [Tooltip("This client's own peer id. A decision is applied ONLY if the " +
                 "backend's target_peer is null/empty OR equals this id. Defends " +
                 "SOC-01 / NET-03 (global-broadcast cross-peer attack).")]
        public string ownPeerId;

        private void Start()
        {
            context = NetworkScene.Register(this, networkId);
        }

        // Ubiq delivers NID 94 frames here. The body is the raw UTF-8 BackendDecision JSON.
        public void ProcessMessage(ReferenceCountedSceneGraphMessage data)
        {
            string json = data.ToString();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }
            if (executor == null)
            {
                Debug.LogWarning("[ActionPlanNetworkBridge] no ActionPlanExecutor assigned; ignoring.");
                return;
            }

            // SOC-01 / NET-03 (global-broadcast cross-peer attack): the backend output
            // on NID 94 reaches EVERY peer in the room. A decision may carry an optional
            // "target_peer" routing field. Apply it ONLY if target_peer is null/empty OR
            // equals THIS client's own peer id — otherwise another peer's decision could
            // mutate our scene. Parse defensively; malformed JSON is handled by Execute.
            if (!IsForThisPeer(json))
            {
                Debug.Log("[ActionPlanNetworkBridge] NID94 decision targeted another peer; ignoring.");
                return;
            }

            GameObject selected = (selectRay != null ? selectRay.selectedObject : null);
            if (selected == null)
            {
                selected = targetObject;
            }

            // ActionPlanExecutor.Execute parses the {action_plan|raw} envelope with
            // Newtonsoft and fail-safes on bad/oversized/missing-target input. It never
            // compiles code. Safety lives entirely in the executor + SafeBehaviourRegistry.
            bool ok = executor.Execute(json, selected, sceneController);
            Debug.Log($"[ActionPlanNetworkBridge] NID94 -> ActionPlanExecutor ok={ok}");
        }

        /// <summary>
        /// SOC-01 / NET-03: true if this decision is addressed to us. A decision with
        /// no/empty target_peer is broadcast-to-all (legacy behaviour). A decision with
        /// a target_peer is applied ONLY by the matching client. Fail-open ONLY on
        /// unparseable JSON (the executor then rejects it anyway as bad input).
        /// </summary>
        private bool IsForThisPeer(string json)
        {
            string targetPeer;
            try
            {
                JObject root = JObject.Parse(json);
                // routing field may live on the envelope or inside action_plan.
                JToken tok = root["target_peer"] ?? (root["action_plan"] as JObject)?["target_peer"];
                targetPeer = tok != null && tok.Type == JTokenType.String ? (string)tok : null;
            }
            catch (System.Exception)
            {
                return true; // let Execute reject malformed JSON via its own fail-safe.
            }

            if (string.IsNullOrEmpty(targetPeer))
            {
                return true; // broadcast-to-all decision.
            }
            // Targeted: apply only if it is for us.
            return !string.IsNullOrEmpty(ownPeerId) && targetPeer == ownPeerId;
        }
    }
}
