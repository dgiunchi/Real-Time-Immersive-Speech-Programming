use thiserror::Error;

/// A single validation failure. Plans are rejected if *any* violation is found.
/// Variants carry the offending value so logs/refusals are specific and useful.
#[derive(Debug, Clone, PartialEq, Error, serde::Serialize, serde::Deserialize)]
pub enum ValidationError {
    #[error("unsupported schema_version: expected {expected}, found {found}")]
    UnsupportedSchemaVersion { expected: String, found: String },
    #[error("action plan has an empty request_id")]
    EmptyRequestId,
    #[error("action plan has no actions")]
    EmptyActions,
    #[error("too many actions: {count} (max {max})")]
    TooManyActions { count: usize, max: usize },
    #[error("set_scale value {value} out of range [{min}, {max}]")]
    ScaleOutOfRange { value: f64, min: f64, max: f64 },
    #[error("move speed {value} out of range [0, {max}]")]
    MoveSpeedOutOfRange { value: f64, max: f64 },
    #[error("move amplitude {value} out of range [0, {max}]")]
    MoveAmplitudeOutOfRange { value: f64, max: f64 },
    #[error("rotate deg_per_sec {value} out of range [0, {max}]")]
    RotateRateOutOfRange { value: f64, max: f64 },
    #[error("physics mass {value} out of range [{min}, {max}]")]
    PhysicsMassOutOfRange { value: f64, min: f64, max: f64 },
    #[error("spawn_primitive count {count} out of range [1, {max}]")]
    SpawnCountOutOfRange { count: u32, max: u32 },
    #[error("plan would spawn {count} primitives, exceeding the budget of {max}")]
    SpawnBudgetExceeded { count: u32, max: u32 },
    #[error("action plan json too large: {len} bytes (max {max})")]
    PayloadTooLarge { len: usize, max: usize },
    #[error("action plan json nesting too deep: {depth} (max {max})")]
    NestingTooDeep { depth: usize, max: usize },
    #[error("invalid color '{value}', expected #RRGGBB")]
    InvalidColor { value: String },
    #[error("non-finite numeric value in an action")]
    NonFiniteValue,
    #[error("malformed action plan json: {0}")]
    MalformedJson(String),
}
