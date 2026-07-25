//! The RoomServer state machine: pure, I/O-free room/peer bookkeeping.
//!
//! Given an inbound control message or an application frame from a connection,
//! it mutates state and returns a list of [`Outbound`] frames, each already
//! encoded and addressed to a connection id. The async TCP layer (Phase 2) owns
//! the sockets and simply moves those bytes; keeping this layer synchronous and
//! deterministic makes the protocol unit-testable.

use std::collections::HashMap;

use dcvr_protocol::{encode_frame, NetworkFrame, NetworkId};

use crate::message::{build_server_message, ClientMessage, JoinArgs, NetworkIdJson, PeerInfo};
use crate::RoomError;

/// The reserved Ubiq RoomServer control channel (`RoomServerReservedId = 1`).
pub const ROOMSERVER_ID: NetworkId = NetworkId::new(0, 1);

/// An opaque per-connection handle assigned by the transport layer.
pub type ConnId = u64;

/// One encoded frame to send to one connection.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Outbound {
    pub to: ConnId,
    pub bytes: Vec<u8>,
}

struct Peer {
    info: PeerInfo,
    /// The NetworkId the client listens on for room-control replies (Ubiq
    /// addresses `SetRoom`/`PeerAdded`/… to the peer's `clientid`).
    clientid: NetworkId,
    room: Option<String>,
}

struct Room {
    uuid: String,
    joincode: String,
    name: String,
    publish: bool,
    members: Vec<ConnId>,
    /// Room-scoped key/value properties, kept as two aligned arrays because that
    /// is how Ubiq serialises them on the wire.
    keys: Vec<String>,
    values: Vec<String>,
}

/// The in-memory room registry. Not `Clone`; the transport layer holds a single
/// instance behind a lock.
#[derive(Default)]
pub struct RoomServer {
    peers: HashMap<ConnId, Peer>,
    rooms: HashMap<String, Room>,
    /// Opaque client-stored blobs, keyed by uuid (Ubiq `SetBlob`/`GetBlob`).
    blobs: HashMap<String, String>,
}

/// The `version` string reported in a `Rooms` discovery response.
const ROOMS_VERSION: &str = "1.0";

/// Upsert parallel key/value arrays into an existing pair of arrays, keeping the
/// two in lockstep. A repeated key overwrites; a new key is appended.
fn upsert_props(
    keys: &mut Vec<String>,
    values: &mut Vec<String>,
    new_keys: &[String],
    new_values: &[String],
) {
    // Defensive: an earlier malformed update could have desynced the arrays.
    values.resize(keys.len(), String::new());
    for (i, k) in new_keys.iter().enumerate() {
        let v = new_values.get(i).cloned().unwrap_or_default();
        match keys.iter().position(|existing| existing == k) {
            Some(idx) => {
                if let Some(slot) = values.get_mut(idx) {
                    *slot = v;
                }
            }
            None => {
                keys.push(k.clone());
                values.push(v);
            }
        }
    }
}

fn to_network_id(j: NetworkIdJson) -> NetworkId {
    NetworkId::new(j.a, j.b)
}

/// Whether `s` is an RFC 4122 **version 4** UUID in canonical 8-4-4-4-12 form.
///
/// Upstream Ubiq rejects a Join whose room uuid is not a v4 UUID ("we were
/// expecting an RFC4122 v4 uuid"), so a stock client relies on that contract; we
/// mirror it rather than silently accepting an arbitrary string as a room id.
fn is_rfc4122_v4(s: &str) -> bool {
    let b = s.as_bytes();
    if b.len() != 36 {
        return false;
    }
    for (i, c) in b.iter().enumerate() {
        match i {
            8 | 13 | 18 | 23 => {
                if *c != b'-' {
                    return false;
                }
            }
            // Version nibble: must be '4'.
            14 => {
                if *c != b'4' {
                    return false;
                }
            }
            // Variant nibble: one of 8, 9, a, b.
            19 => {
                if !matches!(c.to_ascii_lowercase(), b'8' | b'9' | b'a' | b'b') {
                    return false;
                }
            }
            _ => {
                if !c.is_ascii_hexdigit() {
                    return false;
                }
            }
        }
    }
    true
}

/// A deterministic 3-char join code derived from the room uuid (Phase 1). The
/// upstream server picks a random code; a stable derivation is fine until room
/// discovery by join code is wired up.
fn derive_joincode(uuid: &str) -> String {
    uuid.chars()
        .filter(|c| c.is_ascii_alphanumeric())
        .take(3)
        .collect()
}

