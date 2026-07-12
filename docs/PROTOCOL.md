# Protocol

DreamCodeVR+ speaks the **Ubiq** wire protocol so it can join the same room as the
unmodified Unity client. Framing and codec live in `crates/protocol`
(no I/O, `unsafe`-free, golden-byte tested).

## Framing

- A Ubiq frame length counts **`NetworkId` (8 bytes) + payload** (not payload
  alone) — a bug found and fixed by live testing.
- The `Join` handshake's `args` field must be **stringified JSON** (the RoomServer
  does `JSON.parse(message.object.args)`).
- Each application payload is `{ peer_uuid, body }` (`split_peer_payload`).

## NetworkIds

| NID | Direction | Purpose | Encoding | Notes |
|---|---|---|---|---|
| **93** | client → backend | Selected object id | UTF-8 string | sets the per-peer target |
| **98** | client → backend | Push-to-talk audio | 16 kHz mono PCM; control strings `__STT_CONTROL__:start` / `:stop` | accumulated per peer; bounded by a max-utterance cap |
| **94** | backend → client | Backend decision | JSON | action plan **or** `{type:"code", peer, data:<C#>}` (Mode A/B) |
| **95** | client → backend | Like / dislike feedback | JSON `{liked:bool}` | drives personalization |
| **96** | client → backend | Runtime compile result | JSON `{ok, ms, error}` | surfaced to the admin panel |

## Trust and validation notes (see SECURITY_MODEL.md)

- Peers **self-assert** their `peer_uuid`; there is no enforced peer
  authentication in this release. An HMAC token module exists but is unwired.
- Inbound audio is size-bounded; malformed frames are dropped, not trusted.
- NID-94 code messages are applied by the Unity client without a peer check, so
  Mode A should be used only on trusted networks.
- The channel is plaintext TCP; add TLS/WSS for untrusted networks.

## HTTP (admin/debug panel, optional)

The admin panel (`crates/admin`, axum) exposes SSE + JSON routes (config, stats,
safety log, validate, red-team, command, sandbox, profiles). It binds to loopback
by default and honours an optional `X-Admin-Token`. See `crates/admin/src/lib.rs`
for the authoritative route list.

## Internal service interfaces

- **Rust → Roslyn analyzer** (Mode B): HTTP `POST` to `DCVR_ROSLYN_URL`
  (the client POSTs to this URL verbatim), default `http://127.0.0.1:5099/analyze`.
- **Rust → sandbox** (Mode D): the Docker runner streams code to a .NET harness
  container's stdin; only a `SandboxReport` returns.
- **STT / LLM / embeddings**: HTTPS (rustls) to configured endpoints, or mocks.
