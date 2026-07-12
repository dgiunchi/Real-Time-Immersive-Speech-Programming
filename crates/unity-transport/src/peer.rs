use std::time::Duration;

use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;
use tokio::sync::mpsc;
use tokio::time::timeout;

use dcvr_protocol::{decode_frame, encode_frame, NetworkFrame, NetworkId, ProtocolError};

use crate::error::TransportError;
use crate::ids::PeerInfo;

/// Reserved RoomServer control object id (Ubiq `RoomClient.roomServerObjectId`).
const NID_ROOMSERVER: NetworkId = NetworkId::new(0, 1);
const NID_AUDIO: u32 = 98;
const NID_SELECTION: u32 = 93;
const NID_OUTPUT: u32 = 94;

/// A connected, room-joined Ubiq service peer. Mirrors what the Node Genie app
/// does (connect to RoomServer:port -> Join(roomGuid)). After join, app frames
/// (NID 98 audio, NID 93 selection) arrive via [`recv`]; results go out via
/// [`send`] (NID 94). Control (Ping/SetRoom/Peer*) is handled internally.
#[derive(Debug)]
pub struct UbiqServicePeer {
    write_tx: mpsc::Sender<Vec<u8>>,
    incoming_rx: mpsc::Receiver<NetworkFrame>,
}

fn frame_type(frame: &NetworkFrame) -> Option<String> {
    serde_json::from_slice::<serde_json::Value>(&frame.payload)
        .ok()
        .and_then(|v| v.get("type").and_then(|t| t.as_str()).map(str::to_string))
}

impl UbiqServicePeer {
    /// Connect to the RoomServer and join the room. Returns once `SetRoom` is
    /// received (the peer is then a room member) or fails (timeout/closed).
    pub async fn connect_and_join(addr: &str, room_guid: &str) -> Result<Self, TransportError> {
        let stream = TcpStream::connect(addr).await?;
        let (mut read_half, mut write_half) = stream.into_split();

        let peer = PeerInfo::random("RustService");
        // The RoomServer does JSON.parse(message.args), so `args` must be a STRING
        // of JSON, not a nested object (roomserver.js:401).
        let join_args =
            serde_json::json!({ "uuid": room_guid, "peer": peer.to_json() }).to_string();
        let join = serde_json::json!({ "type": "Join", "args": join_args });
        write_half
            .write_all(&encode_frame(NID_ROOMSERVER, join.to_string().as_bytes())?)
            .await?;

        // Read frames until SetRoom (or timeout). Keep leftover bytes for the reader.
        let mut buf: Vec<u8> = Vec::new();
        let mut tmp = [0u8; 8192];
        let join_result = timeout(Duration::from_secs(10), async {
            loop {
                loop {
                    match decode_frame(&buf) {
                        Ok(d) => {
                            let consumed = d.consumed;
                            let is_set_room = frame_type(&d.frame).as_deref() == Some("SetRoom");
                            buf.drain(0..consumed);
                            if is_set_room {
                                return Ok::<(), TransportError>(());
                            }
                        }
                        Err(ProtocolError::Incomplete { .. }) => break,
                        Err(e) => return Err(TransportError::Protocol(e)),
                    }
                }
                let n = read_half.read(&mut tmp).await?;
                if n == 0 {
                    return Err(TransportError::ClosedBeforeJoin);
                }
                buf.extend_from_slice(&tmp[..n]);
            }
        })
        .await
        .map_err(|_| TransportError::JoinTimeout)?;
        join_result?;

        let (write_tx, mut write_rx) = mpsc::channel::<Vec<u8>>(256);
        let (incoming_tx, incoming_rx) = mpsc::channel::<NetworkFrame>(256);

        // writer task
        tokio::spawn(async move {
            while let Some(bytes) = write_rx.recv().await {
                if write_half.write_all(&bytes).await.is_err() {
                    break;
                }
            }
        });

        // ping keepalive (1s, mirroring Unity RoomClient)
        let ping_tx = write_tx.clone();
        let clientid = peer.clientid;
        tokio::spawn(async move {
            let mut tick = tokio::time::interval(Duration::from_secs(1));
            loop {
                tick.tick().await;
                let ping_args =
                    serde_json::json!({ "clientid": { "a": clientid.a, "b": clientid.b } })
                        .to_string();
                let ping = serde_json::json!({ "type": "Ping", "args": ping_args });
                match encode_frame(NID_ROOMSERVER, ping.to_string().as_bytes()) {
                    Ok(bytes) => {
                        if ping_tx.send(bytes).await.is_err() {
                            break;
                        }
                    }
                    Err(_) => break,
                }
            }
        });

        // reader task: route app NIDs to `incoming`, drop control frames.
        tokio::spawn(async move {
            let mut buf = buf; // leftover after join
            let mut tmp = [0u8; 8192];
            loop {
                loop {
                    match decode_frame(&buf) {
                        Ok(d) => {
                            let consumed = d.consumed;
                            let b = d.frame.network_id.b;
                            if (b == NID_AUDIO || b == NID_SELECTION || b == NID_OUTPUT)
                                && incoming_tx.send(d.frame).await.is_err()
                            {
                                return;
                            }
                            buf.drain(0..consumed);
                        }
                        Err(ProtocolError::Incomplete { .. }) => break,
                        Err(_) => {
                            buf.clear();
                            break;
                        }
                    }
                }
                match read_half.read(&mut tmp).await {
                    Ok(0) | Err(_) => return,
                    Ok(n) => buf.extend_from_slice(&tmp[..n]),
                }
            }
        });

        Ok(Self {
            write_tx,
            incoming_rx,
        })
    }

    /// Receive the next app frame (NID 98 audio or NID 93 selection).
    pub async fn recv(&mut self) -> Option<NetworkFrame> {
        self.incoming_rx.recv().await
    }

    /// Send a payload on a NetworkId (e.g. NID 94 backend output).
    pub async fn send(&self, network_id: NetworkId, payload: &[u8]) -> Result<(), TransportError> {
        self.sender().send(network_id, payload).await
    }

    /// A cheaply-cloneable sender handle that can be moved into spawned tasks so
    /// per-utterance processing can emit NID 94 without holding the recv loop.
    pub fn sender(&self) -> PeerSender {
        PeerSender {
            write_tx: self.write_tx.clone(),
        }
    }
}

/// Cloneable handle for sending frames on a joined peer connection.
#[derive(Debug, Clone)]
pub struct PeerSender {
    write_tx: mpsc::Sender<Vec<u8>>,
}

impl PeerSender {
    pub async fn send(&self, network_id: NetworkId, payload: &[u8]) -> Result<(), TransportError> {
        let bytes = encode_frame(network_id, payload)?;
        self.write_tx
            .send(bytes)
            .await
            .map_err(|_| TransportError::Closed)
    }
}
