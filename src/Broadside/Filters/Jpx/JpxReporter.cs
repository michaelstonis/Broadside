using Broadside.Diagnostics;

namespace Broadside.Filters.Jpx;

/// <summary>Reports the deviations of one JPXDecode call through its filter context, each code once per call.</summary>
/// <remarks>ADR 0005: lenient mode records and continues; strict mode throws from <see cref="Report"/>.</remarks>
internal sealed class JpxReporter(FilterContext context)
{
    private readonly List<string> _reported = [];

    /// <summary>Gets a value indicating whether a feature this decoder does not implement was met.</summary>
    public bool Unsupported { get; private set; }

    /// <summary>Records a deviation unless one with the same code (and <paramref name="key"/>) was already recorded in this call.</summary>
    public void Report(string code, DiagnosticSeverity severity, string message, string? key = null)
    {
        string seen = key is null ? code : code + "/" + key;
        if (_reported.Contains(seen))
        {
            return;
        }

        _reported.Add(seen);
        context.Report(code, severity, message);
    }

    /// <summary>Records a legal feature this decoder does not implement yet (Information) and marks the image undecodable.</summary>
    public void ReportUnsupported(string feature)
    {
        Unsupported = true;
        Report(
            Parsing.DiagnosticCodes.JpxUnsupportedFeature,
            DiagnosticSeverity.Information,
            $"The JPEG 2000 codestream uses {feature}, which is not decoded yet; the image is not decoded.",
            feature);
    }
}
