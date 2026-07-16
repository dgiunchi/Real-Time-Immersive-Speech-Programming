# Hardened Deployment Guide

How to run DreamCodeVR+ in the **`hardened`** security profile. The default
(`legacy`) needs none of this and behaves exactly as the original build.

## 1. Generate the secrets

```bash
cargo run --bin keygen
```

This prints fresh random values (admission secret, backend Ed25519 signing seed,
profile-encryption key) plus the **backend Ed25519 public key**. Store the private
values in a secret manager or protected environment — **never commit them** or send
them to the admin panel. Only the public key goes to the Unity client.

## 2. Backend environment

Export the printed `DCVR_*` vars, then add the Mode-A + analyzer + admin settings:

```bash
export DCVR_SECURITY_PROFILE=hardened
export DCVR_PEER_AUTH_SECRET=...          # from keygen
export DCVR_BACKEND_SIGNING_SEED=...       # from keygen
export DCVR_PROFILE_ENC_KEY=...            # from keygen (profiles encrypted at rest)
export DCVR_MODE_A=true
export DCVR_ROSLYN_URL=http://127.0.0.1:5099   # a REAL analyzer (no mock in hardened)
export DCVR_ADMIN_TOKEN=...                # if the admin panel is enabled
```

If a required control is missing, the backend **refuses to start** (fail-closed) —
it never silently downgrades to `legacy`.

## 3. Unity client

Provision `BACKEND_ED25519_PUBLIC_KEY` to the `BackendVerifier`, wire an
`IEd25519Verifier` (BouncyCastle / NaCl), and set `RequireSignature = true` so any
unsigned NID-94 is rejected. See `Assets/DreamCodeVRPlus/Security/README-INTEGRATION.md`.

## What `hardened` enforces (fail-closed)

| Control | Effect |
|---|---|
| Admission + signing keys required | refuses to start without them |
| Real analyzer required for Mode A | no approve-all mock |
| Backend-signed NID-94 | Unity compiles only backend-approved code |
| Constant-time admin token + off-loopback bind refusal | no token timing leak; no public exposure without a token |
| Profiles: encrypted at rest + `0600` + TTL + erasure | personal data minimised and protected |

## Honest limits (see `docs/HARDENING.md`)

- Live Quest end-to-end verification is pending hardware (2026-07-23).
- Incoming envelope verification in the recv loop activates once the Unity client
  emits envelopes.
- TLS is a transport-confidentiality deployment step; message auth already gives
  end-to-end integrity through an untrusted relay.
- In-process C# limits (loop/memory/guard integrity) remain contained-not-prevented
  and are addressed by the later containment/perceptual phases.
