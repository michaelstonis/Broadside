using Broadside.Signatures;
using Broadside.Signatures.Validation;

namespace Broadside.Fuzz;

/// <summary>
/// The signature validation targets (issue #196). <c>signature-validation</c> opens the input as a whole file and validates every
/// signature; invariants: validation never throws, a signature is valid exactly when it has no problem, and a certificate path exists
/// exactly when a signer certificate was found, starting with it; a time-stamp token's status follows its problems the same way, and a
/// revocation is listed only for a certificate of the path. <c>cms</c> decodes the input as the <c>Contents</c> of a signature (a CMS
/// <c>ContentInfo</c>, zero padding allowed) and, when it decodes, reads a timestamp token's TSTInfo from its content and decodes the
/// revocation information and time-stamp tokens it carries (issue #197); invariant: none of it throws. <c>ocsp-response</c> and
/// <c>crl</c> (issue #197) decode the input as an OCSP response and a CRL, and look a serial number up in the CRL; invariant: neither
/// throws.
/// </summary>
/// <remarks>ISO 32000-2 §12.8.1, §12.8.3.3.1, §12.8.3.3.2, §12.8.5; RFC 5652 §5 and §10.2.1; RFC 3161 §2.4.2; RFC 6960 §4.2.1; RFC 5280 §5.1.</remarks>
internal static class SignatureValidationTarget
{
    private static readonly PdfEngine Engine = new(new PdfOptions().UseSystemFontResolver(null));

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument? document = FuzzTargets.OpenOrNull(data, Engine);
        if (document is null)
        {
            return;
        }

        foreach (PdfSignatureValidation validation in document.ValidateSignatures())
        {
            if ((validation.Status == PdfSignatureStatus.Valid) != (validation.Problems.Count == 0))
            {
                throw new InvalidOperationException($"Status {validation.Status} with {validation.Problems.Count} problems.");
            }

            if ((validation.SignerCertificate is null) != (validation.CertificatePath.Count == 0)
                || (validation.SignerCertificate is { } signer && !ReferenceEquals(signer, validation.CertificatePath[0])))
            {
                throw new InvalidOperationException("The certificate path does not start with the signer certificate.");
            }

            if (validation.Timestamp is { } timestamp && (timestamp.Status == PdfSignatureStatus.Valid) != (timestamp.Problems.Count == 0))
            {
                throw new InvalidOperationException($"Time-stamp status {timestamp.Status} with {timestamp.Problems.Count} problems.");
            }

            if (validation.Revocations.Any(revocation => !validation.CertificatePath.Contains(revocation.Certificate)))
            {
                throw new InvalidOperationException("A revocation names a certificate outside the path.");
            }
        }
    }

    public static void Cms(ReadOnlySpan<byte> data)
    {
        if (!CmsSignature.TryDecode(data.ToArray(), out CmsSignature cms, out _))
        {
            return;
        }

        if (cms.Content is { } content)
        {
            _ = CmsSignature.TryReadTstInfo(content, out _);
        }

        foreach (byte[] response in cms.OcspResponses)
        {
            OcspResponse(response);
        }

        foreach (byte[] list in cms.Crls)
        {
            Crl(list);
        }

        foreach (byte[] token in cms.TimestampTokens)
        {
            Cms(token);
        }
    }

    public static void OcspResponse(ReadOnlySpan<byte> data) => _ = Signatures.Validation.OcspResponse.TryDecode(data.ToArray(), out _);

    public static void Crl(ReadOnlySpan<byte> data)
    {
        if (RevocationList.TryDecode(data.ToArray(), out RevocationList? list))
        {
            _ = list!.TryFind(data[..Math.Min(data.Length, 8)], out _, out _);
        }
    }
}
