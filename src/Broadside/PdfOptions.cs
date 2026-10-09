namespace Broadside;

/// <summary>
/// The configuration of a <see cref="PdfEngine"/>, set fluently: <c>Use*</c> methods select an implementation or behavior,
/// <c>With*</c> methods set a value.
/// </summary>
/// <remarks>
/// An engine copies its options when it is constructed, so changing an options object afterwards does not change any engine built
/// from it. The properties are settable so the same type binds through the options pattern of dependency injection.
/// </remarks>
public sealed class PdfOptions
{
    /// <summary>Gets or sets how deviations from the specification are treated. The default is <see cref="PdfReadingMode.Lenient"/>.</summary>
    /// <remarks>ADR 0005.</remarks>
    public PdfReadingMode ReadingMode { get; set; } = PdfReadingMode.Lenient;

    /// <summary>Repairs deviations and records them as diagnostics on the document. The default.</summary>
    /// <returns>These options.</returns>
    public PdfOptions UseLenient()
    {
        ReadingMode = PdfReadingMode.Lenient;
        return this;
    }

    /// <summary>Throws a <see cref="Diagnostics.DiagnosticException"/> for the first deviation.</summary>
    /// <returns>These options.</returns>
    public PdfOptions UseStrict()
    {
        ReadingMode = PdfReadingMode.Strict;
        return this;
    }
}
