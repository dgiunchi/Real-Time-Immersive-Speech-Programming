//! `keygen` — generate the secrets required to run the DreamCodeVR+ **hardened**
//! security profile, and print the backend Ed25519 PUBLIC key to provision to the
//! Unity `BackendVerifier`. Run with `cargo run --bin keygen`.
//!
//! The printed private values are freshly random each run. Store them in a secret
//! manager or protected environment; NEVER commit them or send them to the admin
//! panel. Only the PUBLIC key goes to the Unity client.
#![allow(clippy::print_stdout)]

use dcvr_unity_transport::{random_32, BackendSigner};

fn hex(bytes: &[u8]) -> String {
    let mut s = String::with_capacity(bytes.len() * 2);
    for b in bytes {
        s.push_str(&format!("{b:02x}"));
    }
    s
}

fn main() {
    let (Some(admission), Some(seed), Some(enc)) = (random_32(), random_32(), random_32()) else {
        eprintln!("error: OS random number generator failed");
        std::process::exit(1);
    };
    let public_key = match BackendSigner::from_seed(&seed) {
        Ok(signer) => hex(&signer.public_key_bytes()),
        Err(_) => {
            eprintln!("error: could not derive the Ed25519 public key");
            std::process::exit(1);
        }
    };

    println!("# DreamCodeVR+ hardened-profile secrets — store securely, DO NOT commit.");
    println!("export DCVR_SECURITY_PROFILE=hardened");
    println!("export DCVR_PEER_AUTH_SECRET={}", hex(&admission));
    println!("export DCVR_BACKEND_SIGNING_SEED={}", hex(&seed));
    println!("export DCVR_PROFILE_ENC_KEY={}", hex(&enc));
    println!("# Hardened Mode A also needs: DCVR_MODE_A=true  DCVR_ROSLYN_URL=<real analyzer>");
    println!();
    println!("# Provision this backend Ed25519 PUBLIC key to the Unity BackendVerifier:");
    println!("BACKEND_ED25519_PUBLIC_KEY={public_key}");
}
