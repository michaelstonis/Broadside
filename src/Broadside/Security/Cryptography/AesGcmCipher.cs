using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Broadside.Security.Cryptography;

/// <summary>AES-GCM (NIST SP 800-38D) with a 12-byte IV, a 16-byte tag and no additional authenticated data, as ISO/TS 32003 uses it.</summary>
/// <remarks>
/// ISO/TS 32003 §5.2. The base class library's <see cref="AesGcm"/> where the platform has it (not the browser, not iOS or tvOS before
/// 13), the managed implementation below elsewhere (<see cref="CryptographyBackend"/>).
/// </remarks>
internal static class AesGcmCipher
{
    /// <summary>The IV length ISO/TS 32003 prescribes.</summary>
    public const int NonceSize = 12;

    /// <summary>The tag length ISO/TS 32003 prescribes.</summary>
    public const int TagSize = 16;

    /// <summary>Decrypts and authenticates.</summary>
    /// <param name="key">16, 24 or 32 bytes.</param>
    /// <param name="nonce">12 bytes.</param>
    /// <param name="ciphertext">The ciphertext.</param>
    /// <param name="tag">16 bytes.</param>
    /// <param name="plaintext">As long as <paramref name="ciphertext"/>; cleared when authentication fails.</param>
    /// <returns><see langword="false"/> when the tag does not authenticate the ciphertext.</returns>
    public static bool TryDecrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext)
    {
        if (!CryptographyBackend.UseManagedGcm)
        {
            using var gcm = new AesGcm(key, TagSize);
            try
            {
                gcm.Decrypt(nonce, ciphertext, tag, plaintext);
                return true;
            }
            catch (AuthenticationTagMismatchException)
            {
                return false;
            }
        }

        var aes = new ManagedAes(key);
        Span<byte> expected = stackalloc byte[TagSize];
        ComputeTag(aes, nonce, ciphertext, expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, tag))
        {
            plaintext[..ciphertext.Length].Clear();
            return false;
        }

        Counter(aes, nonce, ciphertext, plaintext);
        return true;
    }

    /// <summary>Encrypts and computes the tag (writers and tests).</summary>
    /// <param name="key">16, 24 or 32 bytes.</param>
    /// <param name="nonce">12 bytes.</param>
    /// <param name="plaintext">The plaintext.</param>
    /// <param name="ciphertext">As long as <paramref name="plaintext"/>.</param>
    /// <param name="tag">16 bytes.</param>
    public static void Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag)
    {
        if (!CryptographyBackend.UseManagedGcm)
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Encrypt(nonce, plaintext, ciphertext, tag);
            return;
        }

        var aes = new ManagedAes(key);
        Counter(aes, nonce, plaintext, ciphertext);
        ComputeTag(aes, nonce, ciphertext[..plaintext.Length], tag);
    }

    /// <summary>GCTR from inc32(J0) (SP 800-38D §6.5, §7.1 step 3 and §7.2 step 4).</summary>
    private static void Counter(ManagedAes aes, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> input, Span<byte> output)
    {
        Span<byte> counter = stackalloc byte[16];
        Span<byte> keystream = stackalloc byte[16];
        nonce[..NonceSize].CopyTo(counter);
        uint block = 1;
        for (int offset = 0; offset < input.Length; offset += 16)
        {
            block++;
            BinaryPrimitives.WriteUInt32BigEndian(counter[12..], block);
            aes.EncryptBlock(counter, keystream);
            int count = Math.Min(16, input.Length - offset);
            for (int i = 0; i < count; i++)
            {
                output[offset + i] = (byte)(input[offset + i] ^ keystream[i]);
            }
        }
    }

    /// <summary>T = E(K, J0) xor GHASH_H(C ‖ pad ‖ [0]64 ‖ [len(C)]64), with J0 = IV ‖ 0^31 ‖ 1 (SP 800-38D §7.1).</summary>
    private static void ComputeTag(ManagedAes aes, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, Span<byte> tag)
    {
        Span<byte> h = stackalloc byte[16];
        h.Clear();
        aes.EncryptBlock(h, h);
        ulong hHigh = BinaryPrimitives.ReadUInt64BigEndian(h);
        ulong hLow = BinaryPrimitives.ReadUInt64BigEndian(h[8..]);

        ulong yHigh = 0;
        ulong yLow = 0;
        Span<byte> block = stackalloc byte[16];
        for (int offset = 0; offset < ciphertext.Length; offset += 16)
        {
            block.Clear();
            ciphertext.Slice(offset, Math.Min(16, ciphertext.Length - offset)).CopyTo(block);
            yHigh ^= BinaryPrimitives.ReadUInt64BigEndian(block);
            yLow ^= BinaryPrimitives.ReadUInt64BigEndian(block[8..]);
            (yHigh, yLow) = Multiply(yHigh, yLow, hHigh, hLow);
        }

        yLow ^= (ulong)ciphertext.Length * 8;
        (yHigh, yLow) = Multiply(yHigh, yLow, hHigh, hLow);

        Span<byte> j0 = stackalloc byte[16];
        nonce[..NonceSize].CopyTo(j0);
        BinaryPrimitives.WriteUInt32BigEndian(j0[12..], 1);
        aes.EncryptBlock(j0, j0);
        BinaryPrimitives.WriteUInt64BigEndian(tag, yHigh ^ BinaryPrimitives.ReadUInt64BigEndian(j0));
        BinaryPrimitives.WriteUInt64BigEndian(tag[8..], yLow ^ BinaryPrimitives.ReadUInt64BigEndian(j0[8..]));
    }

    /// <summary>Multiplication in GF(2^128) with the GCM bit order (SP 800-38D §6.3, Algorithm 1).</summary>
    private static (ulong High, ulong Low) Multiply(ulong xHigh, ulong xLow, ulong yHigh, ulong yLow)
    {
        ulong zHigh = 0;
        ulong zLow = 0;
        ulong vHigh = yHigh;
        ulong vLow = yLow;
        for (int i = 0; i < 128; i++)
        {
            ulong bit = i < 64 ? (xHigh >> (63 - i)) & 1 : (xLow >> (127 - i)) & 1;
            if (bit != 0)
            {
                zHigh ^= vHigh;
                zLow ^= vLow;
            }

            bool carry = (vLow & 1) != 0;
            vLow = (vLow >> 1) | (vHigh << 63);
            vHigh >>= 1;
            if (carry)
            {
                vHigh ^= 0xE100000000000000UL;
            }
        }

        return (zHigh, zLow);
    }
}
