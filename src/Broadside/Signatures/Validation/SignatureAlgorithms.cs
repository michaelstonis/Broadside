using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Security.Cryptography;

namespace Broadside.Signatures.Validation;

/// <summary>An ASN.1 <c>AlgorithmIdentifier</c>: an object identifier and its encoded parameters, if any (RFC 5280 §4.1.1.2).</summary>
/// <param name="Oid">The algorithm's object identifier.</param>
/// <param name="Parameters">The DER or BER encoding of the parameters, or empty when there are none.</param>
internal readonly record struct AlgorithmIdentifier(string Oid, ReadOnlyMemory<byte> Parameters)
{
    /// <summary>Reads an <c>AlgorithmIdentifier</c> sequence.</summary>
    public static AlgorithmIdentifier Read(AsnReader reader)
    {
        AsnReader sequence = reader.ReadSequence();
        string oid = sequence.ReadObjectIdentifier();
        ReadOnlyMemory<byte> parameters = sequence.HasData ? sequence.ReadEncodedValue() : ReadOnlyMemory<byte>.Empty;
        return new AlgorithmIdentifier(oid, parameters);
    }
}

/// <summary>The outcome of checking one signature value.</summary>
internal enum SignatureCheck
{
    /// <summary>The signature value verifies.</summary>
    Valid,

    /// <summary>The signature value does not verify, or the key cannot have made it.</summary>
    Invalid,

    /// <summary>The algorithm is not one this table implements, or the platform cannot run it.</summary>
    Unsupported,
}

/// <summary>
/// The digest and signature algorithms validation recognizes, as tables keyed by object identifier, and the verification of a
/// signature value over a digest with a certificate's public key: RSA (PKCS #1 v1.5 and PSS), ECDSA and DSA.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.8.3.1, Table 260 (SHA-1, SHA-256, SHA-384, SHA-512; RSA, DSA, ECDSA per RFC 5480); ISO/TS 32001 §5.1 (SHA-3);
/// ISO/TS 32002 §5 (ECDSA curves, RSASSA-PSS); RFC 5754, RFC 4055, RFC 5758 and the NIST CSOR registrations for the identifiers.
/// A new algorithm is a new table row, not a new code path, so signatures from issue #194 (ECDSA, RSA-PSS, SHA-3) validate as soon as
/// they are written.
/// </para>
/// <para>
/// RSA verifies through the platform where it implements the digest and padding, and through <see cref="ManagedRsa"/> where it does
/// not (SHA-3 on macOS, iOS and the browser; PSS with a salt length other than the digest length; whenever
/// <see cref="CryptographyBackend.UseManaged"/>). ECDSA and DSA always use the platform; where it lacks them (the browser) the result
/// is <see cref="SignatureCheck.Unsupported"/>.
/// </para>
/// </remarks>
internal static class SignatureAlgorithms
{
    private const string RsaEncryption = "1.2.840.113549.1.1.1";
    private const string RsaPss = "1.2.840.113549.1.1.10";
    private const string EcPublicKey = "1.2.840.10045.2.1";
    private const string DsaKey = "1.2.840.10040.4.1";
    private const string Mgf1 = "1.2.840.113549.1.1.8";

    /// <summary>Digest algorithms by identifier, including the RSA signature identifiers some signers write in their place.</summary>
    private static readonly Dictionary<string, HashAlgorithmName> Digests = new(StringComparer.Ordinal)
    {
        ["1.3.14.3.2.26"] = HashAlgorithmName.SHA1,
        ["2.16.840.1.101.3.4.2.1"] = HashAlgorithmName.SHA256,
        ["2.16.840.1.101.3.4.2.2"] = HashAlgorithmName.SHA384,
        ["2.16.840.1.101.3.4.2.3"] = HashAlgorithmName.SHA512,
        ["2.16.840.1.101.3.4.2.8"] = HashAlgorithmName.SHA3_256,
        ["2.16.840.1.101.3.4.2.9"] = HashAlgorithmName.SHA3_384,
        ["2.16.840.1.101.3.4.2.10"] = HashAlgorithmName.SHA3_512,
        ["1.2.840.113549.1.1.5"] = HashAlgorithmName.SHA1,
        ["1.2.840.113549.1.1.11"] = HashAlgorithmName.SHA256,
        ["1.2.840.113549.1.1.12"] = HashAlgorithmName.SHA384,
        ["1.2.840.113549.1.1.13"] = HashAlgorithmName.SHA512,
    };

