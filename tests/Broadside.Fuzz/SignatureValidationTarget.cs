using Broadside.Signatures;
using Broadside.Signatures.Validation;

namespace Broadside.Fuzz;

/// <summary>
/// The signature validation targets (issue #196). <c>signature-validation</c> opens the input as a whole file and validates every
/// signature; invariants: validation never throws, a signature is valid exactly when it has no problem, and a certificate path exists
/// exactly when a signer certificate was found, starting with it. <c>cms</c> decodes the input as the <c>Contents</c> of a signature
/// (a CMS <c>ContentInfo</c>, zero padding allowed) and, when it decodes, reads a timestamp token's message imprint from its content;
/// invariant: neither throws.
/// </summary>
/// <remarks>ISO 32000-2 §12.8.1, §12.8.3.3.1, §12.8.5; RFC 5652 §5; RFC 3161 §2.4.2.</remarks>
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
        }
    }

    public static void Cms(ReadOnlySpan<byte> data)
    {
        if (CmsSignature.TryDecode(data.ToArray(), out CmsSignature cms, out _) && cms.Content is { } content)
        {
            _ = CmsSignature.TryReadMessageImprint(content, out _, out _);
        }
    }
}
