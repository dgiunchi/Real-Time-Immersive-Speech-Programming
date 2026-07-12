//! Phase-3 end-to-end: the LLM's C# candidate must pass static validation and be
//! consistent with its action plan. Uses the mock (template-derived C#), so it
//! runs offline; the same `validate_csharp` gate applies to real model C#.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_csharp_policy::{validate_csharp, CsharpDecision};
use dcvr_llm_client::{LlmClient, MockLlmClient};

#[tokio::test]
async fn mock_dual_output_csharp_passes_policy() {
    let gen = MockLlmClient
        .generate_dual("r1", "make this cube red and bigger and spin")
        .await
        .unwrap();
    let cs = gen.csharp_candidate.expect("mock emits c#");
    let verdict = validate_csharp(&gen.plan, &cs);
    assert_eq!(
        verdict.decision,
        CsharpDecision::ApproveForResearch,
        "template C# should pass policy; violations={:?}",
        verdict.violations
    );
}

#[tokio::test]
async fn plan_only_path_has_no_csharp_by_default() {
    // generate_plan (Mode C fast path) never produces C#.
    let plan = MockLlmClient
        .generate_plan("r1", "make it red")
        .await
        .unwrap();
    assert_eq!(plan.request_id, "r1");
}
