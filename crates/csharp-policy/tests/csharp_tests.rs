//! Static C# validation tests (lexical pre-filter + consistency).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_behaviour_dsl::{Action, ActionPlan, Axis, MoveMode, Target};
use dcvr_csharp_policy::{validate_csharp, CsharpDecision, CsharpViolation};

fn plan_with(actions: Vec<Action>) -> ActionPlan {
    ActionPlan {
        schema_version: "1.0".to_string(),
        request_id: "r".to_string(),
        target: Target::SelectedObject,
        actions,
    }
}

const SET_COLOR_CS: &str = r#"
using UnityEngine;
public class Gen : MonoBehaviour {
    void Start() {
        var r = GetComponent<Renderer>();
        if (r != null) r.material.color = Color.red;
    }
}
"#;

#[test]
fn valid_set_color_is_approved() {
    let plan = plan_with(vec![Action::SetColor {
        color: "#FF0000".to_string(),
    }]);
    let v = validate_csharp(&plan, SET_COLOR_CS);
    assert_eq!(
        v.decision,
        CsharpDecision::ApproveForResearch,
        "{:?}",
        v.violations
    );
    assert!(v.violations.is_empty());
}

#[test]
fn network_api_is_rejected() {
    let cs = r#"
using System.Net;
using UnityEngine;
public class Gen : MonoBehaviour {
    void Start() { var c = new System.Net.Http.HttpClient(); }
}
"#;
    let plan = plan_with(vec![Action::SetColor {
        color: "#FF0000".to_string(),
    }]);
    let v = validate_csharp(&plan, cs);
    assert_eq!(v.decision, CsharpDecision::Reject);
    assert!(v
        .violations
        .iter()
        .any(|x| matches!(x, CsharpViolation::BannedToken(t) if t == "System.Net")));
}

#[test]
fn process_start_is_rejected() {
    let cs = r#"
using UnityEngine;
public class Gen : MonoBehaviour {
    void Start() { System.Diagnostics.Process.Start("x"); }
}
"#;
    let plan = plan_with(vec![Action::SetColor {
        color: "#FF0000".to_string(),
    }]);
    assert_eq!(validate_csharp(&plan, cs).decision, CsharpDecision::Reject);
}

#[test]
fn spawning_when_plan_says_set_color_is_inconsistent() {
    let cs = r#"
using UnityEngine;
public class Gen : MonoBehaviour {
    void Start() { GameObject.CreatePrimitive(PrimitiveType.Cube); }
}
"#;
    // Plan authorizes only set_color; the C# spawns -> inconsistency.
    let plan = plan_with(vec![Action::SetColor {
        color: "#00FF00".to_string(),
    }]);
    let v = validate_csharp(&plan, cs);
    assert_eq!(v.decision, CsharpDecision::Reject);
    assert!(v.violations.iter().any(
        |x| matches!(x, CsharpViolation::InconsistentCapability(c) if c == "CreatePrimitive")
    ));
}

#[test]
fn move_consistency_passes_when_plan_has_move() {
    let cs = r#"
using UnityEngine;
public class Gen : MonoBehaviour {
    void Update() { transform.position += Vector3.up * Time.deltaTime; }
}
"#;
    let plan = plan_with(vec![Action::Move {
        axis: Axis::Y,
        mode: MoveMode::Once,
        speed: 1.0,
        amplitude: None,
    }]);
    assert_eq!(
        validate_csharp(&plan, cs).decision,
        CsharpDecision::ApproveForResearch
    );
}

#[test]
fn not_monobehaviour_is_rejected() {
    let cs = "public class Gen { void Start() {} }";
    let plan = plan_with(vec![Action::SetColor {
        color: "#FF0000".to_string(),
    }]);
    let v = validate_csharp(&plan, cs);
    assert_eq!(v.decision, CsharpDecision::Reject);
    assert!(v.violations.contains(&CsharpViolation::NotMonoBehaviour));
}

#[test]
fn two_classes_rejected() {
    let cs = r#"
using UnityEngine;
public class A : MonoBehaviour {}
public class B : MonoBehaviour {}
"#;
    let plan = plan_with(vec![Action::SetColor {
        color: "#FF0000".to_string(),
    }]);
    let v = validate_csharp(&plan, cs);
    assert_eq!(v.decision, CsharpDecision::Reject);
    assert!(v.violations.contains(&CsharpViolation::ClassCount(2)));
}

#[test]
fn malformed_csharp_rejected() {
    let cs = "public class Gen : MonoBehaviour { void Start( {";
    let plan = plan_with(vec![Action::SetColor {
        color: "#FF0000".to_string(),
    }]);
    let v = validate_csharp(&plan, cs);
    assert_eq!(v.decision, CsharpDecision::Reject);
    assert!(v.violations.contains(&CsharpViolation::ParseError));
}
