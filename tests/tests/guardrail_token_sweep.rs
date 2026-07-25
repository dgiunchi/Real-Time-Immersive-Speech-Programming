//! Exhaustive per-token sweep of the C# guardrail's two denylists.
//!
//! The other suites prove the guardrail's *mechanisms* (alias resolution, unicode
//! decoding, the unsafe/pointer bans…). This one proves its *coverage*: that every
//! individual banned namespace and identifier is actually rejected, and rejected
//! under the correct profile. A token silently dropped from a denylist during a
//! refactor would pass every other test in the workspace but fail here.
//!
//! Contract asserted, per token:
//!   * SYSTEM-security tokens -> rejected under BOTH profiles (always-on layer).
//!   * PERCEPTUAL/XR tokens -> ALLOWED under `CreativeFreedom` (creative freedom is
//!     preserved) but REJECTED under `DeployHardened`.
//!
//! Every probe is syntactically valid C# in a single `MonoBehaviour`, so a rejection
//! can only come from the denylist — never from a parse error or a shape violation.
//! `harness_does_not_reject_everything` pins that, otherwise a broken harness that
//! rejected all input would make this file vacuously pass.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_csharp_policy::{validate_csharp_freeform_profile, CsharpDecision, HardeningProfile};

/// Wrap a statement in a minimal, always-valid single-MonoBehaviour class.
fn probe(statement: &str) -> String {
    format!(
        "public class GeneratedBehaviour : MonoBehaviour {{\n    void Start() {{\n        {statement}\n    }}\n}}\n"
    )
}

fn approved(csharp: &str, profile: HardeningProfile) -> bool {
    validate_csharp_freeform_profile(csharp, profile).decision == CsharpDecision::ApproveForResearch
}

/// Reference a dotted namespace path in a way that always parses.
fn namespace_probe(path: &str) -> String {
    probe(&format!("var _n = {path}.Placeholder;"))
}

/// Reference a bare identifier in a way that always parses.
fn identifier_probe(ident: &str) -> String {
    probe(&format!("var _i = {ident}.Placeholder;"))
}

// ---------------------------------------------------------------------------
// The denylists, mirrored from crates/csharp-policy/src/lexical.rs.
// Kept as literal tables so a deletion on either side shows up as a test failure.
// ---------------------------------------------------------------------------

/// `SECURITY_BANNED_NAMESPACES` — always active, both profiles.
const SECURITY_NAMESPACES: &[&str] = &[
    "System.IO",
    "System.Net",
    "System.Reflection",
    "System.Diagnostics",
    "System.Threading",
    "System.Runtime.InteropServices",
    "UnityEngine.Networking",
    "Application.Quit",
];

/// `SECURITY_BANNED_IDENTIFIERS` — always active, both profiles.
const SECURITY_IDENTIFIERS: &[&str] = &[
    "Process",
    "Assembly",
    "AppDomain",
    "Environment",
    "Activator",
    "Marshal",
    "DllImport",
    "UnityWebRequest",
    "GetType",
    "SendMessage",
    "SendMessageUpwards",
    "BroadcastMessage",
    "GetMethod",
    "GetField",
    "GetProperty",
    "GetConstructor",
    "MethodInfo",
    "FieldInfo",
    "PropertyInfo",
    "PlayerPrefs",
    "Resources",
    "InvokeRepeating",
];

/// `PERCEPTUAL_BANNED_NAMESPACES` — added only by `DeployHardened`.
const PERCEPTUAL_NAMESPACES: &[&str] =
    &["UnityEngine.XR", "Unity.XR", "UnityEngine.InputSystem.XR"];

/// `PERCEPTUAL_BANNED_IDENTIFIERS` — added only by `DeployHardened`.
const PERCEPTUAL_IDENTIFIERS: &[&str] = &[
    "XRSettings",
    "XRDevice",
    "InputTracking",
    "XROrigin",
    "TrackedPoseDriver",
    "OVRManager",
    "OVRCameraRig",
    "OVRPlugin",
    "OVRBoundary",
    "OVRInput",
    "InputDevices",
    "XRInputSubsystem",
    "OVRHaptics",
    "SendHapticImpulse",
    "Vibrate",
    "WebCamTexture",
];

// ---------------------------------------------------------------------------
// Sanity: the harness must be able to APPROVE things, or everything below is vacuous.
// ---------------------------------------------------------------------------

#[test]
fn harness_does_not_reject_everything() {
    // Same shape as every probe, but with a harmless API.
    let benign = probe("var _n = Mathf.PI;");
    assert!(
        approved(&benign, HardeningProfile::CreativeFreedom),
        "the probe template itself must be approvable, else the sweep proves nothing"
    );
    assert!(
        approved(&benign, HardeningProfile::DeployHardened),
        "the probe template must also pass the hardened profile"
    );
}

// ---------------------------------------------------------------------------
// System-security layer: rejected under BOTH profiles.
// ---------------------------------------------------------------------------

