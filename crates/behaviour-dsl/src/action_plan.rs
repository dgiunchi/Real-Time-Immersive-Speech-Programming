use serde::{Deserialize, Serialize};

use crate::actions::{Action, Target};
use crate::error::DslError;

/// A complete action plan (the safety source of truth).
///
/// `deny_unknown_fields` rejects unexpected top-level keys. Numeric/semantic
/// bounds are NOT enforced here — that is `code-policy`'s job, keeping this
/// crate a pure data model. Parsing is fail-closed via [`ActionPlan::from_json`].
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct ActionPlan {
    pub schema_version: String,
    pub request_id: String,
    pub target: Target,
    pub actions: Vec<Action>,
}

impl ActionPlan {
    /// Parse from JSON. Returns [`DslError::Malformed`] on any parse failure
    /// (unknown field, unknown action `type`, bad shape) — never panics.
    pub fn from_json(input: &str) -> Result<Self, DslError> {
        serde_json::from_str(input).map_err(|e| DslError::Malformed(e.to_string()))
    }

    /// Serialise to compact JSON.
    pub fn to_json(&self) -> Result<String, DslError> {
        serde_json::to_string(self).map_err(|e| DslError::Serialize(e.to_string()))
    }
}
