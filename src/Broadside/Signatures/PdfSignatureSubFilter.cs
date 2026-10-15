namespace Broadside.Signatures;

/// <summary>The encoding of a signature's value, written as the signature dictionary's <c>SubFilter</c>.</summary>
/// <remarks>ISO 32000-2 §12.8.3.1, Table 260; §12.8.3.3 and §12.8.3.4.</remarks>
public enum PdfSignatureSubFilter
{
    /// <summary>
    /// <c>adbe.pkcs7.detached</c>: a detached CMS signature over the byte ranges (ISO 32000-2 §12.8.3.3), readable by every PDF
    /// version that has the digest algorithm (PDF 1.6 for SHA-256).
    /// </summary>
    AdobePkcs7Detached,

    /// <summary>
    /// <c>ETSI.CAdES.detached</c>: a PAdES signature (ISO 32000-2 §12.8.3.4; ETSI EN 319 142-1), here the baseline B-B level: a
    /// detached CMS signature whose signed attributes hold the content type, the message digest and the signing certificate, and
    /// no signing time. PDF 2.0.
    /// </summary>
    EtsiCadesDetached,
}