fn room_info_json(room: &Room) -> serde_json::Value {
    serde_json::json!({
        "uuid": room.uuid,
        "joincode": room.joincode,
        "publish": room.publish,
        "name": room.name,
        "keys": room.keys,
        "values": room.values,
    })
}

fn peer_info_json(p: &PeerInfo) -> Result<serde_json::Value, RoomError> {
    Ok(serde_json::to_value(p)?)
}

impl RoomServer {
    pub fn new() -> Self {
        Self::default()
    }

    /// Number of active rooms (observability / tests).
    pub fn room_count(&self) -> usize {
        self.rooms.len()
    }

    /// Number of joined peers (observability / tests).
    pub fn peer_count(&self) -> usize {
        self.peers.len()
    }

    /// Handle an inbound control frame (carried on NID `{0,1}`) from `conn`.
    pub fn on_control(&mut self, conn: ConnId, payload: &[u8]) -> Result<Vec<Outbound>, RoomError> {
        match crate::message::parse_control(payload)? {
            ClientMessage::Join(args) => self.on_join(conn, args),
            ClientMessage::Ping(_) => self.on_ping(conn),
            ClientMessage::AppendPeerProperties(a) => {
                self.on_append_peer_properties(conn, &a.keys, &a.values)
            }
            ClientMessage::AppendRoomProperties(a) => {
                self.on_append_room_properties(conn, &a.keys, &a.values)
            }
            ClientMessage::DiscoverRooms(a) => self.on_discover_rooms(conn, a.joincode.as_deref()),
            ClientMessage::SetBlob(a) => {
                self.blobs.insert(a.uuid, a.blob);
                Ok(Vec::new())
            }
            ClientMessage::GetBlob(a) => self.on_get_blob(conn, &a.uuid),
            ClientMessage::Other(_) => Ok(Vec::new()),
        }
    }

    fn on_join(&mut self, conn: ConnId, args: JoinArgs) -> Result<Vec<Outbound>, RoomError> {
        let clientid = to_network_id(args.peer.clientid);

        // Resolve which room this Join is for, mirroring upstream Ubiq's contract.
        // A `Rejected` reply needs an address, and the peer is not registered yet,
        // so it goes back on the reserved control channel via `control_reply`.
        let joincode = args.joincode.as_deref().filter(|c| !c.trim().is_empty());
        let uuid_arg = args.uuid.as_deref().filter(|u| !u.trim().is_empty());
        let room_uuid = if let Some(code) = joincode {
            // Join-by-code only ever joins an EXISTING room; an unknown code is a
            // rejection, never a silent room creation.
            match self.rooms.values().find(|r| r.joincode == code) {
                Some(r) => r.uuid.clone(),
                None => {
                    return Ok(vec![self.control_reply(
                        conn,
                        "Rejected",
                        &serde_json::json!({
                            "reason": format!("join code {code} not found"),
                            "joinArgs": { "joincode": code },
                        }),
                    )?])
                }
            }
        } else if let Some(uuid) = uuid_arg {
            if !is_rfc4122_v4(uuid) {
                return Ok(vec![self.control_reply(
                    conn,
                    "Rejected",
                    &serde_json::json!({
                        "reason": "we were expecting an RFC4122 v4 uuid",
                        "joinArgs": { "uuid": uuid },
                    }),
                )?]);
            }
            uuid.to_string()
        } else {
            // Neither given: fall back to the peer's own uuid so the peer lands in a
            // private room of its own rather than failing outright.
            args.peer.uuid.clone()
        };

        // Resolve or create the room, then record membership.
        let (room_info, existing_members) = {
            let room = self.rooms.entry(room_uuid.clone()).or_insert_with(|| Room {
                uuid: room_uuid.clone(),
                joincode: derive_joincode(&room_uuid),
                name: args.name.clone().unwrap_or_default(),
                publish: args.publish.unwrap_or(false),
                members: Vec::new(),
                keys: Vec::new(),
                values: Vec::new(),
            });
            let existing = room.members.clone();
            room.members.push(conn);
            (room_info_json(room), existing)
        };

        // Register the newcomer before addressing frames to it.
        self.peers.insert(
            conn,
            Peer {
                info: args.peer.clone(),
                clientid,
                room: Some(room_uuid),
            },
        );

        let newcomer_info = peer_info_json(&args.peer)?;
        let mut out = Vec::new();

        // 1) Confirm the room to the newcomer.
        out.push(self.control_to(conn, "SetRoom", &serde_json::json!({ "room": room_info }))?);

        // 2) Introduce existing peers to the newcomer, and the newcomer to them.
        for existing in existing_members {
            let existing_info = match self.peers.get(&existing) {
                Some(p) => peer_info_json(&p.info)?,
                None => continue,
            };
            out.push(self.control_to(
                conn,
                "PeerAdded",
                &serde_json::json!({ "peer": existing_info }),
            )?);
            out.push(self.control_to(
                existing,
                "PeerAdded",
                &serde_json::json!({ "peer": newcomer_info.clone() }),
            )?);
        }

        Ok(out)
    }

