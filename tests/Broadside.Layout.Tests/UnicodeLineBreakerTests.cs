using System.Globalization;
using System.IO.Compression;
using System.Text;
using Broadside.Layout.Flow;

namespace Broadside.Layout.Tests;

/// <summary>
/// The line-breaking seam of flow layout against Unicode's own conformance test of UAX #14, Unicode Line Breaking Algorithm
/// (revision 53, Unicode 16.0.0): <c>LineBreakTest.txt</c> lists, for each test string, where the default algorithm breaks.
/// </summary>
public sealed class UnicodeLineBreakerTests
{
    [Fact]
    public void Every_case_of_the_unicode_line_break_test_breaks_where_the_test_file_says()
    {
        var breaks = new List<BreakOpportunity>();
        var text = new StringBuilder();
        var expected = new List<int>();
        var failures = new List<string>();
        int cases = 0;
        foreach (string line in TestFileLines())
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            text.Clear();
            expected.Clear();
            foreach (string token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "÷")
                {
                    if (text.Length > 0)
                    {
                        expected.Add(text.Length);
                    }
                }
                else if (token != "×")
                {
                    text.Append(char.ConvertFromUtf32(int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                }
            }

            breaks.Clear();
            UnicodeLineBreaker.Instance.FindBreaks(text.ToString(), breaks);
            int[] actual = [.. breaks.Select(static opportunity => opportunity.Position)];
            if (!actual.SequenceEqual(expected))
            {
                failures.Add($"{line} -> breaks at [{string.Join(", ", actual)}], expected [{string.Join(", ", expected)}]");
            }

            cases++;
        }

        Assert.Equal(16672, cases);
        Assert.True(failures.Count == 0, $"{failures.Count} of {cases} cases differ:\n" + string.Join('\n', failures.Take(40)));
    }

    [Fact]
    public void Breaks_after_hard_line_breaks_are_mandatory_and_the_end_of_text_always_is()
    {
        var breaks = new List<BreakOpportunity>();

        UnicodeLineBreaker.Instance.FindBreaks("ab cd\r\nef\u2028gh\u000Bij\rk\u0085l\nm", breaks);

        Assert.Equal(
            [new(3, false), new(7, true), new(10, true), new(13, true), new(16, true), new(18, true), new(20, true), new(21, true)],
            breaks);
    }

    [Fact]
    public void An_empty_text_has_one_mandatory_break_at_its_end()
    {
        var breaks = new List<BreakOpportunity>();

        UnicodeLineBreaker.Instance.FindBreaks("", breaks);

        Assert.Equal([new BreakOpportunity(0, true)], breaks);
    }

    [Fact]
    public void Lone_surrogates_break_like_letters()
    {
        var breaks = new List<BreakOpportunity>();

        UnicodeLineBreaker.Instance.FindBreaks("a\uD800b \uDC00", breaks);

        Assert.Equal([new BreakOpportunity(4, false), new BreakOpportunity(5, true)], breaks);
    }

    [Fact]
    public void Flow_layout_breaks_lines_with_the_unicode_algorithm_by_default()
    {
        Assert.IsType<UnicodeLineBreaker>(FlowServices.Default.LineBreaker);
    }

    internal static IEnumerable<string> TestFileLines()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "LineBreakTest-16.0.0.txt.gz");
        using var reader = new StreamReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress), Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
