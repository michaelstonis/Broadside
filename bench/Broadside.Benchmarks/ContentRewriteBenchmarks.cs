using BenchmarkDotNet.Attributes;
using Broadside.Content;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Content stream rewriting (issue #147, ISO 32000-2 §7.8.2): a 256 KB path-heavy content stream run through a reused editor that
/// wraps every tenth operator in <c>q</c>/<c>Q</c> and removes the one after it, written into the editor's own buffer.
/// <c>Allocated</c> must read <c>-</c>: neither numbering the operators nor writing the edits allocates once the editor is warm.
/// </summary>
/// <remarks>
/// <see cref="Rewrite"/> is the internal byte-level rewrite that <c>PdfPage.RewriteContent</c> runs before it stores the result (one
/// copy of the output per call, which is not the hot path). <see cref="Interpret"/> is the same content through the interpreter
/// alone, so the difference is the editor's cost.
/// </remarks>
[MemoryDiagnoser]
public class ContentRewriteBenchmarks
{
    private readonly Editor _editor = new();
    private readonly Counter _counter = new();
    private ReadOnlyMemory<byte> _content;
    private PdfDocument? _document;

    [GlobalSetup]
    public void Setup()
    {
        _content = ContentSamples.PathHeavy(256 * 1024);
        _document = PdfDocument.Create();
        _ = Rewrite();
        Interpret();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public int Rewrite() => ContentRewriting.RewriteBytes(_content, _document!, _editor, ContentInterpreter.DefaultOptions).Length;

    [Benchmark(Baseline = true)]
    public void Interpret() => ContentInterpreter.RunBytes(_content.Span, _document!, _counter, ContentInterpreter.DefaultOptions);

    /// <summary>Wraps every tenth operator and removes the one after it.</summary>
    private sealed class Editor : ContentEditor
    {
        public override ContentEvents Events => ContentEvents.Operators;

        public override void VisitOperator(in ContentOperator op, ContentContext context)
        {
            int index = CurrentOperator;
            if (index % 10 == 0)
            {
                Wrap(index, index, "q"u8, "Q"u8);
            }
            else if (index % 10 == 1)
            {
                Remove(index);
            }
        }
    }

    /// <summary>Asks for operators only and does nothing with them.</summary>
    private sealed class Counter : ContentProcessor
    {
        public override ContentEvents Events => ContentEvents.Operators;
    }
}