    fn on_ping(&mut self, conn: ConnId) -> Result<Vec<Outbound>, RoomError> {
        // Only a registered (joined) peer has a clientid to reply on; an
        // unjoined connection's ping is a harmless no-op.
        if !self.peers.contains_key(&conn) {
            return Ok(Vec::new());
        }
        let session = conn.to_string();
        Ok(vec![self.control_to(
            conn,
            "Ping",
            &serde_json::json!({ "sessionId": session }),
        )?])
    }

    /// `AppendPeerProperties`: upsert the sender's own key/value properties and
    /// tell the rest of the room. The sender is not echoed — it set the value and
    /// already knows it.
    fn on_append_peer_properties(
        &mut self,
        conn: ConnId,
        keys: &[String],
        values: &[String],
    ) -> Result<Vec<Outbound>, RoomError> {
        let (uuid, room_uuid) = {
            let peer = match self.peers.get_mut(&conn) {
                Some(p) => p,
                None => return Ok(Vec::new()),
            };
            upsert_props(&mut peer.info.keys, &mut peer.info.values, keys, values);
            (peer.info.uuid.clone(), peer.room.clone())
        };
        let room_uuid = match room_uuid {
            Some(r) => r,
            None => return Ok(Vec::new()),
        };
        let members = match self.rooms.get(&room_uuid) {
            Some(r) => r.members.clone(),
            None => return Ok(Vec::new()),
        };
        let args = serde_json::json!({ "uuid": uuid, "keys": keys, "values": values });
        let mut out = Vec::new();
        for member in members {
            if member != conn {
                out.push(self.control_to(member, "PeerPropertiesAppended", &args)?);
            }
        }
        Ok(out)
    }

    /// `AppendRoomProperties`: upsert the room's shared properties and tell every
    /// member, including the sender — the room state is authoritative, so all
    /// peers converge on the server's view.
    fn on_append_room_properties(
        &mut self,
        conn: ConnId,
        keys: &[String],
        values: &[String],
    ) -> Result<Vec<Outbound>, RoomError> {
        let room_uuid = match self.peers.get(&conn).and_then(|p| p.room.clone()) {
            Some(r) => r,
            None => return Ok(Vec::new()),
        };
        let members = {
            let room = match self.rooms.get_mut(&room_uuid) {
                Some(r) => r,
                None => return Ok(Vec::new()),
            };
            upsert_props(&mut room.keys, &mut room.values, keys, values);
            room.members.clone()
        };
        let args = serde_json::json!({ "keys": keys, "values": values });
        let mut out = Vec::new();
        for member in members {
            out.push(self.control_to(member, "RoomPropertiesAppended", &args)?);
        }
        Ok(out)
    }

    /// `DiscoverRooms`: with a joincode, return that one room; without, return
    /// every room flagged `publish`. Answered even before the caller has joined.
    fn on_discover_rooms(
        &self,
        conn: ConnId,
        joincode: Option<&str>,
    ) -> Result<Vec<Outbound>, RoomError> {
        let rooms: Vec<serde_json::Value> = match joincode {
            Some(code) => self
                .rooms
                .values()
                .filter(|r| r.joincode == code)
                .map(room_info_json)
                .collect(),
            None => self
                .rooms
                .values()
                .filter(|r| r.publish)
                .map(room_info_json)
                .collect(),
        };
        let args = serde_json::json!({
            "rooms": rooms,
            "version": ROOMS_VERSION,
            "request": { "joincode": joincode.unwrap_or_default() },
        });
        Ok(vec![self.control_reply(conn, "Rooms", &args)?])
    }

