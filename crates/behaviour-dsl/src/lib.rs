//! Typed action-plan / IR model for DreamCodeVR+ (Phase 1).
//!
//! This crate is *data only*: the [`ActionPlan`] type, the allow-listed
//! [`Action`] variants, and the bound [`bounds`] constants. It performs no
//! policy decisions (that is `dcvr-code-policy`) so the model and the validator
//! stay independently testable. No I/O, no async, no `unsafe`.

mod action_plan;
mod actions;
pub mod bounds;
mod error;

pub use action_plan::ActionPlan;
pub use actions::{Action, Axis, MoveMode, ParentRef, Shape, Target};
pub use error::DslError;
