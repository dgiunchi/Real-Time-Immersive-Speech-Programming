//! Phase-8 adversarial campaign against the COMPOSED Phase-1 authentication stack.
//!
//! Unlike the per-crate unit tests, this drives the public API across three crates
//! (`dcvr-protocol` envelope + `dcvr-unity-transport` crypto + `dreamcodevr-server`
//! `ServerAuth` seam) end-to-end, firing a battery of forged / tampered / expired /
//! replayed / wrong-domain / downgraded / malformed messages and asserting a **0%
//! bypass rate** and **0% false-positive rate**. Deterministic; no network or keys.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_config::{SecurityProfile, Settings};
use dcvr_protocol::{AuthEnvelope, ENVELOPE_VERSION};
use dcvr_unity_transport::{payload_hash, BackendVerifier, EnvelopeMac, SessionSequence};
use dreamcodevr_server::auth_gate::ServerAuth;

const SECRET: &str = "campaign-room-secret";
const SEED: &str = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
const NOW: u64 = 1_000;
const FUTURE: u64 = 2_000_000_000;

fn active_auth() -> ServerAuth {
    let s = Settings {
        security_profile: SecurityProfile::Test,
        peer_auth_secret: Some(SECRET.to_string()),
        backend_signing_seed_hex: Some(SEED.to_string()),
        ..Default::default()
    };
    ServerAuth::from_settings(&s)
}

/// Mint a client message exactly as a legitimate hardened client would:
/// `[u32 LE env_len][HMAC-signed envelope][body]`.
fn mint(secret: &str, nid: u32, seq: u64, body: &[u8], expiry: u64) -> Vec<u8> {
    let mut env = AuthEnvelope {
        version: ENVELOPE_VERSION,
        profile: 2,
        network_id_b: nid,
        sequence_number: seq,
        expiry_unix: expiry,
        session_id: "sess".into(),
        authenticated_peer_id: "peer-1".into(),
        request_id: "req".into(),
        target_peer_id: String::new(),
        payload_hash: payload_hash(body),
        auth_tag: Vec::new(),
    };
    EnvelopeMac::new(secret.as_bytes()).sign(&mut env).unwrap();
    let eb = env.to_bytes().unwrap();
    let mut out = (eb.len() as u32).to_le_bytes().to_vec();
    out.extend_from_slice(&eb);
    out.extend_from_slice(body);
    out
}

#[test]
fn incoming_auth_blocks_every_attack_with_no_false_positives() {
    let auth = active_auth();

    // (name, channel NID, crafted message) — each MUST be rejected.
    let attacks: Vec<(&str, u32, Vec<u8>)> = vec![
        (
            "forged MAC (attacker secret)",
            98,
            mint("attacker", 98, 1, b"x", FUTURE),
        ),
        ("tampered body", 98, {
            let mut m = mint(SECRET, 98, 1, b"original", FUTURE);
            let n = m.len();
            m[n - 1] ^= 0xFF;
            m
        }),
        ("expired envelope", 98, mint(SECRET, 98, 1, b"x", 100)),
        (
            "wrong domain (NID-98 tag on NID-93)",
            93,
            mint(SECRET, 98, 1, b"x", FUTURE),
        ),
        ("downgraded unsigned legacy [uuid][body]", 98, {
            let mut v = b"u".repeat(36);
            v.extend_from_slice(b"body");
            v
        }),
        ("truncated frame", 98, vec![0x00, 0x01]),
        ("garbage bytes", 98, vec![0xFF; 8]),
    ];

    let mut bypassed = 0usize;
    for (name, nid, msg) in &attacks {
        let mut seq = SessionSequence::default();
        if auth.verify_incoming(*nid, msg, &mut seq, NOW).is_ok() {
            bypassed += 1;
            eprintln!("BYPASS: {name}");
        }
    }

    // Replay: the first copy is accepted, an identical second is rejected.
    let mut seq = SessionSequence::default();
    let m = mint(SECRET, 98, 5, b"x", FUTURE);
    assert!(auth.verify_incoming(98, &m, &mut seq, NOW).is_ok());
    if auth.verify_incoming(98, &m, &mut seq, NOW).is_ok() {
        bypassed += 1;
        eprintln!("BYPASS: replay");
    }
    let total_attacks = attacks.len() + 1;

    // False positives: legitimate fresh messages must be accepted and return the
    // AUTHENTICATED peer id (not a self-asserted one) plus the intact body.
    let mut false_positives = 0usize;
    let mut seq_ok = SessionSequence::default();
    let benign: [&[u8]; 3] = [b"make the cube red", b"add gentle rain", b"grow the tree"];
    for (i, body) in benign.iter().enumerate() {
        let msg = mint(SECRET, 98, (i as u64) + 1, body, FUTURE);
        match auth.verify_incoming(98, &msg, &mut seq_ok, NOW) {
            Ok(v) => {
                assert_eq!(v.peer_id, "peer-1", "returns proven identity");
                assert_eq!(&v.body, body, "body intact");
            }
            Err(_) => false_positives += 1,
        }
    }

    eprintln!(
        "[auth-redteam] attacks={total_attacks} bypassed={bypassed} benign={} false_positives={false_positives}",
        benign.len()
    );
    assert_eq!(bypassed, 0, "bypass rate must be 0% (all attacks blocked)");
    assert_eq!(false_positives, 0, "false-positive rate must be 0%");
}

#[test]
fn outgoing_nid94_is_backend_signed_and_tamper_evident() {
    let auth = active_auth();
    let plain = br#"{"type":"code","data":"GetComponent<Renderer>().material.color=Color.red;"}"#;
    let signed = auth.sign_nid94(plain, "peer-1", "req-1", NOW).unwrap();

    // Unframe [u32 len][envelope][body] and confirm the body travelled intact.
    let len = u32::from_le_bytes(signed[0..4].try_into().unwrap()) as usize;
    let env = AuthEnvelope::from_bytes(&signed[4..4 + len]).unwrap();
    let body = &signed[4 + len..];
    assert_eq!(body, plain, "code body intact under the envelope");

    // A verifier holding the backend PUBLIC key accepts it (Unity's exact check)...
    let verifier = BackendVerifier::new(auth.backend_public_key().unwrap());
    assert!(verifier.verify(&env, NOW).is_ok());

    // ...but a swapped code hash (attacker substitutes different C#) is rejected,
    // because the code hash is inside the Ed25519-signed region.
    let mut forged = env.clone();
    forged.payload_hash = payload_hash(b"System.IO.File.Delete(\"/etc/passwd\");");
    assert!(
        verifier.verify(&forged, NOW).is_err(),
        "substituted code must break the backend signature"
    );
}
