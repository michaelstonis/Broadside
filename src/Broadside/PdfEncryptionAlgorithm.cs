namespace Broadside;

/// <summary>How the standard security handler encrypts a saved document's strings and streams.</summary>
/// <remarks>
/// ISO 32000-2 §7.6.2 (Table 20, <c>V</c>), §7.6.3 and §7.6.4.2 (Table 21, <c>R</c>); ISO/TS 32003. <see cref="Aes256"/> is the PDF 2.0
/// algorithm and <see cref="Aes256Gcm"/> its ISO/TS 32003 extension; the others are deprecated in PDF 2.0 and are written only when
/// chosen explicitly, for readers that predate AES-256, with an <c>EncryptionDeprecated</c> diagnostic.
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

    /// <summary>
    /// AES-256 in Galois/Counter Mode with a random 256-bit file key: <c>V</c> 6, revision 7, crypt filter method <c>AESV4</c>. PDF 2.0
    /// with the ISO/TS 32003 extension, which the save declares in the catalog's <c>Extensions</c> (<c>ISO_</c>, level 32003). Every
    /// string and stream carries its own authentication tag; few readers support it yet.
    /// </summary>
    /// <remarks>
    /// ISO/TS 32003 §5.1 (Tables 2 to 4: <c>V</c> 6, <c>R</c> 7, <c>AESV4</c>; the password algorithms of revision 6) and §5.2 (AES-GCM
    /// with the file key, a 12-byte IV per string or stream, a 16-byte tag, no additional authenticated data).
    /// </remarks>
    Aes256Gcm,
}
