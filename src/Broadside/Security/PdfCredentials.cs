using System.Text;

namespace Broadside.Security;

/// <summary>What a reader offers a security handler to open an encrypted document: a password, a certificate, or another secret.</summary>
/// <remarks>
/// ISO 32000-2 §7.6.4 (passwords for the standard security handler) and §7.6.5 (certificates for public-key handlers). A handler
/// accepts the kinds it understands and treats others as no credential.
/// </remarks>
public abstract class PdfCredentials
{
    /// <summary>Initializes a new instance of the <see cref="PdfCredentials"/> class.</summary>
    protected PdfCredentials()
    {
    }
}

/// <summary>A password for the standard security handler, the user or the owner password.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.4. Given as text, the password is encoded the way the document's revision requires: PDFDocEncoding for revisions
/// 2 to 4 (§7.6.4.3.2, step a; when that fails, Latin-1, as other readers do), SASLprep and UTF-8 truncated to 127 bytes for revision 6
/// and later (§7.6.4.1), UTF-8 for the deprecated revision 5. Given as bytes, it is used as is, for passwords in a legacy code page.
/// </para>
/// <para>
/// SASLprep's normalization (NFKC) uses the platform's Unicode data; in an application built with invariant globalization it is not
/// available, so a revision 6 password must then be given in its normalized form (what keyboards produce for most scripts). The
/// mapping steps (non-ASCII spaces to SPACE, soft hyphens and joiners to nothing) apply everywhere.
/// </para>
/// <para>The empty password is the default user password, which readers always try first.</para>
/// </remarks>
public sealed class PdfPassword : PdfCredentials
{
    private readonly byte[]? _bytes;

    /// <summary>Initializes a new instance of the <see cref="PdfPassword"/> class from text.</summary>
    /// <param name="password">The password.</param>
    public PdfPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        Text = password;
    }

    /// <summary>Initializes a new instance of the <see cref="PdfPassword"/> class from bytes, used without any encoding.</summary>
    /// <param name="password">The password's bytes, copied.</param>
    public PdfPassword(ReadOnlySpan<byte> password) => _bytes = password.ToArray();

    /// <summary>Gets the password as text, or <see langword="null"/> when it was given as bytes.</summary>
    public string? Text { get; }

    /// <summary>Gets a value indicating whether this is the empty password.</summary>
    public bool IsEmpty => Text is { Length: 0 } || _bytes is { Length: 0 };

    /// <summary>Gets the bytes the password was given as, or <see langword="null"/> when it was given as text.</summary>
    internal ReadOnlySpan<byte> RawBytes => _bytes;

    /// <summary>Gets a value indicating whether the password was given as bytes.</summary>
    internal bool IsRaw => _bytes is not null;

    /// <inheritdoc/>
    public override string ToString() => "PdfPassword";

    /// <summary>Encodes text as UTF-8 (used when no handler-specific encoding applies).</summary>
    internal byte[] Utf8() => _bytes ?? Encoding.UTF8.GetBytes(Text!);
}