    /// `GetBlob`: return the stored blob, or an empty string if the uuid is
    /// unknown (Ubiq treats a missing blob as empty rather than an error).
    fn on_get_blob(&self, conn: ConnId, uuid: &str) -> Result<Vec<Outbound>, RoomError> {
        let blob = self.blobs.get(uuid).cloned().unwrap_or_default();
        let args = serde_json::json!({ "uuid": uuid, "blob": blob });
        Ok(vec![self.control_reply(conn, "Blob", &args)?])
    }

    /// Relay an application frame (any NID other than `{0,1}`) from `conn` to the
    /// other members of its room, preserving the original NetworkId.
    pub fn on_app_frame(
        &mut self,
        conn: ConnId,
        frame: &NetworkFrame,
    ) -> Result<Vec<Outbound>, RoomError> {
        let room_uuid = match self.peers.get(&conn).and_then(|p| p.room.clone()) {
            Some(r) => r,
            None => return Ok(Vec::new()),
        };
        let members = match self.rooms.get(&room_uuid) {
            Some(r) => r.members.clone(),
            None => return Ok(Vec::new()),
        };
        let bytes = encode_frame(frame.network_id, &frame.payload)?;
        let mut out = Vec::new();
        for member in members {
            if member != conn {
                out.push(Outbound {
                    to: member,
                    bytes: bytes.clone(),
                });
            }
        }
        Ok(out)
    }

    /// Handle a dropped connection: remove the peer and announce `PeerRemoved`
    /// to the remaining members; drop the room once it is empty.
    pub fn on_disconnect(&mut self, conn: ConnId) -> Result<Vec<Outbound>, RoomError> {
        let peer = match self.peers.remove(&conn) {
            Some(p) => p,
            None => return Ok(Vec::new()),
        };
        let room_uuid = match peer.room {
            Some(r) => r,
            None => return Ok(Vec::new()),
        };
        let uuid = peer.info.uuid;

        let remaining: Vec<ConnId> = {
            let room = match self.rooms.get_mut(&room_uuid) {
                Some(r) => r,
                None => return Ok(Vec::new()),
            };
            room.members.retain(|&c| c != conn);
            room.members.clone()
        };

        let mut out = Vec::new();
        for member in &remaining {
            out.push(self.control_to(
                *member,
                "PeerRemoved",
                &serde_json::json!({ "uuid": uuid }),
            )?);
        }
        if remaining.is_empty() {
            self.rooms.remove(&room_uuid);
        }
        Ok(out)
    }

    /// Like [`Self::control_to`], but tolerates a connection that has not joined
    /// yet: `DiscoverRooms`/`GetBlob` may legitimately precede `Join`, in which
    /// case there is no `clientid` to address, so the reply goes back on the
    /// reserved RoomServer channel the client is already using.
    fn control_reply(
        &self,
        target: ConnId,
        ty: &str,
        args: &serde_json::Value,
    ) -> Result<Outbound, RoomError> {
        let nid = self
            .peers
            .get(&target)
            .map(|p| p.clientid)
            .unwrap_or(ROOMSERVER_ID);
        let payload = build_server_message(ty, args)?;
        let bytes = encode_frame(nid, &payload)?;
        Ok(Outbound { to: target, bytes })
    }

