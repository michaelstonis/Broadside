namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about stream filters.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.4. Each code is reported under <see cref="DiagnosticArea.Filter"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FilterDiagnostics
{
    /// <summary>Gets the <c>FilterInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterInvalid { get; } = new(nameof(FilterInvalid), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "The Filter entry is not a name or an array of names, or an array element is not a name. The stream is left encoded or the filter chain stops at the bad element.");

    /// <summary>Gets the <c>FilterUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterUnsupported { get; } = new(nameof(FilterUnsupported), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "The stream names a filter that Broadside does not support. The stream is left encoded from that filter onward.");

    /// <summary>
    /// Gets the <c>FilterCodecNotRegistered</c> code: a stream names a standard image codec (<c>DCTDecode</c>, <c>CCITTFaxDecode</c>,
    /// <c>JBIG2Decode</c> or <c>JPXDecode</c>) that the engine's options did not register (<see cref="PdfOptions.CreateWithoutImageCodecs"/>),
    /// so the stream is left encoded from that filter on.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.1 Table 6, §7.4.6 to §7.4.9. Information: the file is valid; the app chose not to carry the codec.</remarks>
    public static DiagnosticCode FilterCodecNotRegistered { get; } = new(nameof(FilterCodecNotRegistered), DiagnosticArea.Filter, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.1 Table 6, §7.4.6-§7.4.9",
        "The stream uses an image codec that is not registered in the engine options. The stream is left encoded from that filter onward.");

    /// <summary>Gets the <c>FilterAbbreviationNotAllowed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterAbbreviationNotAllowed { get; } = new(nameof(FilterAbbreviationNotAllowed), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "A filter name uses an inline-image abbreviation outside an inline image. The abbreviation is read as the full filter name.");

    /// <summary>Gets the <c>FilterDataInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterDataInvalid { get; } = new(nameof(FilterDataInvalid), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "Encoded stream data is damaged, such as a missing zlib header or corrupt deflate or ASCII85 data. The bytes decoded before the error are kept, or the data is read as raw deflate.");

    /// <summary>Gets the <c>FilterDataTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterDataTruncated { get; } = new(nameof(FilterDataTruncated), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "Encoded stream data ends early, before its end-of-data marker, final block or complete run. The bytes decoded so far are kept.");

    /// <summary>Gets the <c>FilterFailed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode FilterFailed { get; } = new(nameof(FilterFailed), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "A filter threw an error while decoding, or an image decoded to a different size than its header promised. The bytes decoded before the failure are kept, or the image is not decoded.");

    /// <summary>Gets the <c>DecodeParmsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode DecodeParmsInvalid { get; } = new(nameof(DecodeParmsInvalid), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "The DecodeParms entry has the wrong type or the wrong number of entries for the Filter entry. Invalid parameters are ignored, missing entries take defaults, or the first array element is used.");

    /// <summary>Gets the <c>PredictorInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode PredictorInvalid { get; } = new(nameof(PredictorInvalid), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "A predictor parameter or predicted data is invalid, such as an unknown Predictor value, oversized rows, too little data or an unknown PNG row tag. The data is passed through or read unpredicted.");

    /// <summary>Gets the <c>CryptFilterNotFirst</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode CryptFilterNotFirst { get; } = new(nameof(CryptFilterNotFirst), DiagnosticArea.Filter, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "A Crypt filter appears in the Filter array somewhere other than first. The filter is applied where it is.");

    /// <summary>Gets the <c>CryptFilterUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode CryptFilterUnsupported { get; } = new(nameof(CryptFilterUnsupported), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "The stream names a crypt filter that the document's security handler does not know. The stream is left encrypted.");

    /// <summary>Gets the <c>StreamExternalFileUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode StreamExternalFileUnsupported { get; } = new(nameof(StreamExternalFileUnsupported), DiagnosticArea.Filter, DiagnosticSeverity.Information, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "The stream's data is held in an external file, which Broadside does not read. The bytes in the stream itself are decoded instead.");

    /// <summary>Gets the <c>StreamDecodedLengthExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode StreamDecodedLengthExceeded { get; } = new(nameof(StreamDecodedLengthExceeded), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "A stream decodes to more bytes than the configured decoded-length limit. The output is truncated at the limit.");

    /// <summary>Gets the <c>StreamDecodingTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2 Table 5, §7.4</remarks>
    public static DiagnosticCode StreamDecodingTooDeep { get; } = new(nameof(StreamDecodingTooDeep), DiagnosticArea.Filter, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.8.2 Table 5, §7.4",
        "Decoding a stream requires more nested stream decodes than the depth limit allows. The stream decodes to nothing.");

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
