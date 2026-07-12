use uuid::Uuid;

/// A Ubiq NetworkId as it appears in room-control JSON: `{"a":u32,"b":u32}`.
#[derive(Debug, Clone, Copy)]
pub struct NetworkIdJson {
    pub a: u32,
    pub b: u32,
}

/// The peer descriptor sent in a Join (mirrors Ubiq RoomClient.GetPeerInfo).
#[derive(Debug, Clone)]
pub struct PeerInfo {
    pub uuid: String,
    pub sceneid: NetworkIdJson,
    pub clientid: NetworkIdJson,
    pub name: String,
}

impl PeerInfo {
    /// Generate a random peer with a random scene id + client id (derived from a
    /// v4 UUID's bytes). The server addresses replies to `clientid`; the Rust peer
    /// receives all frames on its own connection and routes by NetworkId, so exact
    /// Unity-style derivation of clientid is not required to participate.
    pub fn random(name: &str) -> Self {
        let u = Uuid::new_v4();
        let by = u.as_bytes();
        let sceneid = NetworkIdJson {
            a: u32::from_le_bytes([by[0], by[1], by[2], by[3]]),
            b: u32::from_le_bytes([by[4], by[5], by[6], by[7]]),
        };
        let clientid = NetworkIdJson {
            a: u32::from_le_bytes([by[8], by[9], by[10], by[11]]),
            b: u32::from_le_bytes([by[12], by[13], by[14], by[15]]),
        };
        Self {
            uuid: u.to_string(),
            sceneid,
            clientid,
            name: name.to_string(),
        }
    }

    pub fn to_json(&self) -> serde_json::Value {
        serde_json::json!({
            "uuid": self.uuid,
            "sceneid": { "a": self.sceneid.a, "b": self.sceneid.b },
            "clientid": { "a": self.clientid.a, "b": self.clientid.b },
            "keys": ["ubiq.samples.social.name"],
            "values": [self.name],
        })
    }
}