    /// Build a control reply of type `ty` addressed to `target`'s `clientid`.
    fn control_to(
        &self,
        target: ConnId,
        ty: &str,
        args: &serde_json::Value,
    ) -> Result<Outbound, RoomError> {
        let peer = self
            .peers
            .get(&target)
            .ok_or(RoomError::UnknownConn(target))?;
        let payload = build_server_message(ty, args)?;
        let bytes = encode_frame(peer.clientid, &payload)?;
        Ok(Outbound { to: target, bytes })
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;
    use dcvr_protocol::decode_frame;

    /// Build a `Join` control-frame payload for `room`, from a peer with the
    /// given uuid and clientid `b` component.
    fn join_payload(room: &str, peer_uuid: &str, clientid_b: u32) -> Vec<u8> {
        let args = serde_json::json!({
            "uuid": room,
            "peer": {
                "uuid": peer_uuid,
                "sceneid": { "a": 0u32, "b": 10u32 },
                "clientid": { "a": 0u32, "b": clientid_b },
                "keys": ["ubiq.samples.social.name"],
                "values": ["Tester"],
            }
        })
        .to_string();
        serde_json::to_vec(&serde_json::json!({ "type": "Join", "args": args })).unwrap()
    }

    /// Decode an outbound frame and return (NetworkId.b, message "type").
    fn outbound_type(o: &Outbound) -> (u32, String) {
        let d = decode_frame(&o.bytes).unwrap();
        let v: serde_json::Value = serde_json::from_slice(&d.frame.payload).unwrap();
        (
            d.frame.network_id.b,
            v["type"].as_str().unwrap().to_string(),
        )
    }

    #[test]
    fn join_creates_room_and_replies_set_room() {
        let mut rs = RoomServer::new();
        let out = rs
            .on_control(
                1,
                &join_payload("11111111-1111-4111-8111-111111111111", "peer-1", 111),
            )
            .unwrap();
        assert_eq!(rs.room_count(), 1);
        assert_eq!(rs.peer_count(), 1);
        assert_eq!(out.len(), 1, "first join yields exactly a SetRoom");
        let (nid_b, ty) = outbound_type(&out[0]);
        assert_eq!(ty, "SetRoom");
        assert_eq!(
            nid_b, 111,
            "control reply is addressed to the peer's clientid"
        );
    }

    #[test]
    fn second_peer_is_introduced_both_ways() {
        let mut rs = RoomServer::new();
        rs.on_control(
            1,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-1", 111),
        )
        .unwrap();
        let out = rs
            .on_control(
                2,
                &join_payload("11111111-1111-4111-8111-111111111111", "peer-2", 222),
            )
            .unwrap();
        assert_eq!(rs.room_count(), 1, "both peers share one room");
        assert_eq!(rs.peer_count(), 2);
        // Expect: SetRoom->2, PeerAdded(peer1)->2, PeerAdded(peer2)->1.
        let kinds: Vec<(u32, String)> = out.iter().map(outbound_type).collect();
        assert!(kinds.contains(&(222, "SetRoom".to_string())));
        assert!(
            kinds.contains(&(222, "PeerAdded".to_string())),
            "newcomer told about peer-1"
        );
        assert!(
            kinds.contains(&(111, "PeerAdded".to_string())),
            "peer-1 told about newcomer"
        );
    }

    #[test]
    fn app_frame_relays_to_others_only() {
        let mut rs = RoomServer::new();
        rs.on_control(
            1,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-1", 111),
        )
        .unwrap();
        rs.on_control(
            2,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-2", 222),
        )
        .unwrap();
        // A NID-94 application frame from conn 1.
        let frame = NetworkFrame {
            network_id: NetworkId::new(0, 94),
            payload: b"{\"type\":\"code\"}".to_vec(),
        };
        let out = rs.on_app_frame(1, &frame).unwrap();
        assert_eq!(
            out.len(),
            1,
            "relayed to the one other member, not the sender"
        );
        assert_eq!(out[0].to, 2);
        // The relayed frame keeps the original NID 94.
        let d = decode_frame(&out[0].bytes).unwrap();
        assert_eq!(d.frame.network_id.b, 94);
        assert_eq!(d.frame.payload, frame.payload);
    }

    #[test]
    fn disconnect_announces_peer_removed_and_reaps_empty_room() {
        let mut rs = RoomServer::new();
        rs.on_control(
            1,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-1", 111),
        )
        .unwrap();
        rs.on_control(
            2,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-2", 222),
        )
        .unwrap();

        let out = rs.on_disconnect(1).unwrap();
        assert_eq!(out.len(), 1, "the remaining peer is told peer-1 left");
        let (nid_b, ty) = outbound_type(&out[0]);
        assert_eq!(ty, "PeerRemoved");
        assert_eq!(nid_b, 222);
        assert_eq!(rs.room_count(), 1, "room persists while a peer remains");

        let out2 = rs.on_disconnect(2).unwrap();
        assert!(out2.is_empty(), "no one left to notify");
        assert_eq!(rs.room_count(), 0, "empty room is reaped");
        assert_eq!(rs.peer_count(), 0);
    }

    /// Build a control-frame payload for an arbitrary `{type, args}` message.
    fn ctrl(ty: &str, args: serde_json::Value) -> Vec<u8> {
        serde_json::to_vec(&serde_json::json!({
            "type": ty,
            "args": args.to_string(),
        }))
        .unwrap()
    }

    /// Decode an outbound frame's parsed `args` object.
    fn outbound_args(o: &Outbound) -> serde_json::Value {
        let d = decode_frame(&o.bytes).unwrap();
        let v: serde_json::Value = serde_json::from_slice(&d.frame.payload).unwrap();
        serde_json::from_str(v["args"].as_str().unwrap()).unwrap()
    }

    /// Look up a property value by key in a `{keys:[…], values:[…]}` object.
    /// (Peers already carry `ubiq.samples.social.name` from Join, so positional
    /// indexing is not safe.)
    fn prop(obj: &serde_json::Value, key: &str) -> Option<String> {
        let keys = obj["keys"].as_array()?;
        let idx = keys.iter().position(|k| k == key)?;
        Some(obj["values"].as_array()?.get(idx)?.as_str()?.to_string())
    }

    #[test]
    fn append_peer_properties_updates_peer_and_notifies_others() {
        let mut rs = RoomServer::new();
        rs.on_control(
            1,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-1", 111),
        )
        .unwrap();
        rs.on_control(
            2,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-2", 222),
        )
        .unwrap();

        let out = rs
            .on_control(
                1,
                &ctrl(
                    "AppendPeerProperties",
                    serde_json::json!({ "keys": ["nickname"], "values": ["Sandeep"] }),
                ),
            )
            .unwrap();

        assert_eq!(out.len(), 1, "only the OTHER member is notified");
        assert_eq!(out[0].to, 2);
        let (_, ty) = outbound_type(&out[0]);
        assert_eq!(ty, "PeerPropertiesAppended");
        let args = outbound_args(&out[0]);
        assert_eq!(args["uuid"], "peer-1");
        assert_eq!(args["keys"][0], "nickname");
        assert_eq!(args["values"][0], "Sandeep");

        // The property is persisted on the peer: a third peer joining is told
        // about peer-1 WITH the new property.
        let out3 = rs
            .on_control(
                3,
                &join_payload("11111111-1111-4111-8111-111111111111", "peer-3", 333),
            )
            .unwrap();
        let peer_added: Vec<serde_json::Value> = out3
            .iter()
            .filter(|o| o.to == 3 && outbound_type(o).1 == "PeerAdded")
            .map(|o| outbound_args(o)["peer"].clone())
            .collect();
        let p1 = peer_added
            .iter()
            .find(|p| p["uuid"] == "peer-1")
            .expect("peer-1 announced to the newcomer");
        assert_eq!(prop(p1, "nickname").as_deref(), Some("Sandeep"));
        // The property set at Join survives alongside the new one.
        assert_eq!(
            prop(p1, "ubiq.samples.social.name").as_deref(),
            Some("Tester")
        );
    }

    #[test]
    fn append_peer_properties_overwrites_an_existing_key() {
        let mut rs = RoomServer::new();
        rs.on_control(
            1,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-1", 111),
        )
        .unwrap();
        rs.on_control(
            2,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-2", 222),
        )
        .unwrap();
        let set = |v: &str| {
            ctrl(
                "AppendPeerProperties",
                serde_json::json!({ "keys": ["k"], "values": [v] }),
            )
        };
        rs.on_control(1, &set("first")).unwrap();
        rs.on_control(1, &set("second")).unwrap();

        // A newcomer sees exactly one `k`, with the latest value.
        let out = rs
            .on_control(
                3,
                &join_payload("11111111-1111-4111-8111-111111111111", "peer-3", 333),
            )
            .unwrap();
        let p1 = out
            .iter()
            .filter(|o| o.to == 3 && outbound_type(o).1 == "PeerAdded")
            .map(|o| outbound_args(o)["peer"].clone())
            .find(|p| p["uuid"] == "peer-1")
            .unwrap();
        let keys = p1["keys"].as_array().unwrap();
        assert_eq!(
            keys.iter().filter(|k| *k == "k").count(),
            1,
            "key `k` must appear exactly once, not be appended twice"
        );
        assert_eq!(
            prop(&p1, "k").as_deref(),
            Some("second"),
            "value overwritten, not appended"
        );
    }

    #[test]
    fn append_room_properties_notifies_every_member_including_sender() {
        let mut rs = RoomServer::new();
        rs.on_control(
            1,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-1", 111),
        )
        .unwrap();
        rs.on_control(
            2,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-2", 222),
        )
        .unwrap();

        let out = rs
            .on_control(
                1,
                &ctrl(
                    "AppendRoomProperties",
                    serde_json::json!({ "keys": ["scene"], "values": ["forest"] }),
                ),
            )
            .unwrap();
        assert_eq!(
            out.len(),
            2,
            "room state is authoritative: everyone is told"
        );
        for o in &out {
            assert_eq!(outbound_type(o).1, "RoomPropertiesAppended");
        }

        // Persisted: a newcomer's SetRoom carries the property.
        let out3 = rs
            .on_control(
                3,
                &join_payload("11111111-1111-4111-8111-111111111111", "peer-3", 333),
            )
            .unwrap();
        let set_room = out3
            .iter()
            .find(|o| outbound_type(o).1 == "SetRoom")
            .expect("SetRoom present");
        let room = outbound_args(set_room)["room"].clone();
        assert_eq!(room["keys"][0], "scene");
        assert_eq!(room["values"][0], "forest");
    }

    #[test]
    fn discover_rooms_lists_only_published_rooms() {
        let mut rs = RoomServer::new();
        // A published room and an unpublished one.
        let mut pub_join: serde_json::Value = serde_json::from_slice(&join_payload(
            "22222222-2222-4222-8222-222222222222",
            "p1",
            111,
        ))
        .unwrap();
        let mut args: serde_json::Value =
            serde_json::from_str(pub_join["args"].as_str().unwrap()).unwrap();
        args["publish"] = serde_json::json!(true);
        args["name"] = serde_json::json!("Public Room");
        pub_join["args"] = serde_json::json!(args.to_string());
        rs.on_control(1, &serde_json::to_vec(&pub_join).unwrap())
            .unwrap();
        rs.on_control(
            2,
            &join_payload("33333333-3333-4333-8333-333333333333", "p2", 222),
        )
        .unwrap();

        let out = rs
            .on_control(2, &ctrl("DiscoverRooms", serde_json::json!({})))
            .unwrap();
        assert_eq!(out.len(), 1);
        assert_eq!(outbound_type(&out[0]).1, "Rooms");
        let args = outbound_args(&out[0]);
        let rooms = args["rooms"].as_array().unwrap();
        assert_eq!(rooms.len(), 1, "only the published room is discoverable");
        assert_eq!(rooms[0]["uuid"], "22222222-2222-4222-8222-222222222222");
        assert_eq!(rooms[0]["name"], "Public Room");
        assert_eq!(args["version"], "1.0");
    }

    #[test]
    fn discover_rooms_by_joincode_finds_the_room_even_before_joining() {
        let mut rs = RoomServer::new();
        rs.on_control(
            1,
            &join_payload("abcdef01-2345-4678-89ab-cdef01234567", "p1", 111),
        )
        .unwrap();
        // joincode is derived from the uuid's first 3 alphanumerics -> "abc".
        let out = rs
            .on_control(
                9, // conn 9 has NOT joined — the reply must still be addressable
                &ctrl("DiscoverRooms", serde_json::json!({ "joincode": "abc" })),
            )
            .unwrap();
        assert_eq!(out.len(), 1);
        assert_eq!(out[0].to, 9);
        // Pre-join replies go out on the reserved RoomServer channel.
        let (nid_b, ty) = outbound_type(&out[0]);
        assert_eq!(ty, "Rooms");
        assert_eq!(nid_b, ROOMSERVER_ID.b);
        let rooms = outbound_args(&out[0])["rooms"].as_array().unwrap().clone();
        assert_eq!(rooms.len(), 1);
        assert_eq!(rooms[0]["uuid"], "abcdef01-2345-4678-89ab-cdef01234567");
    }

    #[test]
    fn set_blob_then_get_blob_round_trips() {
        let mut rs = RoomServer::new();
        rs.on_control(
            1,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-1", 111),
        )
        .unwrap();

        let out = rs
            .on_control(
                1,
                &ctrl(
                    "SetBlob",
                    serde_json::json!({ "uuid": "blob-1", "blob": "hello-world" }),
                ),
            )
            .unwrap();
        assert!(out.is_empty(), "SetBlob has no reply");

        let out = rs
            .on_control(1, &ctrl("GetBlob", serde_json::json!({ "uuid": "blob-1" })))
            .unwrap();
        assert_eq!(out.len(), 1);
        assert_eq!(outbound_type(&out[0]).1, "Blob");
        let args = outbound_args(&out[0]);
        assert_eq!(args["uuid"], "blob-1");
        assert_eq!(args["blob"], "hello-world");
    }

    #[test]
    fn get_blob_for_unknown_uuid_returns_empty_not_an_error() {
        let mut rs = RoomServer::new();
        rs.on_control(
            1,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-1", 111),
        )
        .unwrap();
        let out = rs
            .on_control(1, &ctrl("GetBlob", serde_json::json!({ "uuid": "nope" })))
            .unwrap();
        assert_eq!(outbound_args(&out[0])["blob"], "");
    }

    /// Upstream Ubiq refuses a Join whose room uuid is not an RFC4122 v4 UUID
    /// (verified against the real Node RoomServer, which logs "we were expecting an
    /// RFC4122 v4 uuid"). We mirror that rather than accepting any string as a room
    /// id, so a stock Ubiq client sees the reply it is written against.
    #[test]
    fn join_with_a_non_uuid_room_is_rejected() {
        let mut rs = RoomServer::new();
        let out = rs
            .on_control(1, &join_payload("not-a-uuid", "peer-1", 111))
            .unwrap();
        assert_eq!(out.len(), 1);
        let (nid_b, ty) = outbound_type(&out[0]);
        assert_eq!(ty, "Rejected");
        assert_eq!(
            nid_b, ROOMSERVER_ID.b,
            "pre-join reply uses the control channel"
        );
        assert!(outbound_args(&out[0])["reason"]
            .as_str()
            .unwrap()
            .contains("RFC4122 v4"));
        assert_eq!(rs.room_count(), 0, "a rejected join creates no room");
        assert_eq!(rs.peer_count(), 0, "and registers no peer");
    }

    #[test]
    fn uuid_validation_accepts_v4_and_rejects_near_misses() {
        assert!(is_rfc4122_v4("6765c52b-3ad6-4fb0-9030-2c9a05dc4731"));
        assert!(
            is_rfc4122_v4("ABCDEF01-2345-4678-89AB-CDEF01234567"),
            "case-insensitive"
        );
        assert!(
            !is_rfc4122_v4("6765c52b-3ad6-3fb0-9030-2c9a05dc4731"),
            "v3, not v4"
        );
        assert!(
            !is_rfc4122_v4("6765c52b-3ad6-4fb0-c030-2c9a05dc4731"),
            "bad variant nibble"
        );
        assert!(
            !is_rfc4122_v4("6765c52b3ad64fb090302c9a05dc4731"),
            "missing dashes"
        );
        assert!(
            !is_rfc4122_v4("6765c52b-3ad6-4fb0-9030-2c9a05dc473"),
            "too short"
        );
        assert!(
            !is_rfc4122_v4("6765c52b-3ad6-4fb0-9030-2c9a05dc47zz"),
            "non-hex"
        );
        assert!(!is_rfc4122_v4(""));
    }

    #[test]
    fn join_by_unknown_joincode_is_rejected_and_creates_nothing() {
        let mut rs = RoomServer::new();
        let out = rs
            .on_control(
                1,
                &ctrl(
                    "Join",
                    serde_json::json!({
                        "joincode": "zzz",
                        "peer": {
                            "uuid": "peer-1",
                            "sceneid": { "a": 0u32, "b": 10u32 },
                            "clientid": { "a": 0u32, "b": 111u32 },
                            "keys": [], "values": []
                        }
                    }),
                ),
            )
            .unwrap();
        assert_eq!(outbound_type(&out[0]).1, "Rejected");
        assert_eq!(rs.room_count(), 0);
    }

    #[test]
    fn join_by_known_joincode_lands_in_the_existing_room() {
        let mut rs = RoomServer::new();
        // "111…" derives joincode "111".
        rs.on_control(
            1,
            &join_payload("11111111-1111-4111-8111-111111111111", "peer-1", 111),
        )
        .unwrap();
        let out = rs
            .on_control(
                2,
                &ctrl(
                    "Join",
                    serde_json::json!({
                        "joincode": "111",
                        "peer": {
                            "uuid": "peer-2",
                            "sceneid": { "a": 0u32, "b": 10u32 },
                            "clientid": { "a": 0u32, "b": 222u32 },
                            "keys": [], "values": []
                        }
                    }),
                ),
            )
            .unwrap();
        let kinds: Vec<String> = out.iter().map(|o| outbound_type(o).1).collect();
        assert!(kinds.contains(&"SetRoom".to_string()), "joined by code");
        assert_eq!(rs.room_count(), 1, "no second room was created");
        assert_eq!(rs.peer_count(), 2);
    }

    #[test]
    fn unknown_control_type_is_ignored() {
        let mut rs = RoomServer::new();
        // A type this server does not implement (and Ubiq may add later).
        let env = serde_json::to_vec(&serde_json::json!({
            "type": "SomeFutureUbiqMessage",
            "args": "{}"
        }))
        .unwrap();
        // Not joined yet; an unhandled type is a safe no-op, not an error.
        let out = rs.on_control(9, &env).unwrap();
        assert!(out.is_empty());
    }
}
