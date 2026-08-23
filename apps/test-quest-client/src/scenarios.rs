use dcvr_protocol::{NID_AUDIO_INPUT, NID_SELECTED_OBJECT};

use crate::client::FakeQuestClient;

/// Default Phase-1 demo: select an object (NID 93), send a mock transcript
/// (NID 98), and print the decoded NID 94 decision.
pub fn run_default(addr: &str) -> std::io::Result<()> {
    // Exactly 36 chars, matching the Unity peer-uuid prefix length.
    let peer = "00000000-0000-4000-8000-000000000001";
    let mut client = FakeQuestClient::connect(addr)?;
    client.send(NID_SELECTED_OBJECT, peer, b"Cube:DefaultMaterial")?;
    client.send(NID_AUDIO_INPUT, peer, b"make this cube red")?;
    let frame = client.recv_frame()?;
    let text = String::from_utf8_lossy(&frame.payload);
    println!("NID {} response: {text}", frame.network_id.b);
    Ok(())
}
