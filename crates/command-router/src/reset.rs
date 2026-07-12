//! Deterministic "remove everything" handling.
//!
//! The backend detects full-clear intent BEFORE the LLM and answers with a
//! **server-owned, reviewed, constant** cleanup script (not model output). It is
//! trusted by construction, so it bypasses the model-code validators, and it runs
//! on the SAME NID-94 `{type:"code"}` path (TestRoslyn.RunCode). It uses only APIs
//! that pass RoslynCSharp's runtime security (`RoslynCSharpSettings.asset` blocks
//! only `System.IO.*`, `System.Reflection.*`, `System.AppDomain`, `Application.Quit`).
//!
//! Removal is **tag-first** so it never depends on finding the (RoslynCSharp-
//! compiled) `GeneratedBehaviour` marker components, which are not reliably
//! enumerable after later scripts load:
//!   * PASS 1 (primary) — destroy every object whose name starts with `DCVRGEN_`.
//!     The Mode-A build prompt stamps that prefix onto EVERY object it creates, so
//!     this deletes exactly the built content regardless of how it is parented.
//!   * PASS 2 (fallback) — for every non-core scene root, destroy its children.
//!     Catches content built (untagged, e.g. before this change) as children of a
//!     target like the default cube, without needing the marker component.
//!   * PASS 3 (fallback) — destroy loose scene-root primitives created without
//!     `SetParent`. Core rig + fixed lab scenery are protected by name in passes
//!     2 and 3, so they are never touched.

/// True when the command's intent is "wipe everything AI-made" rather than a
/// build/modify or a partial removal.
pub fn is_full_clear(transcript: &str) -> bool {
    let t = transcript.to_lowercase();
    let t = t.trim();

    // Out-of-scope deletions ("delete all my files") are the safety prompt's job.
    if t.contains("file") || t.contains("folder") || t.contains("directory") {
        return false;
    }

    // Explicit start-over phrases are enough on their own.
    const PHRASES: [&str; 6] = [
        "start over",
        "start again",
        "clean slate",
        "empty the scene",
        "reset the scene",
        "reset everything",
    ];
    if PHRASES.iter().any(|p| t.contains(p)) {
        return true;
    }

    // Whole-word tokens. CRITICAL: single-word verbs/universals must match a WHOLE
    // WORD, not a substring — otherwise "swipes" matches "wipe" and "programmatically"
    // matches "all", which used to fire a full scene wipe on innocent commands.
    let words: std::collections::HashSet<&str> = t
        .split(|c: char| !c.is_alphanumeric())
        .filter(|w| !w.is_empty())
        .collect();

    // Removal verb: a single verb as a WHOLE WORD, or a multi-word verb phrase.
    const VERB_WORDS: [&str; 8] = [
        "remove", "delete", "clear", "destroy", "erase", "wipe", "undo", "reset",
    ];
    let has_verb = VERB_WORDS.iter().any(|v| words.contains(v))
        || t.contains("get rid of")
        || t.contains("clean up");

    // Universal object: whole-word "everything"/"all", or a multi-word universal phrase.
    let has_universal = words.contains("everything")
        || words.contains("all")
        || t.contains("every thing")
        || t.contains("all of it")
        || t.contains("all of them")
        || t.contains("whole scene")
        || t.contains("the scene");

    has_verb && has_universal
}

