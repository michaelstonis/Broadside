using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Filters.Jbig2;

/// <summary>Reports the deviations of one JBIG2Decode call through its filter context, each code (and key) once per call.</summary>
/// <remarks>ADR 0005: lenient mode records and continues; strict mode throws from <see cref="Report"/>. Without a context it only tracks.</remarks>
internal sealed class Jbig2Reporter(FilterContext? context)
{
    private readonly List<string> _reported = [];

    /// <summary>Gets a value indicating whether a region this decoder does not implement paints the page, so the image is not decoded.</summary>
    public bool Undecodable { get; private set; }

    /// <summary>Records a deviation unless one with the same code (and <paramref name="key"/>) was already recorded in this call.</summary>
    public void Report(string code, DiagnosticSeverity severity, string message, string? key = null)
    {
        string seen = key is null ? code : code + "/" + key;
        if (_reported.Contains(seen))
        {
            return;
        }

        _reported.Add(seen);
        context?.Report(code, severity, message);
    }

    /// <summary>Records a legal feature this decoder does not implement yet (Information); when it paints the page the image is not decoded.</summary>
    /// <param name="feature">What the stream uses, for the message and as the deduplication key.</param>
    /// <param name="paintsPage">Whether the feature changes the page bitmap.</param>
    public void ReportUnsupported(string feature, bool paintsPage)
    {
        Undecodable |= paintsPage;
        Report(
            DiagnosticCodes.Jbig2UnsupportedFeature,
            DiagnosticSeverity.Information,
            paintsPage
                ? $"The JBIG2 stream uses {feature}, which is not decoded yet; the image is not decoded."
                : $"The JBIG2 stream uses {feature}, which is not decoded yet; it is skipped.",
            feature);
    }
}
