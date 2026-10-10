using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.OptionalContent;

/// <summary>
/// The visibility checks a content interpreter makes per <c>/OC</c> section and per optional XObject allocate nothing once each
/// membership is compiled (ISO 32000-2 §8.11; CLAUDE.md hot-path rule). Benchmark: <c>OptionalContentBenchmarks</c>.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class OptionalContentAllocationTests
{
    [Fact]
    public void Visibility_checks_and_section_tracking_allocate_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("optional-content.pdf"));
        PdfOptionalContentProperties properties = document.OptionalContent!;
        PdfOptionalContentState state = properties.GetDefaultStates();
        var tracker = new PdfOptionalContentTracker(properties, state);
        var oc = new CosName("OC");
        CosObject[] operands = [.. ((CosDictionary)document.Resolve(document.Pages[0].Resources![new CosName("Properties")])).Values];
        int visible = 0;

        long allocated = Allocations.Measure(
            () =>
            {
                visible = 0;
                for (int iteration = 0; iteration < 1000; iteration++)
                {
                    visible += Run();
                }
            },
            warmUpCalls: 1);

        Assert.Equal(1000 * 3, visible);
        Assert.Equal(0, allocated);

        int Run()
        {
            int count = 0;
            foreach (CosObject operand in operands)
            {
                count += properties.IsVisible(operand, state) ? 1 : 0;
                tracker.BeginMarkedContent(oc, operand);
            }

            foreach (CosObject _ in operands)
            {
                count += tracker.IsVisible ? 1 : 0;
                tracker.EndMarkedContent();
            }

            return count;
        }
    }
}
