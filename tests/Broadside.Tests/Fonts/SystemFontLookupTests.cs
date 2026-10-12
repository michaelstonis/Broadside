using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The operating-system font resolver reads only the font files whose names point at the font asked for, and finds exactly what
/// reading every font file found before (issue #59): checked against the machine's own fonts, and in the Corpus trait for every
/// non-embedded font of the five real-world corpora. ISO 32000-2 §9.6.2.2, §9.8; ADR 0009.
/// </summary>
public sealed class SystemFontLookupTests(ITestOutputHelper output)
{
    public static TheoryData<string> CorpusIds => new(["pdfjs", "pdfbox", "qpdf", "pdfium-tests", "pdf20examples"]);

    [Fact]
    public void Lookups_on_this_machine_find_what_reading_every_font_file_finds()
    {
        var resolver = new SystemFontResolver();
        List<FontQuery> queries =
        [
            .. Enum.GetValues<Standard14Font>().Select(font => new FontQuery(font.ToString()) { Standard14 = font }),
            .. new[] { "Arial", "Arial,Bold", "ArialMT", "Arial-BoldItalicMT", "TimesNewRomanPSMT", "TimesNewRoman,Italic", "CourierNew", "Verdana", "Georgia", "Calibri", "Cambria", "Tahoma", "SegoeUI", "LucidaGrande", "HelveticaNeue-Bold", "Menlo-Regular", "DejaVuSans", "LiberationSerif-Bold", "NotoSans-Regular", "MS-Mincho", "HiraMinProN-W3", "SimSun", "Wingdings", "Broadside-NoSuchFont" }
                .Select(name => new FontQuery(name)),
        ];

        foreach (FontQuery query in queries)
        {
            Assert.Equal(Describe(resolver.ResolveFontFromEveryFile(query)), Describe(resolver.ResolveFont(query)));
        }
    }

    [Theory]
    [MemberData(nameof(CorpusIds))]
    [Trait("Category", "Corpus")]
    public void Every_non_embedded_font_of_a_real_world_corpus_resolves_as_reading_every_font_file_does(string corpusId)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpusId);
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus '{corpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        var recorder = new RecordingResolver();
        PdfOptions options = new PdfOptions().UseFontResolver(recorder).UseSystemFontResolver(null);
        foreach (string file in files)
        {
            using PdfDocument? document = OpenOrNull(file, options);
            if (document is not null)
            {
                AskForEverySubstitute(document);
            }
        }

        var resolver = new SystemFontResolver();
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (FontQuery query in recorder.Queries)
        {
            string expected = Describe(resolver.ResolveFontFromEveryFile(query));
            Assert.Equal((query.Name, expected), (query.Name, Describe(resolver.ResolveFont(query))));
            string kind = expected.Split(' ')[^1];
            kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
        }

        output.WriteLine($"{corpusId}: {recorder.Queries.Count} distinct queries; {string.Join(", ", kinds.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}"))}.");
    }

    private static string Describe(FontResolution? resolution) =>
        resolution is null ? "none" : $"{resolution.Name}#{resolution.FaceIndex} {resolution.MatchKind}";

    private static void AskForEverySubstitute(PdfDocument document)
    {
        foreach (PdfPage page in document.Pages)
        {
            if (page.Resources is not { } resources
                || !resources.TryGetValue(new CosName("Font"), out CosObject? value)
                || document.Resolve(value) is not CosDictionary fonts)
            {
                continue;
            }

            foreach (KeyValuePair<CosName, CosObject> entry in fonts)
            {
                try
                {
                    _ = document.GetFont(entry.Value) switch
                    {
                        PdfSimpleFont { FontType: not PdfFontType.Type3, Program: null } simple => simple.Substitute,
                        PdfType0Font { DescendantFont: { Program: null } cidFont } => cidFont.Substitute,
                        _ => null,
                    };
                }
                catch (DiagnosticException)
                {
                    // A font that does not read is not asked for.
                }
            }
        }
    }

    private static PdfDocument? OpenOrNull(string file, PdfOptions options)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)), options);
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException or DiagnosticException)
        {
            return null;
        }
    }

    /// <summary>Records every distinct query and finds nothing, so the core goes on to its Standard 14 stand-in queries.</summary>
    private sealed class RecordingResolver : IFontResolver
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public List<FontQuery> Queries { get; } = [];

        public FontResolution? ResolveFont(FontQuery query)
        {
            string key = string.Join('|', query.Name, query.Standard14, query.Flags, query.FontFamily, query.FontWeight, query.FontStretch, query.ItalicAngle, query.StemV, query.FontType, query.CidFontType);
            lock (_seen)
            {
                if (_seen.Add(key))
                {
                    Queries.Add(query);
                }
            }

            return null;
        }
    }
}
