using SharpFuzz;

namespace Broadside.Fuzz;

/// <summary>
/// The fuzz targets, by name. A target takes one input and must either return or throw; the harness treats any escaped
/// exception as a finding. Every parser and codec gets a target here as it lands (CLAUDE.md, "Code conventions").
/// </summary>
internal static class FuzzTargets
{
    /// <summary>Every registered target. Names are the stable identifiers used on the command line and in CI.</summary>
    public static IReadOnlyDictionary<string, ReadOnlySpanAction> All { get; } = new Dictionary<string, ReadOnlySpanAction>(StringComparer.Ordinal)
    {
        ["pdf-header"] = PdfHeader,
    };

    /// <summary>
    /// Placeholder until the lexer lands (#11): decides whether the input starts with the <c>%PDF-</c> header marker.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5.2.</remarks>
    private static void PdfHeader(ReadOnlySpan<byte> data)
    {
        bool hasHeader = data.StartsWith("%PDF-"u8);
        if (hasHeader && data.Length < "%PDF-"u8.Length)
        {
            throw new InvalidOperationException("StartsWith claimed a header shorter than the marker; the input cannot be both.");
        }
    }
}
