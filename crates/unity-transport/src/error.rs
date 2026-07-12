use thiserror::Error;

#[derive(Debug, Error)]
pub enum TransportError {
    #[error("io error: {0}")]
    Io(#[from] std::io::Error),
    #[error("protocol error: {0}")]
    Protocol(#[from] dcvr_protocol::ProtocolError),
    #[error("connection closed before room join completed")]
    ClosedBeforeJoin,
    #[error("timed out waiting for SetRoom (room join failed)")]
    JoinTimeout,
    #[error("connection closed")]
    Closed,
}
