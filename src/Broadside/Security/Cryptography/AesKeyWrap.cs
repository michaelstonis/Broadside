using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Broadside.Security.Cryptography;

/// <summary>The AES key wrap of RFC 3394 (without padding), which ISO/TS 32004 uses to protect the MAC key.</summary>
/// <remarks>
/// ISO/TS 32004 §6.3.3, Table 7. The base class library implements only the padded variant of RFC 5649 (a different initial value
/// and a length prefix), so the wrap is built here on single AES blocks (<see cref="AesCipher"/>).
/// </remarks>
internal static class AesKeyWrap
{
    private const ulong DefaultInitialValue = 0xA6A6A6A6A6A6A6A6UL;

    /// <summary>Unwraps a key (RFC 3394 §2.2.2, index-based) and checks its integrity (§2.2.3.1).</summary>
    /// <param name="keyEncryptionKey">The KEK: 16, 24 or 32 bytes.</param>
    /// <param name="wrapped">The wrapped key: a multiple of 8 bytes, at least 24.</param>
    /// <returns>The key, or <see langword="null"/> when the input is malformed or fails the integrity check.</returns>
    public static byte[]? Unwrap(ReadOnlySpan<byte> keyEncryptionKey, ReadOnlySpan<byte> wrapped)
    {
        if (wrapped.Length < 24 || wrapped.Length % 8 != 0)
        {
            return null;
        }

        int n = (wrapped.Length / 8) - 1;
        ulong a = BinaryPrimitives.ReadUInt64BigEndian(wrapped);
        byte[] r = wrapped[8..].ToArray();
        Span<byte> block = stackalloc byte[16];
        using AesCipher aes = AesCipher.Create(keyEncryptionKey);
        for (int j = 5; j >= 0; j--)
        {
            for (int i = n; i >= 1; i--)
            {
                ulong t = (ulong)((n * j) + i);
                BinaryPrimitives.WriteUInt64BigEndian(block, a ^ t);
                r.AsSpan((i - 1) * 8, 8).CopyTo(block[8..]);
                aes.DecryptEcb(block, block);
                a = BinaryPrimitives.ReadUInt64BigEndian(block);
                block[8..].CopyTo(r.AsSpan((i - 1) * 8, 8));
            }
        }

        Span<byte> check = stackalloc byte[8];
        Span<byte> expected = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(check, a);
        BinaryPrimitives.WriteUInt64BigEndian(expected, DefaultInitialValue);
        if (!CryptographicOperations.FixedTimeEquals(check, expected))
        {
            CryptographicOperations.ZeroMemory(r);
            return null;
        }

        return r;
    }

    /// <summary>Wraps a key (RFC 3394 §2.2.1, index-based); used by writers and tests.</summary>
    /// <param name="keyEncryptionKey">The KEK: 16, 24 or 32 bytes.</param>
    /// <param name="key">The key to wrap: a multiple of 8 bytes, at least 16.</param>
    /// <returns>The wrapped key, 8 bytes longer.</returns>
    public static byte[] Wrap(ReadOnlySpan<byte> keyEncryptionKey, ReadOnlySpan<byte> key)
    {
        if (key.Length < 16 || key.Length % 8 != 0)
        {
            throw new ArgumentException("RFC 3394 wraps keys of at least 16 bytes, in multiples of 8.", nameof(key));
        }

        int n = key.Length / 8;
        ulong a = DefaultInitialValue;
        byte[] output = new byte[key.Length + 8];
        key.CopyTo(output.AsSpan(8));
        Span<byte> block = stackalloc byte[16];
        using AesCipher aes = AesCipher.Create(keyEncryptionKey);
        for (int j = 0; j <= 5; j++)
        {
            for (int i = 1; i <= n; i++)
            {
                BinaryPrimitives.WriteUInt64BigEndian(block, a);
                output.AsSpan(i * 8, 8).CopyTo(block[8..]);
                aes.EncryptEcb(block, block);
                a = BinaryPrimitives.ReadUInt64BigEndian(block) ^ (ulong)((n * j) + i);
                block[8..].CopyTo(output.AsSpan(i * 8, 8));
            }
        }

        BinaryPrimitives.WriteUInt64BigEndian(output, a);
        return output;
    }
}
