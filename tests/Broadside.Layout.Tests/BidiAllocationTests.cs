using Broadside.Layout.Flow;
using Broadside.TestSupport;

namespace Broadside.Layout.Tests;

/// <summary>
/// Resolving the bidi levels of a paragraph and reordering its lines allocate nothing per character (UAX #9 classes from a
/// generated table, the per-character state in pooled buffers, the explicit and bracket stacks on the stack).
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class BidiAllocationTests
{
    [Fact]
    public void Resolving_and_reordering_into_reused_buffers_allocates_nothing()
    {
        string text = string.Concat(Enumerable.Repeat(BidiSample.Paragraph, 40));
        byte[] levels = new byte[text.Length];
        byte[] line = new byte[200];
        LineRun[] runs = [.. Enumerable.Range(0, 9).Select(static index => new LineRun(index * 10, 10, 1, 0, index % 3))];
        Action resolve = () =>
        {
            UnicodeBidiResolver.Instance.Resolve(text, TextDirection.Auto, levels);
            UnicodeBidiResolver.Instance.ResetWhitespace(text.AsSpan(0, line.Length), 1, line);
            UnicodeBidiResolver.Instance.ReorderLine(runs);
        };

        long allocated = Allocations.Measure(resolve);

        Assert.Contains(levels, static level => (level & UnicodeBidi.LevelMask) == 2);
        Assert.Equal(0, allocated);
    }
}

/// <summary>A paragraph of Hebrew, Arabic, Latin, numbers, brackets, an isolate and an embedding, ending in a paragraph separator.</summary>
internal static class BidiSample
{
    public const string Paragraph =
        "\u05E9\u05DC\u05D5\u05DD (world) 123.45 \u05E2\u05D5\u05DC\u05DD [\u0627\u0644\u0639\u0631\u0628\u064A\u0629 \u0661\u0662] \u2067abc\u2069 \u202Bdef\u202C mixed\u2029";
}
