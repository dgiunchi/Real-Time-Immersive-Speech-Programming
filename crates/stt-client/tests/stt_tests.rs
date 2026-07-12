#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_stt_client::{AudioUtterance, MockSttClient, SttClient};

#[tokio::test]
async fn mock_stt_treats_bytes_as_text() {
    let stt = MockSttClient;
    let audio = AudioUtterance::new_16k_mono(b"make this cube red".to_vec());
    let t = stt.transcribe(&audio).await.unwrap();
    assert_eq!(t.text, "make this cube red");
}

#[tokio::test]
async fn mock_stt_rejects_empty() {
    let stt = MockSttClient;
    assert!(stt
        .transcribe(&AudioUtterance::new_16k_mono(vec![]))
        .await
        .is_err());
}

#[test]
fn wav_container_has_correct_size_and_header() {
    let wav = AudioUtterance::new_16k_mono(vec![0u8; 320]).to_wav();
    assert_eq!(wav.len(), 44 + 320);
    assert_eq!(&wav[0..4], b"RIFF");
    assert_eq!(&wav[8..12], b"WAVE");
    assert_eq!(&wav[36..40], b"data");
}

// Integration: exercise the real HTTP client path against a local mock server so
// the request/response handling is validated without a real STT endpoint.
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpListener;

async fn spawn_http_mock(status_line: &'static str, body: &'static str) -> String {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();
    tokio::spawn(async move {
        if let Ok((mut sock, _)) = listener.accept().await {
            let mut buf = [0u8; 8192];
            let _ = sock.read(&mut buf).await; // drain (partial) request; small bodies fit
            let resp = format!(
                "HTTP/1.1 {status_line}\r\nContent-Length: {}\r\nContent-Type: text/plain\r\nConnection: close\r\n\r\n{body}",
                body.len()
            );
            let _ = sock.write_all(resp.as_bytes()).await;
            let _ = sock.shutdown().await;
        }
    });
    format!("http://{addr}/stt/transcribe")
}

#[tokio::test]
async fn http_stt_posts_wav_and_parses_response() {
    let url = spawn_http_mock("200 OK", "make this cube red").await;
    let client = dcvr_stt_client::HttpSttClient::new(url);
    let t = client
        .transcribe(&AudioUtterance::new_16k_mono(vec![0u8; 64]))
        .await
        .unwrap();
    assert_eq!(t.text, "make this cube red");
}

#[tokio::test]
async fn http_stt_maps_non_2xx_to_status_error() {
    let url = spawn_http_mock("500 Internal Server Error", "boom").await;
    let client = dcvr_stt_client::HttpSttClient::new(url);
    let err = client
        .transcribe(&AudioUtterance::new_16k_mono(vec![0u8; 64]))
        .await
        .unwrap_err();
    assert!(matches!(
        err,
        dcvr_stt_client::SttError::Status { status: 500, .. }
    ));
}
