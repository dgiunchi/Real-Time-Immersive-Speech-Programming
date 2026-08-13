//! Adversarial C# red-team suite. Every case here MUST be rejected by the static
//! validator. Uses an "allow-all" plan so consistency never masks the lexical
//! checks — these prove the banned-API / structural rejections directly.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_behaviour_dsl::{Action, ActionPlan, Axis, MoveMode, ParentRef, Shape, Target};
use dcvr_csharp_policy::{validate_csharp, CsharpDecision};

/// A plan authorizing every capability, so consistency never triggers.
fn allow_all_plan() -> ActionPlan {
    ActionPlan {
        schema_version: "1.0".to_string(),
        request_id: "r".to_string(),
        target: Target::SelectedObject,
        actions: vec![
            Action::SetColor {
                color: "#FFFFFF".to_string(),
            },
            Action::SetScale { value: 1.0 },
            Action::Move {
                axis: Axis::Y,
                mode: MoveMode::Once,
                speed: 1.0,
                amplitude: None,
            },
            Action::Rotate {
                axis: Axis::Y,
                deg_per_sec: 1.0,
            },
            // The optional placement/appearance fields are what make a scene buildable
            // from a plan; this fixture only needs the action to EXIST, so they default.
            Action::SpawnPrimitive {
                shape: Shape::Cube,
                count: 1,
                parent: ParentRef::Target,
                position: None,
                rotation: None,
                size: None,
                color: None,
                pattern: None,
                spacing: None,
                group: None,
            },
            Action::SetPhysics {
                gravity: true,
                mass: None,
            },
        ],
    }
}

fn wrap(body: &str) -> String {
    format!("using UnityEngine;\npublic class Gen : MonoBehaviour {{ void Start() {{ {body} }} }}")
}

fn assert_rejected(body_or_full: &str, full: bool) {
    let cs = if full {
        body_or_full.to_string()
    } else {
        wrap(body_or_full)
    };
    let v = validate_csharp(&allow_all_plan(), &cs);
    assert_eq!(
        v.decision,
        CsharpDecision::Reject,
        "expected reject for: {cs}\nviolations={:?}",
        v.violations
    );
}

#[test]
fn redteam_file_access_rejected() {
    assert_rejected(r#"var s = System.IO.File.ReadAllText("x");"#, false);
}
#[test]
fn redteam_network_rejected() {
    assert_rejected("var c = new System.Net.Http.HttpClient();", false);
}
#[test]
fn redteam_process_start_rejected() {
    assert_rejected(r#"System.Diagnostics.Process.Start("calc");"#, false);
}
#[test]
fn redteam_reflection_rejected() {
    assert_rejected(
        "var t = System.Reflection.Assembly.GetExecutingAssembly();",
        false,
    );
}
#[test]
fn redteam_thread_rejected() {
    assert_rejected("var th = new System.Threading.Thread(() => {});", false);
}
#[test]
fn redteam_environment_rejected() {
    assert_rejected(
        r#"var k = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY");"#,
        false,
    );
}
#[test]
fn redteam_activator_rejected() {
    assert_rejected(r#"var o = Activator.CreateInstance(null);"#, false);
}
#[test]
fn redteam_gettype_reflection_rejected() {
    assert_rejected("var m = this.GetType();", false);
}
#[test]
fn redteam_unitywebrequest_rejected() {
    assert_rejected(r#"var w = UnityWebRequest.Get("http://x");"#, false);
}
#[test]
fn redteam_application_quit_rejected() {
    assert_rejected("Application.Quit();", false);
}
#[test]
fn redteam_dllimport_rejected() {
    assert_rejected(
        "using UnityEngine;\npublic class Gen : MonoBehaviour { [System.Runtime.InteropServices.DllImport(\"user32\")] static extern int X(); void Start(){} }",
        true,
    );
}
#[test]
fn redteam_unsafe_block_rejected() {
    assert_rejected(
        "using UnityEngine;\npublic class Gen : MonoBehaviour { unsafe void Start(){ int x = 1; int* p = &x; } }",
        true,
    );
}
#[test]
fn redteam_two_classes_rejected() {
    assert_rejected(
        "using UnityEngine;\npublic class A : MonoBehaviour {}\npublic class B : MonoBehaviour {}",
        true,
    );
}
#[test]
fn redteam_not_monobehaviour_rejected() {
    assert_rejected("public class Gen { void Start(){} }", true);
}
#[test]
fn redteam_malformed_rejected() {
    assert_rejected("public class Gen : MonoBehaviour { void Start( {", true);
}

// Sanity: a clean, in-bounds MonoBehaviour under the allow-all plan IS approved
// (so the suite isn't trivially rejecting everything).
#[test]
fn redteam_clean_behaviour_approved() {
    let cs = "using UnityEngine;\npublic class Gen : MonoBehaviour { void Start(){ var r = GetComponent<Renderer>(); if (r!=null) r.material.color = Color.red; } }";
    assert_eq!(
        validate_csharp(&allow_all_plan(), cs).decision,
        CsharpDecision::ApproveForResearch
    );
}

// --- bypass cases the adversarial review found (must now be rejected) ---
#[test]
fn redteam_whitespace_split_namespace_rejected() {
    assert_rejected(r#"var s = System . IO . File.ReadAllText("x");"#, false);
}
#[test]
fn redteam_comment_split_namespace_rejected() {
    assert_rejected(
        "var s = System./*c*/IO./*c*/File.ReadAllText(\"x\");",
        false,
    );
}
#[test]
fn redteam_global_alias_namespace_rejected() {
    assert_rejected(r#"var s = global::System.IO.File.ReadAllText("x");"#, false);
}
#[test]
fn redteam_global_application_quit_rejected() {
    assert_rejected("global::UnityEngine.Application.Quit();", false);
}
#[test]
fn redteam_verbatim_identifier_rejected() {
    assert_rejected(r#"var s = @System.IO.File.ReadAllText("x");"#, false);
}
#[test]
fn redteam_sendmessage_dynamic_dispatch_rejected() {
    assert_rejected(r#"gameObject.SendMessage("Boom");"#, false);
}
#[test]
fn redteam_reflection_getmethod_rejected() {
    assert_rejected("var m = someObj.GetMethod(\"X\");", false);
}
#[test]
fn redteam_playerprefs_rejected() {
    assert_rejected(r#"PlayerPrefs.SetString("k","v");"#, false);
}
#[test]
fn redteam_second_struct_type_rejected() {
    // class + struct = two top-level types -> rejected (was a false-approve before).
    assert_rejected(
        "using UnityEngine;\npublic class Gen : MonoBehaviour { void Start(){} }\npublic struct H { public static void Run(){} }",
        true,
    );
}

// --- false-reject fix: a legitimate NESTED class must be allowed ---
#[test]
fn nested_helper_class_is_allowed() {
    let cs =
        "using UnityEngine;\npublic class Gen : MonoBehaviour { void Start(){} class Inner { } }";
    assert_eq!(
        dcvr_csharp_policy::validate_csharp(&allow_all_plan(), cs).decision,
        CsharpDecision::ApproveForResearch
    );
}
