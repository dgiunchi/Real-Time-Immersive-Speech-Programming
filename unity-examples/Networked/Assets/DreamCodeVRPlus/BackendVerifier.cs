// DreamCodeVR+ — Unity-side backend authentication (Phase 1, P1.5).
//
// Verifies that a NID-94 "run this C#" message genuinely came from the trusted
// Rust backend BEFORE the code is handed to RoslynCSharp for runtime compilation.
// This is the Unity mirror of `dcvr_unity_transport::BackendVerifier`; it parses
// the exact same wire layout produced by `apps/dreamcodevr-server/auth_gate.rs`:
//
//     NID-94 payload = [u32 LE envelope_len][envelope][body]
//     envelope       = signing_input || [u16 LE tag_len][tag(Ed25519 sig, 64B)]
//     signing_input  = u16 version | u8 profile | u32 network_id_b |
//                      u64 sequence | u64 expiry | str session_id |
//                      str authenticated_peer_id | str request_id |
//                      str target_peer_id | [32] payload_hash(SHA-256 of body)
//     str            = u16 LE len + UTF-8 bytes    (all integers little-endian)
//
// SECURITY MODEL: the backend signs with an Ed25519 PRIVATE key it never shares;
// Unity holds only the PUBLIC key, so even a leaked client MAC secret cannot forge
// an approved code decision. LEGACY compatibility: if the payload is a plain UTF-8
// JSON object (the current, unsigned format), `TryVerify` returns it unchanged so a
// legacy backend still works — HARDENED deployments must set RequireSignature=true
// so unsigned NID-94 is rejected (fail-closed).
//
// UNITY CRYPTO NOTE: .NET/Mono in Unity has SHA-256 built in but NOT Ed25519.
// Provide an IEd25519Verifier (BouncyCastle `Org.BouncyCastle.Math.EC.Rfc8032.Ed25519`,
// or Chaos.NaCl, or libsodium via a plugin). The envelope parsing + all binding
// checks below are complete and Editor-independent; only the signature primitive is
// pluggable. THIS FILE COMPILES AND IS UNIT-CHECKABLE; the Ed25519 backend is wired
// on-device (verification pass after 2026-07-23).

using System;
using System.Security.Cryptography;
using System.Text;

namespace DreamCodeVRPlus.Security
{
    /// <summary>Pluggable Ed25519 signature verifier (supply a BouncyCastle/NaCl impl).</summary>
    public interface IEd25519Verifier
    {
        bool Verify(byte[] publicKey32, byte[] message, byte[] signature64);
    }

    /// <summary>Result of checking a NID-94 payload.</summary>
    public struct VerifyResult
    {
        public bool Ok;
        public string Body;     // the trusted JSON body ({"type":"code",...}) when Ok
        public string Reason;   // human-readable rejection reason when !Ok
    }

    public sealed class BackendVerifier
    {
        public const ushort EnvelopeVersion = 1;
        public const int HashLen = 32;
        public const int MaxFieldLen = 256;
        public const int MaxTagLen = 128;
        public const uint Nid94 = 94;

        private readonly byte[] _publicKey;   // 32-byte Ed25519 public key from the backend
        private readonly IEd25519Verifier _ed;

        /// <summary>If true (HARDENED), a plain/unsigned NID-94 payload is rejected.</summary>
        public bool RequireSignature { get; set; }

        /// <summary>Allowed clock skew (seconds) when checking envelope expiry.</summary>
        public long ClockSkewSeconds { get; set; } = 60;

        public BackendVerifier(byte[] publicKey32, IEd25519Verifier ed)
        {
            _publicKey = publicKey32;
            _ed = ed;
        }

