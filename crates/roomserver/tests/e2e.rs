//! End-to-end: our real Ubiq service peer (`dcvr-unity-transport`) connects to
//! our new Rust RoomServer over a real TCP socket, joins a room, and has a frame
//! relayed to a second peer. This proves wire-compatibility of the whole loop
//! (Join -> SetRoom, then application-frame relay) against the actual client.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use dcvr_protocol::NID_BACKEND_OUTPUT;
use dcvr_roomserver::RoomServerHandle;
use dcvr_unity_transport::UbiqServicePeer;
use tokio::time::timeout;

#[tokio::test]
async fn single_peer_join_registers_one_room() {
    let server = RoomServerHandle::bind("127.0.0.1:0").await.unwrap();
    let addr = server.local_addr().to_string();

    let _peer = UbiqServicePeer::connect_and_join(&addr, "room-x")
        .await
        .expect("join succeeds against the Rust RoomServer");

    assert_eq!(server.room_count().await, 1);
}

#[tokio::test]
async fn two_peers_relay_a_frame_through_the_server() {
    let server = RoomServerHandle::bind("127.0.0.1:0").await.unwrap();
    let addr = server.local_addr().to_string();
    let room = "6765c52b-3ad6-4fb0-9030-2c9a05dc4731";

    // Two real clients join the same room.
    let sender = UbiqServicePeer::connect_and_join(&addr, room)
        .await
        .expect("peer A joins");
    let mut receiver = UbiqServicePeer::connect_and_join(&addr, room)
        .await
        .expect("peer B joins");

    assert_eq!(server.room_count().await, 1, "both peers share one room");

    // A sends on NID 94 (backend output). The server must relay it to B.
    sender
        .send(NID_BACKEND_OUTPUT, b"hello-from-a")
        .await
        .expect("send succeeds");

    let frame = timeout(Duration::from_secs(3), receiver.recv())
        .await
        .expect("frame relayed within timeout")
        .expect("frame is present");

    assert_eq!(frame.network_id.b, 94, "relayed frame keeps its NetworkId");
    assert_eq!(frame.payload, b"hello-from-a");
}
