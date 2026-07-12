//! Fail-closed safety gate for DreamCodeVR+ (Phase 1).
//!
//! Accepts: an [`dcvr_behaviour_dsl::ActionPlan`] (or its JSON) whose schema
//! version, action set, and every numeric field lie within
//! [`dcvr_behaviour_dsl::bounds`]. Rejects (returning [`Decision::RejectUnsafe`]
//! and a list of [`ValidationError`]): unknown schema versions, empty/oversized
//! action lists, out-of-range or non-finite values, bad colours, oversized
//! spawn counts, and malformed JSON.
//!
//! Invariant: `validate_plan` returns `ApproveActionPlan` **iff** `violations`
//! is empty. There is no path that approves a plan with a recorded violation.

mod decision;
mod errors;
mod plan_validator;

pub use decision::Decision;
pub use errors::ValidationError;
pub use plan_validator::{validate_json, validate_plan, PolicyOutcome};
