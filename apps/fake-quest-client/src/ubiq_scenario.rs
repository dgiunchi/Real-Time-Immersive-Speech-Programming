//! Real-Ubiq peer scenario: join the room, send NID 93 selection + NID 98
//! push-to-talk (start / transcript / stop), and await the NID 94 decision.
use std::time::Duration;

use dcvr_protocol::{join_peer_payload, NID_AUDIO_INPUT, NID_SELECTED_OBJECT};
use dcvr_unity_transport::UbiqServicePeer;

pub async fn run(addr: &str, room: &str, command: &str) -> Result<(), String> {
    let peer = "00000000-0000-4000-8000-0000000000ab"; // 36 chars
    let mut conn = UbiqServicePeer::connect_and_join(addr, room)
        .await
        .map_err(|e| format!("join failed: {e}"))?;
    eprintln!("fake-quest: joined room {room} as a Ubiq peer");
    eprintln!("fake-quest: command (the 'spoken' transcript) = {command:?}");

    conn.send(
        NID_SELECTED_OBJECT,
        &join_peer_payload(peer, b"Cube:DefaultMaterial"),
    )
    .await
    .map_err(|e| e.to_string())?;
    conn.send(
        NID_AUDIO_INPUT,
        &join_peer_payload(peer, b"__STT_CONTROL__:start"),
    )
    .await
    .map_err(|e| e.to_string())?;
    conn.send(
        NID_AUDIO_INPUT,
        &join_peer_payload(peer, command.as_bytes()),
    )
    .await
    .map_err(|e| e.to_string())?;
    conn.send(
        NID_AUDIO_INPUT,
        &join_peer_payload(peer, b"__STT_CONTROL__:stop"),
    )
    .await
    .map_err(|e| e.to_string())?;
    eprintln!("fake-quest: sent selection + push-to-talk; waiting for NID 94 ...");

    let got = tokio::time::timeout(Duration::from_secs(30), async {
        loop {
            match conn.recv().await {
                Some(frame) if frame.network_id.b == 94 => {
                    return Some(String::from_utf8_lossy(&frame.payload).to_string());
                }
                Some(_) => continue,
                None => return None,
            }
        }
    })
    .await;

    match got {
        Ok(Some(text)) => {
            println!("NID 94 (via real Ubiq RoomServer): {text}");
            Ok(())
        }
        Ok(None) => Err("connection closed before NID 94".into()),
        Err(_) => Err("timed out waiting for NID 94".into()),
    }
}
