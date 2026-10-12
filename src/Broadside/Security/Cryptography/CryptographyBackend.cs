using System.Security.Cryptography;

namespace Broadside.Security.Cryptography;

/// <summary>
/// Chooses, at run time, between the base class library's implementation of a primitive and the managed one in this folder.
/// </summary>
/// <remarks>
/// ADR 0001 and the WebAssembly commitment of the specification: the base class library has no MD5, AES or AES-GCM in the browser,
/// no AES-GCM before iOS and tvOS 13, and no RFC 3394 key wrap anywhere. Every primitive the security handlers use goes through the
/// facades here (<see cref="Md5"/>, <see cref="AesCipher"/>, <see cref="AesGcmCipher"/>, <see cref="AesKeyWrap"/>, <see cref="Rc4"/>,
/// <see cref="IncrementalDigest"/>), which use the base class library where the platform supports it and the managed implementation
/// elsewhere. SHA-3 (FIPS 202) is missing from the base class library on macOS, iOS and the browser, so <see cref="IncrementalDigest"/>
/// falls back to <see cref="ManagedSha3"/> there. SHA-2, HMAC and HKDF are available on every platform and are called directly.
/// </remarks>
internal static class CryptographyBackend
{
    private static readonly AsyncLocal<bool> ForcedManaged = new();

    /// <summary>Gets a value indicating whether MD5, AES and SHA-3 use the managed implementations.</summary>
    public static bool UseManaged => ForcedManaged.Value || OperatingSystem.IsBrowser();

    /// <summary>Gets a value indicating whether AES-GCM uses the managed implementation.</summary>
    public static bool UseManagedGcm => UseManaged || !AesGcm.IsSupported;

    /// <summary>Returns a value indicating whether the SHA-3 digest <paramref name="algorithm"/> uses the managed implementation.</summary>
    /// <param name="algorithm">SHA3-256, SHA3-384 or SHA3-512.</param>
    /// <returns><see langword="true"/> when the platform lacks it or the managed implementations are forced.</returns>
    public static bool UseManagedSha3(HashAlgorithmName algorithm) =>
        UseManaged
        || !(algorithm == HashAlgorithmName.SHA3_256 ? SHA3_256.IsSupported
            : algorithm == HashAlgorithmName.SHA3_384 ? SHA3_384.IsSupported
            : algorithm == HashAlgorithmName.SHA3_512 && SHA3_512.IsSupported);

    /// <summary>Makes the current asynchronous flow use the managed implementations until the result is disposed (tests).</summary>
    /// <returns>The scope.</returns>
    public static IDisposable ForceManaged()
    {
        bool previous = ForcedManaged.Value;
        ForcedManaged.Value = true;
        return new Scope(previous);
    }

    private sealed class Scope(bool previous) : IDisposable
    {
        public void Dispose() => ForcedManaged.Value = previous;
    }
}
