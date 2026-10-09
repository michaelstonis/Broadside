using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>
/// Everything an <see cref="IFontProgramParser"/> may need beyond the program's bytes: where the program comes from, the font file
/// stream's length entries, the limits on hostile data, and the diagnostics sink. A parsed program keeps its context and reports
/// what it finds later (a damaged glyph) through it.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.9, Tables 124 and 125. A context created with the public constructor stands alone: its diagnostics are kept in
/// <see cref="Diagnostics"/> only, which is how a parser is tested or used outside a document. Inside a document, diagnostics are
/// recorded on the document against the font file stream, once per code.
/// </para>
/// <para>Thread safety: <see cref="Report"/> may be called from several threads at once (concurrent renders of one document).</para>
/// </remarks>
public sealed class FontProgramContext
{
    /// <summary>The default of <see cref="MaxCompositeDepth"/>.</summary>
    public const int DefaultMaxCompositeDepth = 16;

    /// <summary>The default of <see cref="MaxGlyphPoints"/>.</summary>
    public const int DefaultMaxGlyphPoints = 65536;

    private readonly DiagnosticSink? _sink;
    private readonly CosReference? _objectReference;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly HashSet<string> _codes = [];

    /// <summary>Initializes a new instance of the <see cref="FontProgramContext"/> class that stands alone, outside any document.</summary>
    public FontProgramContext()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FontProgramContext"/> class for a font file stream of a document.</summary>
    internal FontProgramContext(DiagnosticSink sink, CosReference? objectReference)
    {
        _sink = sink;
        _objectReference = objectReference;
        ReadingMode = sink.IsStrict ? PdfReadingMode.Strict : PdfReadingMode.Lenient;
    }

    /// <summary>Gets the font descriptor entry the program comes from; <see cref="FontProgramSource.Unspecified"/> outside a document.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120.</remarks>
    public FontProgramSource Source { get; init; }

    /// <summary>Gets the font file stream's <c>Subtype</c> (<c>Type1C</c>, <c>CIDFontType0C</c>, <c>OpenType</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §9.9, Table 125 (required for <c>FontFile3</c>), and Table 124.</remarks>
    public CosName? Subtype { get; init; }

    /// <summary>
    /// Gets the stream's <c>Length1</c>: the length of a TrueType program (the whole program), or of the clear-text portion of a Type 1
    /// program; <see langword="null"/> when absent.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.9, Table 125.</remarks>
    public long? Length1 { get; init; }

    /// <summary>Gets the stream's <c>Length2</c>: the length of a Type 1 program's encrypted portion; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §9.9, Table 125.</remarks>
    public long? Length2 { get; init; }

    /// <summary>Gets the stream's <c>Length3</c>: the length of a Type 1 program's fixed-content portion; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §9.9, Table 125.</remarks>
    public long? Length3 { get; init; }

    /// <summary>
    /// Gets a value indicating whether the program belongs to a CIDFont, which selects glyphs by CID rather than through the program's
    /// "cmap" table (so a TrueType program for a CIDFont needs none).
    /// </summary>
    /// <remarks>ISO 32000-2 §9.9 (the paragraph after Table 125) and §9.7.4.2.</remarks>
    public bool IsCidFont { get; init; }

    /// <summary>Gets which font of a font collection (a TrueType collection, "ttcf") to read when <see cref="FaceName"/> names none of them; default 0.</summary>
    public int FaceIndex { get; init; }

    /// <summary>
    /// Gets the PostScript name of the font to read from a font collection, such as the font's <c>BaseFont</c> without a subset
    /// prefix; <see langword="null"/> to use <see cref="FaceIndex"/>.
    /// </summary>
    public string? FaceName { get; init; }

    /// <summary>Gets how deviations are treated: in <see cref="PdfReadingMode.Strict"/> mode, <see cref="Report"/> throws.</summary>
    /// <remarks>ADR 0005.</remarks>
    public PdfReadingMode ReadingMode { get; init; } = PdfReadingMode.Lenient;

    /// <summary>Gets how deeply composite glyphs may nest; a deeper component is skipped with a diagnostic. Default 16.</summary>
    /// <remarks>Guards against hostile programs (OpenType "glyf": composite glyph descriptions); the program's own maximum is only a hint.</remarks>
    public int MaxCompositeDepth { get; init; } = DefaultMaxCompositeDepth;

    /// <summary>Gets how many points one glyph may have, components included; a larger glyph is dropped with a diagnostic. Default 65,536.</summary>
    /// <remarks>Guards against composite fan-out: a few levels of many components each multiply into billions of points.</remarks>
    public int MaxGlyphPoints { get; init; } = DefaultMaxGlyphPoints;

    /// <summary>Gets the diagnostics reported through this context: the first of each code, in order.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics
    {
        get
        {
            lock (_diagnostics)
            {
                return [.. _diagnostics];
            }
        }
    }

    /// <summary>Records a deviation found in the program and what the parser did about it. Only the first of each code is kept.</summary>
    /// <param name="code">A stable PascalCase identifier of the kind of deviation, such as <c>FontGlyphInvalid</c>.</param>
    /// <param name="severity">Whether the repair kept the glyphs (<see cref="DiagnosticSeverity.Warning"/>) or lost some.</param>
    /// <param name="message">What was wrong and what the parser did.</param>
    /// <exception cref="DiagnosticException">In strict mode, unless the severity is <see cref="DiagnosticSeverity.Information"/>.</exception>
    /// <remarks>ADR 0005. Inside a document the diagnostic is recorded on the document, against the font file stream.</remarks>
    public void Report(string code, DiagnosticSeverity severity, string message)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);
        var diagnostic = new Diagnostic(code, severity, message, objectReference: _objectReference);
        lock (_diagnostics)
        {
            if (_codes.Add(code))
            {
                _diagnostics.Add(diagnostic);
            }
        }

        if (_sink is not null)
        {
            _sink.ReportOnce(code, severity, message, objectReference: _objectReference);
        }
        else if (ReadingMode == PdfReadingMode.Strict && severity != DiagnosticSeverity.Information)
        {
            throw new DiagnosticException(diagnostic);
        }
    }
}
