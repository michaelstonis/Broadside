using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Broadside.Signatures.Validation;

/// <summary>
/// A decoded certificate revocation list (RFC 5280 §5.1): the signed <c>tbsCertList</c>, its issuer and update times, the revoked serial
/// numbers with their dates and reasons, and the scope its issuing distribution point gives it.
/// </summary>
/// <remarks>
/// RFC 5280 §5.1 (structure), §5.2.4 and §5.2.5 (delta CRLs and issuing distribution point), §5.3.1 (reason code), §5.3.3
/// (certificate issuer). Delta CRLs, indirect CRLs, CRLs partitioned by reason and CRLs with a critical extension this reader does not
/// know are decoded but not <see cref="Complete"/>, so they never establish that a certificate is good (RFC 5280 §6.3.3).
/// </remarks>
internal sealed class RevocationList
{
    private const string ReasonCodeOid = "2.5.29.21";
    private const string CertificateIssuerOid = "2.5.29.29";
    private const string IssuingDistributionPointOid = "2.5.29.28";
    private const string DeltaCrlIndicatorOid = "2.5.29.27";
    private const string CrlNumberOid = "2.5.29.20";
    private const string AuthorityKeyIdentifierOid = "2.5.29.35";
    private const string FreshestCrlOid = "2.5.29.46";

    private RevocationList()
    {
    }

    /// <summary>Gets the DER encoding of <c>tbsCertList</c>, what the signature covers.</summary>
    public ReadOnlyMemory<byte> SignedData { get; private init; }

    /// <summary>Gets the signature algorithm.</summary>
    public AlgorithmIdentifier SignatureAlgorithm { get; private init; }

    /// <summary>Gets the signature value.</summary>
    public byte[] Signature { get; private init; } = [];

    /// <summary>Gets the issuer's distinguished name.</summary>
    public X500DistinguishedName Issuer { get; private init; } = new(string.Empty);

    /// <summary>Gets the issue date.</summary>
    public DateTimeOffset ThisUpdate { get; private init; }

    /// <summary>Gets the date by which the next list will be issued, when given.</summary>
    public DateTimeOffset? NextUpdate { get; private init; }

    /// <summary>
    /// Gets a value indicating whether the list covers every reason and every certificate of its scope, issued by its issuer: not a
    /// delta, not indirect, not partitioned by reason, and without unknown critical extensions.
    /// </summary>
    public bool Complete { get; private init; }

    /// <summary>Gets the encoded <c>CertificateList</c>, as a document security store holds it (ISO 32000-2 §12.8.4.3, Table 261).</summary>
    public ReadOnlyMemory<byte> Encoded { get; private init; }

    /// <summary>Gets a value indicating whether the list only covers end-entity certificates.</summary>
    public bool OnlyUserCertificates { get; private init; }

    /// <summary>Gets a value indicating whether the list only covers certification authority certificates.</summary>
    public bool OnlyAuthorityCertificates { get; private init; }

    private Dictionary<string, (DateTimeOffset Date, X509RevocationReason? Reason)> Entries { get; init; } = [];

    /// <summary>Finds the entry for the serial number <paramref name="serial"/> (leading zeros removed).</summary>
    public bool TryFind(ReadOnlySpan<byte> serial, out DateTimeOffset date, out X509RevocationReason? reason)
    {
        if (Entries.TryGetValue(Convert.ToHexString(serial), out (DateTimeOffset Date, X509RevocationReason? Reason) entry))
        {
            (date, reason) = entry;
            return true;
        }

        date = default;
        reason = null;
        return false;
    }

