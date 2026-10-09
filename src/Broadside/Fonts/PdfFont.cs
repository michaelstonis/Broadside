using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>A font: a live view over a font dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.5. The type of the view follows the dictionary's <c>Subtype</c> (Table 108): <see cref="PdfType1Font"/> for
/// <c>Type1</c> and <c>MMType1</c>, <see cref="PdfTrueTypeFont"/>, <see cref="PdfType3Font"/> and <see cref="PdfType0Font"/>. A
/// dictionary whose <c>Subtype</c> is missing or not a font type is read as a Type 1 font, with a diagnostic.
/// </para>
/// <para>
/// Views are obtained from <see cref="PdfDocument.GetFont(CosObject)"/> or <see cref="PdfPage.GetFont(string)"/>, which return the
/// same instance for the same dictionary. Properties read the COS objects when called; values derived from several entries (the
/// encoding and widths of a simple font) are cached and rebuilt when one of the dictionaries or arrays they come from changes. A
/// font is safe for concurrent reads while nobody mutates the document.
/// </para>
/// </remarks>
public abstract class PdfFont
{
    private PdfFontDescriptor? _descriptor;

    private protected PdfFont(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfFontType fontType)
    {
        Document = document;
        Dictionary = dictionary;
        Reference = reference;
        FontType = fontType;
    }

    /// <summary>Gets the font dictionary.</summary>
    /// <remarks>ISO 32000-2 §9.5; Table 109 for Type 1 fonts.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the font dictionary, or <see langword="null"/> when the resource dictionary holds it directly.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the type of the font, from <c>Subtype</c>; <see cref="PdfFontType.Type1"/> when that is missing or not a font type.</summary>
    /// <remarks>ISO 32000-2 §9.5, Table 108.</remarks>
    public PdfFontType FontType { get; }

    /// <summary>Gets the PostScript name of the font, or <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.1, Table 109, <c>BaseFont</c> (required except for Type 3 fonts); §9.6.3 for TrueType fonts.</remarks>
    public string? BaseFont => (Get(FontNames.BaseFont) as CosName)?.Value;

    /// <summary>
    /// Gets the font descriptor: a view over the dictionary's <c>FontDescriptor</c>, or, for a non-embedded Standard 14 font without
    /// one, a descriptor synthesized from the font's metrics; <see langword="null"/> otherwise.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.8 and §9.6.2.1 (the paragraph after Table 109).</remarks>
    public PdfFontDescriptor? Descriptor
    {
        get
        {
            if (DescriptorDictionary is not { } dictionary)
            {
                return SynthesizedDescriptor;
            }

            PdfFontDescriptor? cached = _descriptor;
            if (cached?.Dictionary != dictionary)
            {
                cached = new PdfFontDescriptor(Document, dictionary, Dictionary.TryGetValue(FontNames.FontDescriptor, out CosObject? value) ? value as CosReference : null);
                _descriptor = cached;
            }

            return cached;
        }
    }

    /// <summary>Gets a value indicating whether the font program is embedded: the font descriptor has a <c>FontFile</c>, <c>FontFile2</c> or <c>FontFile3</c> stream.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, and §9.9.</remarks>
    public bool IsEmbedded => DescriptorDictionary is { } descriptor && IsEmbeddedIn(descriptor);

    /// <summary>Gets the document the font belongs to.</summary>
    internal PdfDocument Document { get; }

    /// <summary>Gets the font descriptor dictionary, resolved, or <see langword="null"/> when absent or not a dictionary.</summary>
    internal CosDictionary? DescriptorDictionary => Get(FontNames.FontDescriptor) as CosDictionary;

    /// <summary>Gets the descriptor a font without a <c>FontDescriptor</c> entry is given; <see langword="null"/> unless a Standard 14 font.</summary>
    private protected virtual PdfFontDescriptor? SynthesizedDescriptor => null;

    /// <summary>Creates the view for a font dictionary, recording a diagnostic when its <c>Type</c> or <c>Subtype</c> is not a font's.</summary>
    /// <param name="document">The document.</param>
    /// <param name="dictionary">The font dictionary.</param>
    /// <param name="reference">The reference it was reached through, if any.</param>
    /// <returns>The view.</returns>
    internal static PdfFont Create(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        DiagnosticSink diagnostics = document.DiagnosticSink;
        CosObject? type = dictionary.TryGetValue(KnownNames.Type, out CosObject? typeValue) ? document.Resolve(typeValue) : null;
        if (!FontNames.Font.Equals(type))
        {
            diagnostics.Report(
                DiagnosticCodes.FontTypeInvalid,
                DiagnosticSeverity.Warning,
                "A font dictionary's Type shall be Font (ISO 32000-2 §9.6.2.1, Table 109); read as a font.",
                objectReference: reference);
        }

        CosName? subtype = (dictionary.TryGetValue(FontNames.Subtype, out CosObject? subtypeValue) ? document.Resolve(subtypeValue) : null) as CosName;
        if (FontNames.Type1.Equals(subtype))
        {
            return new PdfType1Font(document, dictionary, reference, PdfFontType.Type1);
        }

        if (FontNames.MMType1.Equals(subtype))
        {
            return new PdfType1Font(document, dictionary, reference, PdfFontType.MMType1);
        }

        if (FontNames.TrueType.Equals(subtype))
        {
            return new PdfTrueTypeFont(document, dictionary, reference);
        }

        if (FontNames.Type3.Equals(subtype))
        {
            return new PdfType3Font(document, dictionary, reference);
        }

        if (FontNames.Type0.Equals(subtype))
        {
            return new PdfType0Font(document, dictionary, reference);
        }

        diagnostics.Report(
            DiagnosticCodes.FontSubtypeInvalid,
            DiagnosticSeverity.Warning,
            $"A font dictionary's Subtype shall be one of the font types of ISO 32000-2 §9.5 Table 108, not {(subtype is null ? "absent" : "/" + subtype.Value)}; read as a Type 1 font.",
            objectReference: reference);
        return new PdfType1Font(document, dictionary, reference, PdfFontType.Type1);
    }

    /// <summary>Gets a value indicating whether a font descriptor holds a font program.</summary>
    internal bool IsEmbeddedIn(CosDictionary descriptor) =>
        GetFrom(descriptor, FontNames.FontFile) is CosStream
        || GetFrom(descriptor, FontNames.FontFile2) is CosStream
        || GetFrom(descriptor, FontNames.FontFile3) is CosStream;

    /// <summary>The font dictionary's entry, resolved; <see langword="null"/> when absent or a reference to nothing.</summary>
    internal CosObject? Get(CosName key) => GetFrom(Dictionary, key);

    /// <summary>A dictionary's entry, resolved; <see langword="null"/> when absent or a reference to nothing.</summary>
    internal CosObject? GetFrom(CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) && Document.Resolve(value) is not CosNull and var resolved ? resolved : null;

    /// <summary>Records a deviation found in this font.</summary>
    internal void Report(string code, DiagnosticSeverity severity, string message) =>
        Document.DiagnosticSink.Report(code, severity, message, objectReference: Reference);
}
