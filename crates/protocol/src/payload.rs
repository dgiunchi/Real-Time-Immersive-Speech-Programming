use crate::error::ProtocolError;

/// Length of the ASCII peer UUID prefix that Unity prepends to NID 98 / NID 93
/// payloads (matches the original DreamCodeVR client, e.g. `MicrophoneCapture`).
pub const PEER_UUID_LEN: usize = 36;

/// A payload split into its peer-UUID prefix and the remaining body bytes.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PeerPayload {
    pub peer_uuid: String,
    pub body: Vec<u8>,
}

/// Split a `[36-byte peer uuid][body...]` payload. Fail-closed: rejects payloads
/// shorter than the UUID prefix or whose prefix is not valid UTF-8.
pub fn split_peer_payload(payload: &[u8]) -> Result<PeerPayload, ProtocolError> {
    let uuid_bytes = payload
        .get(0..PEER_UUID_LEN)
        .ok_or(ProtocolError::Incomplete {
            needed: PEER_UUID_LEN,
            have: payload.len(),
        })?;
    let peer_uuid =
        String::from_utf8(uuid_bytes.to_vec()).map_err(|_| ProtocolError::MalformedHeader)?;
    let body = payload.get(PEER_UUID_LEN..).unwrap_or(&[]).to_vec();
    Ok(PeerPayload { peer_uuid, body })
}

/// Build a `[36-byte peer uuid][body...]` payload (inverse of `split_peer_payload`).
pub fn join_peer_payload(peer_uuid: &str, body: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(peer_uuid.len() + body.len());
    out.extend_from_slice(peer_uuid.as_bytes());
    out.extend_from_slice(body);
    out
}
