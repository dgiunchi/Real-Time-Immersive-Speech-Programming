use dcvr_unity_transport::UbiqServicePeer;

/// Usage: ubiq-probe [ADDR] [ROOM_GUID]
///   ADDR       default 127.0.0.1:8009 (the real Node RoomServer / runbook port)
///   ROOM_GUID  default 6765c52b-3ad6-4fb0-9030-2c9a05dc4731
///
/// Run the original Node RoomServer + Unity, then this probe, to confirm the Rust
/// codec + room-join work against REAL Ubiq and to see live NID 98 audio frames.
#[tokio::main]
async fn main() {
    let args: Vec<String> = std::env::args().collect();
    let addr = args
        .get(1)
        .cloned()
        .unwrap_or_else(|| "127.0.0.1:8009".to_string());
    let guid = args
        .get(2)
        .cloned()
        .unwrap_or_else(|| "6765c52b-3ad6-4fb0-9030-2c9a05dc4731".to_string());

    eprintln!("ubiq-probe: connecting to {addr}, joining room {guid} ...");
    let mut peer = match UbiqServicePeer::connect_and_join(&addr, &guid).await {
        Ok(p) => {
            eprintln!(
                "JOINED OK. Listening for NID 98 (audio) / NID 93 (selection). Ctrl-C to stop."
            );
            p
        }
        Err(e) => {
            eprintln!("JOIN FAILED: {e}");
            eprintln!(
                "(check the RoomServer addr/port and that the room guid matches RoomJoiner.Guid)"
            );
            std::process::exit(1);
        }
    };

    while let Some(frame) = peer.recv().await {
        let nid = frame.network_id.b;
        let body = &frame.payload;
        let preview: String = body
            .iter()
            .take(56)
            .map(|b| {
                if b.is_ascii_graphic() || *b == b' ' {
                    *b as char
                } else {
                    '.'
                }
            })
            .collect();
        println!("NID {nid}: {} bytes | {preview}", body.len());
    }
    eprintln!("connection closed.");
}
