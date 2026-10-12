using BenchmarkDotNet.Attributes;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Optional content visibility (ISO 32000-2 §8.11, issue #76): the check a content interpreter makes on every <c>/OC</c> marked-content
/// section and every optional XObject. Both must allocate nothing (<c>Allocated</c> = <c>-</c>) once memberships are compiled.
/// </summary>
[MemoryDiagnoser]
public class OptionalContentBenchmarks
{
    private static readonly CosName OC = new("OC");

    private PdfDocument? _document;
    private PdfOptionalContentProperties? _properties;
    private PdfOptionalContentState? _state;
    private PdfOptionalContentTracker? _tracker;
    private CosObject[] _operands = [];

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, "optional-content.pdf"));
        _properties = _document.OptionalContent!;
        _state = _properties.GetDefaultStates();
        _tracker = new PdfOptionalContentTracker(_properties, _state);
        var resources = (CosDictionary)_document.Resolve(_document.Pages[0].Resources![new CosName("Properties")]);
        _operands = [.. resources.Values];
        _ = IsVisible();
        _ = TrackSections();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>Evaluates a group, a policy membership and a visibility-expression membership.</summary>
    [Benchmark]
    public int IsVisible()
    {
        int visible = 0;
        foreach (CosObject operand in _operands)
        {
            if (_properties!.IsVisible(operand, _state!))
            {
                visible++;
            }
        }

        return visible;
    }

    /// <summary>Opens and closes nested <c>/OC</c> sections the way an interpreter does on <c>BDC</c> and <c>EMC</c>.</summary>
    [Benchmark]
    public int TrackSections()
    {
        int visible = 0;
        foreach (CosObject operand in _operands)
        {
            _tracker!.BeginMarkedContent(OC, operand);
            visible += _tracker.IsVisible ? 1 : 0;
        }

        foreach (CosObject _ in _operands)
        {
            _tracker!.EndMarkedContent();
        }

        return visible;
    }
}
