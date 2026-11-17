namespace Broadside.Filters;

/// <summary>
/// The managed image codecs, and the registry of the built-in filters with all of them: the filter set of <see cref="PdfOptions()"/>,
/// of a stand-alone <see cref="FilterContext"/> and of every engine before issue #544.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.4.1, Table 6; §7.4.6 to §7.4.9. Kept apart from <see cref="FilterRegistry"/> so that only code that asks for the codecs (the
/// default options, <see cref="PdfOptions.UseImageCodecs"/>, a stand-alone context) reaches their types: an app built on
/// <see cref="PdfOptions.CreateWithoutImageCodecs"/> does not carry them after trimming.
/// </remarks>
internal static class ImageCodecRegistry
{
    /// <summary>Gets the four managed image codecs, one stateless instance each.</summary>
    public static IReadOnlyList<IStreamFilter> Codecs { get; } =
    [
        new DctDecodeFilter(),
        new CcittFaxDecodeFilter(),
        new JpxDecodeFilter(),
        new Jbig2DecodeFilter(),
    ];

    /// <summary>
    /// Gets the registry of the built-in filters and the four codecs, built once and shared by every engine whose options register
    /// exactly the codecs (see <see cref="FilterRegistry.WithoutImageCodecs"/> for why it is shared).
    /// </summary>
    public static FilterRegistry All { get; } = FilterRegistry.Create(Codecs);
}
