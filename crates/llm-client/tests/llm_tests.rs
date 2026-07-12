#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_llm_client::{mock_generate, LlmClient, MockLlmClient};

#[tokio::test]
async fn mock_llm_generates_valid_plan() {
    let plan = MockLlmClient
        .generate_plan("r1", "make this cube red")
        .await
        .unwrap();
    assert_eq!(plan.request_id, "r1");
    assert_eq!(plan.schema_version, "1.0");
    assert!(!plan.actions.is_empty());
}

#[test]
fn mock_generate_is_deterministic() {
    assert_eq!(
        mock_generate("r", "make it red"),
        mock_generate("r", "make it red")
    );
}

#[tokio::test]
async fn mock_dual_output_includes_csharp() {
    let gen = MockLlmClient
        .generate_dual("r1", "make this cube red")
        .await
        .unwrap();
    assert_eq!(gen.plan.request_id, "r1");
    let cs = gen.csharp_candidate.expect("mock should emit c#");
    assert!(cs.contains("MonoBehaviour"));
    assert!(cs.contains("class GeneratedBehaviour"));
}

// Integration: exercise OpenAiLlmClient against a local OpenAI-shaped mock so the
// request (bearer auth + chat/completions) and response parsing are validated.
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpListener;

async fn spawn_openai_mock(body: String) -> String {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();
    tokio::spawn(async move {
        if let Ok((mut sock, _)) = listener.accept().await {
            let mut buf = [0u8; 8192];
            let _ = sock.read(&mut buf).await;
            let resp = format!(
                "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n{body}",
                body.len()
            );
            let _ = sock.write_all(resp.as_bytes()).await;
            let _ = sock.shutdown().await;
        }
    });
    format!("http://{addr}/v1")
}

#[tokio::test]
async fn openai_client_parses_action_plan_from_chat_completion() {
    let body = serde_json::json!({
        "choices": [{
            "message": {
                "content": r##"{"schema_version":"1.0","request_id":"ignored","target":"selected_object","actions":[{"type":"set_color","color":"#FF0000"}]}"##
            }
        }]
    })
    .to_string();
    let base = spawn_openai_mock(body).await;
    let client = dcvr_llm_client::OpenAiLlmClient::new(
        secrecy::SecretString::from("test-key".to_string()),
        "gpt-4o-mini",
    )
    .with_base_url(base);
    let plan = client
        .generate_plan("req-7", "make this cube red")
        .await
        .unwrap();
    assert_eq!(plan.request_id, "req-7"); // backend overrides the model's id
    assert!(
        matches!(plan.actions[0], dcvr_behaviour_dsl::Action::SetColor { ref color } if color == "#FF0000")
    );
}
