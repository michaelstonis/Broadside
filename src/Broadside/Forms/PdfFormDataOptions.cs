namespace Broadside.Forms;

/// <summary>
/// Options for exporting form data and annotations with <see cref="PdfDocument.SaveFormData"/> and importing them with
/// <see cref="PdfDocument.ImportFormData"/>: the format, whether field values and markup annotations are included, which fields,
/// and the source file the data names.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.7.8 (FDF) and Adobe XFDF 3.0 (ISO 19444-1). The members chain: each <c>With*</c> method sets its property and
/// returns the same instance.
/// </remarks>
public sealed class PdfFormDataOptions
{
    /// <summary>Gets or sets the file format; XFDF by default.</summary>
    /// <remarks>ISO 32000-2 §12.7.8; Adobe XFDF 3.0.</remarks>
    public PdfFormDataFormat Format { get; set; } = PdfFormDataFormat.Xfdf;

    /// <summary>Gets or sets whether field values are exported or imported; <see langword="true"/> by default.</summary>
    /// <remarks>ISO 32000-2 §12.7.8.3.2.</remarks>
    public bool IncludeFields { get; set; } = true;

    /// <summary>Gets or sets whether markup annotations are exported or imported; <see langword="false"/> by default.</summary>
    /// <remarks>ISO 32000-2 §12.7.8.3.4 and §12.5.6.2.</remarks>
    public bool IncludeAnnotations { get; set; }

    /// <summary>
    /// Gets or sets the fully qualified names of the fields to export or import; <see langword="null"/>, the default, for every
    /// field. Entries of a file for other fields are ignored on import.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.4.2 (fully qualified field names).</remarks>
    public IReadOnlyList<string>? FieldNames { get; set; }

    /// <summary>
    /// Gets or sets the source file an export names: the FDF dictionary's <c>F</c> entry, or XFDF's <c>&lt;f href&gt;</c>;
    /// <see langword="null"/>, the default, names none.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.8.3.1, Table 246.</remarks>
    public string? SourceFile { get; set; }

    /// <summary>Sets <see cref="Format"/>.</summary>
    /// <param name="format">The file format.</param>
    /// <returns>This instance.</returns>
    public PdfFormDataOptions WithFormat(PdfFormDataFormat format)
    {
        Format = format;
        return this;
    }

    /// <summary>Sets <see cref="IncludeFields"/>.</summary>
    /// <param name="include">Whether field values are included.</param>
    /// <returns>This instance.</returns>
    public PdfFormDataOptions WithFields(bool include = true)
    {
        IncludeFields = include;
        return this;
    }

    /// <summary>Sets <see cref="IncludeAnnotations"/>.</summary>
    /// <param name="include">Whether markup annotations are included.</param>
    /// <returns>This instance.</returns>
    public PdfFormDataOptions WithAnnotations(bool include = true)
    {
        IncludeAnnotations = include;
        return this;
    }

    /// <summary>Sets <see cref="FieldNames"/>: only the named fields are exported or imported.</summary>
    /// <param name="names">Fully qualified field names.</param>
    /// <returns>This instance.</returns>
    public PdfFormDataOptions WithFieldNames(params IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        FieldNames = [.. names];
        return this;
    }

    /// <summary>Sets <see cref="SourceFile"/>.</summary>
    /// <param name="file">The source file's name or path, or <see langword="null"/> for none.</param>
    /// <returns>This instance.</returns>
    public PdfFormDataOptions WithSourceFile(string? file)
    {
        SourceFile = file;
        return this;
    }
}
