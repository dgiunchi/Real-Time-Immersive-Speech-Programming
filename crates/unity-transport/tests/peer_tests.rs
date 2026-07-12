//! End-to-end self-test against a MOCK RoomServer: connect -> Join -> SetRoom ->
//! receive a NID 98 frame -> send a NID 94 frame. Validates framing + join flow
//! without the real Node server.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream};

use dcvr_protocol::{decode_frame, encode_frame, NetworkFrame, NetworkId, ProtocolError};
use dcvr_unity_transport::UbiqServicePeer;

async fn read_one_frame(sock: &mut TcpStream) -> NetworkFrame {
    let mut buf = Vec::new();
    let mut tmp = [0u8; 8192];
    loop {
        match decode_frame(&buf) {
            Ok(d) => return d.frame,
            Err(ProtocolError::Incomplete { .. }) => {}
            Err(e) => panic!("decode: {e}"),
        }
        let n = sock.read(&mut tmp).await.unwrap();
        assert!(n > 0, "mock server: connection closed early");
        buf.extend_from_slice(&tmp[..n]);
    }
}

#[tokio::test]
async fn peer_joins_room_and_exchanges_nid98_nid94() {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();

    let server = tokio::spawn(async move {
        let (mut sock, _) = listener.accept().await.unwrap();
        // 1) expect a Join to NID 1
        let join = read_one_frame(&mut sock).await;
        assert_eq!(join.network_id, NetworkId::new(0, 1));
        let v: serde_json::Value = serde_json::from_slice(&join.payload).unwrap();
        assert_eq!(v["type"], "Join");
        let args_str = v["args"].as_str().expect("args must be a JSON string");
        let args: serde_json::Value = serde_json::from_str(args_str).unwrap();
        assert!(args["peer"]["uuid"].is_string());
        assert_eq!(args["uuid"], "6765c52b-3ad6-4fb0-9030-2c9a05dc4731");
        // 2) reply SetRoom (addressed to some client id; content is what matters)
        let set_room =
            serde_json::json!({"type":"SetRoom","args":{"room":{"uuid":"r"},"peers":[]}})
                .to_string();
        sock.write_all(&encode_frame(NetworkId::new(0, 5), set_room.as_bytes()).unwrap())
            .await
            .unwrap();
        // 3) send a NID 98 audio frame (36-byte uuid + text)
        let mut body = b"00000000-0000-4000-8000-000000000001".to_vec();
        body.extend_from_slice(b"make this cube red");
        sock.write_all(&encode_frame(NetworkId::new(0, 98), &body).unwrap())
            .await
            .unwrap();
        // 4) expect a NID 94 output frame back
        let out = read_one_frame(&mut sock).await;
        assert_eq!(out.network_id, NetworkId::new(0, 94));
        assert!(String::from_utf8_lossy(&out.payload).contains("CodeGenerated"));
    });

    let mut peer = UbiqServicePeer::connect_and_join(
        &addr.to_string(),
        "6765c52b-3ad6-4fb0-9030-2c9a05dc4731",
    )
    .await
    .unwrap();

    let audio = peer.recv().await.unwrap();
    assert_eq!(audio.network_id, NetworkId::new(0, 98));
    assert!(String::from_utf8_lossy(&audio.payload).contains("make this cube red"));

    peer.send(
        NetworkId::new(0, 94),
        br#"{"type":"CodeGenerated","peer":"p","data":"ok"}"#,
    )
    .await
    .unwrap();

    server.await.unwrap();
}
