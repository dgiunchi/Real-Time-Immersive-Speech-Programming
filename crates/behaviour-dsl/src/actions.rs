use serde::{Deserialize, Serialize};

/// What an action plan targets.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Target {
    SelectedObject,
    SceneRoot,
}

/// Axis for movement / rotation.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Axis {
    X,
    Y,
    Z,
}

/// Movement mode.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum MoveMode {
    Once,
    Oscillate,
}

/// Allow-listed primitive shapes (Unity built-ins only; no arbitrary meshes).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Shape {
    Cube,
    Sphere,
    Capsule,
    Cylinder,
    Plane,
    Quad,
}

/// Where spawned primitives are parented.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ParentRef {
    Target,
    SceneRoot,
}

/// A single allow-listed action. Internally tagged by `type`; an unknown `type`
/// value is rejected by serde (there is no catch-all variant), which gives us
/// "unknown action rejected" for free at the parse layer.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum Action {
    SetColor {
        color: String,
    },
    SetScale {
        value: f64,
    },
    Move {
        axis: Axis,
        mode: MoveMode,
        speed: f64,
        #[serde(default)]
        amplitude: Option<f64>,
    },
    Rotate {
        axis: Axis,
        deg_per_sec: f64,
    },
    SpawnPrimitive {
        shape: Shape,
        count: u32,
        parent: ParentRef,
    },
    SetPhysics {
        gravity: bool,
        #[serde(default)]
        mass: Option<f64>,
    },
}