        /// <summary>
        /// Verify a NID-94 payload. On success, <c>Body</c> is the trusted code JSON.
        /// Legacy (unsigned JSON) is accepted only when RequireSignature == false.
        /// </summary>
        public VerifyResult TryVerify(byte[] payload, long nowUnix)
        {
            if (payload == null || payload.Length == 0)
                return Fail("empty payload");

            // Legacy detection: a plain JSON object starts with '{'. A signed frame
            // begins with a little-endian u32 length, which for our envelopes is a
            // large value (>= ~90), so byte[0] is never '{' (0x7B) in practice.
            if (payload[0] == (byte)'{')
            {
                if (RequireSignature)
                    return Fail("unsigned NID-94 rejected (hardened profile requires a signature)");
                return new VerifyResult { Ok = true, Body = Encoding.UTF8.GetString(payload), Reason = "" };
            }

            // Signed frame: [u32 env_len][envelope][body]
            if (payload.Length < 4) return Fail("truncated frame");
            uint envLen = ReadU32(payload, 0);
            long envEnd = 4L + envLen;
            if (envEnd > payload.Length) return Fail("declared envelope length exceeds frame");
            int envStart = 4;
            int bodyStart = (int)envEnd;
            byte[] body = new byte[payload.Length - bodyStart];
            Array.Copy(payload, bodyStart, body, 0, body.Length);

            // Parse envelope fields sequentially; track the end of signing_input.
            int p = envStart;
            int end = bodyStart;
            try
            {
                ushort version = ReadU16(payload, ref p, end);
                if (version != EnvelopeVersion) return Fail("unsupported envelope version " + version);
                p += 1; // profile (u8) — part of signed region, value not enforced here
                uint nid = ReadU32(payload, p); p += 4;
                ulong seq = ReadU64(payload, p); p += 8;
                ulong expiry = ReadU64(payload, p); p += 8;
                SkipString(payload, ref p, end); // session_id
                SkipString(payload, ref p, end); // authenticated_peer_id
                SkipString(payload, ref p, end); // request_id
                SkipString(payload, ref p, end); // target_peer_id
                if (p + HashLen > end) return Fail("truncated payload_hash");
                int hashOff = p;
                p += HashLen;

                int signingInputEnd = p; // signing_input = [envStart, p)
                ushort tagLen = ReadU16(payload, ref p, end);
                if (tagLen > MaxTagLen) return Fail("tag too long");
                if (p + tagLen != end) return Fail("envelope length mismatch (trailing bytes)");
                byte[] tag = new byte[tagLen];
                Array.Copy(payload, p, tag, 0, tagLen);

                // (1) domain separation: this must be a NID-94 decision.
                if (nid != Nid94) return Fail("wrong domain (envelope NID " + nid + ")");

                // (2) freshness.
                if (nowUnix > (long)expiry + ClockSkewSeconds) return Fail("envelope expired");

                // (3) code-hash binding: the body is exactly what the backend signed.
                byte[] bodyHash = Sha256(body);
                for (int i = 0; i < HashLen; i++)
                    if (bodyHash[i] != payload[hashOff + i]) return Fail("body/code hash mismatch");

                // (4) authenticity: Ed25519 signature over the exact signing_input bytes.
                int siLen = signingInputEnd - envStart;
                byte[] signingInput = new byte[siLen];
                Array.Copy(payload, envStart, signingInput, 0, siLen);
                if (_ed == null) return Fail("no Ed25519 verifier configured");
                if (!_ed.Verify(_publicKey, signingInput, tag)) return Fail("invalid backend signature");

                _ = seq; // sequence/replay is enforced per-session by the caller if desired
                return new VerifyResult { Ok = true, Body = Encoding.UTF8.GetString(body), Reason = "" };
            }
            catch (Exception e)
            {
                return Fail("malformed envelope: " + e.Message);
            }
        }

        private static VerifyResult Fail(string reason) =>
            new VerifyResult { Ok = false, Body = null, Reason = reason };

        private static byte[] Sha256(byte[] data)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(data);
        }

        private static ushort ReadU16(byte[] b, ref int p, int end)
        {
            if (p + 2 > end) throw new FormatException("u16");
            ushort v = (ushort)(b[p] | (b[p + 1] << 8));
            p += 2;
            return v;
        }

        private static uint ReadU32(byte[] b, int p) =>
            (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));

        private static ulong ReadU64(byte[] b, int p)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)b[p + i] << (8 * i);
            return v;
        }

        private static void SkipString(byte[] b, ref int p, int end)
        {
            ushort len = ReadU16(b, ref p, end);
            if (len > MaxFieldLen) throw new FormatException("field too long");
            if (p + len > end) throw new FormatException("string overruns envelope");
            p += len;
        }
    }
}
