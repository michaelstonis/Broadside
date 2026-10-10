using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Broadside.Security.Cryptography;

/// <summary>
/// AES with one key, in the CBC and ECB modes ISO 32000-2 uses, always without padding (the caller strips PKCS#5 padding itself so
/// a damaged pad can be repaired rather than rejected).
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.6.3 (AES-CBC for strings and streams), §7.6.4.3.3-7.6.4.3.4 (CBC with a zero IV for OE and UE, CBC for the
/// Algorithm 2.B hash), §7.6.4.4.12 (ECB for Perms); RFC 3394 (ECB blocks for the key wrap). The base class library's AES where the
/// platform has it, <see cref="ManagedAes"/> elsewhere. Not thread-safe: create one per use.
/// </remarks>
[SuppressMessage("Security", "CA5358:Review cipher mode usage with cryptography experts", Justification = "ISO 32000-2 §7.6.4.4.12 and RFC 3394 prescribe ECB on single blocks.")]
internal sealed class AesCipher : IDisposable
{
    private readonly Aes? _aes;
    private ManagedAes? _managed;

    private AesCipher(ReadOnlySpan<byte> key)
    {
        if (CryptographyBackend.UseManaged)
        {
            _managed = new ManagedAes(key);
        }
        else
        {
            _aes = Aes.Create();
            _aes.SetKey(key);
        }
    }

    /// <summary>The block size in bytes.</summary>
    public const int BlockSize = 16;

    /// <summary>Creates a cipher for <paramref name="key"/>.</summary>
    /// <param name="key">16, 24 or 32 bytes.</param>
    /// <returns>The cipher. Dispose it.</returns>
    public static AesCipher Create(ReadOnlySpan<byte> key) => new(key);

    /// <summary>
    /// Replaces the key, keeping the platform cipher object: Algorithm 2.B re-keys AES in every one of its 64 or more rounds, and a
    /// new cipher per round costs as much as the round's encryption on some platforms.
    /// </summary>
    /// <param name="key">16, 24 or 32 bytes.</param>
    public void SetKey(ReadOnlySpan<byte> key)
    {
        if (_aes is not null)
        {
            _aes.SetKey(key);
        }
        else
        {
            _managed = new ManagedAes(key);
        }
    }

    /// <summary>Decrypts whole blocks in CBC mode without removing any padding.</summary>
    /// <param name="ciphertext">A multiple of 16 bytes.</param>
    /// <param name="iv">16 bytes.</param>
    /// <param name="destination">At least as long as <paramref name="ciphertext"/>.</param>
    public void DecryptCbc(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> iv, Span<byte> destination)
    {
        if (_aes is not null)
        {
            _aes.DecryptCbc(ciphertext, iv, destination, PaddingMode.None);
            return;
        }

        Span<byte> previous = stackalloc byte[BlockSize];
        Span<byte> current = stackalloc byte[BlockSize];
        iv[..BlockSize].CopyTo(previous);
        for (int offset = 0; offset < ciphertext.Length; offset += BlockSize)
        {
            ReadOnlySpan<byte> block = ciphertext.Slice(offset, BlockSize);
            block.CopyTo(current);
            Span<byte> output = destination.Slice(offset, BlockSize);
            _managed!.DecryptBlock(block, output);
            for (int i = 0; i < BlockSize; i++)
            {
                output[i] ^= previous[i];
            }

            current.CopyTo(previous);
        }
    }

    /// <summary>Encrypts whole blocks in CBC mode without adding padding.</summary>
    /// <param name="plaintext">A multiple of 16 bytes.</param>
    /// <param name="iv">16 bytes.</param>
    /// <param name="destination">At least as long as <paramref name="plaintext"/>.</param>
    public void EncryptCbc(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> iv, Span<byte> destination)
    {
        if (_aes is not null)
        {
            _aes.EncryptCbc(plaintext, iv, destination, PaddingMode.None);
            return;
        }

        Span<byte> chain = stackalloc byte[BlockSize];
        iv[..BlockSize].CopyTo(chain);
        for (int offset = 0; offset < plaintext.Length; offset += BlockSize)
        {
            for (int i = 0; i < BlockSize; i++)
            {
                chain[i] ^= plaintext[offset + i];
            }

            _managed!.EncryptBlock(chain, chain);
            chain.CopyTo(destination.Slice(offset, BlockSize));
        }
    }

    /// <summary>Decrypts whole blocks in ECB mode.</summary>
    /// <param name="ciphertext">A multiple of 16 bytes.</param>
    /// <param name="destination">At least as long as <paramref name="ciphertext"/>.</param>
    public void DecryptEcb(ReadOnlySpan<byte> ciphertext, Span<byte> destination)
    {
        if (_aes is not null)
        {
            _aes.DecryptEcb(ciphertext, destination, PaddingMode.None);
            return;
        }

        for (int offset = 0; offset < ciphertext.Length; offset += BlockSize)
        {
            _managed!.DecryptBlock(ciphertext.Slice(offset, BlockSize), destination.Slice(offset, BlockSize));
        }
    }

    /// <summary>Encrypts whole blocks in ECB mode.</summary>
    /// <param name="plaintext">A multiple of 16 bytes.</param>
    /// <param name="destination">At least as long as <paramref name="plaintext"/>.</param>
    public void EncryptEcb(ReadOnlySpan<byte> plaintext, Span<byte> destination)
    {
        if (_aes is not null)
        {
            _aes.EncryptEcb(plaintext, destination, PaddingMode.None);
            return;
        }

        for (int offset = 0; offset < plaintext.Length; offset += BlockSize)
        {
            _managed!.EncryptBlock(plaintext.Slice(offset, BlockSize), destination.Slice(offset, BlockSize));
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _aes?.Dispose();
}