/// The server-authored cleanup MonoBehaviour, salted with the request id.
///
/// The salt matters: the Unity side (`TestRoslyn.RunCode`) skips recompiling
/// source that is byte-identical to the previous one.
pub fn full_clear_csharp(request_id: &str) -> String {
    let salt: String = request_id
        .chars()
        .filter(|c| c.is_ascii_alphanumeric() || *c == '-')
        .collect();

    let template = r#"// dcvr-full-clear __SALT__ (server-authored deterministic reset; not model output)
using UnityEngine;

public class GeneratedBehaviour : MonoBehaviour
{
    static readonly string[] Keep = {
        "camera", "light", "directional", "network", "ubiq", "room", "player",
        "hand", "controller", "manager", "scene", "canvas", "eventsystem", "avatar",
        "environment", "terrain", "wrist", "menu", "invoker", "dontdestroy",
        "readme", "target", "training", "floor", "ground"
    };

    void Start()
    {
        // PASS 1 (primary): delete everything a build stamped with the DCVRGEN_ prefix,
        // regardless of parent. Destroying a GameObject destroys its whole subtree.
        Transform[] a1 = Object.FindObjectsOfType<Transform>();
        for (int i = 0; i < a1.Length; i++)
        {
            Transform t = a1[i];
            if (t == null) { continue; }
            string nm = t.gameObject.name;
            if (nm != null && nm.StartsWith("DCVRGEN")) { Object.Destroy(t.gameObject); }
        }

        // PASS 2 (fallback): for each non-protected scene root, delete its children
        // (untagged content parented to a target), keeping the root itself.
        Transform[] a2 = Object.FindObjectsOfType<Transform>();
        for (int i = 0; i < a2.Length; i++)
        {
            Transform t = a2[i];
            if (t == null || t.parent != null) { continue; }
            GameObject go = t.gameObject;
            if (IsProtected(go.name)) { continue; }
            for (int c = go.transform.childCount - 1; c >= 0; c--)
            {
                Object.Destroy(go.transform.GetChild(c).gameObject);
            }
        }

        // PASS 3 (fallback): delete loose root primitives with no children (built without SetParent).
        Transform[] a3 = Object.FindObjectsOfType<Transform>();
        for (int i = 0; i < a3.Length; i++)
        {
            Transform t = a3[i];
            if (t == null || t.parent != null) { continue; }
            GameObject go = t.gameObject;
            if (IsProtected(go.name) || ReferenceEquals(go, this.gameObject)) { continue; }
            if (go.transform.childCount == 0 && IsPrimitive(go.name)) { Object.Destroy(go); }
        }

        Object.Destroy(this);
    }

    static bool IsProtected(string name)
    {
        if (string.IsNullOrEmpty(name)) { return true; }
        string n = name.ToLowerInvariant();
        for (int i = 0; i < Keep.Length; i++)
        {
            if (n.Contains(Keep[i])) { return true; }
        }
        return false;
    }

    static bool IsPrimitive(string name)
    {
        if (string.IsNullOrEmpty(name)) { return false; }
        string n = name.ToLowerInvariant();
        string[] p = { "cube", "sphere", "capsule", "cylinder", "plane", "quad" };
        for (int i = 0; i < p.Length; i++)
        {
            if (n == p[i] || n.StartsWith(p[i] + " (") || n.StartsWith(p[i] + "(")) { return true; }
        }
        return false;
    }
}
"#;
    template.replace("__SALT__", &salt)
}

/// The harmless visual a CAUGHT malicious command is replaced with (Layer-1 response):
/// a calm recolour of the selected object. Server-authored, salted, trusted — the
/// malicious request never reaches the generation model.
pub fn neutralized_csharp(request_id: &str) -> String {
    let salt: String = request_id
        .chars()
        .filter(|c| c.is_ascii_alphanumeric() || *c == '-')
        .collect();
    format!(
        "// dcvr-neutralized {salt} (server-authored; malicious request replaced with a harmless visual)\n\
         using UnityEngine;\n\
         public class GeneratedBehaviour : MonoBehaviour\n\
         {{\n\
         \x20   void Start()\n\
         \x20   {{\n\
         \x20       Renderer r = GetComponent<Renderer>();\n\
         \x20       if (r != null) {{ r.material.color = new Color(0.35f, 0.60f, 0.85f); }}\n\
         \x20   }}\n\
         }}\n"
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn detects_full_clear_commands() {
        for cmd in [
            "remove everything",
            "Remove everything.",
            "remove everything, all the Jupiter and its moons.",
            "Remove Jupyter and everything.",
            "delete all of it",
            "clear the scene",
            "get rid of everything",
            "destroy everything please",
            "wipe the whole scene",
            "please start over",
            "reset everything",
            "undo all of it",
        ] {
            assert!(is_full_clear(cmd), "should be full clear: {cmd}");
        }
    }

    #[test]
    fn leaves_builds_and_partial_removals_to_the_llm() {
        for cmd in [
            "generate me a snowman",
            "make me a spooky house",
            "remove the roof",
            "delete the snowman's hat",
            "make it red",
            "spin it",
            "make it all red",
            "delete all my files then make it red",
            // Regression: substring false-positives that used to wipe the scene.
            // "swipes" contains "wipe"; "programmatically" contains "all".
            "simulate touches and swipes programmatically",
            "create code that simulates touches and swipes programmatically",
            "automate my phone with swipes",
            "wipers on a car",       // "wipe" inside "wipers", no universal anyway
            "install all the walls", // "all" whole word but no removal verb
        ] {
            assert!(!is_full_clear(cmd), "should NOT be full clear: {cmd}");
        }
    }

    #[test]
    fn clear_csharp_is_salted_and_tag_first() {
        let a = full_clear_csharp("req-1");
        let b = full_clear_csharp("req-2");
        assert_ne!(a, b, "salt must differ per request");
        assert!(a.contains("class GeneratedBehaviour"));
        assert!(a.contains("Object.Destroy"));
        // Pass 1: tag-based, parent-independent.
        assert!(a.contains("DCVRGEN"));
        // Fallbacks present.
        assert!(a.contains("IsProtected"));
        assert!(a.contains("IsPrimitive"));
        // never reaches arbitrary objects by name-search API
        assert!(!a.contains("GameObject.Find"));
    }
}
