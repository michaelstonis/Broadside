using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using Broadside.Security.Cryptography;

namespace Broadside.Signatures.Validation;

/// <summary>
/// RSA signature verification in managed code: the public-key operation with <see cref="BigInteger.ModPow"/> and the two encoding
/// checks of RFC 8017, EMSA-PKCS1-v1_5 (§9.2) and EMSA-PSS (§9.1.2), over any digest <see cref="IncrementalDigest"/> computes.
/// </summary>
/// <remarks>
/// RFC 8017 §5.2.2 (RSAVP1), §8.1.2 and §8.2.2. Used where the platform cannot verify: SHA-3 digests on macOS, iOS and the browser,
/// PSS salt lengths other than the digest length, and the browser for every RSA signature. Verification handles only public values,
/// so it need not run in constant time.
/// </remarks>
internal static class ManagedRsa
{
    /// <summary>RSASSA-PKCS1-v1_5 verification (RFC 8017 §8.2.2), accepting a DigestInfo with or without NULL parameters (§9.2, note 2).</summary>
    public static bool VerifyPkcs1(byte[] modulus, byte[] exponent, string digestOid, ReadOnlySpan<byte> digest, ReadOnlySpan<byte> signature)
    {
        int length = modulus.Length;
        if (!TryOpen(modulus, exponent, signature, length, out byte[] encoded))
        {
            return false;
        }

        return encoded.AsSpan().SequenceEqual(Pkcs1Encoding(digestOid, digest, length, withNull: true))
            || encoded.AsSpan().SequenceEqual(Pkcs1Encoding(digestOid, digest, length, withNull: false));
    }

    /// <summary>RSASSA-PSS verification (RFC 8017 §8.1.2 with EMSA-PSS-VERIFY, §9.1.2) with MGF1 (§B.2.1).</summary>
    public static bool VerifyPss(
        byte[] modulus,
        byte[] exponent,
        HashAlgorithmName digestAlgorithm,
        HashAlgorithmName maskDigest,
        int saltLength,
        ReadOnlySpan<byte> digest,
        ReadOnlySpan<byte> signature)
    {
        long modulusBits = new BigInteger(modulus, isUnsigned: true, isBigEndian: true).GetBitLength();
        int encodedBits = (int)(modulusBits - 1);
        int encodedLength = (encodedBits + 7) / 8;
        if (!TryOpen(modulus, exponent, signature, modulus.Length, out byte[] opened))
        {
            return false;
        }

        // I2OSP(m, emLen): the leading bytes past emLen must be zero (RFC 8017 §8.1.2 step 2c).
        int excess = opened.Length - encodedLength;
        if (excess < 0 || opened.AsSpan(0, excess).IndexOfAnyExcept((byte)0) >= 0)
        {
            return false;
        }

        ReadOnlySpan<byte> encoded = opened.AsSpan(excess);
        int digestLength = digest.Length;
        if (encodedLength < digestLength + saltLength + 2 || encoded[^1] != 0xBC)
        {
            return false;
        }

        int maskedLength = encodedLength - digestLength - 1;
        ReadOnlySpan<byte> masked = encoded[..maskedLength];
        ReadOnlySpan<byte> hash = encoded.Slice(maskedLength, digestLength);
        int unusedBits = (8 * encodedLength) - encodedBits;
        byte topMask = (byte)(0xFF >> unusedBits);
        if ((masked[0] & ~topMask) != 0)
        {
            return false;
        }

        byte[] block = Mgf1(hash, maskedLength, maskDigest);
        for (int index = 0; index < maskedLength; index++)
        {
            block[index] ^= masked[index];
        }

        block[0] &= topMask;
        int padding = encodedLength - digestLength - saltLength - 2;
        if (block.AsSpan(0, padding).IndexOfAnyExcept((byte)0) >= 0 || block[padding] != 0x01)
        {
            return false;
        }

        using IncrementalDigest check = IncrementalDigest.Create(digestAlgorithm);
        check.AppendData(stackalloc byte[8]);
        check.AppendData(digest);
        check.AppendData(block.AsSpan(maskedLength - saltLength));
        return check.GetHashAndReset().AsSpan().SequenceEqual(hash);
    }

    /// <summary>RSAVP1 (RFC 8017 §5.2.2): the signature as an integer below the modulus, raised to the exponent, as <paramref name="length"/> bytes.</summary>
    private static bool TryOpen(byte[] modulus, byte[] exponent, ReadOnlySpan<byte> signature, int length, out byte[] encoded)
    {
        encoded = [];
        if (signature.Length != length)
        {
            return false;
        }

        var n = new BigInteger(modulus, isUnsigned: true, isBigEndian: true);
        var s = new BigInteger(signature, isUnsigned: true, isBigEndian: true);
        if (n.IsZero || s >= n)
        {
            return false;
        }

        BigInteger m = BigInteger.ModPow(s, new BigInteger(exponent, isUnsigned: true, isBigEndian: true), n);
        encoded = new byte[length];
        int size = m.GetByteCount(isUnsigned: true);
        return m.TryWriteBytes(encoded.AsSpan(length - size), out _, isUnsigned: true, isBigEndian: true);
    }

    /// <summary>EMSA-PKCS1-v1_5 (RFC 8017 §9.2): <c>00 01 FF..FF 00 DigestInfo</c>.</summary>
    private static byte[] Pkcs1Encoding(string digestOid, ReadOnlySpan<byte> digest, int length, bool withNull)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(digestOid);
                if (withNull)
                {
                    writer.WriteNull();
                }
            }

            writer.WriteOctetString(digest);
        }

        byte[] info = writer.Encode();
        byte[] encoded = new byte[length];
        if (info.Length + 11 > length)
        {
            return [];
        }

        encoded[1] = 0x01;
        encoded.AsSpan(2, length - info.Length - 3).Fill(0xFF);
        info.CopyTo(encoded, length - info.Length);
        return encoded;
    }

    /// <summary>MGF1 (RFC 8017 §B.2.1).</summary>
    private static byte[] Mgf1(ReadOnlySpan<byte> seed, int length, HashAlgorithmName digestAlgorithm)
    {
        byte[] mask = new byte[length];
        using IncrementalDigest digest = IncrementalDigest.Create(digestAlgorithm);
        Span<byte> counter = stackalloc byte[4];
        Span<byte> block = stackalloc byte[digest.HashLengthInBytes];
        for (int offset = 0, round = 0; offset < length; round++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(counter, round);
            digest.AppendData(seed);
            digest.AppendData(counter);
            digest.GetHashAndReset(block);
            int take = Math.Min(block.Length, length - offset);
            block[..take].CopyTo(mask.AsSpan(offset));
            offset += take;
        }

        return mask;
    }
}
