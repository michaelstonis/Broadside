using System.Globalization;
using Broadside.Diagnostics;

namespace Broadside.Text;

/// <summary>Collects one page of hOCR or ALTO input: its lines, its repairs and, when the input gives none, its size from the words' extent.</summary>
internal sealed class OcrPageBuilder
{
    private readonly List<OcrLine> _lines = [];
    private readonly List<Diagnostic> _diagnostics;
    private readonly string _format;
    private double _right;
    private double _bottom;

    /// <param name="format">The input format, as messages name it: <c>hOCR</c> or <c>ALTO</c>.</param>
    /// <param name="width">The page's image width, or <see langword="null"/> when the input gives no valid one.</param>
    /// <param name="height">The page's image height, or <see langword="null"/>.</param>
    /// <param name="diagnostics">Repairs already made for the page.</param>
    public OcrPageBuilder(string format, double? width, double? height, List<Diagnostic>? diagnostics = null)
    {
        _format = format;
        Width = width;
        Height = height;
        _diagnostics = diagnostics ?? [];
    }

    /// <summary>Gets the page's image width from the input, or <see langword="null"/>.</summary>
    public double? Width { get; }

    /// <summary>Gets the page's image height from the input, or <see langword="null"/>.</summary>
    public double? Height { get; }

    /// <summary>Adds a line, unless it has no words.</summary>
    public void AddLine(List<OcrWord> words, double angle, string? language)
    {
        if (words.Count == 0)
        {
            return;
        }

        foreach (OcrWord word in words)
        {
            _right = Math.Max(_right, word.Bounds.Right);
            _bottom = Math.Max(_bottom, word.Bounds.Top);
        }

        _lines.Add(new OcrLine([.. words], angle, language));
    }

    /// <summary>Records a word skipped for its box.</summary>
    public void BoundsInvalid(string detail) =>
        _diagnostics.Add(new Diagnostic(TextDiagnostics.OcrBoundsInvalid, TextDiagnostics.OcrBoundsInvalid.DefaultSeverity,
            $"A word of the {_format} input is skipped: {detail}."));

    /// <summary>Records a confidence read as 1.</summary>
    public void ConfidenceInvalid(string value) =>
        _diagnostics.Add(new Diagnostic(TextDiagnostics.OcrConfidenceInvalid, TextDiagnostics.OcrConfidenceInvalid.DefaultSeverity,
            $"A word of the {_format} input has the confidence '{Shorten(value)}', which is not a number in range; it is read as 1."));

    /// <summary>The page: its size from the input, else the extent of its words (at least 1 by 1) with a diagnostic.</summary>
    public OcrPage Build()
    {
        double width = Width ?? 0;
        double height = Height ?? 0;
        if (Width is null || Height is null)
        {
            width = Width ?? Math.Max(_right, 1);
            height = Height ?? Math.Max(_bottom, 1);
            _diagnostics.Add(new Diagnostic(TextDiagnostics.OcrPageSizeInvalid, TextDiagnostics.OcrPageSizeInvalid.DefaultSeverity,
                string.Create(CultureInfo.InvariantCulture, $"A page of the {_format} input gives no valid image size; {width} by {height}, the extent of its words, is used.")));
        }

        return new OcrPage(width, height, [.. _lines], [.. _diagnostics]);
    }

    /// <summary>Parses a finite number in the invariant culture.</summary>
    public static bool TryNumber(ReadOnlySpan<char> text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    /// <summary>A size from the input: positive and finite, else <see langword="null"/>.</summary>
    public static double? Size(ReadOnlySpan<char> text) => TryNumber(text, out double value) && value > 0 && value < 1e9 ? value : null;

    /// <summary>At most 40 characters of an input value, for a message.</summary>
    public static string Shorten(string value) => value.Length <= 40 ? value : string.Concat(value.AsSpan(0, 40), "…");
}
