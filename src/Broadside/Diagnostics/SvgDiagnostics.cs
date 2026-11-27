namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics <see cref="PdfDocument.CreateFormXObjectFromSvg(ReadOnlySpan{byte}, Graphics.SvgImportOptions?)"/> records while it imports SVG.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.10 (form XObjects) and SVG 1.1 (W3C Recommendation, second edition). Each code is reported under
/// <see cref="DiagnosticArea.Svg"/>; see <see cref="DiagnosticCatalog"/> for every area. The importer records them on the document it
/// imports into (<see cref="PdfDocument.ReportDiagnostic"/>), so they appear in <see cref="PdfDocument.Diagnostics"/>; a document opened
/// or created in strict mode throws the first one that is not informational. Malformed XML is not a diagnostic: it throws.
/// </para>
/// </remarks>
public static class SvgDiagnostics
{
    /// <summary>Gets the <c>SvgSizeMissing</c> code: the root <c>svg</c> element has neither a width and height nor a view box, so it is imported at 300 by 150 pixels.</summary>
    /// <remarks>SVG 1.1 §7.2</remarks>
    public static DiagnosticCode SvgSizeMissing { get; } = new(nameof(SvgSizeMissing), DiagnosticArea.Svg, DiagnosticSeverity.Warning, "SVG 1.1 §7.2",
        "The SVG root element has no width, height or viewBox. It is imported at 300 by 150 pixels, the size HTML gives a replaced element.");

    /// <summary>Gets the <c>SvgFeatureUnsupported</c> code: an element or property outside the supported subset (filters, foreignObject, animation, scripts, markers) is skipped.</summary>
    /// <remarks>SVG 1.1 §15</remarks>
    public static DiagnosticCode SvgFeatureUnsupported { get; } = new(nameof(SvgFeatureUnsupported), DiagnosticArea.Svg, DiagnosticSeverity.Warning, "SVG 1.1 §15",
        "An SVG element or property the importer does not support, such as a filter, foreignObject, animation, script or marker, is skipped. The rest of the image is drawn without it.");

    /// <summary>Gets the <c>SvgValueInvalid</c> code: an attribute or property value cannot be read; the element is skipped, drawn up to the error (path data), or drawn with the inherited value.</summary>
    /// <remarks>SVG 1.1 Appendix F.2</remarks>
    public static DiagnosticCode SvgValueInvalid { get; } = new(nameof(SvgValueInvalid), DiagnosticArea.Svg, DiagnosticSeverity.Warning, "SVG 1.1 Appendix F.2",
        "An SVG attribute or property value cannot be read. Path data is drawn up to the error; an unreadable geometry skips the element; an unreadable style value keeps the inherited one.");

    /// <summary>Gets the <c>SvgReferenceMissing</c> code: a reference (<c>href</c>, <c>url(#id)</c>) names no element of the right kind, or the resource resolver supplied nothing; the reference is ignored.</summary>
    /// <remarks>SVG 1.1 §5.3.1</remarks>
    public static DiagnosticCode SvgReferenceMissing { get; } = new(nameof(SvgReferenceMissing), DiagnosticArea.Svg, DiagnosticSeverity.Warning, "SVG 1.1 §5.3.1",
        "An SVG reference names no element of the right kind, or the resource resolver supplies nothing for it. The reference is ignored: a paint falls back, a clip, mask or use is left out.");

    /// <summary>Gets the <c>SvgReferenceCycle</c> code: a <c>use</c>, pattern, mask or nested image refers back to itself, or nests deeper than 32 levels; the innermost reference is not drawn.</summary>
    /// <remarks>SVG 1.1 §5.6</remarks>
    public static DiagnosticCode SvgReferenceCycle { get; } = new(nameof(SvgReferenceCycle), DiagnosticArea.Svg, DiagnosticSeverity.Warning, "SVG 1.1 §5.6",
        "An SVG reference refers back to an element that contains it, or references nest deeper than 32 levels. The reference that closes the cycle is not drawn.");

    /// <summary>Gets the <c>SvgStyleInvalid</c> code: a style sheet rule, at-rule or declaration cannot be read and is skipped.</summary>
    /// <remarks>SVG 1.1 §6.3</remarks>
    public static DiagnosticCode SvgStyleInvalid { get; } = new(nameof(SvgStyleInvalid), DiagnosticArea.Svg, DiagnosticSeverity.Warning, "SVG 1.1 §6.3",
        "A CSS rule, at-rule or declaration in the SVG cannot be read or is outside the supported subset. It is skipped and the rest of the style sheet applies.");

    /// <summary>Gets the <c>SvgImageInvalid</c> code: an <c>image</c> element's data is not PNG, JPEG or SVG, or cannot be decoded; the image is not drawn.</summary>
    /// <remarks>SVG 1.1 §5.7</remarks>
    public static DiagnosticCode SvgImageInvalid { get; } = new(nameof(SvgImageInvalid), DiagnosticArea.Svg, DiagnosticSeverity.Warning, "SVG 1.1 §5.7",
        "The data of an SVG image element is not PNG, JPEG or SVG, or cannot be decoded. The image is not drawn.");

    /// <summary>Gets the <c>SvgFontNotFound</c> code: none of the font families a text element names is found by the document's font resolvers, so the default font is used.</summary>
    /// <remarks>ISO 32000-2 §9.6.2</remarks>
    public static DiagnosticCode SvgFontNotFound { get; } = new(nameof(SvgFontNotFound), DiagnosticArea.Svg, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.2",
        "None of the font families an SVG text element names is found by the font resolvers. The default font of the import options, or Helvetica, is used instead.");

    /// <summary>Gets the <c>SvgTextNotEncodable</c> code: a character of SVG text has no code in the font it is shown with, so a question mark is shown instead.</summary>
    /// <remarks>ISO 32000-2 §9.4.3</remarks>
    public static DiagnosticCode SvgTextNotEncodable { get; } = new(nameof(SvgTextNotEncodable), DiagnosticArea.Svg, DiagnosticSeverity.Warning, "ISO 32000-2 §9.4.3",
        "A character of SVG text has no code in the font it is shown with. A question mark is shown instead; an embedded font from the resolvers covers more characters.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        SvgSizeMissing,
        SvgFeatureUnsupported,
        SvgValueInvalid,
        SvgReferenceMissing,
        SvgReferenceCycle,
        SvgStyleInvalid,
        SvgImageInvalid,
        SvgFontNotFound,
        SvgTextNotEncodable,
    ];
}