    /// <summary>Signature algorithms by identifier: the key they need and their digest (<see langword="null"/> when the signer info's applies).</summary>
    private static readonly Dictionary<string, (KeyKind Key, HashAlgorithmName? Digest)> Signatures = new(StringComparer.Ordinal)
    {
        [RsaEncryption] = (KeyKind.Rsa, null),
        ["1.2.840.113549.1.1.5"] = (KeyKind.Rsa, HashAlgorithmName.SHA1),
        ["1.2.840.113549.1.1.11"] = (KeyKind.Rsa, HashAlgorithmName.SHA256),
        ["1.2.840.113549.1.1.12"] = (KeyKind.Rsa, HashAlgorithmName.SHA384),
        ["1.2.840.113549.1.1.13"] = (KeyKind.Rsa, HashAlgorithmName.SHA512),
        ["2.16.840.1.101.3.4.3.14"] = (KeyKind.Rsa, HashAlgorithmName.SHA3_256),
        ["2.16.840.1.101.3.4.3.15"] = (KeyKind.Rsa, HashAlgorithmName.SHA3_384),
        ["2.16.840.1.101.3.4.3.16"] = (KeyKind.Rsa, HashAlgorithmName.SHA3_512),
        [RsaPss] = (KeyKind.RsaPss, null),
        [EcPublicKey] = (KeyKind.Ecdsa, null),
        ["1.2.840.10045.4.1"] = (KeyKind.Ecdsa, HashAlgorithmName.SHA1),
        ["1.2.840.10045.4.3.2"] = (KeyKind.Ecdsa, HashAlgorithmName.SHA256),
        ["1.2.840.10045.4.3.3"] = (KeyKind.Ecdsa, HashAlgorithmName.SHA384),
        ["1.2.840.10045.4.3.4"] = (KeyKind.Ecdsa, HashAlgorithmName.SHA512),
        ["2.16.840.1.101.3.4.3.10"] = (KeyKind.Ecdsa, HashAlgorithmName.SHA3_256),
        ["2.16.840.1.101.3.4.3.11"] = (KeyKind.Ecdsa, HashAlgorithmName.SHA3_384),
        ["2.16.840.1.101.3.4.3.12"] = (KeyKind.Ecdsa, HashAlgorithmName.SHA3_512),
        [DsaKey] = (KeyKind.Dsa, null),
        ["1.2.840.10040.4.3"] = (KeyKind.Dsa, HashAlgorithmName.SHA1),
        ["2.16.840.1.101.3.4.3.2"] = (KeyKind.Dsa, HashAlgorithmName.SHA256),
    };

    /// <summary>The digest identifiers DigestInfo uses for RSASSA-PKCS1-v1_5 (RFC 8017 §9.2).</summary>
    private static readonly Dictionary<HashAlgorithmName, string> DigestOids = new()
    {
        [HashAlgorithmName.SHA1] = "1.3.14.3.2.26",
        [HashAlgorithmName.SHA256] = "2.16.840.1.101.3.4.2.1",
        [HashAlgorithmName.SHA384] = "2.16.840.1.101.3.4.2.2",
        [HashAlgorithmName.SHA512] = "2.16.840.1.101.3.4.2.3",
        [HashAlgorithmName.SHA3_256] = "2.16.840.1.101.3.4.2.8",
        [HashAlgorithmName.SHA3_384] = "2.16.840.1.101.3.4.2.9",
        [HashAlgorithmName.SHA3_512] = "2.16.840.1.101.3.4.2.10",
    };

    private enum KeyKind
    {
        Rsa,
        RsaPss,
        Ecdsa,
        Dsa,
    }

    /// <summary>Finds the digest algorithm an identifier names.</summary>
    public static bool TryGetDigest(string oid, out HashAlgorithmName digest) => Digests.TryGetValue(oid, out digest);

    /// <summary>
    /// The digest a signature algorithm hashes with: its own (sha256WithRSAEncryption, ecdsa-with-SHA384, the PSS parameters'), or
    /// <paramref name="signerDigest"/> for a bare key algorithm (rsaEncryption, id-ecPublicKey) as CMS signer infos write it.
    /// </summary>
    /// <returns><see langword="false"/> when the algorithm or its parameters are not in the tables.</returns>
    public static bool TryGetSignatureDigest(AlgorithmIdentifier algorithm, HashAlgorithmName? signerDigest, out HashAlgorithmName digest)
    {
        digest = default;
        if (!Signatures.TryGetValue(algorithm.Oid, out (KeyKind Key, HashAlgorithmName? Digest) entry))
        {
            return false;
        }

        if (entry.Key == KeyKind.RsaPss)
        {
            if (!TryReadPssParameters(algorithm.Parameters, out PssParameters parameters))
            {
                return false;
            }

            digest = parameters.Digest;
            return true;
        }

        if ((entry.Digest ?? signerDigest) is not { } chosen || !IncrementalDigest.IsSupported(chosen))
        {
            return false;
        }

        digest = chosen;
        return true;
    }

