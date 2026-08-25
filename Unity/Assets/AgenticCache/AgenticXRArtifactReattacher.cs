using System;
using System.Collections.Generic;
using UnityEngine;

namespace AgenticCache
{
    /// <summary>
    /// Restores committed artifacts onto their objects after a scene reset.
    ///
    /// The server side keeps the C# of each committed artifact and a checkpoint
    /// now separates three cases: reattachable (the target survived and a source
    /// was captured), unreattachable (the target survived but no source exists),
    /// and orphaned (the target is gone). This component consumes the first case
    /// and recompiles those artifacts onto their objects.
    ///
    /// It deliberately does not decide anything. Whether an artifact should come
    /// back is the checkpoint's judgement; this only carries it out and reports
    /// what happened, so a restore that silently half-succeeds is visible rather
    /// than assumed.
    /// </summary>
    [DisallowMultipleComponent]
    public class AgenticXRArtifactReattacher : MonoBehaviour
    {
        private const string Tag = "[AgenticXRArtifactReattacher]";

        [Serializable]
        public class ReattachableArtifact
        {
            public string artifactId;
            public string targetObjectId;
            public string source;
            public string artifactVersion;
        }

        [Serializable]
        public class ReattachOutcome
        {
            public string artifactId;
            public string targetObjectId;
            public bool reattached;
            public string error;
        }

        public AgenticRuntimeCompiler compiler;
        public AgenticSceneRegistry registry;

        /// <summary>
        /// Reattaches each supplied artifact, returning one outcome per entry.
        /// Never throws for a single failure: one artifact that will not compile
        /// must not prevent the rest of the scene being restored, and a partial
        /// restore has to be legible afterwards rather than silent.
        /// </summary>
        public List<ReattachOutcome> Reattach(IEnumerable<ReattachableArtifact> artifacts)
        {
            var outcomes = new List<ReattachOutcome>();
            if (artifacts == null) return outcomes;

            foreach (var artifact in artifacts)
            {
                var outcome = new ReattachOutcome
                {
                    artifactId = artifact != null ? artifact.artifactId : null,
                    targetObjectId = artifact != null ? artifact.targetObjectId : null,
                    reattached = false,
                };

                if (artifact == null || string.IsNullOrWhiteSpace(artifact.targetObjectId))
                {
                    outcome.error = "entry has no targetObjectId";
                    outcomes.Add(outcome);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(artifact.source))
                {
                    // The checkpoint should not have offered this as reattachable.
                    // Reported rather than skipped, so the disagreement is visible.
                    outcome.error = "entry carries no source, so it was not reattachable";
                    outcomes.Add(outcome);
                    continue;
                }
                if (compiler == null)
                {
                    outcome.error = "no AgenticRuntimeCompiler is assigned";
                    outcomes.Add(outcome);
                    continue;
                }

                var target = ResolveTarget(artifact.targetObjectId);
                if (target == null)
                {
                    // The scene changed between the checkpoint being classified
                    // and this restore running. That is an orphan, not a failure
                    // of the artifact.
                    outcome.error = $"target '{artifact.targetObjectId}' is not present in the current scene";
                    outcomes.Add(outcome);
                    continue;
                }

                try
                {
                    outcome.reattached = compiler.TryCompileAndAttach(target, artifact.source, out _, out var error);
                    if (!outcome.reattached) outcome.error = error;
                }
                catch (Exception exception)
                {
                    outcome.error = exception.Message;
                }

                Debug.Log(outcome.reattached
                    ? $"{Tag} reattached {artifact.artifactId} to {artifact.targetObjectId}"
                    : $"{Tag} could not reattach {artifact.artifactId} to {artifact.targetObjectId}: {outcome.error}");
                outcomes.Add(outcome);
            }

            var restored = outcomes.FindAll(item => item.reattached).Count;
            Debug.Log($"{Tag} restored {restored} of {outcomes.Count} artifact(s)");
            return outcomes;
        }

        private GameObject ResolveTarget(string stableObjectId)
        {
            if (registry != null)
            {
                var fromRegistry = registry.Find(stableObjectId);
                if (fromRegistry != null) return fromRegistry;
            }
            // Falls back to a name match so a restore still works in a scene
            // assembled without the registry, for example a smoke test.
            return GameObject.Find(stableObjectId);
        }
    }
}
