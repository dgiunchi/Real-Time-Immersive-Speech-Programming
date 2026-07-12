use async_trait::async_trait;

use dcvr_behaviour_dsl::{Action, ActionPlan, Axis, MoveMode, Target};

use crate::error::LlmError;
use crate::template::template_csharp;
use crate::{Generation, LlmClient};

/// Deterministic transcript -> action plan mapping (the seam the real model
/// replaces). Re-exported by `dcvr-command-router::mock`.
pub fn mock_generate(request_id: &str, transcript: &str) -> ActionPlan {
    let t = transcript.to_lowercase();
    let mut actions: Vec<Action> = Vec::new();

    if t.contains("red") {
        actions.push(Action::SetColor {
            color: "#FF0000".to_string(),
        });
    }
    if t.contains("green") {
        actions.push(Action::SetColor {
            color: "#00FF00".to_string(),
        });
    }
    if t.contains("blue") {
        actions.push(Action::SetColor {
            color: "#0000FF".to_string(),
        });
    }
    if t.contains("bigger") || t.contains("scale") || t.contains("grow") {
        actions.push(Action::SetScale { value: 1.5 });
    }
    if t.contains("spin") || t.contains("rotate") {
        actions.push(Action::Rotate {
            axis: Axis::Y,
            deg_per_sec: 90.0,
        });
    }
    if t.contains("bounce") || t.contains("move") || t.contains("up and down") {
        actions.push(Action::Move {
            axis: Axis::Y,
            mode: MoveMode::Oscillate,
            speed: 1.0,
            amplitude: Some(1.0),
        });
    }
    if actions.is_empty() {
        actions.push(Action::SetColor {
            color: "#FF0000".to_string(),
        });
    }

    ActionPlan {
        schema_version: "1.0".to_string(),
        request_id: request_id.to_string(),
        target: Target::SelectedObject,
        actions,
    }
}

/// Offline LLM stand-in. `generate_dual` emits template-derived C# that is
/// consistent-by-construction with the plan (passes `dcvr-csharp-policy`).
#[derive(Debug, Default, Clone)]
pub struct MockLlmClient;

#[async_trait]
impl LlmClient for MockLlmClient {
    async fn generate_plan(
        &self,
        request_id: &str,
        transcript: &str,
    ) -> Result<ActionPlan, LlmError> {
        Ok(mock_generate(request_id, transcript))
    }

    async fn generate_dual(
        &self,
        request_id: &str,
        transcript: &str,
    ) -> Result<Generation, LlmError> {
        let plan = mock_generate(request_id, transcript);
        let csharp = template_csharp(&plan);
        Ok(Generation {
            plan,
            csharp_candidate: Some(csharp),
        })
    }
}
