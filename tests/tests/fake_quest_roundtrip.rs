//! Required test 23 (Phase-2 async): a fake-Quest client round-trips NID 93 +
//! NID 98 through a real loopback async server and receives a validated NID 94
//! response (mock STT+LLM, so it runs offline with no keys).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream};

use dcvr_protocol::{
    decode_frame, encode_frame, join_peer_payload, NetworkFrame, ProtocolError, NID_AUDIO_INPUT,
    NID_BACKEND_OUTPUT, NID_SELECTED_OBJECT,
};

#[tokio::test]
async fn fake_quest_roundtrip_produces_nid94_response() {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();
    let services =
        dreamcodevr_server::server::services_from_settings(dcvr_config::Settings::default());
    let handle = tokio::spawn(async move {
        dreamcodevr_server::server::serve_one(listener, services)
            .await
            .unwrap();
    });

    let mut stream = TcpStream::connect(addr).await.unwrap();
    let peer = "00000000-0000-4000-8000-000000000001";
    stream
        .write_all(
            &encode_frame(
                NID_SELECTED_OBJECT,
                &join_peer_payload(peer, b"Cube:Default"),
            )
            .unwrap(),
        )
        .await
        .unwrap();
    stream
        .write_all(
            &encode_frame(
                NID_AUDIO_INPUT,
                &join_peer_payload(peer, b"spawn a new red cube"),
            )
            .unwrap(),
        )
        .await
        .unwrap();

    let frame = read_one_frame(&mut stream).await;
    assert_eq!(frame.network_id, NID_BACKEND_OUTPUT);
    let text = String::from_utf8(frame.payload).unwrap();
    assert!(text.contains("ApproveActionPlan"), "got: {text}");
    assert!(text.contains("#FF0000"), "got: {text}");

    drop(stream);
    handle.await.unwrap();
}

async fn read_one_frame(stream: &mut TcpStream) -> NetworkFrame {
    let mut buf = Vec::new();
    let mut tmp = [0u8; 4096];
    loop {
        match decode_frame(&buf) {
            Ok(d) => return d.frame,
            Err(ProtocolError::Incomplete { .. }) => {}
            Err(e) => panic!("decode error: {e}"),
        }
        let n = stream.read(&mut tmp).await.unwrap();
        assert!(n > 0, "server closed before responding");
        buf.extend_from_slice(&tmp[..n]);
    }
}
