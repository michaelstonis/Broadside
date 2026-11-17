namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about stream filters.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.4. Each code is reported under <see cref="DiagnosticArea.Filter"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FilterDiagnostics
{
    /// <summary>Gets the <c>FilterInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterInvalid { get; } = new(nameof(FilterInvalid), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets the <c>FilterUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterUnsupported { get; } = new(nameof(FilterUnsupported), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>
    /// Gets the <c>FilterCodecNotRegistered</c> code: a stream names a standard image codec (<c>DCTDecode</c>, <c>CCITTFaxDecode</c>,
    /// <c>JBIG2Decode</c> or <c>JPXDecode</c>) that the engine's options did not register (<see cref="PdfOptions.CreateWithoutImageCodecs"/>),
    /// so the stream is left encoded from that filter on.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.1 Table 6, §7.4.6 to §7.4.9. Information: the file is valid; the app chose not to carry the codec.</remarks>
    public static DiagnosticCode FilterCodecNotRegistered { get; } = new(nameof(FilterCodecNotRegistered), DiagnosticArea.Filter, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.1 Table 6, §7.4.6-§7.4.9");

    /// <summary>Gets the <c>FilterAbbreviationNotAllowed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterAbbreviationNotAllowed { get; } = new(nameof(FilterAbbreviationNotAllowed), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets the <c>FilterDataInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterDataInvalid { get; } = new(nameof(FilterDataInvalid), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets the <c>FilterDataTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterDataTruncated { get; } = new(nameof(FilterDataTruncated), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets the <c>FilterFailed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterFailed { get; } = new(nameof(FilterFailed), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets the <c>DecodeParmsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode DecodeParmsInvalid { get; } = new(nameof(DecodeParmsInvalid), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets the <c>PredictorInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode PredictorInvalid { get; } = new(nameof(PredictorInvalid), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets the <c>CryptFilterNotFirst</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode CryptFilterNotFirst { get; } = new(nameof(CryptFilterNotFirst), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets the <c>CryptFilterUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode CryptFilterUnsupported { get; } = new(nameof(CryptFilterUnsupported), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets the <c>StreamExternalFileUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode StreamExternalFileUnsupported { get; } = new(nameof(StreamExternalFileUnsupported), DiagnosticArea.Filter, DiagnosticSeverity.Information, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets the <c>StreamDecodedLengthExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode StreamDecodedLengthExceeded { get; } = new(nameof(StreamDecodedLengthExceeded), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets the <c>StreamDecodingTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode StreamDecodingTooDeep { get; } = new(nameof(StreamDecodingTooDeep), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        FilterInvalid,
        FilterUnsupported,
        FilterCodecNotRegistered,
        FilterAbbreviationNotAllowed,
        FilterDataInvalid,
        FilterDataTruncated,
        FilterFailed,
        DecodeParmsInvalid,
        PredictorInvalid,
        CryptFilterNotFirst,
        CryptFilterUnsupported,
        StreamExternalFileUnsupported,
        StreamDecodedLengthExceeded,
        StreamDecodingTooDeep,
    ];
}
