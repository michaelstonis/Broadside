using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Broadside;

/// <summary>A certificate a document is encrypted for, and what its holder may do with the document.</summary>
/// <remarks>ISO 32000-2 §7.6.5.1, §7.6.5.2 (Tables 23 and 24).</remarks>
public sealed class PdfEncryptionRecipient
{
    internal PdfEncryptionRecipient(X509Certificate2 certificate, PdfPermissions permissions, PdfAccessLevel access)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        // In a browser no certificate key can be read; the save refuses public-key encryption there (PdfEncryptionNotSupportedException).
        using (RSA? key = OperatingSystem.IsBrowser() ? null : certificate.GetRSAPublicKey())
        {
            if (key is null && !OperatingSystem.IsBrowser())
            {
                throw new ArgumentException(
                    $"The certificate '{certificate.Subject}' has no RSA public key; the enveloped data's key is encrypted for each recipient with RSA (key transport, RFC 5652 §6.2.1).",
                    nameof(certificate));
            }
        }

        Certificate = certificate;
        Permissions = permissions;
        Access = access;
    }

    /// <summary>Gets the recipient's certificate.</summary>
    /// <remarks>ISO 32000-2 §7.6.5.1: each recipient's X.509 public key certificate shall be available when encrypting.</remarks>
    public X509Certificate2 Certificate { get; }

    /// <summary>Gets what the recipient may do: <see cref="PdfPermissions.All"/> for an owner.</summary>
    /// <remarks>ISO 32000-2 §7.6.5.2, Table 24.</remarks>
    public PdfPermissions Permissions { get; }

    /// <summary>Gets the recipient's access: <see cref="PdfAccessLevel.Owner"/> may also change the encryption.</summary>
    /// <remarks>ISO 32000-2 §7.6.5.2, Table 24, bit 2.</remarks>
    public PdfAccessLevel Access { get; }
}
