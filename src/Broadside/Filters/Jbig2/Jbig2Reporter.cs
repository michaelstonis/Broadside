using Broadside.Diagnostics;
using Broadside.Filters.Codecs;
using Broadside.Parsing;

namespace Broadside.Filters.Jbig2;

/// <summary>Reports the deviations of one JBIG2Decode call through its filter context, each code (and key) once per call.</summary>
/// <remarks>
/// ADR 0005: lenient mode records and continues; strict mode throws from <see cref="CodecReporter.Report"/>. Without a context it
/// only tracks, and keeps what it reports in <see cref="CodecReporter.Recorded"/> (the global segments, decoded once per
/// <c>JBIG2Globals</c> stream and replayed into each image's context).
/// </remarks>
internal sealed class Jbig2Reporter(FilterContext? context) : CodecReporter(context)
{
    /// <summary>Gets a value indicating whether a region this decoder does not implement paints the page, so the image is not decoded.</summary>
    public bool Undecodable { get; private set; }

    /// <summary>Marks the image as not decodable (a replayed global segment used a feature that paints the page).</summary>
    public void MarkUndecodable() => Undecodable = true;

    /// <summary>Records a legal feature this decoder does not implement (Information); when it paints the page the image is not decoded.</summary>
    /// <param name="feature">What the stream uses, for the message and as the deduplication key.</param>
    /// <param name="paintsPage">Whether the feature changes the page bitmap.</param>
    public void ReportUnsupported(string feature, bool paintsPage)
    {
        Undecodable |= paintsPage;
        Report(
            DiagnosticCodes.Jbig2UnsupportedFeature,
            DiagnosticSeverity.Information,
            paintsPage
                ? $"The JBIG2 stream uses {feature}, which is not decoded; the image is not decoded."
                : $"The JBIG2 stream uses {feature}, which is not decoded; it is skipped.",
            feature);
    }
}