#[test]
fn every_security_namespace_is_blocked_under_both_profiles() {
    for ns in SECURITY_NAMESPACES {
        let code = namespace_probe(ns);
        assert!(
            !approved(&code, HardeningProfile::CreativeFreedom),
            "security namespace `{ns}` must be blocked under CreativeFreedom"
        );
        assert!(
            !approved(&code, HardeningProfile::DeployHardened),
            "security namespace `{ns}` must be blocked under DeployHardened"
        );
    }
}

#[test]
fn every_security_identifier_is_blocked_under_both_profiles() {
    for id in SECURITY_IDENTIFIERS {
        let code = identifier_probe(id);
        assert!(
            !approved(&code, HardeningProfile::CreativeFreedom),
            "security identifier `{id}` must be blocked under CreativeFreedom"
        );
        assert!(
            !approved(&code, HardeningProfile::DeployHardened),
            "security identifier `{id}` must be blocked under DeployHardened"
        );
    }
}

// ---------------------------------------------------------------------------
// Perceptual/XR layer: free under CreativeFreedom, blocked under DeployHardened.
// This two-sided assertion is the profile contract — it catches both a token
// going missing AND a token leaking into the always-on layer.
// ---------------------------------------------------------------------------

#[test]
fn every_perceptual_namespace_is_profile_gated() {
    for ns in PERCEPTUAL_NAMESPACES {
        let code = namespace_probe(ns);
        assert!(
            approved(&code, HardeningProfile::CreativeFreedom),
            "perceptual namespace `{ns}` must stay ALLOWED under CreativeFreedom"
        );
        assert!(
            !approved(&code, HardeningProfile::DeployHardened),
            "perceptual namespace `{ns}` must be blocked under DeployHardened"
        );
    }
}

#[test]
fn every_perceptual_identifier_is_profile_gated() {
    for id in PERCEPTUAL_IDENTIFIERS {
        let code = identifier_probe(id);
        assert!(
            approved(&code, HardeningProfile::CreativeFreedom),
            "perceptual identifier `{id}` must stay ALLOWED under CreativeFreedom"
        );
        assert!(
            !approved(&code, HardeningProfile::DeployHardened),
            "perceptual identifier `{id}` must be blocked under DeployHardened"
        );
    }
}

// ---------------------------------------------------------------------------
// Deliberate NON-bans. These dual-use MR / authoring APIs must stay usable under
// BOTH profiles; banning them lexically would over-block legitimate creative work
// (documented in the lexical.rs NOTE). Pinning them guards against over-blocking.
// ---------------------------------------------------------------------------

#[test]
fn dual_use_mr_apis_are_deliberately_not_banned() {
    for id in [
        "OVREyeGaze",
        "OVRPassthroughLayer",
        "OVRSpatialAnchor",
        "OVRSceneManager",
        "OVRHand",
        "OVRSkeleton",
        "OVRFaceExpressions",
    ] {
        let code = identifier_probe(id);
        assert!(
            approved(&code, HardeningProfile::CreativeFreedom),
            "{id} is a dual-use MR authoring API and must stay allowed (CreativeFreedom)"
        );
        assert!(
            approved(&code, HardeningProfile::DeployHardened),
            "{id} is deliberately NOT in the perceptual denylist — see lexical.rs NOTE"
        );
    }
    // `Destroy` is likewise intentionally allowed: removing objects is creation.
    assert!(approved(
        &probe("Destroy(gameObject);"),
        HardeningProfile::DeployHardened
    ));
}

// ---------------------------------------------------------------------------
// Evasion defences — one probe per documented bypass class.
// ---------------------------------------------------------------------------

#[test]
fn evasion_using_alias_single_hop() {
    let code = "using Sys = System;\npublic class GeneratedBehaviour : MonoBehaviour {\n    void Start() { Sys.IO.File.Delete(\"/tmp/x\"); }\n}\n";
    assert!(!approved(code, HardeningProfile::CreativeFreedom));
}

#[test]
fn evasion_using_alias_deep_chain() {
    // A 9-deep chain: only a fixpoint resolver (not a fixed hop cap) closes this.
    let mut src = String::from("using A1 = System;\n");
    for i in 2..=9 {
        src.push_str(&format!("using A{i} = A{};\n", i - 1));
    }
    src.push_str("public class GeneratedBehaviour : MonoBehaviour {\n    void Start() { A9.IO.File.Delete(\"/tmp/x\"); }\n}\n");
    assert!(
        !approved(&src, HardeningProfile::CreativeFreedom),
        "a 9-deep alias chain must still resolve back to System.IO"
    );
}

#[test]
fn evasion_alias_cycle_terminates_and_does_not_hang() {
    // Cyclic aliases must not loop forever; reaching a verdict at all is the assertion.
    let code = "using A = B;\nusing B = A;\npublic class GeneratedBehaviour : MonoBehaviour {\n    void Start() { var _x = A.Placeholder; }\n}\n";
    let _ = validate_csharp_freeform_profile(code, HardeningProfile::CreativeFreedom);
}

