using System.Security.Cryptography;

namespace Broadside.Security.Cryptography;

/// <summary>
/// An incremental message digest over SHA-1, SHA-2 or SHA-3, the latter from the base class library where the platform has it and from
/// <see cref="ManagedSha3"/> elsewhere (<see cref="CryptographyBackend"/>).
/// </summary>
/// <remarks>
/// <para>
/// ISO/TS 32001 §5.1 adds SHA3-256, SHA3-384 and SHA3-512 (FIPS 202) to the digests a signature may use, and ISO/TS 32004 Table 8
/// to those a PDF MAC token may use. The base class library implements SHA-3 only where the operating system's library does
/// (Windows 11 and Linux with OpenSSL 1.1.1 or later, not macOS, iOS or the browser), so SHA-3 goes through this facade and works
/// everywhere. SHA-256, SHA-384 and SHA-512 exist on every platform and pass straight through to <see cref="IncrementalHash"/>, as
/// does SHA-1, which signature validation still needs for signatures made before PDF 2.0 deprecated it (ISO 32000-2 Table 260).
/// </para>
/// <para>
/// Byte ranges are hashed with <see cref="AppendData"/> a window at a time; neither implementation allocates per call, so a
/// signed range of any length costs one digest object. One instance per hashing operation; not thread-safe.
/// </para>
/// </remarks>
internal sealed class IncrementalDigest : IDisposable
{
    private readonly IncrementalHash? _platform;
    private readonly ManagedSha3? _managed;

    private IncrementalDigest(HashAlgorithmName algorithm, IncrementalHash? platform, ManagedSha3? managed, int hashLengthInBytes)
    {
        AlgorithmName = algorithm;
        _platform = platform;
        _managed = managed;
        HashLengthInBytes = hashLengthInBytes;
    }

    /// <summary>Gets the digest algorithm.</summary>
    public HashAlgorithmName AlgorithmName { get; }

    /// <summary>Gets the digest length in bytes.</summary>
    public int HashLengthInBytes { get; }

    /// <summary>Returns a value indicating whether <paramref name="algorithm"/> is one this facade computes (on every platform).</summary>
    /// <param name="algorithm">SHA-1, SHA-256, SHA-384, SHA-512, SHA3-256, SHA3-384 or SHA3-512.</param>
    /// <returns><see langword="true"/> for those seven.</returns>
    public static bool IsSupported(HashAlgorithmName algorithm) => HashLength(algorithm) > 0;

    /// <summary>Starts a digest.</summary>
    /// <param name="algorithm">SHA-1, SHA-256, SHA-384, SHA-512, SHA3-256, SHA3-384 or SHA3-512.</param>
    /// <returns>The digest; dispose it.</returns>
    /// <exception cref="CryptographicException"><paramref name="algorithm"/> is not one of the seven.</exception>
    public static IncrementalDigest Create(HashAlgorithmName algorithm)
    {
        int length = HashLength(algorithm);
        if (length == 0)
        {
            throw new CryptographicException($"The digest algorithm '{algorithm.Name}' is not supported.");
        }

        return IsSha3(algorithm) && CryptographyBackend.UseManagedSha3(algorithm)
            ? new IncrementalDigest(algorithm, null, new ManagedSha3(length), length)
            : new IncrementalDigest(algorithm, IncrementalHash.CreateHash(algorithm), null, length);
    }

    /// <summary>Hashes <paramref name="source"/> in one call.</summary>
    /// <param name="algorithm">SHA-1, SHA-256, SHA-384, SHA-512, SHA3-256, SHA3-384 or SHA3-512.</param>
    /// <param name="source">The data.</param>
    /// <returns>The digest.</returns>
    public static byte[] HashData(HashAlgorithmName algorithm, ReadOnlySpan<byte> source)
    {
        using IncrementalDigest digest = Create(algorithm);
        digest.AppendData(source);
        return digest.GetHashAndReset();
    }

    /// <summary>Adds the next bytes of the message.</summary>
    /// <param name="data">The bytes.</param>
    public void AppendData(ReadOnlySpan<byte> data)
    {
        if (_managed is not null)
        {
            _managed.AppendData(data);
        }
        else
        {
            _platform!.AppendData(data);
        }
    }

    /// <summary>Writes the digest of the message so far and starts over with an empty one.</summary>
    /// <param name="destination">At least <see cref="HashLengthInBytes"/> bytes.</param>
    /// <returns>The bytes written, <see cref="HashLengthInBytes"/>.</returns>
    public int GetHashAndReset(Span<byte> destination)
    {
        if (_managed is not null)
        {
            _managed.GetHashAndReset(destination);
            return HashLengthInBytes;
        }

        return _platform!.GetHashAndReset(destination);
    }

    /// <summary>Returns the digest of the message so far and starts over with an empty one.</summary>
    /// <returns>The digest.</returns>
    public byte[] GetHashAndReset()
    {
        byte[] digest = new byte[HashLengthInBytes];
        GetHashAndReset(digest);
        return digest;
    }

    /// <inheritdoc/>
    public void Dispose() => _platform?.Dispose();

    private static bool IsSha3(HashAlgorithmName algorithm) =>
        algorithm == HashAlgorithmName.SHA3_256 || algorithm == HashAlgorithmName.SHA3_384 || algorithm == HashAlgorithmName.SHA3_512;

    private static int HashLength(HashAlgorithmName algorithm) =>
        algorithm == HashAlgorithmName.SHA1 ? 20
        : algorithm == HashAlgorithmName.SHA256 || algorithm == HashAlgorithmName.SHA3_256 ? 32
        : algorithm == HashAlgorithmName.SHA384 || algorithm == HashAlgorithmName.SHA3_384 ? 48
        : algorithm == HashAlgorithmName.SHA512 || algorithm == HashAlgorithmName.SHA3_512 ? 64
        : 0;
}
