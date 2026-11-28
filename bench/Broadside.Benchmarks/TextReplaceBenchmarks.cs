using BenchmarkDotNet.Attributes;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;
using Broadside.Text.Editing;

namespace Broadside.Benchmarks;

/// <summary>
/// Replacing text (issue #641, ISO 32000-2 §9.4.3) in content that does not hold the match: 128 KB of text-heavy and 128 KB of
/// path-heavy content run through a reused text edit editor looking for a glyph the content never shows, so every glyph is keyed
/// and looked up and none is changed. The editor allocates nothing per operator or glyph on a page without the match:
/// <c>Allocated</c> is the interpreter's fixed cost per run, whatever the length of the content (<c>TextReplaceAllocationTests</c>).
/// </summary>
/// <remarks><see cref="Interpret"/> is the same content through the interpreter alone, so the difference is the editor's cost.</remarks>
[MemoryDiagnoser]
public class TextReplaceBenchmarks
{
    private readonly Counter _counter = new();
    private ReadOnlyMemory<byte> _content;
    private PdfDocument? _document;
    private TextEditEditor? _editor;

    [GlobalSetup]
    public void Setup()
    {
        byte[] text = ContentSamples.TextHeavy(20_000);
        byte[] paths = ContentSamples.PathHeavy(128 * 1024);
        _content = (byte[])[.. text, .. "\n"u8, .. paths];
        _document = PdfDocument.Create();
        var session = new TextEditSession(_document, new PdfTextReplaceOptions());
        _editor = Elsewhere(session, _document);
        _ = Replace();
        Interpret();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public int Replace() => ContentRewriting.RewriteBytes(_content, _document!, _editor!, ContentInterpreter.DefaultOptions).Length;

    [Benchmark(Baseline = true)]
    public void Interpret() => ContentInterpreter.RunBytes(_content.Span, _document!, _counter, ContentInterpreter.DefaultOptions);

    /// <summary>An editor whose session looks for one glyph, of a code no font shows, off the page.</summary>
    internal static TextEditEditor Elsewhere(TextEditSession session, PdfDocument document)
    {
        TextEditMatch match = TextEditMatch.OfGlyph(0xFFFF, new PathPoint(-500, -500), document.Pages[0].ExtractText());
        session.Prepare([match]);
        var editor = new TextEditEditor(session);
        editor.Configure(new CosDictionary(), null);
        return editor;
    }

    /// <summary>Asks for the events the text edit editor asks for and does nothing with them.</summary>
    private sealed class Counter : ContentProcessor
    {
        public override ContentEvents Events =>
            ContentEvents.Operators | ContentEvents.Glyphs | ContentEvents.Forms | ContentEvents.MarkedContent | ContentEvents.HiddenContent;
    }
}
