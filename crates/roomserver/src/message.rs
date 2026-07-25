//! The Ubiq room-control message layer: parse inbound `{type, args}` control
//! frames and build the server's outbound replies.
//!
//! The envelope is JSON `{"type": string, "args": string}` where `args` is a
//! **stringified** JSON payload — Ubiq does `JSON.parse(message.args)`
//! (`roomserver.js`), so we double-encode/decode to stay wire-compatible.

use serde::{Deserialize, Serialize};

/// A Ubiq `NetworkId` as it appears in room-control JSON: `{"a":u32,"b":u32}`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct NetworkIdJson {
    pub a: u32,
    pub b: u32,
}

/// A peer descriptor as sent in `Join` and echoed in `PeerAdded`
/// (mirrors Ubiq `RoomClient.GetPeerInfo`).
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PeerInfo {
    pub uuid: String,
    pub sceneid: NetworkIdJson,
    pub clientid: NetworkIdJson,
    #[serde(default)]
    pub keys: Vec<String>,
    #[serde(default)]
    pub values: Vec<String>,
}

/// Arguments of a `Join` control message.
#[derive(Debug, Clone, Deserialize)]
pub struct JoinArgs {
    /// The room to join/create by uuid (our clients always send this).
    #[serde(default)]
    pub uuid: Option<String>,
    /// Alternative: join an existing room by its short join code.
    #[serde(default)]
    pub joincode: Option<String>,
    /// Optional human-readable room name.
    #[serde(default)]
    pub name: Option<String>,
    /// Whether the room should be discoverable.
    #[serde(default)]
    pub publish: Option<bool>,
    /// The joining peer's descriptor (required).
    pub peer: PeerInfo,
}

/// Arguments of a `Ping` keepalive control message.
#[derive(Debug, Clone, Deserialize)]
pub struct PingArgs {
    pub clientid: NetworkIdJson,
}

/// Arguments of `AppendPeerProperties` — parallel key/value arrays to upsert on
/// the sending peer. Ubiq sends them as two aligned arrays, not a map.
#[derive(Debug, Clone, Deserialize)]
pub struct AppendPeerPropertiesArgs {
    #[serde(default)]
    pub keys: Vec<String>,
    #[serde(default)]
    pub values: Vec<String>,
}

/// Arguments of `AppendRoomProperties` — parallel key/value arrays to upsert on
/// the room the sender is in.
#[derive(Debug, Clone, Deserialize)]
pub struct AppendRoomPropertiesArgs {
    #[serde(default)]
    pub keys: Vec<String>,
    #[serde(default)]
    pub values: Vec<String>,
}

/// Arguments of `DiscoverRooms` — either list publishable rooms (no joincode) or
/// look one up by its short join code.
#[derive(Debug, Clone, Default, Deserialize)]
pub struct DiscoverRoomsArgs {
    #[serde(default)]
    pub joincode: Option<String>,
}

/// Arguments of `SetBlob` — store an opaque string against a uuid.
#[derive(Debug, Clone, Deserialize)]
pub struct SetBlobArgs {
    pub uuid: String,
    #[serde(default)]
    pub blob: String,
}

/// Arguments of `GetBlob` — retrieve a previously stored blob by uuid.
#[derive(Debug, Clone, Deserialize)]
pub struct GetBlobArgs {
    pub uuid: String,
}

/// An inbound control message the RoomServer understands. Unrecognised types are
/// preserved as [`ClientMessage::Other`] so they can be logged and ignored rather
/// than dropped as hard errors.
#[derive(Debug, Clone)]
pub enum ClientMessage {
    Join(JoinArgs),
    Ping(PingArgs),
    AppendPeerProperties(AppendPeerPropertiesArgs),
    AppendRoomProperties(AppendRoomPropertiesArgs),
    DiscoverRooms(DiscoverRoomsArgs),
    SetBlob(SetBlobArgs),
    GetBlob(GetBlobArgs),
    Other(String),
}

#[derive(Debug, Deserialize)]
struct Envelope {
    #[serde(rename = "type")]
    ty: String,
    args: String,
}

/// Parse a control-frame payload (the JSON bytes carried on NID `{0,1}`) into a
/// typed [`ClientMessage`].
pub fn parse_control(payload: &[u8]) -> Result<ClientMessage, crate::RoomError> {
    let env: Envelope = serde_json::from_slice(payload)?;
    match env.ty.as_str() {
        "Join" => Ok(ClientMessage::Join(serde_json::from_str(&env.args)?)),
        "Ping" => Ok(ClientMessage::Ping(serde_json::from_str(&env.args)?)),
        "AppendPeerProperties" => Ok(ClientMessage::AppendPeerProperties(serde_json::from_str(
            &env.args,
        )?)),
        "AppendRoomProperties" => Ok(ClientMessage::AppendRoomProperties(serde_json::from_str(
            &env.args,
        )?)),
        // DiscoverRooms legitimately carries `{}` or an absent joincode.
        "DiscoverRooms" => Ok(ClientMessage::DiscoverRooms(
            serde_json::from_str(&env.args).unwrap_or_default(),
        )),
        "SetBlob" => Ok(ClientMessage::SetBlob(serde_json::from_str(&env.args)?)),
        "GetBlob" => Ok(ClientMessage::GetBlob(serde_json::from_str(&env.args)?)),
        other => Ok(ClientMessage::Other(other.to_string())),
    }
}

/// Serialise a server->client control message into the `{type, args}` envelope
/// bytes, with `args` stringified (matching Ubiq's `Message.Create`).
pub fn build_server_message(
    ty: &str,
    args: &serde_json::Value,
) -> Result<Vec<u8>, crate::RoomError> {
    let env = serde_json::json!({
        "type": ty,
        "args": serde_json::to_string(args)?,
    });
    Ok(serde_json::to_vec(&env)?)
}
