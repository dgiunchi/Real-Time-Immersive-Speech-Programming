//! One remembered creation. A user's liked records become the retrieval corpus
//! for few-shot / RAG (levels 2 & 3): on a new command we find the most similar
//! liked record and offer it to the model as a style reference.

use serde::{Deserialize, Serialize};

/// A single past creation for one user.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MemoryRecord {
    /// Monotonic id within a user's history (newest = highest).
    pub seq: u64,
    /// The natural-language command the user gave.
    pub command: String,
    /// The generated C# (kept so a liked record can seed a few-shot example).
    pub result: String,
    /// `Some(true)` 👍, `Some(false)` 👎, `None` = awaiting feedback.
    pub liked: Option<bool>,
    /// Embedding of `command`, used for similarity retrieval.
    pub embedding: Vec<f32>,
}
