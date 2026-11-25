using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Broadside.Filters;
using Broadside.Fonts;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Every parser and codec has a fuzz target (CLAUDE.md, "Code conventions"; spec Testing Decisions; issue #274).
/// <c>tests/Broadside.Fuzz/parsers.txt</c> maps each parser or codec type of the core and rendering assemblies to the fuzz
/// targets that exercise it; the coverage report (<c>docs/fuzz-coverage.md</c>) measures each target over the types mapped to it.
/// A parser or codec is any type registered behind the filter or font program parser extension point, any type whose name ends
/// in Parser, Reader, Decoder, Tokenizer or Lexer, and the parsers added in Phases 3 to 5 that follow neither rule.
/// </summary>
public sealed partial class FuzzTargetMapTests
{
    private static readonly string[] Suffixes = ["Parser", "Reader", "Decoder", "Tokenizer", "Lexer"];

    /// <summary>The Phase 3–5 parsers issue #274 names: ICC profiles, the DA tokenizer, OpenType layout tables, CMS and time-stamp responses, PNG and JPEG input to the canvas, range responses.</summary>
    private static readonly string[] PhaseThreeToFive =
    [
        "Broadside.Rendering.Icc.IccProfile",
        "Broadside.Forms.PdfDefaultAppearance",
        "Broadside.Fonts.OpenType.LayoutTable",
        "Broadside.Fonts.OpenType.GdefTable",
        "Broadside.Fonts.OpenType.KernTable",
        "Broadside.Signatures.Validation.CmsSignature",
        "Broadside.Signatures.Validation.RevocationList",
        "Broadside.Signatures.TimestampProtocol",
        "Broadside.Signatures.Validation.OcspResponse",
        "Broadside.Images.Png.PngReader",
        "Broadside.Images.JpegInfo",
        "Broadside.IO.HttpRangeSource",
    ];

    private static readonly Assembly[] Assemblies = [typeof(PdfDocument).Assembly, typeof(SoftwareRasterizer).Assembly];

    [Fact]
    public void Every_parser_and_codec_has_a_registered_fuzz_target()
    {
        Dictionary<string, string[]> map = ReadMap();
        HashSet<string> targets = ReadTargets();

        string[] unmapped = [.. ParsersAndCodecs().Where(type => !map.ContainsKey(type)).Order(StringComparer.Ordinal)];
        string[] unknownTargets = [.. map.SelectMany(entry => entry.Value.Where(target => !targets.Contains(target)).Select(target => $"{entry.Key} -> {target}"))];

        Assert.True(unmapped.Length == 0, $"Parsers and codecs without a fuzz target in tests/Broadside.Fuzz/parsers.txt (add a target and a line): {string.Join(", ", unmapped)}");
        Assert.True(unknownTargets.Length == 0, $"parsers.txt names targets FuzzTargets.All does not register: {string.Join(", ", unknownTargets)}");
    }

    [Fact]
    public void The_parsers_of_phases_3_to_5_are_mapped()
    {
        Dictionary<string, string[]> map = ReadMap();

        Assert.All(PhaseThreeToFive, type => Assert.True(map.ContainsKey(type), $"{type} has no fuzz target in parsers.txt."));
    }

    [Fact]
    public void Every_mapped_type_exists()
    {
        HashSet<string> types = [.. Assemblies.SelectMany(static assembly => assembly.GetTypes()).Select(static type => type.FullName!)];

        string[] stale = [.. ReadMap().Keys.Where(type => !types.Contains(type)).Order(StringComparer.Ordinal)];

        Assert.True(stale.Length == 0, $"parsers.txt names types that no longer exist: {string.Join(", ", stale)}");
    }

    private static IEnumerable<string> ParsersAndCodecs()
    {
        foreach (Type type in Assemblies.SelectMany(static assembly => assembly.GetTypes()))
        {
            if (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) || type.Name.Contains('<', StringComparison.Ordinal) || type.IsInterface)
            {
                continue;
            }

            string name = type.Name.Split('`')[0];
            bool extension = !type.IsAbstract && (typeof(IStreamFilter).IsAssignableFrom(type) || typeof(IFontProgramParser).IsAssignableFrom(type));
            if (extension || Suffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)))
            {
                yield return type.FullName!;
            }
        }

        foreach (string type in PhaseThreeToFive)
        {
            yield return type;
        }
    }

    /// <summary>Reads <c>parsers.txt</c>: one type (its full name) per line, then the targets that exercise it.</summary>
    private static Dictionary<string, string[]> ReadMap()
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(Path.Combine(RepositoryRoot(), "tests", "Broadside.Fuzz", "parsers.txt")))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            string[] fields = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(fields.Length >= 2, $"parsers.txt line '{line}' names no target.");
            Assert.True(map.TryAdd(fields[0], fields[1..]), $"parsers.txt lists {fields[0]} twice.");
        }

        return map;
    }

    /// <summary>Reads the target names registered in <c>FuzzTargets.All</c> from its source (the harness is not referenced by tests).</summary>
    private static HashSet<string> ReadTargets()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "tests", "Broadside.Fuzz", "FuzzTargets.cs"));
        HashSet<string> targets = [.. TargetEntry().Matches(source).Select(static match => match.Groups[1].Value)];
        Assert.NotEmpty(targets);
        return targets;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find Broadside.slnx above the test output directory.");
    }

    [GeneratedRegex("""^\s*\["([a-z0-9-]+)"\] = """, RegexOptions.Multiline)]
    private static partial Regex TargetEntry();
}
