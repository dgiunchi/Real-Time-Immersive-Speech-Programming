// DreamCodeVR+ — everything the user creates, and the only place it lives.
//
// THE INVARIANT THIS FILE EXISTS TO ENFORCE
//
//     THE USER MOVES. THE WORLD DOES NOT. CREATIONS DO NOT.
//
// A creation is a thing in a room, not a thing on a visor. The player's pose is used
// ONCE, to choose where a creation appears; from that moment the creation is world-space
// content and nothing about head or body movement may touch it again. That is why this
// root is created at the SCENE ROOT and never under the XR rig, and why `Adopt` re-parents
// with `worldPositionStays: true` — a creation that survives adoption unmoved is the
// difference between placing an object and gluing it to someone's face.
//
// It also holds the registry, because identity and ownership are the same question. A
// person says "remove Saturn"; Unity knows only `Sphere(Clone)(4)`. Bridging that needs a
// name, a group, and a lookup that does not walk the scene graph — and all three have to
// agree, so they live together rather than in three components that can disagree.
//
// Deliberately ONE system (§59). `GeneratedMarker` already carried provenance and now
// carries identity too; this class owns grouping and lookup. There is no second registry.

using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVRPlus
{
    /// <summary>One creative request and everything it produced.</summary>
    public sealed class GenerationGroup
    {
        public int Id;
        public string SemanticName = "";
        public string UserPrompt = "";
        public float Timestamp;
        public Transform Root;
        public readonly List<GameObject> Objects = new List<GameObject>();

        /// <summary>World bounds of everything still alive in the group. Recomputed on
        /// demand — cheap at command time, and never touched per frame.</summary>
        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            for (int i = 0; i < Objects.Count; i++)
            {
                GameObject go = Objects[i];
                if (go == null) { continue; }
                var r = go.GetComponent<Renderer>();
                if (r == null) { continue; }
                if (!any) { bounds = r.bounds; any = true; }
                else { bounds.Encapsulate(r.bounds); }
            }
            return any;
        }
    }

    public sealed class DcvrGeneratedContent : MonoBehaviour
    {
        public static DcvrGeneratedContent Instance { get; private set; }

        private readonly Dictionary<int, GameObject> _byId = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GenerationGroup> _groups = new Dictionary<int, GenerationGroup>();
        private readonly List<GenerationGroup> _groupOrder = new List<GenerationGroup>();

        private int _nextObjectId = 1;
        private int _nextGroupId = 1;

        // --- interaction context (§28). Enough to make "make it bigger" work without a
        // model call, and deliberately no more: this is conversational memory, not state
        // anything is allowed to depend on for safety.
        public GameObject LastReferencedObject { get; private set; }
        public GameObject LastGeneratedObject { get; private set; }
        public GenerationGroup LastGeneratedGroup { get; private set; }
        public GameObject SelectedObject { get; set; }
        public GameObject PointedObject { get; set; }

        public IReadOnlyList<GenerationGroup> Groups => _groupOrder;
        public int ObjectCount => _byId.Count;

        /// <summary>Every live generated object. Small by construction (the session spawn
        /// cap bounds it), and only walked at command time — never per frame.</summary>
        public IEnumerable<GameObject> AllObjects
        {
            get
            {
                foreach (KeyValuePair<int, GameObject> kv in _byId)
                {
                    if (kv.Value != null) { yield return kv.Value; }
                }
            }
        }

        /// <summary>The world-space root. Created at the SCENE ROOT — never under the XR
        /// rig, the camera offset, or a controller — so player movement cannot propagate
        /// into anything the user has made.</summary>
        public static DcvrGeneratedContent Ensure()
        {
            if (Instance != null) { return Instance; }

            GameObject existing = GameObject.Find("GeneratedContent");
            GameObject go = existing != null ? existing : new GameObject("GeneratedContent");
            go.transform.SetParent(null, true);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            Instance = go.GetComponent<DcvrGeneratedContent>() ?? go.AddComponent<DcvrGeneratedContent>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance == null) { Instance = this; }
        }

        // ---- groups ----------------------------------------------------------------

        public GenerationGroup BeginGroup(string userPrompt)
        {
            var group = new GenerationGroup
            {
                Id = _nextGroupId++,
                UserPrompt = userPrompt ?? "",
                SemanticName = DeriveGroupName(userPrompt),
                Timestamp = Time.realtimeSinceStartup,
            };

            var root = new GameObject($"Generation_{group.Id:0000}");
            root.transform.SetParent(transform, false);
            group.Root = root.transform;

            _groups[group.Id] = group;
            _groupOrder.Add(group);
            return group;
        }

        /// <summary>A short human name for the whole creation, taken from the request.
        ///
        /// Strips the imperative opening ("build a", "generate the") so "build a small
        /// castle" becomes "small castle" and "delete the castle" can match it later.
        /// This is naming, not parsing: getting it wrong costs a less convenient handle,
        /// never a wrong deletion, because group deletion still requires the user's words
        /// to match this string.</summary>
        private static string DeriveGroupName(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) { return ""; }
            string s = prompt.Trim().ToLowerInvariant();

            string[] leadingVerbs =
            {
                "please ", "can you ", "could you ",
                "generate ", "create ", "build ", "make ", "add ", "spawn ", "place ", "put ",
            };
            bool stripped = true;
            while (stripped)
            {
                stripped = false;
                foreach (string v in leadingVerbs)
                {
                    if (s.StartsWith(v)) { s = s.Substring(v.Length); stripped = true; break; }
                }
                foreach (string a in new[] { "a ", "an ", "the ", "me ", "us " })
                {
                    if (s.StartsWith(a)) { s = s.Substring(a.Length); stripped = true; break; }
                }
            }

            // Drop trailing detail after a clause break, so "castle with four towers"
            // still answers to "castle".
            int cut = s.IndexOf(" with ", System.StringComparison.Ordinal);
            if (cut > 0) { s = s.Substring(0, cut); }
            cut = s.IndexOf(',');
            if (cut > 0) { s = s.Substring(0, cut); }

            return s.Trim();
        }

        // ---- registration ----------------------------------------------------------

        /// <summary>Take ownership of an object: adopt it into the group, stamp its
        /// identity, and index it.
        ///
        /// `worldPositionStays: true` on the re-parent is load-bearing. The object has
        /// already been placed in world space by the spatial layer; adopting it must move
        /// it into a different branch of the hierarchy WITHOUT moving it in the room.</summary>
        public void Register(GameObject go, GenerationGroup group, string semanticName, string role)
        {
            if (go == null || group == null) { return; }

            if (go.transform.parent != group.Root && !IsDescendantOf(go.transform, group.Root))
            {
                go.transform.SetParent(group.Root, worldPositionStays: true);
            }

            var marker = go.GetComponent<GeneratedMarker>() ?? go.AddComponent<GeneratedMarker>();
            marker.RuntimeId = _nextObjectId++;
            marker.SemanticName = semanticName ?? "";
            marker.GenerationId = group.Id;
            marker.Role = role ?? "";
            marker.CreatedAt = Time.realtimeSinceStartup;

            _byId[marker.RuntimeId] = go;
            group.Objects.Add(go);

            LastGeneratedObject = go;
            LastGeneratedGroup = group;
        }

        /// <summary>Destroy now rather than at the end of the frame where that is legal.
        ///
        /// `Object.Destroy` is deferred, so immediately after a clear the objects are still
        /// present and still findable — the registry is empty while the hierarchy is not.
        /// At runtime that gap closes on its own and nobody notices. In the Editor, where
        /// the invariant tests run, deferred destruction never happens at all, so a
        /// perfectly correct clear looks like a leak and the test cannot tell that apart
        /// from a real one. Destroying immediately outside play mode makes the two agree.</summary>
        private static void DestroyNow(GameObject go)
        {
            if (go == null) { return; }
            if (Application.isPlaying) { Destroy(go); }
            else { DestroyImmediate(go); }
        }

        private static bool IsDescendantOf(Transform t, Transform root)
        {
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                if (p == root) { return true; }
            }
            return false;
        }

        public void NoteReference(GameObject go)
        {
            if (go != null) { LastReferencedObject = go; }
        }

        // ---- resolution (§27) ------------------------------------------------------

        /// <summary>Find what the user meant, in a fixed priority order.
        ///
        /// The order matters and is conservative by design: an explicit name beats what
        /// they happen to be pointing at, and recency is the LAST resort. Resolution must
        /// never be ambiguous in a way that could delete the wrong thing, so a name that
        /// matches several objects returns nothing rather than guessing.</summary>
        public GameObject Resolve(string spokenName)
        {
            // 1. explicit semantic name — exact, then unambiguous prefix/substring
            if (!string.IsNullOrWhiteSpace(spokenName))
            {
                GameObject exact = FindByName(spokenName, exact: true);
                if (exact != null) { return exact; }
                GameObject fuzzy = FindByName(spokenName, exact: false);
                if (fuzzy != null) { return fuzzy; }
                // A name was given and did not resolve. Do NOT silently fall through to
                // "whatever they last touched" — acting on the wrong object is worse than
                // reporting that the named one was not found.
                return null;
            }

            // 2-5. deictic ("this", "it"): pointed, then selected, then last referenced,
            // then the most recent creation.
            if (PointedObject != null) { return PointedObject; }
            if (SelectedObject != null) { return SelectedObject; }
            if (LastReferencedObject != null) { return LastReferencedObject; }
            return LastGeneratedObject;
        }

        /// <summary>Every object whose name matches — "the gate" when the generator built
        /// a Gate Frame, a Gate Arch and two Gate Doors.
        ///
        /// Matching several objects is not the same as being unable to tell which one was
        /// meant. A castle's gate genuinely IS four parts, and "make the gate red" that
        /// colours one door and leaves the rest grey is a worse answer than colouring all
        /// four. So multi-match is treated as a PLURAL, not as an ambiguity — the caller
        /// decides whether that is acceptable for the operation it is performing, which is
        /// where the destructive/non-destructive distinction belongs.</summary>
        public List<GameObject> ResolveAll(string spokenName)
        {
            var hits = new List<GameObject>();
            if (string.IsNullOrWhiteSpace(spokenName))
            {
                GameObject one = Resolve(spokenName);
                if (one != null) { hits.Add(one); }
                return hits;
            }

            string want = Normalize(spokenName);
            if (want.Length == 0) { return hits; }

            // Exact matches win outright: if anything is called exactly "gate", the user
            // meant that and not the four things merely containing the word.
            foreach (GameObject go in AllObjects)
            {
                var m = go.GetComponent<GeneratedMarker>();
                if (m == null || string.IsNullOrEmpty(m.SemanticName)) { continue; }
                if (Normalize(m.SemanticName) == want) { hits.Add(go); }
            }
            if (hits.Count > 0) { return hits; }

            foreach (GameObject go in AllObjects)
            {
                var m = go.GetComponent<GeneratedMarker>();
                if (m == null || string.IsNullOrEmpty(m.SemanticName)) { continue; }
                string have = Normalize(m.SemanticName);
                if (have.Contains(want) || want.Contains(have)) { hits.Add(go); }
            }
            return hits;
        }

        private GameObject FindByName(string spoken, bool exact)
        {
            string want = Normalize(spoken);
            if (want.Length == 0) { return null; }

            GameObject hit = null;
            int hits = 0;
            foreach (KeyValuePair<int, GameObject> kv in _byId)
            {
                GameObject go = kv.Value;
                if (go == null) { continue; }
                var m = go.GetComponent<GeneratedMarker>();
                if (m == null || string.IsNullOrEmpty(m.SemanticName)) { continue; }

                string have = Normalize(m.SemanticName);
                bool match = exact
                    ? have == want
                    : have.Contains(want) || want.Contains(have);
                if (!match) { continue; }

                hit = go;
                hits++;
                if (hits > 1) { return null; }   // ambiguous: refuse rather than guess
            }
            return hit;
        }

        /// <summary>Resolve a whole creation: "the castle", "the solar system".</summary>
        public GenerationGroup ResolveGroup(string spokenName)
        {
            if (string.IsNullOrWhiteSpace(spokenName)) { return LastGeneratedGroup; }
            string want = Normalize(spokenName);
            if (want.Length == 0) { return LastGeneratedGroup; }

            GenerationGroup hit = null;
            int hits = 0;
            // Newest first, so re-using a name addresses the one just made.
            for (int i = _groupOrder.Count - 1; i >= 0; i--)
            {
                GenerationGroup g = _groupOrder[i];
                string have = Normalize(g.SemanticName);
                if (have.Length == 0) { continue; }
                if (have == want) { return g; }
                if (have.Contains(want) || want.Contains(have))
                {
                    hit = g;
                    hits++;
                }
            }
            return hits == 1 ? hit : null;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) { sb.Append(c); }
                else if (sb.Length > 0 && sb[sb.Length - 1] != ' ') { sb.Append(' '); }
            }
            return sb.ToString().Trim();
        }

        // ---- deletion (§20, §21, §54) ----------------------------------------------

        /// <summary>Delete one object and everything under it, leaving no stale state.</summary>
        public bool DeleteObject(GameObject go)
        {
            if (go == null) { return false; }

            var m = go.GetComponent<GeneratedMarker>();
            if (m != null)
            {
                _byId.Remove(m.RuntimeId);
                if (_groups.TryGetValue(m.GenerationId, out GenerationGroup g))
                {
                    g.Objects.Remove(go);
                }
            }

            // Children go with it, so their registry entries must go too — otherwise a
            // later "delete Saturn" resolves to a destroyed object and silently does
            // nothing, which reads as the command being ignored.
            foreach (GeneratedMarker child in go.GetComponentsInChildren<GeneratedMarker>(true))
            {
                if (child == null || child.gameObject == go) { continue; }
                _byId.Remove(child.RuntimeId);
                if (_groups.TryGetValue(child.GenerationId, out GenerationGroup cg))
                {
                    cg.Objects.Remove(child.gameObject);
                }
            }

            ForgetIfMatches(go);
            DestroyNow(go);
            return true;
        }

        public bool DeleteGroup(GenerationGroup group)
        {
            if (group == null) { return false; }

            for (int i = group.Objects.Count - 1; i >= 0; i--)
            {
                GameObject go = group.Objects[i];
                if (go == null) { continue; }
                var m = go.GetComponent<GeneratedMarker>();
                if (m != null) { _byId.Remove(m.RuntimeId); }
                ForgetIfMatches(go);
                DestroyNow(go);
            }
            group.Objects.Clear();

            if (group.Root != null) { DestroyNow(group.Root.gameObject); }
            _groups.Remove(group.Id);
            _groupOrder.Remove(group);

            if (LastGeneratedGroup == group) { LastGeneratedGroup = null; }
            DcvrSpatialCompositor.Instance?.ReleaseSlot(group.Id);
            return true;
        }

        /// <summary>"Clear everything" — every creation, and nothing else (§22, §55).
        ///
        /// Scoped by hierarchy rather than by name-matching. Destroying the children of
        /// this root cannot reach the rig, the environment, the HUD or the backend client,
        /// because none of them are under it — which is a much stronger guarantee than a
        /// deny-list of protected names that someone must remember to update.</summary>
        public int ClearAll()
        {
            int n = 0;
            for (int i = _groupOrder.Count - 1; i >= 0; i--)
            {
                n += _groupOrder[i].Objects.Count;
                DeleteGroup(_groupOrder[i]);
            }

            // Anything adopted without a group (defensive; should be empty).
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyNow(transform.GetChild(i).gameObject);
            }

            _byId.Clear();
            _groups.Clear();
            _groupOrder.Clear();
            LastReferencedObject = null;
            LastGeneratedObject = null;
            LastGeneratedGroup = null;
            SelectedObject = null;
            PointedObject = null;
            DcvrSpatialCompositor.Instance?.ReleaseAllSlots();
            // Materials are cached across creations, so a cleared scene should release them
            // too — otherwise a long session accumulates materials for content that no
            // longer exists.
            DcvrMaterialSystem.ClearCache();
            return n;
        }

        private void ForgetIfMatches(GameObject go)
        {
            if (LastReferencedObject == go) { LastReferencedObject = null; }
            if (LastGeneratedObject == go) { LastGeneratedObject = null; }
            if (SelectedObject == go) { SelectedObject = null; }
            if (PointedObject == go) { PointedObject = null; }
        }

        /// <summary>Objects with no group yet — used by the capture pass to find what a
        /// generated script created behind the system's back.</summary>
        public bool IsRegistered(GameObject go)
        {
            if (go == null) { return false; }
            var m = go.GetComponent<GeneratedMarker>();
            return m != null && m.RuntimeId != 0 && _byId.ContainsKey(m.RuntimeId);
        }
    }
}
