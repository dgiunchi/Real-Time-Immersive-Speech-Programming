// Client -> backend authenticated envelope signer — the Unity counterpart that makes
// hardened INCOMING verification actually enforce on the wire. It mirrors the Rust
// `AuthEnvelope` + `EnvelopeMac` byte-for-byte, so a frame it produces verifies against
// the backend's `verify_incoming`. A cross-language golden-vector test
// (unity/Editor/ClientEnvelopeSignerTests.cs vs the Rust `golden_client_envelope_is_stable`
// test) pins the two encoders together.
//
// HMAC-SHA256 + SHA-256 are the audited BUILT-IN providers (System.Security.Cryptography);
// nothing here hand-rolls cryptography. Off unless a client is configured with an
// admission secret (the plain path stays byte-identical for legacy).
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace DreamCodeVRPlus.Security
{
    public sealed class ClientEnvelopeSigner
    {
        public const ushort EnvelopeVersion = 1;

        private readonly byte[] _secret;

        /// <summary>Security profile byte bound into the signed region: 0 legacy / 1 hardened / 2 test.</summary>
        public byte Profile { get; set; } = 1;

        public ClientEnvelopeSigner(byte[] admissionSecret)
        {
            _secret = admissionSecret;
        }

        /// <summary>
        /// Build a signed frame <c>[u32 env_len][envelope][body]</c> for <paramref name="body"/>
        /// on channel <paramref name="networkIdB"/>. The envelope is
        /// <c>signing_input || [u16 tag_len][HMAC-SHA256(secret, signing_input)]</c>, and
        /// <c>payload_hash = SHA-256(body)</c> — identical to the Rust encoder.
        /// </summary>
        public byte[] Sign(
            uint networkIdB,
            ulong sequence,
            ulong expiryUnix,
            string sessionId,
            string authenticatedPeerId,
            string requestId,
            string targetPeerId,
            byte[] body)
        {
            byte[] payloadHash;
            using (var sha = SHA256.Create())
            {
                payloadHash = sha.ComputeHash(body ?? new byte[0]);
            }

            var si = new List<byte>();
            void U16(ushort x)
            {
                si.Add((byte)(x & 0xFF));
                si.Add((byte)(x >> 8));
            }
            void U32(uint x)
            {
                for (int i = 0; i < 4; i++) si.Add((byte)(x >> (8 * i)));
            }
            void U64(ulong x)
            {
                for (int i = 0; i < 8; i++) si.Add((byte)(x >> (8 * i)));
            }
            void Str(string s)
            {
                byte[] b = Encoding.UTF8.GetBytes(s ?? "");
                U16((ushort)b.Length);
                si.AddRange(b);
            }

            U16(EnvelopeVersion);
            si.Add(Profile);
            U32(networkIdB);
            U64(sequence);
            U64(expiryUnix);
            Str(sessionId);
            Str(authenticatedPeerId);
            Str(requestId);
            Str(targetPeerId);
            si.AddRange(payloadHash);

            byte[] signingInput = si.ToArray();
            byte[] tag;
            using (var h = new HMACSHA256(_secret))
            {
                tag = h.ComputeHash(signingInput);
            }

            var envelope = new List<byte>(signingInput);
            envelope.Add((byte)(tag.Length & 0xFF)); // u16 tag_len (LE)
            envelope.Add((byte)(tag.Length >> 8));
            envelope.AddRange(tag);

            var frame = new List<byte>();
            uint envLen = (uint)envelope.Count;
            for (int i = 0; i < 4; i++) frame.Add((byte)(envLen >> (8 * i)));
            frame.AddRange(envelope);
            if (body != null) frame.AddRange(body);
            return frame.ToArray();
        }
    }
}
