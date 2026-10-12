using Broadside.Diagnostics;

namespace Broadside.Filters.Codecs;

/// <summary>A deviation a codec reported (or recorded without a context), with the key it was deduplicated on.</summary>
/// <param name="Code">The diagnostic code.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Message">The message.</param>
/// <param name="Key">The deduplication key besides the code, or <see langword="null"/>.</param>
internal readonly record struct CodecDiagnostic(string Code, DiagnosticSeverity Severity, string Message, string? Key);

/// <summary>
/// Reports the deviations of one image codec call through its filter context, each code (and key) once per call: the one
/// "report once per code" policy the DCT, JPX and JBIG2 decoders share.
/// </summary>
/// <remarks>
/// ADR 0005: lenient mode records and continues; strict mode throws from <see cref="Report"/> (the context throws). A silent reporter
/// (a header read the decode repeats) reports nothing. Without a context it only records, in <see cref="Recorded"/>, for a caller
/// that replays the deviations into another call (JBIG2 globals). A pooled decoder calls <see cref="Reset"/> per call; the set and
/// list keep their capacity, so a call that reports nothing allocates nothing.
/// </remarks>
internal class CodecReporter
{
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly List<CodecDiagnostic> _recorded = [];
    private FilterContext? _context;
    private bool _silent;

    /// <summary>Initializes a reporter.</summary>
    /// <param name="context">Where deviations are reported, or <see langword="null"/> to record only.</param>
    /// <param name="silent">Whether to report and record nothing.</param>
    public CodecReporter(FilterContext? context = null, bool silent = false)
    {
        _context = context;
        _silent = silent;
    }

    /// <summary>Gets what was reported, in order.</summary>
    public IReadOnlyList<CodecDiagnostic> Recorded => _recorded;

    /// <summary>Starts a new call: forgets what was reported and targets <paramref name="context"/>.</summary>
    /// <param name="context">Where deviations are reported, or <see langword="null"/> to record only.</param>
    /// <param name="silent">Whether to report and record nothing.</param>
    public void Reset(FilterContext? context, bool silent = false)
    {
        _reported.Clear();
        _recorded.Clear();
        _context = context;
        _silent = silent;
    }

    /// <summary>Returns whether <see cref="Report"/> would report <paramref name="code"/> now, so a caller can skip building the message.</summary>
    /// <param name="code">The code.</param>
    /// <param name="key">The deduplication key besides the code.</param>
    /// <returns><see langword="true"/> when the reporter is not silent and the code (and key) was not reported in this call.</returns>
    public bool ShouldReport(string code, string? key = null) => !_silent && !_reported.Contains(key is null ? code : code + "/" + key);

    /// <summary>Records a deviation unless one with the same code (and <paramref name="key"/>) was already recorded in this call.</summary>
    /// <param name="code">The code.</param>
    /// <param name="severity">The severity.</param>
    /// <param name="message">The message.</param>
    /// <param name="key">The deduplication key besides the code (one report per distinct key).</param>
    public void Report(string code, DiagnosticSeverity severity, string message, string? key = null)
    {
        if (_silent || !_reported.Add(key is null ? code : code + "/" + key))
        {
            return;
        }

        _recorded.Add(new CodecDiagnostic(code, severity, message, key));
        _context?.Report(code, severity, message);
    }

    /// <summary>Reports again what another reporter recorded (each code and key still once per call).</summary>
    /// <param name="recorded">The recorded deviations.</param>
    public void Replay(IEnumerable<CodecDiagnostic> recorded)
    {
        foreach (CodecDiagnostic diagnostic in recorded)
        {
            Report(diagnostic.Code, diagnostic.Severity, diagnostic.Message, diagnostic.Key);
        }
    }
}
