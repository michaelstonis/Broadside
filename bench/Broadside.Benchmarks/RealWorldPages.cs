using System.Globalization;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The pinned real-world pages of the rendering gate (issue #138, <c>tests/Broadside.Rendering.Tests/CorpusGate/pinned-pages.txt</c>)
/// as benchmark parameters for the Phase 3 baselines (issue #140), each written <c>corpus/path#page</c> with a zero-based page.
/// With no real-world corpus (CI's <c>--job dry</c> and the regression gate) <see cref="FallbackPage"/> stands in, so the
/// benchmarks never fail for want of a corpus.
/// </summary>
internal static class RealWorldPages
{
    /// <summary>The stand-in page under <c>tests/Corpus/</c>: Standard 14 text, so fonts, glyphs and the rasterizer all run.</summary>
    public const string FallbackPage = "text-standard14.pdf#0";

    /// <summary>
    /// The engine every real-world page benchmark opens with: the rendering gate's (Standard 14 and predefined CMaps bundled, no system
    /// fonts, so the same glyphs on every OS) and no display-list cache, so a warm render interprets the page again (issue #103).
    /// </summary>
    public static PdfEngine Engine { get; } = new(new PdfOptions()
        .UseStandard14Fonts()
        .UsePredefinedCMaps()
        .UseSystemFontResolver(null)
        .WithDisplayListCacheBudget(0));

    /// <summary>The pinned pages whose file is present under the real-world corpus, else <see cref="FallbackPage"/>.</summary>
    /// <returns>The parameter values.</returns>
    public static IEnumerable<string> Available()
    {
        string? root = CorpusLocator.RealWorldCorpusDirectory;
        List<string> present = root is null
            ? []
            : [.. Pinned().Where(entry => File.Exists(Path.Combine(root, entry.Path))).Select(static entry => $"{entry.Path}#{entry.Page}")];
        return present.Count > 0 ? present : [FallbackPage];
    }

    /// <summary>Reads the file of a parameter value and returns its bytes and zero-based page.</summary>
    /// <param name="value">A value from <see cref="Available"/>.</param>
    /// <returns>The file's bytes and the page index.</returns>
    public static (byte[] Bytes, int Page) Read(string value)
    {
        int hash = value.LastIndexOf('#');
        string path = value[..hash];
        int page = int.Parse(value[(hash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture);
        string full = value == FallbackPage
            ? Path.Combine(CorpusLocator.CorpusDirectory, path)
            : Path.Combine(CorpusLocator.RealWorldCorpusDirectory!, path);
        return (File.ReadAllBytes(full), page);
    }

    private static IEnumerable<(string Path, int Page)> Pinned()
    {
        string list = Path.Combine(CorpusLocator.RepositoryRoot, "tests", "Broadside.Rendering.Tests", "CorpusGate", "pinned-pages.txt");
        foreach (string line in File.ReadLines(list))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split('\t');
            foreach (string page in fields[1].Split(','))
            {
                yield return (fields[0], int.Parse(page, NumberStyles.None, CultureInfo.InvariantCulture));
            }
        }
    }
}
