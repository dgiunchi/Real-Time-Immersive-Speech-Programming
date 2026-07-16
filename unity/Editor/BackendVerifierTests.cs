using System.Text;
using DreamCodeVRPlus.Security;
using NUnit.Framework;

namespace DreamCodeVRPlus.EditorTests
{
    /// <summary>
    /// EditMode tests for the NID-94 signature gate's pure logic — passthrough,
    /// fail-closed, and malformed-input handling. Device-free. The actual Ed25519
    /// ACCEPT path needs a real provider (BouncyCastle/NaCl) and is verified on-device;
    /// here we prove the parsing + policy are correct with a fail-closed NullEd25519Verifier.
    /// </summary>
    public sealed class BackendVerifierTests
    {
        private static byte[] Json(string s) => Encoding.UTF8.GetBytes(s);

        [Test]
        public void UnsignedJson_PassesThrough_WhenSignatureNotRequired()
        {
            // Legacy default: an unsigned {"type":"code"} JSON body is accepted as-is.
            var v = new BackendVerifier(new byte[32], null) { RequireSignature = false };
            var r = v.TryVerify(Json("{\"type\":\"code\",\"data\":\"cube.red()\"}"), 1000);
            Assert.IsTrue(r.Ok, "unsigned JSON must pass through when not required");
            StringAssert.Contains("\"type\":\"code\"", r.Body);
        }

        [Test]
        public void UnsignedJson_Rejected_WhenSignatureRequired()
        {
            // Hardened: an unsigned NID-94 must be rejected (fail-closed).
            var v = new BackendVerifier(new byte[32], new NullEd25519Verifier())
            {
                RequireSignature = true
            };
            Assert.IsFalse(
                v.TryVerify(Json("{\"type\":\"code\"}"), 1000).Ok,
                "hardened profile must reject unsigned NID-94");
        }

        [Test]
        public void NullVerifier_IsFailClosed()
        {
            // The default provider verifies nothing — so a signed frame cannot be accepted
            // without a real Ed25519 provider installed.
            Assert.IsFalse(new NullEd25519Verifier().Verify(new byte[32], new byte[] { 1 }, new byte[64]));
        }

        [Test]
        public void Empty_And_Truncated_Frames_AreRejected()
        {
            var v = new BackendVerifier(new byte[32], new NullEd25519Verifier())
            {
                RequireSignature = true
            };
            Assert.IsFalse(v.TryVerify(new byte[0], 1000).Ok, "empty payload rejected");
            // A non-'{' leading byte is treated as a (signed) frame; too short to parse.
            Assert.IsFalse(v.TryVerify(new byte[] { 0x50, 0x00, 0x00 }, 1000).Ok, "truncated frame rejected");
        }

        [Test]
        public void SignedFrame_WithoutRealVerifier_FailsClosed()
        {
            // A well-formed signed frame whose body-hash is correct still fails at the
            // signature step because no real Ed25519 provider is present — proving the
            // signature check is load-bearing and fail-closed.
            byte[] body = Json("{\"type\":\"code\",\"data\":\"x\"}");
            byte[] frame = BuildSignedFrame(body, correctHash: true);
            var v = new BackendVerifier(new byte[32], new NullEd25519Verifier())
            {
                RequireSignature = true
            };
            var r = v.TryVerify(frame, 1000);
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("signature", r.Reason.ToLowerInvariant());
        }

        [Test]
        public void SignedFrame_WithWrongHash_IsRejectedBeforeSignature()
        {
            byte[] body = Json("{\"type\":\"code\"}");
            byte[] frame = BuildSignedFrame(body, correctHash: false);
            var v = new BackendVerifier(new byte[32], new NullEd25519Verifier())
            {
                RequireSignature = true
            };
            var r = v.TryVerify(frame, 1000);
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("hash", r.Reason.ToLowerInvariant());
        }

        // Build a signed frame `[u32 env_len][envelope][body]` matching the Rust layout,
        // with a placeholder 64-byte tag (a real Ed25519 provider would reject it, which
        // is exactly what these tests assert). The FIRST byte must not be '{' so it is
        // parsed as a signed frame; the envelope starts with a u16 version = 1 (0x01,0x00).
        private static byte[] BuildSignedFrame(byte[] body, bool correctHash)
        {
            var env = new System.Collections.Generic.List<byte>();
            void U16(ushort x) { env.Add((byte)(x & 0xFF)); env.Add((byte)(x >> 8)); }
            void U32(uint x) { for (int i = 0; i < 4; i++) env.Add((byte)(x >> (8 * i))); }
            void U64(ulong x) { for (int i = 0; i < 8; i++) env.Add((byte)(x >> (8 * i))); }
            void Str(string s)
            {
                var b = Encoding.UTF8.GetBytes(s);
                U16((ushort)b.Length);
                env.AddRange(b);
            }
            U16(1);              // version
            env.Add(1);          // profile
            U32(94);             // network_id_b (must be NID-94)
            U64(7);              // sequence_number
            U64(4000000000UL);   // expiry (far future)
            Str("sess");
            Str("backend");
            Str("req");
            Str("");
            byte[] hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = sha.ComputeHash(body);
            if (!correctHash) hash[0] ^= 0xFF;
            env.AddRange(hash);          // payload_hash (32)
            U16(64);                     // tag_len
            env.AddRange(new byte[64]);  // placeholder signature

            var frame = new System.Collections.Generic.List<byte>();
            uint envLen = (uint)env.Count;
            for (int i = 0; i < 4; i++) frame.Add((byte)(envLen >> (8 * i)));
            frame.AddRange(env);
            frame.AddRange(body);
            return frame.ToArray();
        }
    }
}