    /// <summary>Verifies <paramref name="signature"/> over a digest computed with <see cref="TryGetSignatureDigest"/>.</summary>
    /// <param name="algorithm">The signature algorithm.</param>
    /// <param name="digestAlgorithm">The digest algorithm <paramref name="digest"/> was computed with.</param>
    /// <param name="key">The signer's public key.</param>
    /// <param name="digest">The digest of the signed data.</param>
    /// <param name="signature">The signature value.</param>
    public static SignatureCheck Verify(AlgorithmIdentifier algorithm, HashAlgorithmName digestAlgorithm, PublicKey key, ReadOnlySpan<byte> digest, ReadOnlySpan<byte> signature)
    {
        if (!Signatures.TryGetValue(algorithm.Oid, out (KeyKind Key, HashAlgorithmName? Digest) entry))
        {
            return SignatureCheck.Unsupported;
        }

        try
        {
            return entry.Key switch
            {
                KeyKind.Rsa => VerifyRsaPkcs1(digestAlgorithm, key, digest, signature),
                KeyKind.RsaPss => TryReadPssParameters(algorithm.Parameters, out PssParameters parameters)
                    ? VerifyRsaPss(parameters, key, digest, signature)
                    : SignatureCheck.Unsupported,
                KeyKind.Ecdsa => VerifyEcdsa(key, digest, signature),
                _ => VerifyDsa(key, digest, signature),
            };
        }
        catch (PlatformNotSupportedException)
        {
            return SignatureCheck.Unsupported;
        }
        catch (CryptographicException)
        {
            // A key the platform cannot import, or a signature value it cannot parse: either way nothing verified.
            return SignatureCheck.Invalid;
        }
    }

    private static SignatureCheck VerifyRsaPkcs1(HashAlgorithmName digestAlgorithm, PublicKey key, ReadOnlySpan<byte> digest, ReadOnlySpan<byte> signature)
    {
        if (!TryReadRsaKey(key, out byte[] modulus, out byte[] exponent) || !DigestOids.TryGetValue(digestAlgorithm, out string? digestOid))
        {
            return key.Oid.Value is RsaEncryption or RsaPss ? SignatureCheck.Unsupported : SignatureCheck.Invalid;
        }

        if (UseManagedRsa(digestAlgorithm))
        {
            return ManagedRsa.VerifyPkcs1(modulus, exponent, digestOid, digest, signature) ? SignatureCheck.Valid : SignatureCheck.Invalid;
        }

        using RSA rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
        return rsa.VerifyHash(digest, signature, digestAlgorithm, RSASignaturePadding.Pkcs1) ? SignatureCheck.Valid : SignatureCheck.Invalid;
    }

    private static SignatureCheck VerifyRsaPss(PssParameters parameters, PublicKey key, ReadOnlySpan<byte> digest, ReadOnlySpan<byte> signature)
    {
        if (!TryReadRsaKey(key, out byte[] modulus, out byte[] exponent))
        {
            return key.Oid.Value is RsaEncryption or RsaPss ? SignatureCheck.Unsupported : SignatureCheck.Invalid;
        }

        int digestLength = IncrementalDigest.HashData(parameters.Digest, []).Length;
        bool platform = !UseManagedRsa(parameters.Digest) && parameters.MaskDigest == parameters.Digest && parameters.SaltLength == digestLength;
        if (!platform)
        {
            return ManagedRsa.VerifyPss(modulus, exponent, parameters.Digest, parameters.MaskDigest, parameters.SaltLength, digest, signature)
                ? SignatureCheck.Valid
                : SignatureCheck.Invalid;
        }

        using RSA rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
        return rsa.VerifyHash(digest, signature, parameters.Digest, RSASignaturePadding.Pss) ? SignatureCheck.Valid : SignatureCheck.Invalid;
    }

    private static SignatureCheck VerifyEcdsa(PublicKey key, ReadOnlySpan<byte> digest, ReadOnlySpan<byte> signature)
    {
        if (key.Oid.Value != EcPublicKey)
        {
            return SignatureCheck.Invalid;
        }

        using ECDsa? ecdsa = key.GetECDsaPublicKey();
        if (ecdsa is null)
        {
            return SignatureCheck.Unsupported;
        }

        return ecdsa.VerifyHash(digest, signature, DSASignatureFormat.Rfc3279DerSequence) ? SignatureCheck.Valid : SignatureCheck.Invalid;
    }