#[test]
fn evasion_unicode_escaped_identifier() {
    // `Quit` written with a Q escape still normalises to Application.Quit.
    let code = probe("Application.\\u0051uit();");
    assert!(!approved(&code, HardeningProfile::CreativeFreedom));
}

#[test]
fn evasion_verbatim_at_prefixed_identifier() {
    let code = probe("var _x = @System.IO.Placeholder;");
    assert!(!approved(&code, HardeningProfile::CreativeFreedom));
}

#[test]
fn evasion_whitespace_and_comments_inside_a_dotted_name() {
    for stmt in [
        "var _x = System . IO . Placeholder;",
        "var _x = System./*hide*/IO.Placeholder;",
        "var _x = global::System.IO.Placeholder;",
    ] {
        assert!(
            !approved(&probe(stmt), HardeningProfile::CreativeFreedom),
            "AST reconstruction must see through: {stmt}"
        );
    }
}

#[test]
fn evasion_dynamic_late_binding() {
    assert!(!approved(
        &probe("dynamic d = gameObject; d.Quit();"),
        HardeningProfile::CreativeFreedom
    ));
}

#[test]
fn evasion_unsafe_all_three_forms() {
    // 1) the `unsafe` MODIFIER
    let modifier =
        "public class GeneratedBehaviour : MonoBehaviour {\n    unsafe void Start() { }\n}\n";
    assert!(
        !approved(modifier, HardeningProfile::CreativeFreedom),
        "unsafe modifier"
    );
    // 2) the `unsafe { }` STATEMENT block (the HIGH-severity bypass that was fixed)
    let block = probe("unsafe { int x = 1; int* p = &x; *p = 2; }");
    assert!(
        !approved(&block, HardeningProfile::CreativeFreedom),
        "unsafe statement block"
    );
    // 3) a bare pointer TYPE
    let ptr = "public class GeneratedBehaviour : MonoBehaviour {\n    unsafe int* Ptr;\n}\n";
    assert!(
        !approved(ptr, HardeningProfile::CreativeFreedom),
        "pointer type"
    );
}

#[test]
fn evasion_monobehaviour_impostor_base() {
    for base in ["FakeMonoBehaviourX", "MonoBehaviourImpostor"] {
        let code =
            format!("public class GeneratedBehaviour : {base} {{\n    void Start() {{ }}\n}}\n");
        assert!(
            !approved(&code, HardeningProfile::CreativeFreedom),
            "`{base}` must not satisfy the MonoBehaviour requirement (exact segment match)"
        );
    }
    // A comment mentioning the type must not satisfy it either.
    let commented =
        "public class GeneratedBehaviour /* MonoBehaviour */ {\n    void Start() { }\n}\n";
    assert!(!approved(commented, HardeningProfile::CreativeFreedom));
}

// ---------------------------------------------------------------------------
// Structural limits.
// ---------------------------------------------------------------------------

#[test]
fn shape_requires_exactly_one_top_level_type() {
    let two_classes = "public class GeneratedBehaviour : MonoBehaviour { void Start() { } }\npublic class Second { }\n";
    assert!(
        !approved(two_classes, HardeningProfile::CreativeFreedom),
        "two top-level types"
    );

    let class_plus_struct = "public class GeneratedBehaviour : MonoBehaviour { void Start() { } }\npublic struct S { public int A; }\n";
    assert!(
        !approved(class_plus_struct, HardeningProfile::CreativeFreedom),
        "class + struct"
    );

    // A NESTED helper type is fine — it is not top-level.
    let nested = "public class GeneratedBehaviour : MonoBehaviour {\n    class Helper { public int A; }\n    void Start() { }\n}\n";
    assert!(
        approved(nested, HardeningProfile::CreativeFreedom),
        "nested helper allowed"
    );
}

#[test]
fn shape_rejects_code_that_is_not_a_monobehaviour() {
    let plain = "public class GeneratedBehaviour { void Start() { } }\n";
    assert!(!approved(plain, HardeningProfile::CreativeFreedom));
}

#[test]
fn malformed_csharp_fails_closed() {
    assert!(!approved(
        "public class GeneratedBehaviour : MonoBehaviour { void Start() {",
        HardeningProfile::CreativeFreedom
    ));
    assert!(
        !approved("", HardeningProfile::CreativeFreedom),
        "empty input"
    );
}

#[test]
fn oversized_source_is_rejected_before_it_can_be_parsed() {
    // MAX_LEN is 16384 bytes; pad past it with a comment so the code stays valid.
    let padding = "// ".to_string() + &"x".repeat(17_000);
    let code = format!(
        "{padding}\npublic class GeneratedBehaviour : MonoBehaviour {{ void Start() {{ }} }}\n"
    );
    assert!(code.len() > 16_384);
    assert!(
        !approved(&code, HardeningProfile::CreativeFreedom),
        "over the 16 KiB cap"
    );
}
