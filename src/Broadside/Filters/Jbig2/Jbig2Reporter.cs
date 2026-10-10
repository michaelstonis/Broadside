using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Filters.Jbig2;

/// <summary>Reports the deviations of one JBIG2Decode call through its filter context, each code (and key) once per call.</summary>
/// <remarks>
/// ADR 0005: lenient mode records and continues; strict mode throws from <see cref="Report"/>. Without a context it only tracks, and
/// keeps what it reports in <see cref="Recorded"/> (the global segments, decoded once per <c>JBIG2Globals</c> stream and replayed
/// into each image's context).
/// </remarks>
internal sealed class Jbig2Reporter(FilterContext? context)
{
    private readonly List<string> _reported = [];
    private readonly List<(string Code, DiagnosticSeverity Severity, string Message, string? Key)> _recorded = [];

    /// <summary>Gets what was reported, in order.</summary>
    public IReadOnlyList<(string Code, DiagnosticSeverity Severity, string Message, string? Key)> Recorded => _recorded;

    /// <summary>Gets a value indicating whether a region this decoder does not implement paints the page, so the image is not decoded.</summary>
    public bool Undecodable { get; private set; }

    /// <summary>Marks the image as not decodable (a replayed global segment used a feature that paints the page).</summary>
    public void MarkUndecodable() => Undecodable = true;

    /// <summary>Records a deviation unless one with the same code (and <paramref name="key"/>) was already recorded in this call.</summary>
    public void Report(string code, DiagnosticSeverity severity, string message, string? key = null)
    {
        string seen = key is null ? code : code + "/" + key;
        if (_reported.Contains(seen))
        {
            return;
        }

        _reported.Add(seen);
        _recorded.Add((code, severity, message, key));
        context?.Report(code, severity, message);
    }

    /// <summary>Reports again what another reporter recorded (each code and key still once per call).</summary>
    /// <param name="recorded">The recorded deviations.</param>
    public void Replay(IEnumerable<(string Code, DiagnosticSeverity Severity, string Message, string? Key)> recorded)
    {
        foreach ((string code, DiagnosticSeverity severity, string message, string? key) in recorded)
        {
            Report(code, severity, message, key);
        }
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