    /// <summary>Decodes a <c>CertificateList</c>.</summary>
    /// <param name="encoded">The encoding.</param>
    /// <param name="list">The list.</param>
    /// <returns>Whether it decoded.</returns>
    public static bool TryDecode(ReadOnlyMemory<byte> encoded, out RevocationList? list)
    {
        list = null;
        try
        {
            AsnReader certificateList = new AsnReader(encoded, AsnEncodingRules.BER).ReadSequence();
            ReadOnlyMemory<byte> signed = certificateList.PeekEncodedValue();
            AsnReader tbs = certificateList.ReadSequence();
            AlgorithmIdentifier algorithm = AlgorithmIdentifier.Read(certificateList);
            byte[] signature = certificateList.ReadBitString(out _);

            if (tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Integer))
            {
                _ = tbs.ReadIntegerBytes();
            }

            _ = AlgorithmIdentifier.Read(tbs);
            var issuer = new X500DistinguishedName(tbs.ReadEncodedValue().Span);
            DateTimeOffset thisUpdate = ReadTime(tbs);
            DateTimeOffset? nextUpdate = tbs.HasData && IsTime(tbs.PeekTag()) ? ReadTime(tbs) : null;
            bool complete = true;
            var entries = new Dictionary<string, (DateTimeOffset, X509RevocationReason?)>(StringComparer.Ordinal);
            if (tbs.HasData && tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
            {
                AsnReader revoked = tbs.ReadSequence();
                while (revoked.HasData)
                {
                    AsnReader entry = revoked.ReadSequence();
                    string serial = Convert.ToHexString(OcspResponse.TrimLeadingZeros(entry.ReadIntegerBytes()).Span);
                    DateTimeOffset date = ReadTime(entry);
                    X509RevocationReason? reason = null;
                    if (entry.HasData)
                    {
                        AsnReader extensions = entry.ReadSequence();
                        while (extensions.HasData)
                        {
                            (string id, bool critical, byte[] value) = ReadExtension(extensions.ReadSequence());
                            if (id == ReasonCodeOid)
                            {
                                var code = new AsnReader(value, AsnEncodingRules.BER);
                                reason = OcspResponse.ReadEnumerated(code) is { } number ? (X509RevocationReason)number : null;
                            }
                            else if (id == CertificateIssuerOid || critical)
                            {
                                // RFC 5280 §5.3.3: an indirect list's entries can name other issuers.
                                complete = false;
                            }
                        }
                    }

                    entries.TryAdd(serial, (date, reason));
                }
            }

            bool onlyUser = false;
            bool onlyAuthority = false;
            if (tbs.HasData)
            {
                AsnReader extensions = tbs.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
                while (extensions.HasData)
                {
                    (string id, bool critical, byte[] value) = ReadExtension(extensions.ReadSequence());
                    switch (id)
                    {
                        case IssuingDistributionPointOid:
                            complete &= ReadIssuingDistributionPoint(value, out onlyUser, out onlyAuthority);
                            break;
                        case DeltaCrlIndicatorOid:
                            complete = false;
                            break;
                        case CrlNumberOid or AuthorityKeyIdentifierOid or FreshestCrlOid:
                            break;
                        default:
                            complete &= !critical;
                            break;
                    }
                }
            }

            list = new RevocationList
            {
                Encoded = encoded,
                SignedData = signed,
                SignatureAlgorithm = algorithm,
                Signature = signature,
                Issuer = issuer,
                ThisUpdate = thisUpdate,
                NextUpdate = nextUpdate,
                Complete = complete,
                OnlyUserCertificates = onlyUser,
                OnlyAuthorityCertificates = onlyAuthority,
                Entries = entries,
            };
            return true;
        }
        catch (AsnContentException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads an issuing distribution point (RFC 5280 §5.2.5): whether the list is complete for its scope (not indirect, not partitioned
    /// by reason, not for attribute certificates) and which certificates the scope holds.
    /// </summary>
    private static bool ReadIssuingDistributionPoint(byte[] value, out bool onlyUser, out bool onlyAuthority)
    {
        onlyUser = false;
        onlyAuthority = false;
        bool complete = true;
        AsnReader point = new AsnReader(value, AsnEncodingRules.BER).ReadSequence();
        while (point.HasData)
        {
            Asn1Tag tag = point.PeekTag();
            if (tag.TagClass != TagClass.ContextSpecific || tag.IsConstructed)
            {
                _ = point.ReadEncodedValue();
                continue;
            }

            switch (tag.TagValue)
            {
                case 1:
                    onlyUser = point.ReadBoolean(tag);
                    break;
                case 2:
                    onlyAuthority = point.ReadBoolean(tag);
                    break;
                case 3:
                    _ = point.ReadEncodedValue();
                    complete = false;
                    break;
                case 4 or 5:
                    complete &= !point.ReadBoolean(tag);
                    break;
                default:
                    _ = point.ReadEncodedValue();
                    break;
            }
        }

        return complete;
    }

    private static (string Id, bool Critical, byte[] Value) ReadExtension(AsnReader extension)
    {
        string id = extension.ReadObjectIdentifier();
        bool critical = extension.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean) && extension.ReadBoolean();
        return (id, critical, extension.ReadOctetString());
    }

    private static bool IsTime(Asn1Tag tag) => tag.HasSameClassAndValue(Asn1Tag.UtcTime) || tag.HasSameClassAndValue(Asn1Tag.GeneralizedTime);

    /// <summary>A <c>Time</c> (RFC 5280 §4.1.2.5): UTCTime with years 1950 to 2049, or GeneralizedTime.</summary>
    internal static DateTimeOffset ReadTime(AsnReader reader) =>
        reader.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) ? reader.ReadUtcTime(twoDigitYearMax: 2049) : reader.ReadGeneralizedTime();
}
