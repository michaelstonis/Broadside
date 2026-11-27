using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Broadside.Layout.Flow;

namespace Broadside.Layout.Tests.Markdown;

/// <summary>
/// The CommonMark 0.31.2 example suite (issue #645): every example's Markdown, parsed with the GFM extensions off and rendered by
/// <see cref="MarkdownHtmlRenderer"/>, gives the spec's HTML byte for byte. The examples are the spec's own JSON
/// (https://spec.commonmark.org/0.31.2/spec.json, gzipped in <c>Data/</c>; source in <c>docs/specs-sources.md</c>). The pathological
/// inputs of the reference implementation's test suite parse in linear time.
/// </summary>
public sealed class CommonMarkSpecTests
{
    private static readonly Lazy<Example[]> Examples = new(Load);

    public static TheoryData<string> Sections()
    {
        var data = new TheoryData<string>();
        foreach (string section in Examples.Value.Select(example => example.Section).Distinct())
        {
            data.Add(section);
        }

        return data;
    }

    [Fact]
    public void The_suite_has_every_example_of_the_spec() => Assert.Equal(652, Examples.Value.Length);

    [Theory]
    [MemberData(nameof(Sections))]
    public void Every_example_of_the_section_renders_as_the_spec_says(string section)
    {
        var failures = new StringBuilder();
        foreach (Example example in Examples.Value.Where(example => example.Section == section))
        {
            string actual = MarkdownHtmlRenderer.Render(example.Markdown);
            if (actual != example.Html)
            {
                failures.Append(System.Globalization.CultureInfo.InvariantCulture, $"Example {example.Number}:\n--- markdown\n{example.Markdown}\n--- expected\n{example.Html}\n--- actual\n{actual}\n");
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());
    }

    public static TheoryData<string, string> Pathological() => new()
    {
        { "nested strong emphasis", string.Concat(Enumerable.Repeat("*a **a ", 30000)) + "b" + string.Concat(Enumerable.Repeat(" a** a*", 30000)) },
        { "emphasis closers with no openers", string.Concat(Enumerable.Repeat("a_ ", 50000)) },
        { "emphasis openers with no closers", string.Concat(Enumerable.Repeat("_a ", 50000)) },
        { "link closers with no openers", string.Concat(Enumerable.Repeat("a]", 50000)) },
        { "link openers with no closers", string.Concat(Enumerable.Repeat("[a", 50000)) },
        { "mismatched openers and closers", string.Concat(Enumerable.Repeat("*a_ ", 50000)) },
        { "openers and closers multiple of 3", "a**b" + string.Concat(Enumerable.Repeat("c* ", 50000)) },
        { "link openers and emphasis closers", string.Concat(Enumerable.Repeat("[ a_", 50000)) },
        { "pattern [ (]( repeated", string.Concat(Enumerable.Repeat("[ (](", 50000)) },
        { "nested brackets", new string('[', 50000) + "a" + new string(']', 50000) },
        { "nested block quotes", new string('>', 50000) + " a" },
        { "deeply nested lists", string.Concat(Enumerable.Range(0, 1000).Select(depth => new string(' ', depth * 2) + "* a\n")) },
        { "backticks", string.Concat(Enumerable.Range(1, 1000).Select(n => "e" + new string('`', n))) },
        { "unclosed links A", string.Concat(Enumerable.Repeat("[a](<b", 30000)) },
        { "unclosed links B", string.Concat(Enumerable.Repeat("[a](b", 30000)) },
        { "unclosed comments", string.Concat(Enumerable.Repeat("</ <!-- ", 30000)) },
        { "unclosed attribute values", string.Concat(Enumerable.Repeat("<a b='", 30000)) },
        { "unclosed processing instructions", string.Concat(Enumerable.Repeat("a <?", 30000)) },
        { "autolink openers", new string('<', 50000) },
        { "many references", string.Concat(Enumerable.Range(0, 5000).Select(n => $"[{n}]: /u\n")) + string.Concat(Enumerable.Range(0, 5000).Select(n => $"[{n}] ")) },
        { "link with many trailing parentheses (GFM)", "www.a.com" + new string(')', 50000) },
        { "table with many rows (GFM)", "| a | b |\n|---|---|\n" + string.Concat(Enumerable.Repeat("| c | d |\n", 20000)) },
    };

    [Theory]
    [MemberData(nameof(Pathological))]
    public void A_pathological_input_parses_in_linear_time(string name, string markdown)
    {
        _ = name;
        Stopwatch watch = Stopwatch.StartNew();
        MarkdownNode document = MarkdownParser.Parse(markdown, gitHubExtensions: true);
        _ = MarkdownHtmlRenderer.Render(document);
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"{watch.Elapsed} to parse.");
    }

    private static Example[] Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "commonmark-spec-0.31.2.json.gz");
        using FileStream file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using JsonDocument json = JsonDocument.Parse(gzip);
        return [.. json.RootElement.EnumerateArray().Select(element => new Example(
            element.GetProperty("example").GetInt32(),
            element.GetProperty("section").GetString()!,
            element.GetProperty("markdown").GetString()!,
            element.GetProperty("html").GetString()!))];
    }

    private sealed record Example(int Number, string Section, string Markdown, string Html);
}
