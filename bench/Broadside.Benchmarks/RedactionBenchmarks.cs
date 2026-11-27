using BenchmarkDotNet.Attributes;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Redaction;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Applying redaction (issue #624, ISO 32000-2 §12.5.6.23) to content with nothing under the area: 128 KB of text-heavy and 128 KB
/// of path-heavy content run through a reused redaction editor whose area lies beside everything the content draws, so every glyph
/// and path is tested and none is removed. The editor allocates nothing per operator or glyph on a page with no hits:
/// <c>Allocated</c> is the interpreter's fixed cost per run (the content names a font no resources hold, reported once a run),
/// whatever the length of the content (<c>RedactionAllocationTests</c>).
/// </summary>
/// <remarks><see cref="Interpret"/> is the same content through the interpreter alone, so the difference is the editor's cost.</remarks>
[MemoryDiagnoser]
public class RedactionBenchmarks
{
    private readonly Counter _counter = new();
    private ReadOnlyMemory<byte> _content;
    private PdfDocument? _document;
    private RedactionEditor? _editor;

    [GlobalSetup]
    public void Setup()
    {
        byte[] text = ContentSamples.TextHeavy(20_000);
        byte[] paths = ContentSamples.PathHeavy(128 * 1024);
        _content = (byte[])[.. text, .. "\n"u8, .. paths];
        _document = PdfDocument.Create();
        _editor = new RedactionEditor(new RedactionSession(_document, new PdfRedactionOptions()));
        _editor.Configure([Quad.FromRectangle(Matrix.Identity, -200, -200, -150, -150)], new CosDictionary(), null);
        _ = Redact();
        Interpret();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public int Redact() => ContentRewriting.RewriteBytes(_content, _document!, _editor!, ContentInterpreter.DefaultOptions).Length;

    [Benchmark(Baseline = true)]
    public void Interpret() => ContentInterpreter.RunBytes(_content.Span, _document!, _counter, ContentInterpreter.DefaultOptions);

    /// <summary>Asks for the events the redaction editor asks for and does nothing with them.</summary>
    private sealed class Counter : ContentProcessor
    {
        public override ContentEvents Events =>
            ContentEvents.Operators | ContentEvents.Paths | ContentEvents.Glyphs | ContentEvents.Images | ContentEvents.Shadings
            | ContentEvents.Forms | ContentEvents.MarkedContent | ContentEvents.HiddenContent;
    }
}
