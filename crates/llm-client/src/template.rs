//! Deterministic action-plan -> C# template. Used by `MockLlmClient` so the
//! candidate C# is consistent-by-construction (passes `dcvr-csharp-policy`).
//! This is also the safe shape the real model is steered toward.
use dcvr_behaviour_dsl::{Action, ActionPlan, Axis, Shape};

fn axis_vec(axis: Axis) -> &'static str {
    match axis {
        Axis::X => "Vector3.right",
        Axis::Y => "Vector3.up",
        Axis::Z => "Vector3.forward",
    }
}

fn shape_name(shape: Shape) -> &'static str {
    match shape {
        Shape::Cube => "Cube",
        Shape::Sphere => "Sphere",
        Shape::Capsule => "Capsule",
        Shape::Cylinder => "Cylinder",
        Shape::Plane => "Plane",
        Shape::Quad => "Quad",
    }
}

/// Build a single allow-listed `MonoBehaviour` from a (validated) action plan.
pub fn template_csharp(plan: &ActionPlan) -> String {
    let mut start = String::new();
    let mut update = String::new();
    for action in &plan.actions {
        match action {
            Action::SetColor { color } => start.push_str(&format!(
                "        if (ColorUtility.TryParseHtmlString(\"{color}\", out var __c)) {{ var __r = GetComponent<Renderer>(); if (__r != null) __r.material.color = __c; }}\n"
            )),
            Action::SetScale { value } => start.push_str(&format!(
                "        transform.localScale = Vector3.one * {value}f;\n"
            )),
            Action::Move { axis, .. } => update.push_str(&format!(
                "        transform.position += {} * Time.deltaTime;\n",
                axis_vec(*axis)
            )),
            Action::Rotate { axis, deg_per_sec } => update.push_str(&format!(
                "        transform.Rotate({}, {deg_per_sec}f * Time.deltaTime);\n",
                axis_vec(*axis)
            )),
            Action::SpawnPrimitive { shape, count, .. } => start.push_str(&format!(
                "        for (int __i = 0; __i < {count}; __i++) {{ GameObject.CreatePrimitive(PrimitiveType.{}); }}\n",
                shape_name(*shape)
            )),
            Action::SetMaterial { .. }
            | Action::SpawnLight { .. }
            | Action::SpawnText { .. }
            | Action::Orbit { .. } => {
                // Composition actions. The Mode-C executor builds these directly; there
                // is no faithful single-MonoBehaviour C# rendering, so the template says
                // so rather than emitting code that does not match the plan.
                start.push_str("        // composition action executed by ActionPlanExecutor\n");
            }
            Action::SetPhysics { gravity, mass } => {
                let m = mass.unwrap_or(1.0);
                start.push_str(&format!(
                    "        {{ var __rb = gameObject.AddComponent<Rigidbody>(); __rb.useGravity = {gravity}; __rb.mass = {m}f; }}\n"
                ));
            }
        }
    }
    format!(
        "using UnityEngine;\npublic class GeneratedBehaviour : MonoBehaviour {{\n    void Start() {{\n{start}    }}\n    void Update() {{\n{update}    }}\n}}\n"
    )
}
