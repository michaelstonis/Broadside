namespace Broadside;

/// <summary>
/// Options for a new embedded file: its media type, description, dates and associated-file relationship. Size and checksum are
/// always computed from the contents.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.11.3, Table 43 (<c>Desc</c>, <c>AFRelationship</c>), and §7.11.4.1, Tables 44 and 45 (<c>Subtype</c>,
/// <c>Params</c>). An embedded file used as an associated file shall have a <c>Subtype</c> and should have <c>Params</c> with a
/// <c>ModDate</c> (§14.13.2): the subtype is always written, the modification date only when <see cref="ModificationDate"/> is set.
/// </remarks>
public sealed class PdfEmbeddedFileOptions
{
    private string? _mediaType;
    private PdfFileRelationship? _relationship;

    /// <summary>
    /// Gets or sets the media type written as the stream's <c>Subtype</c>, such as <c>text/csv</c>; <see langword="null"/>, the
    /// default, writes <c>application/octet-stream</c>.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 44 (an RFC 2046 MIME type), and §14.13.2 (unknown types are <c>application/octet-stream</c>).</remarks>
    /// <exception cref="ArgumentException">The value is not of the form <c>type/subtype</c>, or contains U+0000.</exception>
    public string? MediaType
    {
        get => _mediaType;
        set
        {
            if (value is not null)
            {
                int slash = value.IndexOf('/', StringComparison.Ordinal);
                if (slash <= 0 || slash == value.Length - 1 || value.Contains('\0', StringComparison.Ordinal) || value.Any(char.IsWhiteSpace))
                {
                    throw new ArgumentException("A media type is written type/subtype, without spaces (RFC 2046).", nameof(value));
                }
            }

            _mediaType = value;
        }
    }

    /// <summary>Gets or sets the description written as the file specification's <c>Desc</c>; <see langword="null"/> writes none.</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43 (PDF 1.6), and §7.11.4.1: shown by interactive readers for files in <c>EmbeddedFiles</c>.</remarks>
    public string? Description { get; set; }

    /// <summary>Gets or sets when the file was created (<c>Params</c> <c>CreationDate</c>); <see langword="null"/> writes none.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45, and §7.9.4. Written to the second, with its offset from UT.</remarks>
    public DateTimeOffset? CreationDate { get; set; }

    /// <summary>Gets or sets when the file was last modified (<c>Params</c> <c>ModDate</c>); <see langword="null"/> writes none.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.11.4.1, Table 45, and §7.9.4. §14.13.2: an associated file's parameters should carry it, so set it for a file
    /// that will be attached with <see cref="PdfDocument.AddAssociatedFile"/>.
    /// </remarks>
    public DateTimeOffset? ModificationDate { get; set; }

    /// <summary>
    /// Gets or sets the relationship written as <c>AFRelationship</c>, for a file that will be an associated file;
    /// <see langword="null"/>, the default, writes none, which reads as <see cref="PdfFileRelationship.Unspecified"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43 (PDF 2.0), and §14.13.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is <see cref="PdfFileRelationship.Other"/> (which stands for a name this type cannot spell) or not a defined value.
    /// </exception>
    public PdfFileRelationship? Relationship
    {
        get => _relationship;
        set
        {
            if (value is { } relationship && (relationship == PdfFileRelationship.Other || !Enum.IsDefined(relationship)))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a relationship Table 43 names.");
            }

            _relationship = value;
        }
    }

    /// <summary>Sets <see cref="MediaType"/>.</summary>
    /// <param name="mediaType">The media type, or <see langword="null"/> for <c>application/octet-stream</c>.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 44.</remarks>
    public PdfEmbeddedFileOptions WithMediaType(string? mediaType)
    {
        MediaType = mediaType;
        return this;
    }

    /// <summary>Sets <see cref="Description"/>.</summary>
    /// <param name="description">The description, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43.</remarks>
    public PdfEmbeddedFileOptions WithDescription(string? description)
    {
        Description = description;
        return this;
    }

    /// <summary>Sets <see cref="CreationDate"/>.</summary>
    /// <param name="date">The date, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45.</remarks>
    public PdfEmbeddedFileOptions WithCreationDate(DateTimeOffset? date)
    {
        CreationDate = date;
        return this;
    }

    /// <summary>Sets <see cref="ModificationDate"/>.</summary>
    /// <param name="date">The date, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45, and §14.13.2.</remarks>
    public PdfEmbeddedFileOptions WithModificationDate(DateTimeOffset? date)
    {
        ModificationDate = date;
        return this;
    }

    /// <summary>Sets <see cref="Relationship"/>.</summary>
    /// <param name="relationship">The relationship, or <see langword="null"/> to write none.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43 (PDF 2.0), and §14.13.</remarks>
    public PdfEmbeddedFileOptions WithRelationship(PdfFileRelationship? relationship)
    {
        Relationship = relationship;
        return this;
    }
}
