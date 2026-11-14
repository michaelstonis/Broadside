namespace Broadside;

/// <summary>
/// Options for a new unencrypted wrapper document (<see cref="PdfDocument.CreateUnencryptedWrapper(ReadOnlySpan{byte}, PdfUnencryptedWrapperOptions)"/>):
/// its cover pages, the cryptographic filter the encrypted payload dictionary names, and how the payload is named and described.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.6.7 (PDF 2.0) and Table 28; §7.11.3, Table 43 (<c>F</c>, <c>UF</c>, <c>Desc</c>). Every option has a default: a blank
/// cover page, the filter the payload's own encryption dictionary names, no version, and a name and description built from the filter.
/// </remarks>
public sealed class PdfUnencryptedWrapperOptions
{
    private string? _cryptographicFilter;
    private string? _cryptographicFilterVersion;
    private string? _name;

    /// <summary>
    /// Gets or sets the document whose pages become the wrapper's pages, which should tell a reader without the cryptographic filter
    /// what it needs; <see langword="null"/>, the default, gives the wrapper one blank page.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.6.7: a reader without the payload's security handler presents the wrapper, which should guide the user to it.
    /// The pages are copied as <see cref="PdfDocument.AppendPages(PdfDocument)"/> copies them, with their outline, named destinations,
    /// form fields and page labels; the cover itself is only read, and its embedded files are not copied (the wrapper's
    /// <c>EmbeddedFiles</c> holds exactly the payload).
    /// </remarks>
    public PdfDocument? Cover { get; set; }

    /// <summary>
    /// Gets or sets the name of the cryptographic filter written as the encrypted payload dictionary's <c>Subtype</c>;
    /// <see langword="null"/>, the default, writes the security handler the payload's encryption dictionary names (its <c>Filter</c>).
    /// </summary>
    /// <remarks>ISO 32000-2 §7.6.7, Table 28 (required): it lets a reader tell whether it has the filter needed to decrypt the payload.</remarks>
    /// <exception cref="ArgumentException">The value is empty.</exception>
    public string? CryptographicFilter
    {
        get => _cryptographicFilter;
        set
        {
            if (value is not null)
            {
                ArgumentException.ThrowIfNullOrEmpty(value, nameof(value));
            }

            _cryptographicFilter = value;
        }
    }

    /// <summary>
    /// Gets or sets the version of the cryptographic filter written as the encrypted payload dictionary's <c>Version</c>, such as
    /// <c>1.0</c>; <see langword="null"/>, the default, writes none.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.6.7, Table 28 (optional): a name holding integers with a period between them, not a real number.</remarks>
    /// <exception cref="ArgumentException">The value is not integers separated by periods.</exception>
    public string? CryptographicFilterVersion
    {
        get => _cryptographicFilterVersion;
        set
        {
            if (value is not null && !IsVersion(value))
            {
                throw new ArgumentException("A cryptographic filter version is integers with a period between them, such as 1.0 (ISO 32000-2 §7.6.7, Table 28).", nameof(value));
            }

            _cryptographicFilterVersion = value;
        }
    }

    /// <summary>
    /// Gets or sets the payload's name: its key in the <c>EmbeddedFiles</c> name tree, the collection's <c>D</c>, and the file
    /// specification's <c>F</c> and <c>UF</c>; <see langword="null"/>, the default, writes <c>&lt;filter&gt; Protected PDF.pdf</c>.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.11.3, Table 43 (PDF 2.0): for an encrypted payload, <c>F</c> and <c>UF</c> shall not contain or be derived from
    /// the payload's actual file name, and should include the name of the cryptographic filter.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is empty.</exception>
    public string? Name
    {
        get => _name;
        set
        {
            if (value is not null)
            {
                ArgumentException.ThrowIfNullOrEmpty(value, nameof(value));
            }

            _name = value;
        }
    }

    /// <summary>
    /// Gets or sets the description written as the payload's file specification <c>Desc</c>; <see langword="null"/>, the default,
    /// writes one naming the cryptographic filter.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43, and the example in §7.6.7.</remarks>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the payload's modification date (<c>Params</c> <c>ModDate</c>); <see langword="null"/>, the default, writes the time
    /// the wrapper is created.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.13.2: an embedded associated file should carry its modification date.</remarks>
    public DateTimeOffset? ModificationDate { get; set; }

    /// <summary>Sets <see cref="Cover"/>.</summary>
    /// <param name="cover">The cover document, or <see langword="null"/> for one blank page.</param>
    /// <returns>These options.</returns>
    public PdfUnencryptedWrapperOptions WithCover(PdfDocument? cover)
    {
        Cover = cover;
        return this;
    }

    /// <summary>Sets <see cref="CryptographicFilter"/>.</summary>
    /// <param name="filter">The filter name, or <see langword="null"/> for the one the payload's encryption dictionary names.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentException"><paramref name="filter"/> is empty.</exception>
    public PdfUnencryptedWrapperOptions WithCryptographicFilter(string? filter)
    {
        CryptographicFilter = filter;
        return this;
    }

    /// <summary>Sets <see cref="CryptographicFilterVersion"/>.</summary>
    /// <param name="version">The version, such as <c>1.0</c>, or <see langword="null"/> for none.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentException"><paramref name="version"/> is not integers separated by periods.</exception>
    public PdfUnencryptedWrapperOptions WithCryptographicFilterVersion(string? version)
    {
        CryptographicFilterVersion = version;
        return this;
    }

    /// <summary>Sets <see cref="Name"/>.</summary>
    /// <param name="name">The payload's name, or <see langword="null"/> for one built from the filter.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public PdfUnencryptedWrapperOptions WithName(string? name)
    {
        Name = name;
        return this;
    }

    /// <summary>Sets <see cref="Description"/>.</summary>
    /// <param name="description">The description, or <see langword="null"/> for one naming the filter.</param>
    /// <returns>These options.</returns>
    public PdfUnencryptedWrapperOptions WithDescription(string? description)
    {
        Description = description;
        return this;
    }

    /// <summary>Sets <see cref="ModificationDate"/>.</summary>
    /// <param name="date">The date, or <see langword="null"/> for the time the wrapper is created.</param>
    /// <returns>These options.</returns>
    public PdfUnencryptedWrapperOptions WithModificationDate(DateTimeOffset? date)
    {
        ModificationDate = date;
        return this;
    }

    private static bool IsVersion(string value)
    {
        bool digit = false;
        foreach (char character in value)
        {
            if (char.IsAsciiDigit(character))
            {
                digit = true;
            }
            else if (character == '.' && digit)
            {
                digit = false;
            }
            else
            {
                return false;
            }
        }

        return digit;
    }
}
