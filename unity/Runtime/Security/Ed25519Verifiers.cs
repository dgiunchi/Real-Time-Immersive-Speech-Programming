// IEd25519Verifier implementations for BackendVerifier.
//
// BackendVerifier does all the envelope parsing + binding (version / domain / expiry /
// SHA-256 code-hash) in pure managed code; the ONE thing it delegates is the Ed25519
// signature check, because .NET Standard 2.1 / Unity Mono has no built-in Ed25519.
//
// Default (no plugin): NullEd25519Verifier — fail-closed, so a hardened deployment with
// no real Ed25519 provider REJECTS every signed frame (nothing compiles) rather than
// accepting unverified code. To actually accept backend signatures, add a provider:
//   * BouncyCastle: add the package / DLL and define DCVR_BOUNCYCASTLE (adapter below), or
//   * Chaos.NaCl / libsodium: implement IEd25519Verifier over that library the same way.
using System;

namespace DreamCodeVRPlus.Security
{
    /// <summary>
    /// Fail-closed default: no signature ever verifies. Safe when no real Ed25519 provider
    /// is installed — hardened NID-94 then rejects everything (nothing runs), instead of
    /// silently accepting unverified code.
    /// </summary>
    public sealed class NullEd25519Verifier : IEd25519Verifier
    {
        public bool Verify(byte[] publicKey32, byte[] message, byte[] signature64) => false;
    }

#if DCVR_BOUNCYCASTLE
    /// <summary>
    /// Ed25519 via BouncyCastle (Org.BouncyCastle.Math.EC.Rfc8032.Ed25519). Compiled only
    /// when the BouncyCastle package/DLL is present and DCVR_BOUNCYCASTLE is defined.
    /// </summary>
    public sealed class BouncyCastleEd25519Verifier : IEd25519Verifier
    {
        public bool Verify(byte[] publicKey32, byte[] message, byte[] signature64)
        {
            if (publicKey32 == null || publicKey32.Length != 32) return false;
            if (signature64 == null || signature64.Length != 64) return false;
            if (message == null) return false;
            try
            {
                return Org.BouncyCastle.Math.EC.Rfc8032.Ed25519.Verify(
                    signature64, 0, publicKey32, 0, message, 0, message.Length);
            }
            catch
            {
                return false;
            }
        }
    }
#endif
}
