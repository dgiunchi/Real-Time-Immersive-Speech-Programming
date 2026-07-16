using System.Text;
using DreamCodeVRPlus.Security;
using NUnit.Framework;

namespace DreamCodeVRPlus.EditorTests
{
    /// <summary>
    /// Cross-language golden-vector test: the Unity ClientEnvelopeSigner must produce a
    /// frame byte-identical to the Rust encoder (auth_gate::golden_client_envelope_is_stable),
    /// so a client-signed message verifies against the backend's verify_incoming. If either
    /// side changes the wire layout, this test (and its Rust twin) fail.
    /// </summary>
    public sealed class ClientEnvelopeSignerTests
    {
        private const string RustGolden =
            "64000000010002620000000100000000000000009435770000000001007301" +
            "007001007200008f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac" +
            "88b59b2dc327aa42000296c78d32e2f229a0f1cd5a0cfd38ee0717342074be9" +
            "50fb93ccc7257d424c476869";

        private static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        [Test]
        public void ClientEnvelope_MatchesRustGoldenVector()
        {
            var signer = new ClientEnvelopeSigner(Encoding.UTF8.GetBytes("golden-secret"))
            {
                Profile = 2
            };
            byte[] frame = signer.Sign(
                networkIdB: 98,
                sequence: 1,
                expiryUnix: 2000000000UL,
                sessionId: "s",
                authenticatedPeerId: "p",
                requestId: "r",
                targetPeerId: "",
                body: Encoding.UTF8.GetBytes("hi"));

            Assert.AreEqual(
                RustGolden,
                Hex(frame),
                "the Unity client envelope must be byte-identical to what the Rust backend signs/verifies");
        }

        [Test]
        public void DifferentBody_ChangesTheFrame()
        {
            var signer = new ClientEnvelopeSigner(Encoding.UTF8.GetBytes("golden-secret")) { Profile = 2 };
            string a = Hex(signer.Sign(98, 1, 2000000000UL, "s", "p", "r", "", Encoding.UTF8.GetBytes("hi")));
            string b = Hex(signer.Sign(98, 1, 2000000000UL, "s", "p", "r", "", Encoding.UTF8.GetBytes("ho")));
            Assert.AreNotEqual(a, b, "the payload hash + body must change the frame");
        }
    }
}
