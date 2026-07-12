use crate::error::ProtocolError;
use crate::network_id::NetworkId;

/// 4-byte little-endian length prefix.
pub const LENGTH_PREFIX_LEN: usize = 4;
/// 8-byte NetworkId (a:u32 LE, b:u32 LE).
pub const NETWORK_ID_LEN: usize = 8;
/// Bytes before the payload: length prefix + NetworkId.
pub const HEADER_LEN: usize = LENGTH_PREFIX_LEN + NETWORK_ID_LEN;

/// Hard upper bound on a single payload (fail-closed anti-DoS guard).
pub const MAX_PAYLOAD_LEN: usize = 1 << 20; // 1 MiB

/// A decoded frame: a network id plus its raw payload bytes.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NetworkFrame {
    pub network_id: NetworkId,
    pub payload: Vec<u8>,
}

/// Result of decoding: the frame plus how many bytes it consumed from the
/// front of the buffer (so a stream reader can `drain(0..consumed)`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DecodedFrame {
    pub frame: NetworkFrame,
    pub consumed: usize,
}

/// Ubiq wire format (verified against `com.ucl.ubiq` TCP.cs + NetworkGameObject.cs):
///   `[u32 LE body_len][u32 LE a][u32 LE b][payload]`
///
/// where **`body_len = 8 + payload.len()`** — the length prefix counts the
/// NetworkId (8 bytes) PLUS the payload, i.e. the whole message body that follows
/// the prefix (matching Ubiq's `m.length` = NetworkId + scene-graph payload). The
/// receiver reads `body_len` bytes, strips the 8-byte NetworkId, and the rest is
/// the payload.
pub fn encode_frame(network_id: NetworkId, payload: &[u8]) -> Result<Vec<u8>, ProtocolError> {
    if payload.len() > MAX_PAYLOAD_LEN {
        return Err(ProtocolError::PayloadTooLarge {
            declared: payload.len(),
            max: MAX_PAYLOAD_LEN,
        });
    }
    // body = NetworkId (8) + payload. `body_len <= 8 + 1 MiB` fits in u32.
    let body_len = (NETWORK_ID_LEN + payload.len()) as u32;
    let mut out = Vec::with_capacity(LENGTH_PREFIX_LEN + body_len as usize);
    out.extend_from_slice(&body_len.to_le_bytes());
    out.extend_from_slice(&network_id.a.to_le_bytes());
    out.extend_from_slice(&network_id.b.to_le_bytes());
    out.extend_from_slice(payload);
    Ok(out)
}

fn read_u32_le(buf: &[u8], offset: usize) -> Result<u32, ProtocolError> {
    let end = offset
        .checked_add(4)
        .ok_or(ProtocolError::MalformedHeader)?;
    let slice = buf.get(offset..end).ok_or(ProtocolError::MalformedHeader)?;
    let arr: [u8; 4] = slice
        .try_into()
        .map_err(|_| ProtocolError::MalformedHeader)?;
    Ok(u32::from_le_bytes(arr))
}

/// Attempt to decode one frame from the front of `buf`. Never panics.
pub fn decode_frame(buf: &[u8]) -> Result<DecodedFrame, ProtocolError> {
    if buf.len() < LENGTH_PREFIX_LEN {
        return Err(ProtocolError::Incomplete {
            needed: LENGTH_PREFIX_LEN,
            have: buf.len(),
        });
    }
    let body_len = read_u32_le(buf, 0)? as usize;
    // The body must at least contain the 8-byte NetworkId.
    if body_len < NETWORK_ID_LEN {
        return Err(ProtocolError::MalformedHeader);
    }
    let payload_len = body_len - NETWORK_ID_LEN;
    if payload_len > MAX_PAYLOAD_LEN {
        return Err(ProtocolError::PayloadTooLarge {
            declared: payload_len,
            max: MAX_PAYLOAD_LEN,
        });
    }
    let total = LENGTH_PREFIX_LEN
        .checked_add(body_len)
        .ok_or(ProtocolError::MalformedHeader)?;
    if buf.len() < total {
        return Err(ProtocolError::Incomplete {
            needed: total,
            have: buf.len(),
        });
    }
    let a = read_u32_le(buf, 4)?;
    let b = read_u32_le(buf, 8)?;
    let payload = buf
        .get(HEADER_LEN..total)
        .ok_or(ProtocolError::MalformedHeader)?
        .to_vec();
    Ok(DecodedFrame {
        frame: NetworkFrame {
            network_id: NetworkId::new(a, b),
            payload,
        },
        consumed: total,
    })
}
