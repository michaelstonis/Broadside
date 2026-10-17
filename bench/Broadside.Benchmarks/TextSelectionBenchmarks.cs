using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.TestSupport;
using Broadside.Viewer;

namespace Broadside.Benchmarks;

/// <summary>
/// One pointer move of a text-selection drag (issue #214, viewer capability 4) over a page of 10,000 glyphs: extending the selection
/// to a point on a glyph (a hit-test point query) or off the text (the nearest-glyph scan), then reading the quads the shell draws.
/// A shell runs this on every pointer move, so each must read <c>-</c> in <c>Allocated</c>;
/// <c>Broadside.Viewer.Tests.TextSelectionAllocationTests</c> fails the build if one regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class TextSelectionBenchmarks
{
    private readonly Quad[] _quads = new Quad[512];
    private DocumentSession? _session;
    private TextSelection? _selection;
    private PagePoint _onText;
    private PagePoint _onTextOther;
    private PagePoint _offText;
    private bool _flip;

    [GlobalSetup]
    public void Setup()
    {
        _session = DocumentSession.Open(TextSamples.Page(ContentSamples.TextHeavy(10_000)));
        _selection = new TextSelection(_session, ContentReadingOrder.Instance);
        SelectableText text = _selection.LoadPageAsync(0).AsTask().GetAwaiter().GetResult();
        _onText = Centre(text, text.Length / 2);
        _onTextOther = Centre(text, (text.Length / 2) + 40);
        _offText = new PagePoint(0, -50, _onText.Y);
        _selection.Begin(Centre(text, 10));
    }

    [GlobalCleanup]
    public void Cleanup() => _session?.Dispose();

    [Benchmark]
    public int DragOnText()
    {
        _flip = !_flip;
        _selection!.Extend(_flip ? _onText : _onTextOther);
        return _selection.GetQuads(0, _quads);
    }

    [Benchmark]
    public int DragOffText()
    {
        _flip = !_flip;
        _selection!.Extend(_flip ? _offText : _onText);
        return _selection.GetQuads(0, _quads);
    }

    private static PagePoint Centre(SelectableText text, int position)
    {
        PdfRectangle box = text.Index.GetGlyph(text.GetGlyphIndex(position)).Quad.Bounds;
        return new PagePoint(0, (box.Left + box.Right) / 2, (box.Bottom + box.Top) / 2);
    }
}
