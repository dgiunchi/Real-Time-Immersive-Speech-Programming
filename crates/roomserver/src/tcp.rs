//! Async TCP transport for the RoomServer.
//!
//! This layer owns the sockets and drives the pure [`crate::RoomServer`] state
//! machine. Each accepted connection gets a monotonic [`ConnId`] and a writer
//! task fed by an mpsc queue. The reader decodes Ubiq frames, hands control
//! frames (NID `{0,1}`) and application frames to the state machine, and fans
//! the resulting [`Outbound`]s out to the target connections' writers. The
//! state machine is held behind a `tokio::sync::Mutex`; it is never locked
//! across an `.await` on a socket, so one slow peer cannot stall the room.

use std::collections::HashMap;
use std::net::SocketAddr;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;

use dcvr_protocol::{decode_frame, ProtocolError};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::{mpsc, Mutex};

use crate::state::ROOMSERVER_ID;
use crate::{ConnId, Outbound, RoomServer};

const READ_CHUNK: usize = 8192;
const WRITE_QUEUE: usize = 256;

struct Shared {
    rooms: Mutex<RoomServer>,
    conns: Mutex<HashMap<ConnId, mpsc::Sender<Vec<u8>>>>,
    next: AtomicU64,
}

impl Shared {
    fn new() -> Self {
        Self {
            rooms: Mutex::new(RoomServer::new()),
            conns: Mutex::new(HashMap::new()),
            next: AtomicU64::new(1),
        }
    }

    /// Deliver each outbound frame to its target connection's writer. Sender
    /// handles are cloned under the lock and awaited after release, so the
    /// registry is never held across a send.
    async fn dispatch(&self, outs: Vec<Outbound>) {
        if outs.is_empty() {
            return;
        }
        let jobs: Vec<(mpsc::Sender<Vec<u8>>, Vec<u8>)> = {
            let conns = self.conns.lock().await;
            outs.into_iter()
                .filter_map(|o| conns.get(&o.to).map(|tx| (tx.clone(), o.bytes)))
                .collect()
        };
        for (tx, bytes) in jobs {
            let _ = tx.send(bytes).await;
        }
    }
}

/// A running RoomServer TCP endpoint. Dropping it stops the accept loop; live
/// connections end when their peers disconnect.
pub struct RoomServerHandle {
    local_addr: SocketAddr,
    accept_task: tokio::task::JoinHandle<()>,
    shared: Arc<Shared>,
}

impl RoomServerHandle {
    /// Bind and start accepting on `addr` (e.g. `"0.0.0.0:8009"`, or
    /// `"127.0.0.1:0"` for an ephemeral test port).
    pub async fn bind(addr: &str) -> std::io::Result<Self> {
        let listener = TcpListener::bind(addr).await?;
        let local_addr = listener.local_addr()?;
        let shared = Arc::new(Shared::new());
        let accept_task = tokio::spawn(accept_loop(listener, shared.clone()));
        Ok(Self {
            local_addr,
            accept_task,
            shared,
        })
    }

    /// The address actually bound (resolves an ephemeral `:0` port).
    pub fn local_addr(&self) -> SocketAddr {
        self.local_addr
    }

    /// Number of active rooms (observability / tests).
    pub async fn room_count(&self) -> usize {
        self.shared.rooms.lock().await.room_count()
    }
}

impl Drop for RoomServerHandle {
    fn drop(&mut self) {
        self.accept_task.abort();
    }
}

async fn accept_loop(listener: TcpListener, shared: Arc<Shared>) {
    loop {
        match listener.accept().await {
            Ok((stream, _peer)) => {
                let id = shared.next.fetch_add(1, Ordering::Relaxed);
                let sh = shared.clone();
                tokio::spawn(async move {
                    handle_conn(sh, id, stream).await;
                });
            }
            Err(_) => {
                // Transient accept error: yield and keep serving.
                tokio::task::yield_now().await;
            }
        }
    }
}

async fn handle_conn(shared: Arc<Shared>, conn: ConnId, stream: TcpStream) {
    let _ = stream.set_nodelay(true);
    let (mut rd, mut wr) = stream.into_split();

    let (tx, mut rx) = mpsc::channel::<Vec<u8>>(WRITE_QUEUE);
    shared.conns.lock().await.insert(conn, tx);

    let writer = tokio::spawn(async move {
        while let Some(bytes) = rx.recv().await {
            if wr.write_all(&bytes).await.is_err() {
                break;
            }
        }
    });

    let mut buf: Vec<u8> = Vec::new();
    let mut tmp = [0u8; READ_CHUNK];
    'read: loop {
        // Drain every complete frame currently buffered.
        loop {
            match decode_frame(&buf) {
                Ok(d) => {
                    let consumed = d.consumed;
                    let frame = d.frame;
                    buf.drain(0..consumed);
                    let outs = {
                        let mut rooms = shared.rooms.lock().await;
                        if frame.network_id == ROOMSERVER_ID {
                            rooms.on_control(conn, &frame.payload)
                        } else {
                            rooms.on_app_frame(conn, &frame)
                        }
                    };
                    // A malformed control message is ignored (peer stays up);
                    // corrupt framing is caught by the decode error arm below.
                    if let Ok(o) = outs {
                        shared.dispatch(o).await;
                    }
                }
                Err(ProtocolError::Incomplete { .. }) => break,
                Err(_) => break 'read, // corrupt framing: drop the connection
            }
        }
        match rd.read(&mut tmp).await {
            Ok(0) | Err(_) => break,
            Ok(n) => buf.extend_from_slice(&tmp[..n]),
        }
    }

    // Connection gone: announce departure and clean up.
    let outs = { shared.rooms.lock().await.on_disconnect(conn) };
    if let Ok(o) = outs {
        shared.dispatch(o).await;
    }
    shared.conns.lock().await.remove(&conn);
    writer.abort();
}
