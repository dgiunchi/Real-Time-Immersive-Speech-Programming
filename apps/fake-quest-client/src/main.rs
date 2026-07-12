mod client;
mod scenarios;
mod ubiq_scenario;

use std::fs;

use dcvr_protocol::{
    encode_frame, join_peer_payload, ProtocolError, NID_AUDIO_INPUT, NID_BACKEND_OUTPUT,
    NID_SELECTED_OBJECT,
};

fn main() {
    let args: Vec<String> = std::env::args().collect();

    if args.get(1).map(String::as_str) == Some("--ubiq") {
        let addr = args
            .get(2)
            .cloned()
            .unwrap_or_else(|| "127.0.0.1:8009".to_string());
        let room = args
            .get(3)
            .cloned()
            .unwrap_or_else(|| "6765c52b-3ad6-4fb0-9030-2c9a05dc4731".to_string());
        // optional 4th arg: the "spoken" command (default keeps the prior behaviour)
        let command = args
            .get(4)
            .cloned()
            .unwrap_or_else(|| "make this cube red".to_string());
        let rt = match tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
        {
            Ok(rt) => rt,
            Err(e) => {
                eprintln!("runtime error: {e}");
                std::process::exit(1);
            }
        };
        if let Err(e) = rt.block_on(ubiq_scenario::run(&addr, &room, &command)) {
            eprintln!("fake-quest ubiq error: {e}");
            std::process::exit(1);
        }
        return;
    }

    if args.get(1).map(String::as_str) == Some("--emit-fixtures") {
        let dir = args
            .get(2)
            .map(String::as_str)
            .unwrap_or("fixtures/protocol");
        if let Err(e) = emit_fixtures(dir) {
            eprintln!("emit-fixtures error: {e}");
            std::process::exit(1);
        }
        println!("wrote protocol fixtures to {dir}");
        return;
    }

    let addr = args
        .get(1)
        .cloned()
        .unwrap_or_else(|| "127.0.0.1:9098".to_string());
    if let Err(e) = scenarios::run_default(&addr) {
        eprintln!("fake-quest-client error: {e}");
        std::process::exit(1);
    }
}

fn emit_fixtures(dir: &str) -> std::io::Result<()> {
    fs::create_dir_all(dir)?;
    let peer = "00000000-0000-4000-8000-000000000001";

    let nid93 = encode_frame(
        NID_SELECTED_OBJECT,
        &join_peer_payload(peer, b"Cube:DefaultMaterial"),
    )
    .map_err(to_io)?;
    fs::write(format!("{dir}/valid_nid93_selected_object.bin"), nid93)?;

    let nid98 = encode_frame(
        NID_AUDIO_INPUT,
        &join_peer_payload(peer, b"make this cube red"),
    )
    .map_err(to_io)?;
    fs::write(format!("{dir}/valid_nid98_mock_audio.bin"), nid98)?;

    let plan = r##"{"type":"BackendDecision","request_id":"demo","decision":"ApproveActionPlan","action_plan":{"schema_version":"1.0","request_id":"demo","target":"selected_object","actions":[{"type":"set_color","color":"#FF0000"}]},"errors":[]}"##;
    let nid94 = encode_frame(NID_BACKEND_OUTPUT, plan.as_bytes()).map_err(to_io)?;
    fs::write(format!("{dir}/valid_nid94_action_plan.bin"), nid94)?;
    Ok(())
}

fn to_io(e: ProtocolError) -> std::io::Error {
    std::io::Error::new(std::io::ErrorKind::InvalidData, e.to_string())
}
