using Broadside.Diagnostics;

namespace Broadside.Filters.Jpx;

/// <summary>Reports the deviations of one JPXDecode call through its filter context, each code once per call.</summary>
/// <remarks>ADR 0005: lenient mode records and continues; strict mode throws from <see cref="Report"/>.</remarks>
internal sealed class JpxReporter(FilterContext context, bool silent = false)
{
    private readonly List<string> _reported = [];

    /// <summary>Records a deviation unless one with the same code (and <paramref name="key"/>) was already recorded in this call.</summary>
    /// <remarks>A silent reporter (header reads, which the decode repeats) records nothing.</remarks>
    public void Report(string code, DiagnosticSeverity severity, string message, string? key = null)
    {
        string seen = key is null ? code : code + "/" + key;
        if (silent || _reported.Contains(seen))
        {
            return;
        }

        _reported.Add(seen);
        context.Report(code, severity, message);
    }
}
