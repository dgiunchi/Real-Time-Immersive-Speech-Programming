use std::io::{Read, Write};
use std::net::TcpStream;

use dcvr_protocol::{
    decode_frame, encode_frame, join_peer_payload, NetworkFrame, NetworkId, ProtocolError,
};

/// A minimal client that frames messages exactly like the Unity/Quest client.
#[derive(Debug)]
pub struct FakeQuestClient {
    stream: TcpStream,
}

impl FakeQuestClient {
    pub fn connect(addr: &str) -> std::io::Result<Self> {
        Ok(Self {
            stream: TcpStream::connect(addr)?,
        })
    }

    /// Send `[peer uuid][body]` on the given network id.
    pub fn send(&mut self, nid: NetworkId, peer_uuid: &str, body: &[u8]) -> std::io::Result<()> {
        let payload = join_peer_payload(peer_uuid, body);
        let frame = encode_frame(nid, &payload).map_err(to_io)?;
        self.stream.write_all(&frame)
    }

    /// Block until one full frame has been received.
    pub fn recv_frame(&mut self) -> std::io::Result<NetworkFrame> {
        let mut buf = Vec::new();
        let mut tmp = [0u8; 4096];
        loop {
            match decode_frame(&buf) {
                Ok(decoded) => return Ok(decoded.frame),
                Err(ProtocolError::Incomplete { .. }) => {}
                Err(e) => return Err(to_io(e)),
            }
            let n = self.stream.read(&mut tmp)?;
            if n == 0 {
                return Err(std::io::Error::new(
                    std::io::ErrorKind::UnexpectedEof,
                    "connection closed before a full frame arrived",
                ));
            }
            buf.extend_from_slice(&tmp[..n]);
        }
    }
}

fn to_io(e: ProtocolError) -> std::io::Error {
    std::io::Error::new(std::io::ErrorKind::InvalidData, e.to_string())
}