    private static SignatureCheck VerifyDsa(PublicKey key, ReadOnlySpan<byte> digest, ReadOnlySpan<byte> signature)
    {
        if (key.Oid.Value != DsaKey)
        {
            return SignatureCheck.Invalid;
        }

        using DSA? dsa = key.GetDSAPublicKey();
        if (dsa is null)
        {
            return SignatureCheck.Unsupported;
        }

        return dsa.VerifySignature(digest, signature, DSASignatureFormat.Rfc3279DerSequence) ? SignatureCheck.Valid : SignatureCheck.Invalid;
    }

    /// <summary>Whether RSA with <paramref name="digest"/> goes through <see cref="ManagedRsa"/>: forced, or a SHA-3 the platform lacks.</summary>
    private static bool UseManagedRsa(HashAlgorithmName digest) =>
        CryptographyBackend.UseManaged
        || ((digest == HashAlgorithmName.SHA3_256 || digest == HashAlgorithmName.SHA3_384 || digest == HashAlgorithmName.SHA3_512) && CryptographyBackend.UseManagedSha3(digest));

    /// <summary>Reads the modulus and public exponent of an RSA key (RFC 8017 §A.1.1), whether it is marked rsaEncryption or RSASSA-PSS.</summary>
    private static bool TryReadRsaKey(PublicKey key, out byte[] modulus, out byte[] exponent)
    {
        modulus = [];
        exponent = [];
        if (key.Oid.Value is not (RsaEncryption or RsaPss))
        {
            return false;
        }

        try
        {
            var reader = new AsnReader(key.EncodedKeyValue.RawData, AsnEncodingRules.BER);
            AsnReader sequence = reader.ReadSequence();
            modulus = TrimLeadingZeros(sequence.ReadIntegerBytes().Span);
            exponent = TrimLeadingZeros(sequence.ReadIntegerBytes().Span);
            return modulus.Length > 0 && exponent.Length > 0;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private static byte[] TrimLeadingZeros(ReadOnlySpan<byte> value)
    {
        int start = value.IndexOfAnyExcept((byte)0);
        return start < 0 ? [] : value[start..].ToArray();
    }

    /// <summary>
    /// Reads <c>RSASSA-PSS-params</c> (RFC 8017 §A.2.3): the digest (default SHA-1), MGF1 and its digest (default SHA-1) and the
    /// salt length (default 20); the trailer field must be 1.
    /// </summary>
    private static bool TryReadPssParameters(ReadOnlyMemory<byte> encoded, out PssParameters parameters)
    {
        parameters = new PssParameters(HashAlgorithmName.SHA1, HashAlgorithmName.SHA1, 20);
        if (encoded.IsEmpty)
        {
            return true;
        }

        try
        {
            var reader = new AsnReader(encoded, AsnEncodingRules.BER);
            AsnReader sequence = reader.ReadSequence();
            HashAlgorithmName digest = HashAlgorithmName.SHA1;
            HashAlgorithmName mask = HashAlgorithmName.SHA1;
            int salt = 20;
            if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                AsnReader explicitTag = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
                if (!Digests.TryGetValue(AlgorithmIdentifier.Read(explicitTag).Oid, out digest))
                {
                    return false;
                }
            }

            if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
            {
                AsnReader explicitTag = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1));
                AlgorithmIdentifier generator = AlgorithmIdentifier.Read(explicitTag);
                if (generator.Oid != Mgf1 || generator.Parameters.IsEmpty
                    || !Digests.TryGetValue(AlgorithmIdentifier.Read(new AsnReader(generator.Parameters, AsnEncodingRules.BER)).Oid, out mask))
                {
                    return false;
                }
            }

            if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)))
            {
                AsnReader explicitTag = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 2));
                if (!explicitTag.TryReadInt32(out salt) || salt < 0)
                {
                    return false;
                }
            }

            if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 3)))
            {
                AsnReader explicitTag = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 3));
                if (!explicitTag.TryReadInt32(out int trailer) || trailer != 1)
                {
                    return false;
                }
            }

            parameters = new PssParameters(digest, mask, salt);
            return IncrementalDigest.IsSupported(digest) && IncrementalDigest.IsSupported(mask);
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private readonly record struct PssParameters(HashAlgorithmName Digest, HashAlgorithmName MaskDigest, int SaltLength);
}
