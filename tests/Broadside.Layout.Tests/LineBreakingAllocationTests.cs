using Broadside.Layout.Flow;
using Broadside.TestSupport;

namespace Broadside.Layout.Tests;

/// <summary>
/// Finding the break opportunities of a paragraph allocates nothing per character (UAX #14 classes from a generated table, the
/// per-character state in pooled buffers): only the caller's list of opportunities grows, and a reused list does not.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class LineBreakingAllocationTests
{
    [Fact]
    public void Finding_breaks_into_a_reused_list_allocates_nothing()
    {
        string text = string.Concat(Enumerable.Repeat(
            "The well-known café’s “menu” costs $12.50 (incl. tax); 日本語のテキスト, 한국어 텍스트 and 👍🏽 emoji\u00ADcode.\n", 40));
        var breaks = new List<BreakOpportunity>(4096);
        Action find = () =>
        {
            breaks.Clear();
            UnicodeLineBreaker.Instance.FindBreaks(text, breaks);
        };

        long allocated = Allocations.Measure(find);

        Assert.True(breaks.Count > 400, $"{breaks.Count} breaks.");
        Assert.Equal(0, allocated);
    }
}
