namespace Broadside;

/// <summary>How the standard security handler encrypts a saved document's strings and streams.</summary>
/// <remarks>
/// ISO 32000-2 §7.6.2 (Table 20, <c>V</c>), §7.6.3 and §7.6.4.2 (Table 21, <c>R</c>). Only <see cref="Aes256"/> is a PDF 2.0
/// algorithm; the others are deprecated in PDF 2.0 and are written only when chosen explicitly, for readers that predate AES-256, with
/// an <c>EncryptionDeprecated</c> diagnostic.
/// </remarks>
public enum PdfEncryptionAlgorithm
{
    /// <summary>AES-256 in CBC mode with a random 256-bit file key: <c>V</c> 5, revision 6, crypt filter method <c>AESV3</c>. PDF 2.0.</summary>
    /// <remarks>ISO 32000-2 §7.6.3.3 (Algorithm 1.A), §7.6.4.3.3 (Algorithm 2.A), §7.6.4.4.7-§7.6.4.4.9 (Algorithms 8 to 10).</remarks>
    Aes256,

    /// <summary>
    /// Deprecated: AES-128 in CBC mode with a 128-bit key derived from the user password: <c>V</c> 4, revision 4, crypt filter
    /// method <c>AESV2</c>. PDF 1.6.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.6.3.2 (Algorithm 1), §7.6.4.3.2 (Algorithm 2), §7.6.4.4.2-§7.6.4.4.4 (Algorithms 3 and 5).</remarks>
    Aes128,

    /// <summary>Deprecated: RC4 with a 128-bit key derived from the user password: <c>V</c> 2, revision 3. PDF 1.4.</summary>
    /// <remarks>ISO 32000-2 §7.6.3.2 (Algorithm 1), §7.6.4.3.2 (Algorithm 2), §7.6.4.4.2-§7.6.4.4.4 (Algorithms 3 and 5).</remarks>
    Rc4,
}
