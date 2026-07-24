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
}

/// The in-memory room registry. Not `Clone`; the transport layer holds a single
/// instance behind a lock.
#[derive(Default)]
pub struct RoomServer {
    peers: HashMap<ConnId, Peer>,
    rooms: HashMap<String, Room>,
}

fn to_network_id(j: NetworkIdJson) -> NetworkId {
    NetworkId::new(j.a, j.b)
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
        "keys": Vec::<String>::new(),
        "values": Vec::<String>::new(),
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
            ClientMessage::Other(_) => Ok(Vec::new()),
        }
    }

    fn on_join(&mut self, conn: ConnId, args: JoinArgs) -> Result<Vec<Outbound>, RoomError> {
        let clientid = to_network_id(args.peer.clientid);
        // Our clients always send a room uuid; fall back to the peer uuid so a
        // uuid-less Join still lands in a private room rather than failing.
        let room_uuid = args.uuid.clone().unwrap_or_else(|| args.peer.uuid.clone());

        // Resolve or create the room, then record membership.
        let (room_info, existing_members) = {
            let room = self.rooms.entry(room_uuid.clone()).or_insert_with(|| Room {
                uuid: room_uuid.clone(),
                joincode: derive_joincode(&room_uuid),
                name: args.name.clone().unwrap_or_default(),
                publish: args.publish.unwrap_or(false),
                members: Vec::new(),
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
            .on_control(1, &join_payload("room-A", "peer-1", 111))
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
        rs.on_control(1, &join_payload("room-A", "peer-1", 111))
            .unwrap();
        let out = rs
            .on_control(2, &join_payload("room-A", "peer-2", 222))
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
        rs.on_control(1, &join_payload("room-A", "peer-1", 111))
            .unwrap();
        rs.on_control(2, &join_payload("room-A", "peer-2", 222))
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
        rs.on_control(1, &join_payload("room-A", "peer-1", 111))
            .unwrap();
        rs.on_control(2, &join_payload("room-A", "peer-2", 222))
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

    #[test]
    fn unknown_control_type_is_ignored() {
        let mut rs = RoomServer::new();
        let env = serde_json::to_vec(&serde_json::json!({
            "type": "DiscoverRooms",
            "args": "{}"
        }))
        .unwrap();
        // Not joined yet; an unhandled type is a safe no-op, not an error.
        let out = rs.on_control(9, &env).unwrap();
        assert!(out.is_empty());
    }
}
