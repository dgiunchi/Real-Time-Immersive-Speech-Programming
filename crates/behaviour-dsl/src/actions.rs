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

/// How a batch of spawned primitives is laid out. Each is a closed form evaluated by the
/// validator and the executor — there is no expression to evaluate, only a shape name.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Pattern {
    /// All at the same point (the previous, only, behaviour).
    Stack,
    /// Evenly spaced on a circle.
    Ring,
    /// A line along X.
    Row,
    /// A flat grid on XZ.
    Grid,
    /// Stacked vertically — walls, towers.
    Tower,
    /// Scattered deterministically within `spacing` metres.
    Scatter,
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
        /// Where to put it, relative to the creation zone. Without this a plan could
        /// only pile primitives at one point, which is why nothing structural was
        /// buildable before: you cannot make a house out of eight cubes at the origin.
        #[serde(default)]
        position: Option<[f64; 3]>,
        #[serde(default)]
        rotation: Option<[f64; 3]>,
        /// Per-axis size. A wall is a stretched cube; without this every primitive was
        /// a uniform blob.
        #[serde(default)]
        size: Option<[f64; 3]>,
        #[serde(default)]
        color: Option<String>,
        /// How `count` primitives are arranged. One action can therefore lay a ring of
        /// planets or a grid of windows — which is what makes a scene expressible in a
        /// handful of actions instead of hundreds.
        #[serde(default)]
        pattern: Option<Pattern>,
        /// Spacing / radius for the pattern, in metres.
        #[serde(default)]
        spacing: Option<f64>,
        /// Name this group so later actions can address it.
        #[serde(default)]
        group: Option<String>,
    },
    /// Surface appearance. Emissive is what makes a star, a neon sign or a lit window
    /// read correctly; opacity is what makes glass and ghosts.
    SetMaterial {
        #[serde(default)]
        target_group: Option<String>,
        #[serde(default)]
        emission: Option<f64>,
        #[serde(default)]
        metallic: Option<f64>,
        #[serde(default)]
        smoothness: Option<f64>,
        #[serde(default)]
        opacity: Option<f64>,
        #[serde(default)]
        color: Option<String>,
    },
    /// A point light. Bounded in intensity, range and count, because lights are the one
    /// cheap-to-ask-for, expensive-to-render thing in the vocabulary.
    SpawnLight {
        color: String,
        intensity: f64,
        range: f64,
        #[serde(default)]
        position: Option<[f64; 3]>,
        #[serde(default)]
        flicker: Option<bool>,
    },
    /// World-space text: a label, a sign, a title.
    SpawnText {
        text: String,
        size: f64,
        #[serde(default)]
        color: Option<String>,
        #[serde(default)]
        position: Option<[f64; 3]>,
    },
    /// Orbit a group around a point — a solar system, a mobile, a ring of lanterns.
    /// Expressed as a bounded motion, never as code.
    Orbit {
        #[serde(default)]
        target_group: Option<String>,
        radius: f64,
        deg_per_sec: f64,
        #[serde(default)]
        axis: Option<Axis>,
    },
    SetPhysics {
        gravity: bool,
        #[serde(default)]
        mass: Option<f64>,
    },
}
