using System.Globalization;
using System.Reflection;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The rendering guide (site/articles/rendering.md, issue #268) documents every render option with its default: each settable public
/// property of <see cref="RenderOptions"/> has a row in the guide's options table whose default cell names the value a new instance has.
/// </summary>
public sealed class RenderingGuideTests
{
    private static readonly string GuidePath = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "..", "site", "articles", "rendering.md"));

    public static TheoryData<string> Options() =>
        [.. typeof(RenderOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.CanWrite).Select(property => property.Name)];

    [Theory]
    [MemberData(nameof(Options))]
    public void Every_render_option_is_documented_with_its_default(string name)
    {
        string[] rows = [.. File.ReadAllLines(GuidePath).Where(line => line.StartsWith($"| `{name}` / `With", StringComparison.Ordinal))];

        Assert.True(rows.Length == 1, $"{GuidePath} should have one row for RenderOptions.{name} in its options table; it has {rows.Length}.");
        string defaultCell = rows[0].Split('|')[2];
        string expected = $"`{Describe(typeof(RenderOptions).GetProperty(name)!.GetValue(new RenderOptions()))}`";
        Assert.True(defaultCell.Contains(expected, StringComparison.Ordinal), $"The default of RenderOptions.{name} is {expected}; the guide says {defaultCell.Trim()}.");
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        bool flag => flag ? "true" : "false",
        Enum member => $"{member.GetType().Name}.{member}",
        CancellationToken token when token == CancellationToken.None => "CancellationToken.None",
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
