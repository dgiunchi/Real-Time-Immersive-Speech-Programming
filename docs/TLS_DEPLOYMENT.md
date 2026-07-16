# Transport confidentiality (TLS/WSS) — deployment guide

## What's already covered without TLS

In the **hardened** profile, every message carries end-to-end cryptographic
authentication that survives an untrusted relay:

- client→backend **HMAC-SHA256** admission + per-message tag (A001–A009, A016),
- backend→Unity **Ed25519** signatures on NID-94 (A010/A011), and
- SHA-256 payload/code-hash binding + strict-monotonic replay (A004/A005/A006).

So **integrity, authenticity, and anti-replay do NOT depend on the transport** — a
malicious RoomServer (A022) cannot forge, tamper, or replay. The one thing TLS adds is
**confidentiality**: stopping a network eavesdropper from *reading* transcripts/code in
transit (A019). That is why the code treats TLS as a deployment concern, not a
message-layer control.

## Why this is a deployment step, not a code change

The Ubiq RoomServer is **third-party and untrusted-by-design**. Its encrypted channel
is **WSS (WebSocket-over-TLS) on `:8010`** — a different framing from the raw
length-prefixed TCP the service peer uses on `:8009`. Bolting a raw-TLS socket onto the
`:8009` path would not interoperate with Ubiq and can't be validated without the live
RoomServer + real certificates. The robust, relay-agnostic answer is to terminate TLS
**in front of** the relay.

## Recommended: TLS-terminating proxy in front of the relay

Put a TLS proxy between each endpoint and the RoomServer, so both hops are encrypted
while the app keeps speaking its plain framing to `localhost`:

```
Quest  ──TLS──►  proxy ─┐
                        ├─►  Ubiq RoomServer (:8009)  ◄─┐ proxy  ◄──TLS──  backend
Quest  ──TLS──►  proxy ─┘                              └─
```

- **stunnel** (simplest): `accept = 0.0.0.0:9443` / `connect = 127.0.0.1:8009` on the
  server side; the client connects to `:9443` over TLS and the proxy forwards plaintext
  to the local RoomServer. Mirror on the backend side.
- **nginx `stream {}`** or **caddy (layer4)**: same idea with `proxy_pass` to `:8009`.
- Or use **Ubiq's native WSS on `:8010`** with a real certificate, if your client build
  uses the WebSocket transport.

## Certificate pinning

Because integrity is already guaranteed at the message layer, TLS here is about
confidentiality + a second identity check. **Pin the server certificate / its CA** on
the connecting side (the backend's proxy config, and the Quest client's TLS settings)
so a hostile network can't MITM with a rogue cert. Do not rely on public CA trust alone
on a lab/hotspot network.

## Point the backend + client at the proxy

- Backend: set `DCVR_UBIQ_ADDR` to the local TLS-proxy endpoint (which forwards to the
  real RoomServer).
- Quest client: point the Ubiq RoomClient at the client-side TLS proxy / the WSS URL.

## Residual note

TLS protects the wire; it does **not** protect a compromised endpoint. Host/endpoint
compromise remains out of scope (documented in `SECURITY.md`). With hardened message
auth **plus** a pinned-cert TLS proxy, the untrusted-relay threat (read + tamper +
replay + impersonate) is fully addressed for a research deployment.
